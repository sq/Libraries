using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
                queue.Step(1);

                Assert.IsTrue(item.Ran);
            }
        }

        [Test]
        public void WaitForMarker () {
            using (var group = new ThreadGroup(0, 0)) {
                var queue = group.GetQueueForType<TestWorkItem>();

                var item = new TestWorkItem();
                Assert.IsFalse(item.Ran);

                var marker = queue.Mark();
                queue.Enqueue(item);

                Assert.IsFalse(item.Ran);

                System.Threading.ThreadPool.QueueUserWorkItem((_) => {
                    System.Threading.Thread.Sleep(250);
                    while (queue.Step(1) > 0)
                        ;
                });

                marker.Wait(1);

                Assert.IsTrue(item.Ran);
            }
        }

        [Test]
        public void ForciblySpawnThread () {
            using (var group = new ThreadGroup(0, 0)) {
                var queue = group.GetQueueForType<TestWorkItem>();

                var item = new TestWorkItem();
                Assert.IsFalse(item.Ran);

                var marker = queue.Mark();
                queue.Enqueue(item);

                Assert.AreEqual(0, group.Count);
                Assert.IsFalse(item.Ran);

                group.ForciblySpawnThread();
                Assert.AreEqual(1, group.Count);

                marker.Wait(1);

                Assert.IsTrue(item.Ran);
            }
        }

        [Test]
        public void AutoSpawnThread () {
            using (var group = new ThreadGroup(0, 1)) {
                var queue = group.GetQueueForType<TestWorkItem>();

                var item = new TestWorkItem();
                Assert.IsFalse(item.Ran);

                var marker = queue.Mark();
                queue.Enqueue(item);

                Assert.AreEqual(0, group.Count);
                Assert.IsFalse(item.Ran);

                group.ConsiderNewThread(true);
                Assert.AreEqual(1, group.Count);

                marker.Wait(1);

                Assert.IsTrue(item.Ran);
            }
        }

        [Test]
        public void AutoSpawnMoreThreads () {
            using (var group = new ThreadGroup(0, 2)) {
                var queue = group.GetQueueForType<SleepyWorkItem>();

                var marker = queue.Mark();
                queue.Enqueue(new SleepyWorkItem());
                group.ConsiderNewThread(true);

                Assert.GreaterOrEqual(1, group.Count);

                queue.Enqueue(new SleepyWorkItem());
                group.ConsiderNewThread(true);

                Assert.GreaterOrEqual(2, group.Count);

                marker.Wait(2);
            }
        }
    }
}
