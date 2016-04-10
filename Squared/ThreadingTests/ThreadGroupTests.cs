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
            using (var group = new ThreadGroup(minimumThreads: 0)) {
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
            using (var group = new ThreadGroup(minimumThreads: 0)) {
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
    }
}
