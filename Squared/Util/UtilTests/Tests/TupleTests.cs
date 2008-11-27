using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Squared.Util;

namespace Squared.Util {
    [TestFixture]
    public class PairTests {
        [Test]
        public void CompareToTest () {
            var A = new Pair<float>(1.0f, 2.0f);
            var B = new Pair<float>(2.0f, 1.0f);
            var C = new Pair<float>(2.0f, 2.0f);

            Assert.AreEqual(0, A.CompareTo(A));
            Assert.AreEqual(-1, A.CompareTo(B));
            Assert.AreEqual(-1, B.CompareTo(C));
            Assert.AreEqual(-1, A.CompareTo(C));
            Assert.AreEqual(1, C.CompareTo(B));
            Assert.AreEqual(1, C.CompareTo(A));
            Assert.AreEqual(1, B.CompareTo(A));
        }
    }

    [TestFixture]
    public class TripletTests {
        [Test]
        public void CompareToTest () {
            var A = new Triplet<float>(1.0f, 2.0f, 1.0f);
            var B = new Triplet<float>(2.0f, 1.0f, 1.0f);
            var C = new Triplet<float>(2.0f, 2.0f, 2.0f);

            Assert.AreEqual(0, A.CompareTo(A));
            Assert.AreEqual(-1, A.CompareTo(B));
            Assert.AreEqual(-1, B.CompareTo(C));
            Assert.AreEqual(-1, A.CompareTo(C));
            Assert.AreEqual(1, C.CompareTo(B));
            Assert.AreEqual(1, C.CompareTo(A));
            Assert.AreEqual(1, B.CompareTo(A));
        }
    }

    [TestFixture]
    public class IntervalTests {
        [Test]
        public void IntersectsTest () {
            var A = new Interval<float>(0.0f, 5.0f);
            var B = new Interval<float>(2.5f, 7.5f);
            var C = new Interval<float>(6.0f, 10.0f);

            Assert.IsTrue(A.Intersects(B));
            Assert.IsTrue(B.Intersects(C));
            Assert.IsFalse(A.Intersects(C));
            Assert.IsTrue(B.Intersects(A));
            Assert.IsTrue(C.Intersects(B));
            Assert.IsFalse(C.Intersects(A));
            Assert.IsTrue(A.Intersects(A));
            Assert.IsTrue(B.Intersects(B));
            Assert.IsTrue(C.Intersects(C));
        }

        [Test]
        public void GetIntersectionTest () {
            var A = new Interval<float>(0.0f, 5.0f);
            var B = new Interval<float>(2.5f, 7.5f);
            var C = new Interval<float>(6.0f, 10.0f);

            Interval<float> Temp;

            Assert.IsTrue(A.GetIntersection(B, out Temp));
            Assert.AreEqual(new float[] { 2.5f, 5.0f }, Temp.ToArray());
            Assert.IsTrue(B.GetIntersection(A, out Temp));
            Assert.AreEqual(new float[] { 2.5f, 5.0f }, Temp.ToArray());

            Assert.IsTrue(A.GetIntersection(A, out Temp));
            Assert.AreEqual(A.ToArray(), Temp.ToArray());

            Assert.IsTrue(B.GetIntersection(C, out Temp));
            Assert.AreEqual(new float[] { 6.0f, 7.5f }, Temp.ToArray());
            Assert.IsTrue(C.GetIntersection(B, out Temp));
            Assert.AreEqual(new float[] { 6.0f, 7.5f }, Temp.ToArray());

            Assert.IsTrue(B.GetIntersection(B, out Temp));
            Assert.AreEqual(B.ToArray(), Temp.ToArray());

            A.GetIntersection(C, out Temp);
            C.GetIntersection(A, out Temp);

            Assert.IsFalse(A.GetIntersection(C, out Temp));
            Assert.IsFalse(C.GetIntersection(A, out Temp));
        }

        public void Subtract (ref float lhs, ref float rhs, out float result) {
            result = lhs - rhs;
        }

        [Test]
        public void GetDistanceTest () {
            var A = new Interval<float>(0.0f, 5.0f);
            var B = new Interval<float>(2.5f, 7.5f);
            var C = new Interval<float>(6.0f, 10.0f);

            Assert.AreEqual(-2.5f, A.GetDistance(B, Subtract));
            Assert.AreEqual(-2.5f, B.GetDistance(A, Subtract));

            Assert.AreEqual(-1.5f, B.GetDistance(C, Subtract));
            Assert.AreEqual(-1.5f, C.GetDistance(B, Subtract));

            Assert.AreEqual(1.0f, A.GetDistance(C, Subtract));
            Assert.AreEqual(1.0f, C.GetDistance(A, Subtract));
        }
    }
}
