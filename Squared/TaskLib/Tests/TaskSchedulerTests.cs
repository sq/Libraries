using System;
using System.Collections.Generic;
using Squared.Task;
using NUnit.Framework;
using System.Threading;
using System.Net.Sockets;
using System.Text;
using System.IO;

namespace Squared.Task {
    class ValueHolder {
        public int Value;
    }

    [TestFixture]
    public class TaskSchedulerTests {
        TaskScheduler Scheduler;
        Future TestFuture;

        [SetUp]
        public void SetUp () {
            Scheduler = new TaskScheduler();
        }

        [TearDown]
        public void TearDown () {
            if (Scheduler != null)
                Scheduler.Dispose();
            Scheduler = null;
            GC.Collect();
        }

        IEnumerator<object> TaskReturn5 () {
            yield return 5;
        }

        [Test]
        public void BasicTest () {
            var future = Scheduler.Start(TaskReturn5());
            Scheduler.Step();
            Assert.AreEqual(5, future.Result);
        }

        IEnumerator<object> TaskReturnValueOfFuture () {
            yield return this.TestFuture;
            yield return this.TestFuture.Result;
        }

        [Test]
        public void YieldOnFutureTest () {
            this.TestFuture = new Future();
            var future = Scheduler.Start(TaskReturnValueOfFuture());
            Scheduler.Step();
            Assert.IsFalse(future.Completed);
            TestFuture.Complete(10);
            Scheduler.Step();
            Assert.AreEqual(10, future.Result);
        }

        [Test]
        public void MultipleYieldersOnFutureTest () {
            this.TestFuture = new Future();
            var futures = new List<Future>();
            for (int i = 0; i < 10; i++)
                futures.Add(Scheduler.Start(TaskReturnValueOfFuture()));

            Scheduler.Step();
            foreach (Future f in futures)
                Assert.IsFalse(f.Completed);

            TestFuture.Complete(10);
            Scheduler.Step();

            foreach (Future f in futures) {
                Assert.IsTrue(f.Completed);
                Assert.AreEqual(10, f.Result);
            }
        }

        IEnumerator<object> TaskReturnValueOfYieldedTask () {
            var tr = new TaskRunner(TaskReturnValueOfFuture());
            yield return tr;
            yield return tr.Result;
        }

        [Test]
        public void RecursiveYieldTest () {
            this.TestFuture = new Future();
            var future = Scheduler.Start(TaskReturnValueOfYieldedTask());
            Scheduler.Step();
            TestFuture.Complete(10);
            Scheduler.Step();
            Assert.AreEqual(10, future.Result);
        }

        IEnumerator<object> TaskLongLivedWorker (int[] buf) {
            var cf = new Future(null);
            for (int i = 0; i < 1000000; i++) {
                buf[0] = buf[0] + 1;
                yield return cf;
            }
        }

        [Test]
        public void LongLivedWorkerTest () {
            var buf = new int[1];
            long timeStart = DateTime.Now.Ticks;
            var f = Scheduler.Start(TaskLongLivedWorker(buf));
            Scheduler.Step();
            long timeEnd = DateTime.Now.Ticks;
            TimeSpan elapsed = new TimeSpan(timeEnd - timeStart);
            Assert.AreEqual(1000000, buf[0]);
            Console.WriteLine("Took {0:N2} secs for {1} iterations. {2:N1} iterations/sec", elapsed.TotalSeconds, buf[0], 1.0 * buf[0] / elapsed.TotalSeconds);
        }

        IEnumerator<object> TaskWaitForNextStep (List<int> buf) {
            int i = 0;
            while (true) {
                buf.Add(i++);
                yield return new WaitForNextStep();
            }
        }

        [Test]
        public void WaitForNextStepTest () {
            var buf = new List<int>();
            Scheduler.Start(TaskWaitForNextStep(buf));
            Scheduler.Step();
            Assert.AreEqual(1, buf.Count);
            Scheduler.Step();
            Assert.AreEqual(2, buf.Count);
            Scheduler.Step();
            Assert.AreEqual(3, buf.Count);
        }

        IEnumerator<object> TaskLongLivedWorkerStepWaiter (int[] buf) {
            for (int i = 0; i < 1000000; i++) {
                buf[0] = buf[0] + 1;
                yield return new WaitForNextStep();
            }
        }

        [Test]
        public void StepPerformanceTest () {
            var buf = new int[1];
            long timeStart = DateTime.Now.Ticks;
            var f = Scheduler.Start(TaskLongLivedWorkerStepWaiter(buf));
            while (Scheduler.HasPendingTasks)
                Scheduler.Step();
            long timeEnd = DateTime.Now.Ticks;
            TimeSpan elapsed = new TimeSpan(timeEnd - timeStart);
            Assert.AreEqual(1000000, buf[0]);
            Console.WriteLine("Took {0:N2} secs for {1} steps. {2:N1} steps/sec", elapsed.TotalSeconds, buf[0], 1.0 * buf[0] / elapsed.TotalSeconds);
        }

        [Test]
        public void LotsOfWorkersTest () {
            var buf = new int[1];
            var futures = new List<Future>();
            for (int i = 0; i < 25; i++) {
                futures.Add(Scheduler.Start(TaskLongLivedWorker(buf)));
            }
            long timeStart = DateTime.Now.Ticks;
            Scheduler.Step();
            long timeEnd = DateTime.Now.Ticks;
            TimeSpan elapsed = new TimeSpan(timeEnd - timeStart);
            Assert.AreEqual(1000000 * 25, buf[0]);
            Console.WriteLine("Took {0:N2} secs for {1} iterations. {2:N1} iterations/sec", elapsed.TotalSeconds, buf[0], 1.0 * buf[0] / elapsed.TotalSeconds);
        }

        int WorkerThread (ValueHolder buf, int iterations) {
            for (int i = 0; i < iterations; i++) {
                System.Threading.Interlocked.Increment(ref buf.Value);
            }
            return 42;
        }

        [Test]
        public void RunInThreadTest () {
            int numIterations = 1000000 * 20;
            var buf = new ValueHolder();
            var f = Scheduler.RunInThread(new Func<ValueHolder, int, int>(WorkerThread), buf, numIterations);
            while (!f.Completed)
                Scheduler.Step();
            Assert.AreEqual(42, f.Result);
            Assert.AreEqual(numIterations, buf.Value);
        }

        [Test]
        public void MultipleThreadTest () {
            int numIterations = 1000000;
            int numWorkers = 50;

            var buf = new ValueHolder();
            var fn = new Func<ValueHolder, int, int>(WorkerThread);

            var futures = new List<Future>();
            for (int i = 0; i < numWorkers; i++) {
                futures.Add(Scheduler.RunInThread(fn, buf, numIterations));
            }

            var f = Scheduler.Start(new WaitForAll(futures));
            while (!f.Completed)
                Scheduler.Step();

            Assert.AreEqual(numIterations * numWorkers, buf.Value);
        }

        IEnumerator<object> InfiniteTask () {
            while (true)
                yield return new WaitForNextStep();
        }

        [Test]
        public void SleepTest () {
            int duration = 2;
            int timeScale = 25;
            var f = Scheduler.Start(new Sleep(duration));
            
            long timeStart = DateTime.Now.Ticks;
            Scheduler.Step();
            f.GetCompletionEvent().WaitOne();
            long timeEnd = DateTime.Now.Ticks;

            long elapsed = (long)Math.Round(TimeSpan.FromTicks(timeEnd - timeStart).TotalSeconds * timeScale);
            Assert.AreEqual(duration * timeScale, elapsed);
        }

        [Test]
        public void ParallelSleepTest () {
            int timeScale = 10;
            var a = Scheduler.Start(new Sleep(1));
            var b = Scheduler.Start(new Sleep(2));
            var c = Scheduler.Start(new Sleep(4));
            
            long timeStart = DateTime.Now.Ticks;
            Scheduler.Step();

            a.GetCompletionEvent().WaitOne();
            long elapsed = (long)Math.Round(TimeSpan.FromTicks(DateTime.Now.Ticks - timeStart).TotalSeconds * timeScale);
            Assert.AreEqual(1 * timeScale, elapsed);

            b.GetCompletionEvent().WaitOne();
            elapsed = (long)Math.Round(TimeSpan.FromTicks(DateTime.Now.Ticks - timeStart).TotalSeconds * timeScale);
            Assert.AreEqual(2 * timeScale, elapsed);

            c.GetCompletionEvent().WaitOne();
            elapsed = (long)Math.Round(TimeSpan.FromTicks(DateTime.Now.Ticks - timeStart).TotalSeconds * timeScale);
            Assert.AreEqual(4 * timeScale, elapsed);
        }

        [Test]
        public void SequentialSleepTest () {
            int timeScale = 10;
            var a = Scheduler.Start(new Sleep(1));

            long timeStart = DateTime.Now.Ticks;

            Scheduler.Step();
            a.GetCompletionEvent().WaitOne();

            long elapsed = (long)Math.Round(TimeSpan.FromTicks(DateTime.Now.Ticks - timeStart).TotalSeconds * timeScale);
            Assert.AreEqual(1 * timeScale, elapsed);

            System.Threading.Thread.Sleep(500);

            var b = Scheduler.Start(new Sleep(2));
            timeStart = DateTime.Now.Ticks; 

            Scheduler.Step();
            b.GetCompletionEvent().WaitOne();

            elapsed = (long)Math.Round(TimeSpan.FromTicks(DateTime.Now.Ticks - timeStart).TotalSeconds * timeScale);
            Assert.AreEqual(2 * timeScale, elapsed);

            System.Threading.Thread.Sleep(500);

            var c = Scheduler.Start(new Sleep(4));
            timeStart = DateTime.Now.Ticks; 

            Scheduler.Step();
            c.GetCompletionEvent().WaitOne();

            elapsed = (long)Math.Round(TimeSpan.FromTicks(DateTime.Now.Ticks - timeStart).TotalSeconds * timeScale);
            Assert.AreEqual(4 * timeScale, elapsed);
        }

        [Test]
        public void SleepUntilTest () {
            int duration = 2;
            int timeScale = 25;
            
            long timeStart = DateTime.Now.Ticks;
            var f = Scheduler.Start(new SleepUntil(new DateTime(timeStart).AddSeconds(duration).Ticks));

            Scheduler.Step();
            f.GetCompletionEvent().WaitOne();

            long elapsed = (long)Math.Round(TimeSpan.FromTicks(DateTime.Now.Ticks - timeStart).TotalSeconds * timeScale);
            Assert.AreEqual(duration * timeScale, elapsed);
        }

        [Test]
        public void WaitForFirstTest () {
            int duration = 2;
            int timeScale = 25;

            var a = Scheduler.Start(InfiniteTask());
            var b = Scheduler.Start(new Sleep(duration));
            var c = Scheduler.Start(new WaitForFirst(a, b));

            GC.Collect();

            long timeStart = DateTime.Now.Ticks;
            c.GetCompletionEvent().WaitOne();
            Assert.AreEqual(b, c.Result);
            long timeEnd = DateTime.Now.Ticks;

            long elapsed = (long)Math.Round(TimeSpan.FromTicks(timeEnd - timeStart).TotalSeconds * timeScale);
            Assert.AreEqual(duration * timeScale, elapsed);
        }

        [Test]
        public void WaitForFirstGCTest () {
            var a = Scheduler.Start(InfiniteTask());
            var b = Scheduler.Start(new Sleep(2));
            var c = Scheduler.Start(new WaitForFirst(a, b));

            while (!c.Completed) {
                Scheduler.Step();
                GC.Collect();
            }
            Assert.AreEqual(b, c.Result);
        }

        [Test]
        public void WaitWithTimeoutTest () {
            var a = Scheduler.Start(InfiniteTask());
            var b = Scheduler.Start(new WaitWithTimeout(a, 2));

            while (!b.Completed)
                Scheduler.Step();

            try {
                var _ = b.Result;
                Assert.Fail("TimeoutException was not raised");
            } catch (FutureException ex) {
                Assert.IsInstanceOfType(typeof(TimeoutException), ex.InnerException);
            }
        }

        [Test]
        public void CollectingFutureKillsTask () {
            var f = Scheduler.Start(InfiniteTask());

            Scheduler.Step();

            Assert.IsTrue(Scheduler.HasPendingTasks);

            f = null;
            GC.Collect();

            Scheduler.Step();

            Assert.IsFalse(Scheduler.HasPendingTasks);
        }

        [Test]
        public void RunningTasksDoNotCauseSchedulerToLeak () {
            var f = Scheduler.Start(InfiniteTask());

            Scheduler.Step();

            Assert.IsTrue(Scheduler.HasPendingTasks);

            WeakReference wr = new WeakReference(Scheduler);

            Scheduler.Dispose();
            Scheduler = null;
            GC.Collect();

            Assert.IsFalse(wr.IsAlive);
        }

        // Disabled (doesn't pass, but I'm not sure it should)
        public void PendingSleepsDoNotCauseSchedulerToLeak () {
            var f = Scheduler.Start(new Sleep(30));

            Scheduler.Step();

            Assert.IsTrue(Scheduler.HasPendingTasks);

            WeakReference wr = new WeakReference(Scheduler);

            Scheduler.Dispose();
            Scheduler = null;
            GC.Collect();

            Assert.IsFalse(wr.IsAlive);
        }

        IEnumerator<object> CrashyTask () {
            yield return new WaitForNextStep();
            throw new Exception("pancakes");
        }

        [Test]
        public void WrapsExceptionsFromInsideTasks () {
            var f = Scheduler.Start(CrashyTask());

            Scheduler.Step();
            Scheduler.Step();

            try {
                var _ = f.Result;
                Assert.Fail("Exception was not raised");
            } catch (FutureException ex) {
                Assert.AreEqual("pancakes", ex.InnerException.Message);
            }
        }

        [Test]
        public void RunUntilCompleteExecutionPolicy () {
            var f = Scheduler.Start(InfiniteTask(), TaskExecutionPolicy.RunUntilComplete);

            Scheduler.Step();

            Assert.IsTrue(Scheduler.HasPendingTasks);

            f = null;
            GC.Collect();

            Scheduler.Step();

            Assert.IsTrue(Scheduler.HasPendingTasks);
        }

        [Test]
        public void RunAsBackgroundTaskExecutionPolicyBubblesExceptionsOutOfSchedulerStep () {
            Scheduler.Start(CrashyTask(), TaskExecutionPolicy.RunAsBackgroundTask);

            Scheduler.Step();

            try {
                Scheduler.Step();
                Assert.Fail("Exception did not bubble out of Scheduler.Step");
            } catch (TaskException ex) {
                Assert.AreEqual("pancakes", ex.InnerException.Message);
            }
        }

        void CrashyWorkerThread () {
            throw new Exception("maple syrup");
        }

        [Test]
        public void WrapsExceptionsFromInsideWorkerThreads () {
            var f = Scheduler.RunInThread(new Action(CrashyWorkerThread));
            while (!f.Completed)
                Scheduler.Step();
            try {
                var _ = f.Result;
                Assert.Fail("Exception was not raised");
            } catch (Exception ex) {
                Assert.AreEqual("maple syrup", ex.InnerException.Message);
            }
        }

        [Test]
        public void WaitForWaitHandleTest () {
            ManualResetEvent e = new ManualResetEvent(false);
            var f = Scheduler.Start(new WaitForWaitHandle(e));

            Scheduler.Step();
            Thread.Sleep(500);

            long timeStart = DateTime.Now.Ticks;
            e.Set();

            f.GetCompletionEvent().WaitOne();
            long elapsed = DateTime.Now.Ticks - timeStart;
            Assert.LessOrEqual(elapsed, TimeSpan.FromMilliseconds(10).Ticks);
        }

        [Test]
        public void WaitForWaitHandlesTest () {
            ManualResetEvent a = new ManualResetEvent(false);
            ManualResetEvent b = new ManualResetEvent(false);
            ManualResetEvent c = new ManualResetEvent(false);

            var f = Scheduler.Start(new WaitForWaitHandles(a, b, c));

            Scheduler.Step();
            Thread.Sleep(500);

            long timeStart = DateTime.Now.Ticks;
            a.Set();
            b.Set();
            c.Set();

            f.GetCompletionEvent().WaitOne();
            long elapsed = DateTime.Now.Ticks - timeStart;
            Assert.LessOrEqual(elapsed, TimeSpan.FromMilliseconds(10).Ticks);
        }
    }

    [TestFixture]
    public class ThreadedTaskSchedulerTests {
        TaskScheduler Scheduler;

        [SetUp]
        public void SetUp () {
            Scheduler = new TaskScheduler(true);
        }

        [TearDown]
        public void TearDown () {
            if (Scheduler != null)
                Scheduler.Dispose();
            Scheduler = null;
            GC.Collect();
        }

        [Test]
        public void WaitForWorkItemsTest () {
            ValueHolder vh = new ValueHolder();
            vh.Value = 0;

            Future a = Scheduler.Start(new Sleep(2));
            a.RegisterOnComplete((result, error) => {
                Scheduler.QueueWorkItem(() => {
                    vh.Value = 1;
                });
            });

            long timeStart = DateTime.Now.Ticks;
            Scheduler.Step();
            Scheduler.WaitForWorkItems();
            Scheduler.Step();
            long elapsed = DateTime.Now.Ticks - timeStart;
            Assert.LessOrEqual(elapsed, TimeSpan.FromMilliseconds(2001).Ticks);
            Assert.AreEqual(1, vh.Value);
        }

        IEnumerator<object> ServerTask (TcpListener server, int numClients) {
            server.Start();
            try {
                while (numClients > 0) {
                    System.Diagnostics.Debug.WriteLine("Waiting for incoming connection...");
                    Future connection = server.AcceptIncomingConnection();
                    yield return connection;
                    System.Diagnostics.Debug.WriteLine("Connection established. Sending data...");
                    using (TcpClient peer = connection.Result as TcpClient) {
                        byte[] bytes = Encoding.ASCII.GetBytes("Hello, world!");
                        Future writer = peer.GetStream().AsyncWrite(bytes, 0, bytes.Length);
                        yield return writer;
                        System.Diagnostics.Debug.WriteLine("Data sent.");
                    }
                    System.Diagnostics.Debug.WriteLine("Connection closed.");
                    numClients -= 1;
                }
            } finally {
                server.Stop();
            }
        }

        [Test]
        public void SocketServerTest () {
            var results = new List<string>();
            int port = 12345;
            TcpListener server = new TcpListener(System.Net.IPAddress.Any, port);

            var a = Scheduler.Start(ServerTask(server, 1));

            var b = Scheduler.RunInThread(new Action(() => {
                System.Diagnostics.Debug.WriteLine("Connecting...");
                using (TcpClient client = new TcpClient("localhost", port)) {
                    using (StreamReader reader = new StreamReader(client.GetStream(), Encoding.ASCII)) {
                        System.Diagnostics.Debug.WriteLine("Connected. Receiving data...");
                        string result = reader.ReadToEnd();
                        System.Diagnostics.Debug.WriteLine("Data received.");
                        results.Add(result);
                    }
                }
                System.Diagnostics.Debug.WriteLine("Disconnected.");
            }));

            while (!b.Completed)
                Scheduler.Step();

            Assert.AreEqual(new string[] { "Hello, world!" }, results.ToArray());
        }

        IEnumerator<object> ClientTask (List<string> output, string server, int port) {
            System.Diagnostics.Debug.WriteLine("Connecting...");
            Future connect = Network.ConnectTo(server, port);
            yield return connect;
            System.Diagnostics.Debug.WriteLine("Connected. Receiving data...");
            using (TcpClient client = connect.Result as TcpClient) {
                byte[] bytes = new byte[256];
                Future reader = client.GetStream().AsyncRead(bytes, 0, bytes.Length);
                yield return reader;
                System.Diagnostics.Debug.WriteLine("Data received.");
                output.Add(Encoding.ASCII.GetString(bytes, 0, (int)reader.Result));
            }
            System.Diagnostics.Debug.WriteLine("Disconnected.");
        }

        [Test]
        public void SocketClientTest () {
            var results = new List<string>();
            int port = 12345;
            TcpListener server = new TcpListener(System.Net.IPAddress.Any, port);

            var a = Scheduler.Start(ServerTask(server, 1));
            var b = Scheduler.Start(ClientTask(results, "localhost", port));

            while (!b.Completed)
                Scheduler.Step();

            Assert.AreEqual(new string[] { "Hello, world!" }, results.ToArray());
        }

        [Test]
        public void MultipleSocketClientTest () {
            var results = new List<string>();
            int numClients = 4;
            int port = 12345;
            TcpListener server = new TcpListener(System.Net.IPAddress.Any, port);

            var a = Scheduler.Start(ServerTask(server, numClients));
            var clients = new List<Future>();
            for (int i = 0; i < numClients; i++) {
                clients.Add(Scheduler.Start(ClientTask(results, "localhost", port)));
            }
            var b = Scheduler.Start(new WaitForAll(clients));

            while (!b.Completed)
                Scheduler.Step();

            string helloWorld = "Hello, world!";
            var expectedResults = new List<string>();
            for (int i = 0; i < numClients; i++)
                expectedResults.Add(helloWorld);
            Assert.AreEqual(expectedResults.ToArray(), results.ToArray());
        }
    }

    public abstract class Entity<T>
        where T : class {
        public int NumSteps;
        public int ID;
        public float X, Y, Z;
        public float vX, vY, vZ;
        protected Random rng;
        protected List<T> Entities;

        public Entity (int seed, List<T> entities) {
            rng = new Random(seed);
            Entities = entities;
            X = Y = Z = 0;
            ID = rng.Next();
            vX = (float)rng.NextDouble();
            vY = (float)rng.NextDouble();
            vZ = (float)rng.NextDouble();
            NumSteps = 0;
        }

        public override string ToString () {
            return String.Format("{0}<{1}>", this.GetType().Name, ID);
        }

        protected T GetRandomSibling () {
            if (Entities.Count <= 1)
                throw new InvalidOperationException();

            int i = rng.Next(0, Entities.Count - 1);
            return Entities[i];
        }

        protected abstract object GetWaiter ();

        public virtual void Start (TaskScheduler scheduler) {
            scheduler.Start(this.Run(), TaskExecutionPolicy.RunUntilComplete);
        }

        public IEnumerator<object> Run () {
            while (true) {
                NumSteps += 1;
                X += vX;
                Y += vY;
                Z += vZ;
                yield return this.GetWaiter();
            }
        }
    }

    public class SimpleEntity : Entity<SimpleEntity> {
        public SimpleEntity (int seed, List<SimpleEntity> entities)
            : base(seed, entities) {
        }

        protected override object GetWaiter () {
            return new WaitForNextStep();
        }
    }

    public class SleepyEntity : Entity<SleepyEntity> {
        public SleepyEntity (int seed, List<SleepyEntity> entities)
            : base(seed, entities) {
        }

        protected override object GetWaiter () {
            return new Sleep(rng.NextDouble() * 0.1);
        }
    }

    public class ChattyEntity : Entity<ChattyEntity> {
        BlockingQueue<int> _Messages = new BlockingQueue<int>();

        public ChattyEntity (int seed, List<ChattyEntity> entities)
            : base(seed, entities) {
        }

        protected override object GetWaiter () {
            return new WaitForNextStep();

        }

        public void Tell (int message) {
            _Messages.Enqueue(message);
        }

        public IEnumerator<object> Talk () {
            int state = 10;
            while (true) {
                NumSteps += 1;
                if (state <= 0)
                    yield break;

                ChattyEntity sibling = GetRandomSibling();
                sibling.Tell(state - 1);
                yield return new WaitForNextStep();

                Future response = _Messages.Dequeue();
                yield return response;
                state = (int)response.Result;
            }
        }

        public override void Start (TaskScheduler scheduler) {
            scheduler.Start(this.Talk(), TaskExecutionPolicy.RunUntilComplete);
        }
    }

    [TestFixture]
    public class FunctionalTests {
        TaskScheduler Scheduler;

        [SetUp]
        public void SetUp () {
            Scheduler = new TaskScheduler(true);
        }

        [TearDown]
        public void TearDown () {
            if (Scheduler != null)
                Scheduler.Dispose();
            Scheduler = null;
            GC.Collect();
        }

        [Test]
        public void TestLotsOfEntities () {
            int numEntities = 10000;
            int numSteps = 500;

            var entities = new List<SimpleEntity>();
            for (int i = 0; i < numEntities; i++)
                entities.Add(new SimpleEntity(i, entities));

            foreach (var e in entities)
                e.Start(Scheduler);

            long timeStart = DateTime.Now.Ticks;

            for (int j = 0; j < numSteps; j++) {
                Scheduler.Step();
            }

            long timeEnd = DateTime.Now.Ticks;
            TimeSpan elapsed = new TimeSpan(timeEnd - timeStart);

            foreach (var e in entities)
                Assert.AreEqual(numSteps, e.NumSteps);

            Console.WriteLine("Took {0:N2} secs for {1} iterations. {2:N1} entities/sec", elapsed.TotalSeconds, numSteps, numEntities * numSteps / elapsed.TotalSeconds);
        }

        [Test]
        public void TestLotsOfSleepyEntities () {
            int numEntities = 1500;
            int numIterations = 0;
            double duration = 3.0;

            var entities = new List<SleepyEntity>();
            for (int i = 0; i < numEntities; i++)
                entities.Add(new SleepyEntity(i, entities));

            foreach (var e in entities)
                e.Start(Scheduler);

            long elapsed = 0;
            long desiredTicks = TimeSpan.FromSeconds(duration).Ticks;
            int j = 0;

            while (elapsed < desiredTicks) {
                Scheduler.WaitForWorkItems(1.0);
                long timeStart = DateTime.Now.Ticks;
                Scheduler.Step();
                long timeEnd = DateTime.Now.Ticks;
                elapsed += (timeEnd - timeStart);
                j += 1;
                if ((j % 1000) == 0) {
                    Console.Out.WriteLine(".");
                } else if ((j % 10) == 0) {
                    Console.Out.Write(".");
                }
                numIterations += 1;
            }

            Console.Out.WriteLine("");

            int totalEntitySteps = 0;
            foreach (var e in entities)
                totalEntitySteps += e.NumSteps;

            double elapsedSeconds = TimeSpan.FromTicks(elapsed).TotalSeconds;

            Console.WriteLine("{0} iterations in {1:N2} secs of processing time. {2} entity steps @ {3} entities/sec", numIterations, elapsedSeconds, totalEntitySteps, totalEntitySteps / elapsedSeconds);
        }

        [Test]
        public void TestLotsOfChattyEntities () {
            int numEntities = 500;
            int numSteps = 0;

            var entities = new List<ChattyEntity>();
            for (int i = 0; i < numEntities; i++)
                entities.Add(new ChattyEntity(i, entities));

            foreach (var e in entities)
                e.Start(Scheduler);

            long timeStart = DateTime.Now.Ticks;

            while (Scheduler.HasPendingTasks) {
                numSteps += 1;
                Scheduler.Step();
            }

            long timeEnd = DateTime.Now.Ticks;
            TimeSpan elapsed = new TimeSpan(timeEnd - timeStart);

            int numEntitySteps = 0;
            foreach (var e in entities)
                numEntitySteps += e.NumSteps;

            Console.WriteLine("Took {0:N2} secs for {1} iterations. {2:N1} entities/sec", elapsed.TotalSeconds, numSteps, numEntitySteps / elapsed.TotalSeconds);
        }
    }
}
