using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Squared.Util;

namespace Squared.Util {
    [TestFixture]
    public class PriorityQueueTests {
        [Test]
        public void BasicTest () {
            var pq = new PriorityQueue<int>();
            pq.Enqueue(5);
            pq.Enqueue(3);
            pq.Enqueue(10);
            pq.Enqueue(1);
            Assert.AreEqual(1, pq.Dequeue());
            Assert.AreEqual(3, pq.Dequeue());
            Assert.AreEqual(5, pq.Dequeue());
            Assert.AreEqual(10, pq.Dequeue());
        }

        [Test]
        public void ConstructFromSequence () {
            var pq = new PriorityQueue<int>(new int[] { 1, 4, 2, 10, -10 });
            Assert.AreEqual(-10, pq.Dequeue());
            Assert.AreEqual(1, pq.Dequeue());
            Assert.AreEqual(2, pq.Dequeue());
            Assert.AreEqual(4, pq.Dequeue());
            Assert.AreEqual(10, pq.Dequeue());
        }
    }
}
