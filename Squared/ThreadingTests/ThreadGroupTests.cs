﻿using System;
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

        public void Execute (ThreadGroup group) {
            Ran = true;
        }
    }

    public class SleepyWorkItem : IWorkItem {
        public bool Ran;

        public void Execute (ThreadGroup group) {
            Thread.Sleep(200);
            Ran = true;
        }
    }

    public struct VoidWorkItem : IWorkItem {
        public void Execute (ThreadGroup group) {
        }
    }

    public struct SlightlySlowWorkItem : IWorkItem {
        public void Execute (ThreadGroup group) {
            Thread.Sleep(1);
        }
    }

    public struct BlockingWorkItem : IWorkItem {
        public AutoResetEvent Signal;

        public void Execute (ThreadGroup group) {
            Signal?.WaitOne();
        }
    }

    public struct HighPriorityBlockingWorkItem : IWorkItem {
        public static WorkItemConfiguration Configuration =>
            new WorkItemConfiguration {
                Priority = 1
            };

        public AutoResetEvent Signal;

        public void Execute (ThreadGroup group) {
            Signal?.WaitOne();
        }
    }

    [TestFixture]
    public class ThreadGroupTests {
        [Test]
        public void MinimumThreadCount () {
            using (var group = new ThreadGroup(
                threadCount: 2, createBackgroundThreads: true, name: "MinimumThreadCount"
            ) { DefaultConcurrencyPadding = 0 }) {
                Assert.GreaterOrEqual(group.Count, 2);
            }
        }

        [Test]
        public void ManuallyStep () {
            using (var group = new ThreadGroup(0, createBackgroundThreads: true, name: "ManuallyStep") { DefaultConcurrencyPadding = 0 }) {
                var queue = group.GetQueueForType<TestWorkItem>();

                var item = new TestWorkItem();
                Assert.IsFalse(item.Ran);

                queue.Enqueue(item);

                bool exhausted;
                queue.Step(out exhausted, 1);
                Assert.IsTrue(item.Ran);

                queue.Step(out exhausted, 1);
                Assert.IsTrue(exhausted);
            }
        }

        [Test]
        public void SingleThreadPerformanceTest () {
            const int count = 200;

            var timeProvider = Time.DefaultTimeProvider;
            using (var group = new ThreadGroup(1, createBackgroundThreads: true, name: "SingleThreadPerformanceTest") { DefaultConcurrencyPadding = 0 }) {
                var queue = group.GetQueueForType<SlightlySlowWorkItem>();

                var item = new SlightlySlowWorkItem();

                var beforeEnqueue = timeProvider.Ticks;
                for (int i = 0; i < count; i++)
                    queue.Enqueue(ref item, notifyChanged: false);

                var afterEnqueue = timeProvider.Ticks;

                group.NotifyQueuesChanged();

                var beforeWait = timeProvider.Ticks;
                queue.WaitUntilDrained(5000);

                var afterWait = timeProvider.Ticks;
                var perItem = (afterWait - beforeWait) / (double)count / Time.MillisecondInTicks;

                Console.WriteLine(
                    "Enqueue took {0:0000.00}ms, Wait took {1:0000.00}ms. Est speed {3:0.000000}ms/item Final thread count: {2}",
                    TimeSpan.FromTicks(afterEnqueue - beforeEnqueue).TotalMilliseconds,
                    TimeSpan.FromTicks(afterWait - beforeWait).TotalMilliseconds,
                    group.Count, perItem
                );
            }
        }

        [Test]
        public void MainThreadPerformanceTest () {
            const int count = 200;

            var timeProvider = Time.DefaultTimeProvider;
            using (var group = new ThreadGroup(1, createBackgroundThreads: true, name: "MainThreadPerformanceTest") { DefaultConcurrencyPadding = 0 }) {
                var queue = group.GetQueueForType<SlightlySlowWorkItem>(forMainThread: true);

                var item = new SlightlySlowWorkItem();

                var beforeEnqueue = timeProvider.Ticks;
                for (int i = 0; i < count; i++)
                    queue.Enqueue(ref item, notifyChanged: false);

                var afterEnqueue = timeProvider.Ticks;

                var beforeWait = timeProvider.Ticks;
                while (!queue.IsDrained)
                    group.StepMainThread();
                var afterWait = timeProvider.Ticks;
                var perItem = (afterWait - beforeWait) / (double)count / Time.MillisecondInTicks;

                Console.WriteLine(
                    "Enqueue took {0:0000.00}ms, Wait took {1:0000.00}ms. Est speed {3:0.000000}ms/item Final thread count: {2}",
                    TimeSpan.FromTicks(afterEnqueue - beforeEnqueue).TotalMilliseconds,
                    TimeSpan.FromTicks(afterWait - beforeWait).TotalMilliseconds,
                    group.Count, perItem
                );
            }
        }

        [Test]
        public void MultipleThreadPerformanceTest () {
            const int count = 800;

            var timeProvider = Time.DefaultTimeProvider;
            using (var group = new ThreadGroup(4, createBackgroundThreads: true, name: "MultipleThreadPerformanceTest") { DefaultConcurrencyPadding = 0 }) {
                var queue = group.GetQueueForType<SlightlySlowWorkItem>();

                var item = new SlightlySlowWorkItem();

                var beforeEnqueue = timeProvider.Ticks;
                for (int i = 0; i < count; i++) {
                    queue.Enqueue(ref item, notifyChanged: false);

                    // Notify the group periodically that we've added new work items.
                    // This ensures it spins up a reasonable number of threads.
                    if ((i % 500) == 0)
                        group.NotifyQueuesChanged();
                }
                var afterEnqueue = timeProvider.Ticks;

                var beforeWait = timeProvider.Ticks;
                queue.WaitUntilDrained(10000);

                var afterWait = timeProvider.Ticks;
                var perItem = (afterWait - beforeWait) / (double)count / Time.MillisecondInTicks;

                Console.WriteLine(
                    "Enqueue took {0:0000.00}ms, Wait took {1:0000.00}ms. Est speed {3:0.000000}ms/item Final thread count: {2}",
                    TimeSpan.FromTicks(afterEnqueue - beforeEnqueue).TotalMilliseconds,
                    TimeSpan.FromTicks(afterWait - beforeWait).TotalMilliseconds,
                    group.Count, perItem
                );
            }
        }

        [Test]
        public void NotifyOverheadTest () {
            const int count = 800;

            var timeProvider = Time.DefaultTimeProvider;
            using (var group = new ThreadGroup(4, createBackgroundThreads: true, name: "NotifyOverheadTest") { DefaultConcurrencyPadding = 0 }) {
                var queue = group.GetQueueForType<SlightlySlowWorkItem>();

                var item = new SlightlySlowWorkItem();

                var beforeEnqueue = timeProvider.Ticks;
                for (int i = 0; i < count; i++)
                    queue.Enqueue(ref item, notifyChanged: true);
                var afterEnqueue = timeProvider.Ticks;

                var beforeWait = timeProvider.Ticks;
                queue.WaitUntilDrained(10000);

                var afterWait = timeProvider.Ticks;
                var perItem = (afterWait - beforeWait) / (double)count / Time.MillisecondInTicks;

                Console.WriteLine(
                    "Enqueue took {0:0000.00}ms, Wait took {1:0000.00}ms. Est speed {3:0.000000}ms/item Final thread count: {2}",
                    TimeSpan.FromTicks(afterEnqueue - beforeEnqueue).TotalMilliseconds,
                    TimeSpan.FromTicks(afterWait - beforeWait).TotalMilliseconds,
                    group.Count, perItem
                );
            }
        }

        [Test]
        public void WaitUntilDrained () {
            const int count = 50;

            using (var group = new ThreadGroup(1, createBackgroundThreads: true, name: "WaitUntilDrainedSplitsQueue") { DefaultConcurrencyPadding = 0 }) {
                var queue = group.GetQueueForType<BlockingWorkItem>();
                var item = new BlockingWorkItem();
                for (int i = 0; i < count; i++)
                    queue.Enqueue(ref item, notifyChanged: false);
                group.NotifyQueuesChanged();
                var barrier = new BlockingWorkItem {
                    Signal = new AutoResetEvent(false)
                };
                queue.Enqueue(ref barrier, notifyChanged: false);
                Assert.IsFalse(queue.WaitUntilDrained(5), "waitUntilDrained 1");
                group.NotifyQueuesChanged();
                Assert.IsFalse(queue.WaitUntilDrained(5), "waitUntilDrained 2");
                barrier.Signal.Set();
                Assert.IsTrue(queue.WaitUntilDrained(500), "waitUntilDrained 3");
                Assert.IsTrue(queue.WaitUntilDrained(5), "waitUntilDrained 4");
            }
        }

        [Test]
        public void PriorityTest () {
            using (var group = new ThreadGroup(2, createBackgroundThreads: true, name: "PriorityTest") { DefaultConcurrencyPadding = 0 }) {
                var queue = group.GetQueueForType<HighPriorityBlockingWorkItem>();
                var barrier1 = new HighPriorityBlockingWorkItem {
                    Signal = new AutoResetEvent(false)
                };
                queue.Enqueue(barrier1);
                var barrier2 = new HighPriorityBlockingWorkItem {
                    Signal = new AutoResetEvent(false)
                };
                queue.Enqueue(barrier2);
                var barrier3 = new HighPriorityBlockingWorkItem {
                    Signal = new AutoResetEvent(false)
                };
                queue.Enqueue(barrier3);
                group.NotifyQueuesChanged();

                // HACK: Give the thread group time to start processing high priority items
                while (queue.ItemsInFlight < group.ThreadCount)
                    Thread.Sleep(1);

                var voidQueue = group.GetQueueForType<VoidWorkItem>();
                var testItem = new VoidWorkItem();
                for (int i = 0; i < 10; i++)
                    voidQueue.Enqueue(testItem);
                group.NotifyQueuesChanged();

                var ok = voidQueue.WaitUntilDrained(50);
                Assert.IsFalse(ok, "wait for low priority queue to drain while threads are blocked on high priority");
                barrier1.Signal.Set();

                Assert.IsFalse(voidQueue.WaitUntilDrained(50), "ensure new high priority item is claimed by thread that finished previous one");
                barrier2.Signal.Set();
                barrier3.Signal.Set();

                ok = voidQueue.WaitUntilDrained(500);
                Assert.IsTrue(ok, "once high priority items are done the low priority ones should run");
            }
        }

        [Test]
        public void PaddingTest () {
            using (var group = new ThreadGroup(2, createBackgroundThreads: true, name: "PaddingTest") { DefaultConcurrencyPadding = 1 }) {
                var queue = group.GetQueueForType<HighPriorityBlockingWorkItem>();
                var barrier1 = new HighPriorityBlockingWorkItem {
                    Signal = new AutoResetEvent(false)
                };
                queue.Enqueue(barrier1);
                var barrier2 = new HighPriorityBlockingWorkItem {
                    Signal = new AutoResetEvent(false)
                };
                queue.Enqueue(barrier2);
                group.NotifyQueuesChanged();

                Thread.Sleep(300);
                Assert.AreEqual(1, queue.ItemsInFlight);

                barrier1.Signal.Set();
                Assert.IsFalse(queue.WaitUntilDrained(150));
                Assert.AreEqual(1, queue.ItemsInFlight);

                barrier2.Signal.Set();
                Assert.IsTrue(queue.WaitUntilDrained(150));
            }
        }
    }
}
