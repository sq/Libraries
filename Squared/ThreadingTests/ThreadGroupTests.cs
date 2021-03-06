﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Squared.Util;
using NUnit.Framework;

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

    [TestFixture]
    public class ThreadGroupTests {
        [Test]
        public void MinimumThreadCount () {
            using (var group = new ThreadGroup(
                minimumThreads: 2
            )) {
                Assert.GreaterOrEqual(group.Count, 2);
            }
        }

        [Test]
        public void ManuallyStep () {
            using (var group = new ThreadGroup(0, 0)) {
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
            using (var group = new ThreadGroup(0, 0)) {
                var queue = group.GetQueueForType<TestWorkItem>();

                var item = new TestWorkItem();
                Assert.IsFalse(item.Ran);

                queue.Enqueue(item);

                Assert.AreEqual(0, group.Count);
                Assert.IsFalse(item.Ran);

                group.ForciblySpawnThread();
                Assert.AreEqual(1, group.Count);

                queue.WaitUntilDrained();

                Assert.IsTrue(item.Ran);
            }
        }

        [Test]
        public void AutoSpawnThread () {
            using (var group = new ThreadGroup(0, 1)) {
                var queue = group.GetQueueForType<TestWorkItem>();

                var item = new TestWorkItem();
                Assert.IsFalse(item.Ran);

                queue.Enqueue(item);

                Assert.AreEqual(0, group.Count);
                Assert.IsFalse(item.Ran);

                group.NotifyQueuesChanged();
                Assert.AreEqual(1, group.Count);

                queue.WaitUntilDrained();

                Assert.IsTrue(item.Ran);
            }
        }

        [Test]
        public void AutoSpawnMoreThreads () {
            using (var group = new ThreadGroup(0, 2)) {
                var queue = group.GetQueueForType<SleepyWorkItem>();

                queue.Enqueue(new SleepyWorkItem());
                group.NotifyQueuesChanged();

                Assert.GreaterOrEqual(1, group.Count);

                queue.Enqueue(new SleepyWorkItem());
                group.NotifyQueuesChanged();

                Assert.GreaterOrEqual(2, group.Count);

                queue.WaitUntilDrained();
            }
        }

        [Test]
        public void SingleThreadPerformanceTest () {
            const int count = 500000;

            var timeProvider = Time.DefaultTimeProvider;
            using (var group = new ThreadGroup(1, 1)) {
                var queue = group.GetQueueForType<VoidWorkItem>();

                var item = new VoidWorkItem();

                var beforeEnqueue = timeProvider.Ticks;
                for (int i = 0; i < count; i++)
                    queue.Enqueue(ref item);

                var afterEnqueue = timeProvider.Ticks;

                var beforeWait = timeProvider.Ticks;
                queue.WaitUntilDrained();

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
            using (var group = new ThreadGroup(1)) {
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
                queue.WaitUntilDrained();

                var afterWait = timeProvider.Ticks;

                Console.WriteLine(
                    "Enqueue took {0:0000.00}ms, Wait took {1:0000.00}ms. Final thread count: {2}",
                    TimeSpan.FromTicks(afterEnqueue - beforeEnqueue).TotalMilliseconds,
                    TimeSpan.FromTicks(afterWait - beforeWait).TotalMilliseconds,
                    group.Count
                );
            }
        }
    }
}
