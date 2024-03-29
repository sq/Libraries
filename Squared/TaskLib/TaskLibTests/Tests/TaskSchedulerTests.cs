﻿#pragma warning disable 0612

using System;
using System.Collections.Generic;
using Squared.Task;
using Squared.Task.IO;
using Squared.Util;
using NUnit.Framework;
using System.Threading;
using System.Net.Sockets;
using System.Text;
using System.IO;
using System.Windows.Forms;
using Squared.Threading;

namespace Squared.Task {
    class ValueHolder {
        public int Value;
    }

    [Ignore("Base class")]
    public class BasicJobQueueTests {
        protected TaskScheduler Scheduler;
        protected IFuture TestFuture;

        protected IEnumerator<object> TaskReturn5 () {
            yield return new Result(5);
        }

        [Test]
        public void BasicTest () {
            var future = Scheduler.Start(TaskReturn5());
            Scheduler.Step();
            Assert.AreEqual(5, future.Result);
        }

        protected IEnumerator<object> TaskReturnValueOfFuture () {
            yield return this.TestFuture;
            yield return new Result(this.TestFuture.Result);
        }

        [Test]
        public void YieldOnFutureTest () {
            this.TestFuture = new Future<object>();
            var future = Scheduler.Start(TaskReturnValueOfFuture());
            Scheduler.Step();
            Assert.IsFalse(future.Completed);
            TestFuture.Complete(10);
            Scheduler.Step();
            Assert.AreEqual(10, future.Result);
        }

        IEnumerator<object> TaskLongLivedWorker (int[] buf, int numYields) {
            var yieldInstance = new Yield();

            for (int i = 0; i < numYields; i++) {
                buf[0] = buf[0] + 1;
                yield return yieldInstance;
            }
        }

        IEnumerator<object> TaskLongLivedWorkerStepWaiter (int[] buf, int numSteps) {
            var waitInstance = new WaitForNextStep();

            for (int i = 0; i < numSteps; i++) {
                buf[0] = buf[0] + 1;
                yield return waitInstance;
            }
        }

        [Test]
        public void StepPerformanceTest () {
            int numSteps = 100000;
            var buf = new int[1];

            var f = Scheduler.Start(TaskLongLivedWorkerStepWaiter(buf, numSteps));
            while (Scheduler.HasPendingTasks)
                Scheduler.Step();

            Assert.AreEqual(numSteps, buf[0]);
            buf[0] = 0;

            GC.Collect();
            GC.WaitForPendingFinalizers();

            long timeStart = Time.Ticks;

            f = Scheduler.Start(TaskLongLivedWorkerStepWaiter(buf, numSteps));
            while (Scheduler.HasPendingTasks)
                Scheduler.Step();

            long timeEnd = Time.Ticks;
            TimeSpan elapsed = new TimeSpan(timeEnd - timeStart);
            Assert.AreEqual(numSteps, buf[0]);
            Console.WriteLine("Took {0:N2} secs for {1} steps. {2:N1} steps/sec", elapsed.TotalSeconds, buf[0], 1.0 * buf[0] / elapsed.TotalSeconds);
        }

        [Test]
        public void WorkItemPerformanceTest () {
            int numYields = 250000;
            var buf = new int[1];

            var f = Scheduler.Start(TaskLongLivedWorker(buf, numYields));
            while (Scheduler.HasPendingTasks)
                Scheduler.Step();

            Assert.AreEqual(numYields, buf[0]);
            buf[0] = 0;

            GC.Collect();
            GC.WaitForPendingFinalizers();

            long timeStart = Time.Ticks;

            f = Scheduler.Start(TaskLongLivedWorker(buf, numYields));
            while (Scheduler.HasPendingTasks)
                Scheduler.Step();

            long timeEnd = Time.Ticks;
            TimeSpan elapsed = new TimeSpan(timeEnd - timeStart);
            Assert.AreEqual(numYields, buf[0]);
            Console.WriteLine("Took {0:N2} secs for {1} yields. {2:N1} yields/sec", elapsed.TotalSeconds, buf[0], 1.0 * buf[0] / elapsed.TotalSeconds);
        }
    }

    [TestFixture]
    public class TaskSchedulerTests : BasicJobQueueTests {
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

        [Test]
        public void MultipleYieldersOnFutureTest () {
            this.TestFuture = new Future<object>();
            var futures = new List<IFuture>();
            for (int i = 0; i < 10; i++)
                futures.Add(Scheduler.Start(TaskReturnValueOfFuture()));

            Scheduler.Step();
            foreach (IFuture f in futures) {
                Assert.IsFalse(f.Completed);
            }

            TestFuture.Complete(10);
            Scheduler.Step();

            foreach (IFuture f in futures) {
                Assert.IsTrue(f.Completed);
                Assert.AreEqual(10, f.Result);
            }
        }

        IEnumerator<object> TaskReturnValueOfYieldedTask () {
            var tr = new RunToCompletion(TaskReturnValueOfFuture());
            yield return tr;
            yield return new Result(tr.Result);
        }

        [Test]
        public void RunToCompletionFailureTest () {
            var rtc = new RunToCompletion(CrashyTask());
            var f = Scheduler.Start(rtc);

            while (!f.Completed)
                Scheduler.Step();

            Assert.AreEqual(false, f.Result);
            Assert.IsTrue(rtc.Future.Error is Exception);
            Assert.AreEqual("pancakes", rtc.Future.Error.Message);
        }

        [Test]
        public void RecursiveYieldTest () {
            this.TestFuture = new Future<object>();
            var future = Scheduler.Start(TaskReturnValueOfYieldedTask());
            Scheduler.Step();
            TestFuture.Complete(10);
            Scheduler.Step();
            Assert.AreEqual(10, future.Result);
        }

        IEnumerator<object> TaskYieldEnumerator (int[] buf) {
            yield return TaskLongLivedWorker(buf);
        }

        [Test]
        public void YieldEnumeratorTest () {
            var buf = new int[1];
            long timeStart = Time.Ticks;
            var f = Scheduler.Start(TaskYieldEnumerator(buf));
            Scheduler.Step();
            long timeEnd = Time.Ticks;
            TimeSpan elapsed = new TimeSpan(timeEnd - timeStart);
            Assert.AreEqual(1000000, buf[0]);
        }

        IEnumerator<object> TaskYieldValue () {
            yield return 5;
        }

        [Test]
        public void YieldValueTest () {
            var f = Scheduler.Start(TaskYieldValue());
            Scheduler.Step();
            try {
                var _ = f.Result;
                Assert.Fail("Did not raise a TaskYieldedValueException");
            } catch (FutureException fe) {
                Assert.IsInstanceOf<TaskYieldedValueException>(fe.InnerException);
            }
        }

        IEnumerator<object> TaskLongLivedWorker (int[] buf) {
            var cf = new Future<object>(null);
            for (int i = 0; i < 1000000; i++) {
                buf[0] = buf[0] + 1;
                yield return cf;
            }
        }

        [Test]
        public void LongLivedWorkerTest () {
            var buf = new int[1];
            long timeStart = Time.Ticks;
            var f = Scheduler.Start(TaskLongLivedWorker(buf));
            Scheduler.Step();
            long timeEnd = Time.Ticks;
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

        [Test]
        public void LotsOfWorkersTest () {
            var buf = new int[1];
            var futures = new List<IFuture>();
            for (int i = 0; i < 15; i++) {
                futures.Add(Scheduler.Start(TaskLongLivedWorker(buf)));
            }
            long timeStart = Time.Ticks;
            Scheduler.Step();
            long timeEnd = Time.Ticks;
            TimeSpan elapsed = new TimeSpan(timeEnd - timeStart);
            Assert.AreEqual(1000000 * 15, buf[0]);
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
            var f = Future.RunInThread(new Func<ValueHolder, int, int>(WorkerThread), buf, numIterations);
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

            var futures = new List<IFuture>();
            for (int i = 0; i < numWorkers; i++) {
                futures.Add(Future.RunInThread(fn, buf, numIterations));
            }

            var f = Future.WaitForAll(futures);
            while (!f.Completed)
                Scheduler.Step();

            Assert.AreEqual(numIterations * numWorkers, buf.Value);
        }

        IEnumerator<object> InfiniteTask () {
            while (true)
                yield return new WaitForNextStep();
        }

        IEnumerator<object> SleepThenReturn5 () {
            yield return new Sleep(0.1);
            yield return new Result(5);
        }

        [Test]
        public void WaitForFutureTest () {
            var f = Scheduler.Start(SleepThenReturn5());

            var result = Scheduler.WaitFor(f, 30);

            Assert.AreEqual(5, result);
        }

        [Test]
        public void SleepTest () {
            int duration = 2;
            int timeScale = 25;
            var f = Scheduler.Start(new Sleep(duration));
            
            long timeStart = Time.Ticks;
            Scheduler.Step();
            f.GetCompletionEvent().Wait();
            long timeEnd = Time.Ticks;

            long elapsed = (long)Math.Round(TimeSpan.FromTicks(timeEnd - timeStart).TotalSeconds * timeScale);
            Assert.AreEqual(duration * timeScale, elapsed);
        }

        [Test]
        public void ParallelSleepTest () {
            int timeScale = 10;
            var a = Scheduler.Start(new Sleep(1));
            var b = Scheduler.Start(new Sleep(2));
            var c = Scheduler.Start(new Sleep(4));
            
            long timeStart = Time.Ticks;
            Scheduler.Step();

            a.GetCompletionEvent().Wait();
            long elapsed = (long)Math.Round(TimeSpan.FromTicks(Time.Ticks - timeStart).TotalSeconds * timeScale);
            Assert.AreEqual(1 * timeScale, elapsed);

            b.GetCompletionEvent().Wait();
            elapsed = (long)Math.Round(TimeSpan.FromTicks(Time.Ticks - timeStart).TotalSeconds * timeScale);
            Assert.AreEqual(2 * timeScale, elapsed);

            c.GetCompletionEvent().Wait();
            elapsed = (long)Math.Round(TimeSpan.FromTicks(Time.Ticks - timeStart).TotalSeconds * timeScale);
            Assert.AreEqual(4 * timeScale, elapsed);
        }

        [Test]
        public void SequentialSleepTest () {
            int timeScale = 10;
            var a = Scheduler.Start(new Sleep(1));

            long timeStart = Time.Ticks;

            Scheduler.Step();
            a.GetCompletionEvent().Wait();

            long elapsed = (long)Math.Round(TimeSpan.FromTicks(Time.Ticks - timeStart).TotalSeconds * timeScale);
            Assert.AreEqual(1 * timeScale, elapsed);

            System.Threading.Thread.Sleep(500);

            var b = Scheduler.Start(new Sleep(2));
            timeStart = Time.Ticks; 

            Scheduler.Step();
            b.GetCompletionEvent().Wait();

            elapsed = (long)Math.Round(TimeSpan.FromTicks(Time.Ticks - timeStart).TotalSeconds * timeScale);
            Assert.AreEqual(2 * timeScale, elapsed);

            System.Threading.Thread.Sleep(500);

            var c = Scheduler.Start(new Sleep(4));
            timeStart = Time.Ticks; 

            Scheduler.Step();
            c.GetCompletionEvent().Wait();

            elapsed = (long)Math.Round(TimeSpan.FromTicks(Time.Ticks - timeStart).TotalSeconds * timeScale);
            Assert.AreEqual(4 * timeScale, elapsed);
        }

        [Test]
        public void SleepUntilTest () {
            int duration = 2;
            int timeScale = 25;
            
            long timeStart = Time.Ticks;
            var f = Scheduler.Start(new SleepUntil(new DateTime(timeStart).AddSeconds(duration).Ticks));

            Scheduler.Step();
            f.GetCompletionEvent().Wait();

            long elapsed = (long)Math.Round(TimeSpan.FromTicks(Time.Ticks - timeStart).TotalSeconds * timeScale);
            Assert.AreEqual(duration * timeScale, elapsed);
        }

        [Test]
        public void WaitForFirstTest () {
            int duration = 2;
            int timeScale = 25;

            var a = Scheduler.Start(InfiniteTask());
            var b = Scheduler.Start(new Sleep(duration));
            var c = Future.WaitForFirst(a, b);

            GC.Collect();

            long timeStart = Time.Ticks;
            c.GetCompletionEvent().Wait();
            Assert.AreEqual(b, c.Result);
            long timeEnd = Time.Ticks;

            long elapsed = (long)Math.Round(TimeSpan.FromTicks(timeEnd - timeStart).TotalSeconds * timeScale);
            Assert.AreEqual(duration * timeScale, elapsed);
        }

        [Test]
        public void WaitForFirstGCTest () {
            var a = Scheduler.Start(InfiniteTask());
            var b = Scheduler.Start(new Sleep(2));
            var c = Future.WaitForFirst(a, b);

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
                Assert.IsInstanceOf<TimeoutException>(ex.InnerException);
            }
        }

        [Test]
        public void DisposingFutureKillsTask () {
            var f = Scheduler.Start(InfiniteTask());

            Scheduler.Step();

            Assert.IsTrue(Scheduler.HasPendingTasks);

            f.Dispose();

            Scheduler.Step();

            Assert.IsFalse(Scheduler.HasPendingTasks);
        }

        [Test]
        public void DisposedTasksDoNotCauseSchedulerToLeakButRunningTasksDo () {
            var f = Scheduler.Start(InfiniteTask());

            Scheduler.Step();

            Assert.IsTrue(Scheduler.HasPendingTasks);

            WeakReference wr = new WeakReference(Scheduler);

            Scheduler.Dispose();
            Scheduler = null;
            GC.Collect();

            Assert.IsTrue(wr.IsAlive);

            f.Dispose();
            f = null;
            GC.Collect();

            Assert.IsFalse(wr.IsAlive);
        }

        // Doesn't pass yet :(
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

        IEnumerator<object> CallsCrashyTask () {
            yield return CrashyTask();
        }

        [Test]
        public void ExceptionDoesntBubbleOutRepeatedly () {
            var f = Scheduler.Start(CallsCrashyTask(), TaskExecutionPolicy.RunAsBackgroundTask);

            Scheduler.Step();
            try {
                Scheduler.Step();
                Assert.Fail("Exception was not raised");
            } catch (TaskException ex) {
                Assert.AreEqual("pancakes", ex.InnerException.Message);
            }
            Scheduler.Step();
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
            var f = Scheduler.Start(InfiniteTask());

            Scheduler.Step();

            Assert.IsTrue(Scheduler.HasPendingTasks);

            f = null;
            GC.Collect();

            Scheduler.Step();

            Assert.IsTrue(Scheduler.HasPendingTasks);
        }

        IEnumerator<object> YieldOnLongSleep (IFuture sleepFuture) {
            yield return sleepFuture;
            yield return new Result("ok");
        }

        [Test]
        public void DisposingTaskFutureDisposesAnyFuturesTaskIsWaitingOn () {
            var sleep = new Sleep(500);
            var sleepFuture = Scheduler.Start(sleep);
            var f = Scheduler.Start(YieldOnLongSleep(sleepFuture));

            Scheduler.Step();

            f.Dispose();

            Scheduler.Step();

            Assert.IsFalse(Scheduler.HasPendingTasks);
            Assert.IsTrue(f.Disposed);
            Assert.IsTrue(sleepFuture.Disposed);
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
            var f = Future.RunInThread(new Action(CrashyWorkerThread));
            while (!f.Completed)
                Scheduler.Step();
            try {
                var _ = f.Result;
                Assert.Fail("Exception was not raised");
            } catch (Exception ex) {
                Assert.AreEqual("maple syrup", ex.InnerException.Message);
            }
        }

        IEnumerator<object> RunChildTaskThreeTimes (Func<IEnumerator<object>> childTask) {
            yield return childTask();
            yield return childTask();
            yield return childTask();
        }

        IEnumerator<object> YieldAndIgnore (IFuture f) {
            yield return f;
            yield return new Result(true);
        }

        IEnumerator<object> YieldAndCheck (IFuture f) {
            yield return f;
            yield return new Result(!f.Failed);
        }

        [Test]
        public void ChildTaskFailuresAbortTasks () {
            var f = Scheduler.Start(RunChildTaskThreeTimes(CrashyTask), TaskExecutionPolicy.RunWhileFutureLives);
            try {
                Scheduler.WaitFor(f, 5);
                throw new Exception("WaitFor did not bubble an exception");
            } catch (FutureException fe) {
                Assert.AreEqual("pancakes", fe.InnerException.Message);
            }
        }

        [Test]
        public void ChildFutureFailuresAbortTasksIfIgnored () {
            var failed = new Future<object>();
            failed.Fail(new Exception("pancakes"));

            var f = Scheduler.Start(YieldAndIgnore(failed), TaskExecutionPolicy.RunWhileFutureLives);
            try {
                Scheduler.WaitFor(f, 5);
                throw new Exception("WaitFor did not bubble an exception");
            } catch (FutureException fe) {
                Assert.AreEqual("pancakes", fe.InnerException.Message);
            }
        }

        [Test]
        public void ChildFutureFailuresDoNotAbortTasksIfHandled () {
            var failed = new Future<object>();
            failed.Fail(new Exception("pancakes"));

            var f = Scheduler.Start(YieldAndCheck(failed), TaskExecutionPolicy.RunWhileFutureLives);
            Assert.AreEqual(false, Scheduler.WaitFor(f, 15));
        }

        IEnumerator<object> UseRunExtension (IEnumerator<object> task, IFuture[] output) {
            IFuture temp;
            yield return task.Run(out temp);
            output[0] = temp;
        }

        [Test]
        public void RunExtensionStoresFuture () {
            IFuture[] f = new IFuture[1];
            var _ = Scheduler.Start(UseRunExtension(CrashyTask(), f), TaskExecutionPolicy.RunWhileFutureLives);
            Scheduler.WaitFor(_, 5);
            Assert.AreEqual("pancakes", f[0].Error.Message);
        }

        IEnumerator<object> BindToChildTask (IEnumerator<object> task, object[] output) {
            object result = null;
            yield return task.Bind(() => result);
            output[0] = result;
        }

        [Test]
        public void BindTaskToVariableStoresResult () {
            object[] r = new object[1];
            var _ = Scheduler.Start(BindToChildTask(TaskReturn5(), r), TaskExecutionPolicy.RunWhileFutureLives);
            Scheduler.WaitFor(_, 5);
            Assert.AreEqual(5, r[0]);
        }

        [Test]
        public void BindTaskToVariableBubblesExceptions () {
            object[] r = new object[1];
            var _ = Scheduler.Start(BindToChildTask(CrashyTask(), r), TaskExecutionPolicy.RunWhileFutureLives);
            try {
                Scheduler.WaitFor(_, 5);
                Assert.Fail("Exception was not raised");
            } catch (FutureException ex) {
                Assert.AreEqual("pancakes", ex.InnerException.Message);
            }
            Assert.AreEqual(null, r[0]);
        }

        IEnumerator<object> RunAsBackgroundTestTask (Future<int> fOut) {
            yield return new RunAsBackground(CrashyTask());

            fOut.SetResult(5, null);
        }

        [Test]
        public void RunAsBackgroundTest () {
            {
                var result = new Future<int>();
                var theTask = Scheduler.Start(RunAsBackgroundTestTask(result));
                try {
                    Scheduler.WaitFor(theTask, 15);
                    Assert.AreEqual(5, result.Result);
                } catch (TaskException) {
                }
            }

            {
                var result = new Future<int>();
                Scheduler.ErrorHandler = (e) => true;
                var theTask = Scheduler.Start(RunAsBackgroundTestTask(result));
                Assert.AreEqual(null, Scheduler.WaitFor(theTask, 15));
                Assert.AreEqual(5, Scheduler.WaitFor(result, 15));
            }
        }

        [Test]
        public void SchedulableWaitForAllTest () {
            var a = TaskYieldValue();
            var b = new WaitForNextStep();
            var c = CrashyTask();

            var f = Scheduler.Start(new WaitForAll(a, b, c));

            Assert.IsFalse(f.Completed);

            Scheduler.Step();

            Assert.IsFalse(f.Completed);

            Scheduler.Step();

            Assert.IsTrue(f.Completed);
            Assert.IsTrue(f.Failed);
            Assert.IsInstanceOf<WaitForAllException>(f.Error);
        }

        private IEnumerator<object> WaitThenReturn (IFuture f) {
            yield return f;

            yield return new Result(5);
        }

        [Test]
        public void DisposingFutureTaskIsWaitingOnWakesTask () {
            var fWaitTarget = Scheduler.Start(InfiniteTask());
            var fWaiter = Scheduler.Start(WaitThenReturn(fWaitTarget));

            Scheduler.Step();

            fWaitTarget.Dispose();

            Scheduler.Step();

            Assert.AreEqual(5, fWaiter.Result);
            Assert.IsFalse(Scheduler.HasPendingTasks);
        }
    }

    [TestFixture]
    public class ThreadedTaskSchedulerTests : BasicJobQueueTests {
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

        [Test]
        public void WaitForWorkItemsTest () {
            ValueHolder vh = new ValueHolder();
            vh.Value = 0;

            long timeStart = Time.Ticks;
            var a = Scheduler.Start(new Sleep(1.5));
            a.RegisterOnComplete((f) => {
                Scheduler.QueueWorkItem(() => {
                    vh.Value = 1;
                });
            });

            Scheduler.Step();
            Scheduler.WaitForWorkItems(3.0);
            long elapsed = Time.Ticks - timeStart;
            Assert.LessOrEqual(elapsed, TimeSpan.FromMilliseconds(1525).Ticks);

            Scheduler.Step();
            Assert.AreEqual(1, vh.Value);
        }

        IEnumerator<object> ServerTask (TcpListener server, int numClients) {
            server.Start();
            try {
                while (numClients > 0) {
                    System.Diagnostics.Debug.WriteLine("Waiting for incoming connection...");
                    var connection = server.AcceptIncomingConnection();
                    yield return connection;
                    System.Diagnostics.Debug.WriteLine("Connection established. Sending data...");
                    using (TcpClient peer = connection.Result as TcpClient) {
                        byte[] bytes = Encoding.ASCII.GetBytes("Hello, world!");
                        var writer = peer.GetStream().AsyncWrite(bytes, 0, bytes.Length);
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

            var b = Future.RunInThread(new Action(() => {
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
            var connect = Network.ConnectTo(server, port);
            yield return connect;
            System.Diagnostics.Debug.WriteLine("Connected. Receiving data...");
            using (TcpClient client = connect.Result as TcpClient) {
                byte[] bytes = new byte[256];
                var reader = client.GetStream().AsyncRead(bytes, 0, bytes.Length);
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
            var clients = new List<IFuture>();
            for (int i = 0; i < numClients; i++) {
                clients.Add(Scheduler.Start(ClientTask(results, "localhost", port)));
            }
            var b = Future.WaitForAll(clients);

            while (!b.Completed)
                Scheduler.Step();

            string helloWorld = "Hello, world!";
            var expectedResults = new List<string>();
            for (int i = 0; i < numClients; i++)
                expectedResults.Add(helloWorld);
            Assert.AreEqual(expectedResults.ToArray(), results.ToArray());
        }
    }

    [TestFixture]
    public class WindowsMessageBasedTaskSchedulerTests : BasicJobQueueTests {
        [SetUp]
        public void SetUp () {
            Scheduler = new TaskScheduler(JobQueue.WindowsMessageBased);
        }

        [TearDown]
        public void TearDown () {
            if (Scheduler != null)
                Scheduler.Dispose();
            Scheduler = null;
            GC.Collect();
        }

        [Test]
        public void PumpingWindowsMessagesIsEquivalentToStep () {
            var future = Scheduler.Start(TaskReturn5());
            Application.DoEvents();
            Assert.AreEqual(5, future.Result);
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
            scheduler.Start(this.Run(), TaskExecutionPolicy.RunAsBackgroundTask);
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
        int[] _Counter;

        public SleepyEntity (int seed, List<SleepyEntity> entities, int[] counter)
            : base(seed, entities) {
            _Counter = counter;
        }

        protected override object GetWaiter () {
            Interlocked.Increment(ref _Counter[0]);
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
            int state = 30;
            while (true) {
                NumSteps += 1;
                if (state <= 0)
                    yield break;

                ChattyEntity sibling = GetRandomSibling();
                sibling.Tell(state - 1);
                yield return new WaitForNextStep();

                IFuture response = _Messages.Dequeue();
                using (response)
                    yield return response;

                if (response.Disposed)
                    yield break;
                state = (int)response.Result;
            }
        }

        public override void Start (TaskScheduler scheduler) {
            scheduler.Start(this.Talk(), TaskExecutionPolicy.RunAsBackgroundTask);
        }
    }

    [TestFixture]
    public class FunctionalTests {
        TaskScheduler Scheduler;

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

        [Test]
        public void TestLotsOfEntities () {
            int numEntities = 10000;
            int numSteps = 500;

            var entities = new List<SimpleEntity>();
            for (int i = 0; i < numEntities; i++)
                entities.Add(new SimpleEntity(i, entities));

            foreach (var e in entities)
                e.Start(Scheduler);

            long timeStart = Time.Ticks;

            for (int j = 0; j < numSteps; j++) {
                Scheduler.Step();
            }

            long timeEnd = Time.Ticks;
            TimeSpan elapsed = new TimeSpan(timeEnd - timeStart);

            foreach (var e in entities)
                Assert.AreEqual(numSteps, e.NumSteps);

            Console.WriteLine("Took {0:N2} secs for {1} iterations. {2:N1} entities/sec", elapsed.TotalSeconds, numSteps, numEntities * numSteps / elapsed.TotalSeconds);
        }

        [Test]
        public void TestLotsOfSleepyEntities () {
            int numEntities = 1500;
            int numIterations = 200 * numEntities;
            int[] iterationCounter = new int[1];

            var entities = new List<SleepyEntity>();
            for (int i = 0; i < numEntities; i++)
                entities.Add(new SleepyEntity(i, entities, iterationCounter));

            foreach (var e in entities)
                e.Start(Scheduler);

            int j = 0;

            long timeStart = Time.Ticks;
            while (iterationCounter[0] < numIterations) {
                Scheduler.WaitForWorkItems(1.0);
                Scheduler.Step();
                j += 1;
            }
            long timeEnd = Time.Ticks;
            long elapsed = (timeEnd - timeStart);

            int totalEntitySteps = 0;
            foreach (var e in entities)
                totalEntitySteps += e.NumSteps;

            double elapsedSeconds = TimeSpan.FromTicks(elapsed).TotalSeconds;

            Console.WriteLine("{0} iterations in {1:N2} secs. {2} entity steps @ {3} entities/sec", j, elapsedSeconds, totalEntitySteps, totalEntitySteps / elapsedSeconds);
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

            long timeStart = Time.Ticks;

            while (Scheduler.HasPendingTasks) {
                numSteps += 1;
                Scheduler.Step();
            }

            long timeEnd = Time.Ticks;
            TimeSpan elapsed = new TimeSpan(timeEnd - timeStart);

            int numEntitySteps = 0;
            foreach (var e in entities)
                numEntitySteps += e.NumSteps;

            Console.WriteLine("Took {0:N2} secs for {1} iterations. {2:N1} entities/sec", elapsed.TotalSeconds, numSteps, numEntitySteps / elapsed.TotalSeconds);
        }
    }
}
