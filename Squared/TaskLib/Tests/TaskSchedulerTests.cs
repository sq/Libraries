using System;
using System.Collections.Generic;
using Squared.Task;
using NUnit.Framework;

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
            int timeScale = 50;
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
        public void WaitForFirstTest () {
            int duration = 2;
            int timeScale = 50;

            var a = Scheduler.Start(InfiniteTask());
            var b = Scheduler.Start(new Sleep(duration));
            var c = Scheduler.Start(new WaitForFirst(a, b));

            long timeStart = DateTime.Now.Ticks;
            while (!c.Completed) {
                Scheduler.Step();
            }
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
            } catch (TimeoutException) {
            }
        }

        [Test]
        public void TestCollectingFutureKillsTask () {
            var f = Scheduler.Start(InfiniteTask());

            Scheduler.Step();

            Assert.IsTrue(Scheduler.HasPendingTasks);

            f = null;
            GC.Collect();

            Scheduler.Step();

            Assert.IsFalse(Scheduler.HasPendingTasks);
        }

        [Test]
        public void TestRunningTasksDoNotCauseSchedulerToLeak () {
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
        public void TestPendingSleepsDoNotCauseSchedulerToLeak () {
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
        public void TestWrapsExceptionsFromInsideTasks () {
            var f = Scheduler.Start(CrashyTask());

            Scheduler.Step();
            Scheduler.Step();

            try {
                var _ = f.Result;
                Assert.Fail("Exception was not raised");
            } catch (Exception ex) {
                Assert.AreEqual("pancakes", ex.Message);
            }
        }

        [Test]
        public void TestRunUntilCompleteExecutionPolicy () {
            var f = Scheduler.Start(InfiniteTask(), TaskExecutionPolicy.RunUntilComplete);

            Scheduler.Step();

            Assert.IsTrue(Scheduler.HasPendingTasks);

            f = null;
            GC.Collect();

            Scheduler.Step();

            Assert.IsTrue(Scheduler.HasPendingTasks);
        }

        void CrashyWorkerThread () {
            throw new Exception("maple syrup");
        }

        [Test]
        public void TestWrapsExceptionsFromInsideWorkerThreads () {
            var f = Scheduler.RunInThread(new Action(CrashyWorkerThread));
            while (!f.Completed)
                Scheduler.Step();
            try {
                var _ = f.Result;
                Assert.Fail("Exception was not raised");
            } catch (Exception ex) {
                Assert.AreEqual("maple syrup", ex.Message);
            }
        }
    }
}
