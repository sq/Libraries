using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Squared.Util;

namespace Squared.Util {
    [TestFixture]
    public class CurveTests {
        [Test]
        public void GetIndexPairAtPosition () {
            var c = new Curve<double>();
            c[-1] = 0;
            c[-0.5f] = 1;
            c[0] = 2;
            c[1] = 3;
            c[2] = 4;
            c[4] = 5;
            c[32] = 6;

            Pair<int> p;

            p = c.GetIndexPairAtPosition(-2);
            Assert.AreEqual(p, new Pair<int>(0));

            p = c.GetIndexPairAtPosition(-1);
            Assert.AreEqual(p, new Pair<int>(0, 1));

            p = c.GetIndexPairAtPosition(-0.6f);
            Assert.AreEqual(p, new Pair<int>(0, 1));

            p = c.GetIndexPairAtPosition(-0.5f);
            Assert.AreEqual(p, new Pair<int>(1, 2));

            p = c.GetIndexPairAtPosition(0.5f);
            Assert.AreEqual(p, new Pair<int>(2, 3));

            p = c.GetIndexPairAtPosition(5);
            Assert.AreEqual(p, new Pair<int>(5, 6));

            p = c.GetIndexPairAtPosition(36);
            Assert.AreEqual(p, new Pair<int>(6));
        }

        [Test]
        public void SingleValue () {
            var c = new Curve<double>();
            c[0] = 5.0;
            Assert.AreEqual(5.0, c[-1]);
            Assert.AreEqual(5.0, c[0]);
            Assert.AreEqual(5.0, c[1]);
        }

        [Test]
        public void TwoValues () {
            var c = new Curve<double>();
            c[0] = 5.0;
            c[1] = 10.0;
            Assert.AreEqual(5.0, c[-1]);
            Assert.AreEqual(5.0, c[0]);
            Assert.AreEqual(10.0, c[1]);
            Assert.AreEqual(10.0, c[2]);
        }

        [Test]
        public void StartAndEnd () {
            var c = new Curve<double>();
            c[-2] = 1;
            c[7] = 2;
            Assert.AreEqual(c.Start, -2);
            Assert.AreEqual(c.End, 7);
        }

        private void AssertEqualFloat (float lhs, float rhs) {
            AssertEqualFloat(lhs, rhs, 0.00001f);
        }

        private void AssertEqualFloat (float lhs, float rhs, float epsilon) {
            float delta = Math.Abs(lhs - rhs);
            Assert.LessOrEqual(delta, epsilon, "Expected {0} == {1} within {2}", lhs, rhs, epsilon);
        }

        [Test]
        public void Interpolation () {
            var c = new Curve<float>();
            c[0] = 5.0f;
            c[1] = 10.0f;
            AssertEqualFloat(6.0f, c[0.2f]);
            AssertEqualFloat(7.0f, c[0.4f]);
            AssertEqualFloat(8.0f, c[0.6f]);
            AssertEqualFloat(9.0f, c[0.8f]);
        }
    }
}
