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

        [Test]
        public void Interpolation () {
            var c = new Curve<double>();
            c[0] = 5.0;
            c[1] = 10.0;
            Assert.AreEqual(6.0, c[0.2f]);
            Assert.AreEqual(7.0, c[0.4f]);
            Assert.AreEqual(8.0, c[0.6f]);
            Assert.AreEqual(9.0, c[0.8f]);
        }
    }
}
