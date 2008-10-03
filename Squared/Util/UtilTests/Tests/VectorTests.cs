using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Squared.Util;
using System.Linq.Expressions;
using Squared.Util.Vector;

namespace Squared.Util {
    [TestFixture]
    public class Vector2Tests {
        [Test]
        public void Add () {
            var a = new Vector2(2.0f, 1.0f);
            var b = new Vector2(1.0f, 3.0f);
            Assert.AreEqual(new Vector2(3.0f, 4.0f), a + b);
        }

        [Test]
        public void AddScalar () {
            var a = new Vector2(2.0f, 1.0f);
            Assert.AreEqual(new Vector2(3.0f, 2.0f), a + 1.0f);
        }

        [Test]
        public void Subtract () {
            var a = new Vector2(2.0f, 1.0f);
            var b = new Vector2(1.0f, 3.0f);
            Assert.AreEqual(new Vector2(1.0f, -2.0f), a - b);
        }

        [Test]
        public void SubtractScalar () {
            var a = new Vector2(2.0f, 1.0f);
            Assert.AreEqual(new Vector2(1.0f, 0.0f), a - 1.0f);
        }

        [Test]
        public void Multiply () {
            var a = new Vector2(2.0f, 1.0f);
            var b = new Vector2(1.0f, 3.0f);
            Assert.AreEqual(new Vector2(2.0f, 3.0f), a * b);
        }

        [Test]
        public void MultiplyScalar () {
            var a = new Vector2(2.0f, 1.0f);
            Assert.AreEqual(new Vector2(4.0f, 2.0f), a * 2.0f);
        }

        [Test]
        public void Divide () {
            var a = new Vector2(2.0f, 1.0f);
            var b = new Vector2(1.0f, 3.0f);
            Assert.AreEqual(new Vector2(2.0f, 1.0f / 3.0f), a / b);
        }

        [Test]
        public void DivideScalar () {
            var a = new Vector2(2.0f, 1.0f);
            Assert.AreEqual(new Vector2(1.0f, 0.5f), a / 2.0f);
        }

        [Test]
        public void Compare () {
            var a = new Vector2(1.0f, 1.0f);
            var b = new Vector2(1.0f, 2.0f);
            var c = new Vector2(2.0f, 0.0f);

            Assert.IsTrue(a < b);
            Assert.IsTrue(a < c);
            Assert.IsTrue(c > b);
        }

        [Test]
        public void IComparable2 () {
            var a = new Vector2(1.0f, 1.0f);
            var b = new Vector2(1.0f, 2.0f);
            var c = new Vector2(2.0f, 0.0f);

            var r = a.CompareTo(b);
            Assert.AreEqual(new Vector2(0, -1), r);

            r = a.CompareTo(c);
            Assert.AreEqual(new Vector2(-1, 1), r);

            r = c.CompareTo(b);
            Assert.AreEqual(new Vector2(1, -1), r);
        }

        delegate void ComplexPerfFn (ref Vector2 A, ref Vector2 B, float C, out Vector2 Result);

        [Test]
        public void PerformanceTest () {
            int numIterations = 50000;
            Vector2[] r = new Vector2[numIterations];
            float numIterationsF = numIterations;
            Vector2 a = new Vector2(0.0f, 1.0f), b = new Vector2(1.0f, 0.0f);
            float c;

            Func<Vector2, Vector2, float, Vector2> simpleFn = (A, B, C) => new Vector2(
                A.X + (B.X - A.X) * C, A.Y + (B.Y - A.Y) * C
            );

            ComplexPerfFn complexFn = delegate(ref Vector2 A, ref Vector2 B, float C, out Vector2 result) {
                result.X = A.X + (B.X - A.X) * C;
                result.Y = A.Y + (B.Y - A.Y) * C;
            };

            long start = Time.Ticks;
            for (int i = 0; i < numIterations; i++) {
                c = (i / numIterationsF);
                r[i] = new Vector2(a.X + (b.X - a.X) * c, a.Y + (b.Y - a.Y) * c);
            }
            long end = Time.Ticks;
            Console.WriteLine("Native expression execution time: {0} ticks for {1} iterations ({2:0.000} ticks/iter)", end - start, numIterations, (end - start) / numIterationsF);

            start = Time.Ticks;
            for (int i = 0; i < numIterations; i++) {
                c = (i / numIterationsF);
                r[i] = simpleFn(a, b, c);
            }
            end = Time.Ticks;
            Console.WriteLine("Native delegate execution time: {0} ticks for {1} iterations ({2:0.000} ticks/iter)", end - start, numIterations, (end - start) / numIterationsF);

            start = Time.Ticks;
            for (int i = 0; i < numIterations; i++) {
                c = (i / numIterationsF);
                complexFn(ref a, ref b, c, out r[i]);
            }
            end = Time.Ticks;
            Console.WriteLine("Complex delegate execution time: {0} ticks for {1} iterations ({2:0.000} ticks/iter)", end - start, numIterations, (end - start) / numIterationsF);

            start = Time.Ticks;
            for (int i = 0; i < numIterations; i++) {
                c = (i / numIterationsF);
                r[i] = (a + (b - a) * c);
            }
            end = Time.Ticks;
            Console.WriteLine("Operator execution time: {0} ticks for {1} iterations ({2:0.000} ticks/iter)", end - start, numIterations, (end - start) / numIterationsF);
        }
    }
}
