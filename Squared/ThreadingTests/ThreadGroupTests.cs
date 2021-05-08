using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Squared.Util;
using NUnit.Framework;
using System.Threading;

namespace Squared.Threading {
    public class TestWorkItem : IWorkItem {
        public bool Ran;

        public void Execute () {
            Ran = true;
        }
    }

    public class SleepyWorkItem : IWorkItem {
        public bool Ran;

        public void Execute () {
            System.Threading.Thread.Sleep(200);
            Ran = true;
        }
    }

    public struct VoidWorkItem : IWorkItem {
        public void Execute () {
        }
    }

    public struct BlockingWorkItem : IWorkItem {
        public AutoResetEvent Signal;

        public void Execute () {
            Signal?.WaitOne();
        }
    }

    [TestFixture]
    public class ThreadGroupTests {
        [Test]
        public void MinimumThreadCount () {
            using (var group = new ThreadGroup(
                minimumThreads: 2, createBackgroundThreads: true, name: "MinimumThreadCount"
            )) {
                Assert.GreaterOrEqual(group.Count, 2);
            }
        }

        [Test]
        public void ManuallyStep () {
            using (var group = new ThreadGroup(0, 0, createBackgroundThreads: true, name: "ManuallyStep")) {
                var queue = group.GetQueueForType<TestWorkItem>();

                var item = new TestWorkItem();
                Assert.IsFalse(item.Ran);

                queue.Enqueue(item);

                bool exhausted;
                queue.Step(out exhausted, 1);

                Assert.IsTrue(exhausted);
                Assert.IsTrue(item.Ran);
            }
        }

        [Test]
        public void ForciblySpawnThread () {
            using (var group = new ThreadGroup(0, 0, createBackgroundThreads: true, name: "ForciblySpawnThread")) {
                var queue = group.GetQueueForType<TestWorkItem>();

                var item = new TestWorkItem();
                Assert.IsFalse(item.Ran);

                queue.Enqueue(item);

                Assert.AreEqual(0, group.Count);
                Assert.IsFalse(item.Ran);

                group.ForciblySpawnThread();
                Assert.AreEqual(1, group.Count);

                queue.WaitUntilDrained(5000);

                Assert.IsTrue(item.Ran);
            }
        }

        [Test]
        public void AutoSpawnThread () {
            using (var group = new ThreadGroup(0, 1, createBackgroundThreads: true, name: "AutoSpawnThread")) {
                var queue = group.GetQueueForType<TestWorkItem>();

                var item = new TestWorkItem();
                Assert.IsFalse(item.Ran);

                queue.Enqueue(item);

                Assert.AreEqual(0, group.Count);
                Assert.IsFalse(item.Ran, "!item.Ran");

                group.NotifyQueuesChanged();
                Assert.AreEqual(1, group.Count);

                queue.WaitUntilDrained(5000);

                Assert.IsTrue(item.Ran, "item.Ran");
            }
        }

        [Test]
        public void AutoSpawnMoreThreads () {
            using (var group = new ThreadGroup(0, 2, createBackgroundThreads: true, name: "AutoSpawnMoreThreads")) {
                var queue = group.GetQueueForType<SleepyWorkItem>();

                queue.Enqueue(new SleepyWorkItem());
                group.NotifyQueuesChanged();

                Assert.GreaterOrEqual(1, group.Count);

                queue.Enqueue(new SleepyWorkItem());
                group.NotifyQueuesChanged();

                Assert.GreaterOrEqual(2, group.Count);

                queue.WaitUntilDrained(5000);
            }
        }

        [Test]
        public void SingleThreadPerformanceTest () {
            const int count = 500000;

            var timeProvider = Time.DefaultTimeProvider;
            using (var group = new ThreadGroup(1, 1, createBackgroundThreads: true, name: "SingleThreadPerformanceTest")) {
                var queue = group.GetQueueForType<VoidWorkItem>();

                var item = new VoidWorkItem();

                var beforeEnqueue = timeProvider.Ticks;
                for (int i = 0; i < count; i++)
                    queue.Enqueue(ref item);

                var afterEnqueue = timeProvider.Ticks;

                var beforeWait = timeProvider.Ticks;
                queue.WaitUntilDrained(5000);

                var afterWait = timeProvider.Ticks;

                Console.WriteLine(
                    "Enqueue took {0:0000.00}ms, Wait took {1:0000.00}ms. Final thread count: {2}",
                    TimeSpan.FromTicks(afterEnqueue - beforeEnqueue).TotalMilliseconds,
                    TimeSpan.FromTicks(afterWait - beforeWait).TotalMilliseconds,
                    group.Count
                );
            }
        }

        [Test]
        public void MultipleThreadPerformanceTest () {
            const int count = 500000;

            var timeProvider = Time.DefaultTimeProvider;
            using (var group = new ThreadGroup(1, createBackgroundThreads: true, name: "MultipleThreadPerformanceTest")) {
                var queue = group.GetQueueForType<VoidWorkItem>();

                var item = new VoidWorkItem();

                var beforeEnqueue = timeProvider.Ticks;
                for (int i = 0; i < count; i++) {
                    queue.Enqueue(ref item);

                    // Notify the group periodically that we've added new work items.
                    // This ensures it spins up a reasonable number of threads.
                    if ((i % 20000) == 0)
                        group.NotifyQueuesChanged();
                }
                var afterEnqueue = timeProvider.Ticks;

                var beforeWait = timeProvider.Ticks;
                queue.WaitUntilDrained(5000);

                var afterWait = timeProvider.Ticks;

                Console.WriteLine(
                    "Enqueue took {0:0000.00}ms, Wait took {1:0000.00}ms. Final thread count: {2}",
                    TimeSpan.FromTicks(afterEnqueue - beforeEnqueue).TotalMilliseconds,
                    TimeSpan.FromTicks(afterWait - beforeWait).TotalMilliseconds,
                    group.Count
                );
            }
        }

        [Test]
        public void WaitUntilDrainedSplitsQueue () {
            const int count = 50;
            Task drain1, drain2;

            using (var group = new ThreadGroup(1, createBackgroundThreads: true, name: "WaitUntilDrainedSplitsQueue")) {
                var queue = group.GetQueueForType<BlockingWorkItem>();
                var item = new BlockingWorkItem();
                for (int i = 0; i < count; i++)
                    queue.Enqueue(ref item);
                drain1 = queue.WaitUntilDrainedAsync();
                var barrier = new BlockingWorkItem {
                    Signal = new AutoResetEvent(false)
                };
                queue.Enqueue(ref barrier);
                group.NotifyQueuesChanged();

                for (int i = 0; i < count; i++)
                    queue.Enqueue(ref item);
                drain2 = queue.WaitUntilDrainedAsync();
                group.NotifyQueuesChanged();

                Assert.True(drain1.Wait(5000));

                barrier.Signal.Set();
                Assert.True(drain2.Wait(5000));

                ;
            }
        }
    }
}
