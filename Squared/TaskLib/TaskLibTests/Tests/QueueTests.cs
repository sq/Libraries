using System;
using System.Collections.Generic;
using Squared.Task;
using NUnit.Framework;

namespace Squared.Task {
    [TestFixture]
    public class QueueTests {
        BlockingQueue<string> Queue;

        [SetUp]
        public void SetUp () {
            Queue = new BlockingQueue<string>();
        }

        [Test]
        public void QueueReturnsIncompleteFutureIfEmpty () {
            var f = Queue.Dequeue();
            Assert.IsFalse(f.Completed);
        }

        [Test]
        public void QueueCompletesFirstWaitingFutureWhenAValueIsAdded () {
            var f = Queue.Dequeue();
            Queue.Enqueue("a");
            Assert.AreEqual("a", f.Result);
        }

        [Test]
        public void QueueReturnsCompletedFutureIfItContainsValues () {
            Queue.Enqueue("a");
            var f = Queue.Dequeue();
            Assert.AreEqual("a", f.Result);
        }

        [Test]
        public void QueueCompletesWaitingFuturesInOrder () {
            var a = Queue.Dequeue();
            var b = Queue.Dequeue();
            var c = Queue.Dequeue();
            Queue.Enqueue("a");
            Assert.AreEqual("a", a.Result);
            Queue.Enqueue("b");
            Assert.AreEqual("b", b.Result);
            Queue.Enqueue("c");
            Assert.AreEqual("c", c.Result);
        }

        [Test]
        public void QueueCountIsNegativeIfFuturesWaitingAndPositiveIfValuesInQueue () {
            var a = Queue.Dequeue();
            var b = Queue.Dequeue();
            var c = Queue.Dequeue();
            Assert.AreEqual(-3, Queue.Count);
            for (int i = 0; i < 6; i++)
                Queue.Enqueue(i.ToString());
            Assert.AreEqual(3, Queue.Count);
        }

        [Test]
        public void QueueDoesNotStoreEnqueuedValuesIntoDisposedWaitingFutures () {
            var a = Queue.Dequeue();
            var b = Queue.Dequeue();

            a.Dispose();
            Queue.Enqueue("test");

            Assert.IsFalse(a.Completed);
            Assert.IsTrue(a.Disposed);
            Assert.AreEqual("test", b.Result);
            Assert.AreEqual(0, Queue.Count);
        }
    }
}
