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
        public void GetLowerIndexForPosition () {
            var c = new Curve<double>();
            c[-1] = 0;
            c[-0.5f] = 1;
            c[0] = 2;
            c[1] = 3;
            c[2] = 4;
            c[4] = 5;
            c[32] = 6;

            int i;

            i = c.GetLowerIndexForPosition(-2);
            Assert.AreEqual(i, 0);

            i = c.GetLowerIndexForPosition(-1);
            Assert.AreEqual(i, 0);

            i = c.GetLowerIndexForPosition(-0.6f);
            Assert.AreEqual(i, 0);

            i = c.GetLowerIndexForPosition(-0.5f);
            Assert.AreEqual(i, 1);

            i = c.GetLowerIndexForPosition(0.5f);
            Assert.AreEqual(i, 2);

            i = c.GetLowerIndexForPosition(5);
            Assert.AreEqual(i, 5);

            i = c.GetLowerIndexForPosition(36);
            Assert.AreEqual(i, 6);
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
        public void InterpolatorPerNode () {
            var c = new Curve<float>();
            c.SetValueAtPosition(0.0f, 1.0f, Interpolators<float>.Linear);
            c.SetValueAtPosition(1.0f, 2.0f, Interpolators<float>.Null);
            c.SetValueAtPosition(2.0f, 0.0f);
            AssertEqualFloat(1.0f, c[0.0f]);
            AssertEqualFloat(1.5f, c[0.5f]);
            AssertEqualFloat(2.0f, c[1.0f]);
            AssertEqualFloat(2.0f, c[1.5f]);
            AssertEqualFloat(0.0f, c[2.0f]);
        }

        [Test]
        public void NullInterpolation () {
            var c = new Curve<float>();
            c.Interpolator = Interpolators<float>.Null;
            c[0] = 5.0f;
            c[1] = 10.0f;
            AssertEqualFloat(5.0f, c[-0.2f]);
            AssertEqualFloat(5.0f, c[0.0f]);
            AssertEqualFloat(5.0f, c[0.2f]);
            AssertEqualFloat(10.0f, c[1.0f]);
            AssertEqualFloat(10.0f, c[1.2f]);
        }

        [Test]
        public void LinearInterpolation () {
            var c = new Curve<float>();
            c.Interpolator = Interpolators<float>.Linear;
            c[0] = 5.0f;
            c[1] = 10.0f;

            AssertEqualFloat(5.0f, c[-0.2f]);
            AssertEqualFloat(5.0f, c[0.0f]);
            AssertEqualFloat(6.0f, c[0.2f]);
            AssertEqualFloat(7.0f, c[0.4f]);
            AssertEqualFloat(8.0f, c[0.6f]);
            AssertEqualFloat(9.0f, c[0.8f]);
            AssertEqualFloat(10.0f, c[1.0f]);
            AssertEqualFloat(10.0f, c[1.2f]);
        }

        [Test]
        public void CosineInterpolation () {
            var c = new Curve<float>();
            c.Interpolator = Interpolators<float>.Cosine;
            c[0] = 5.0f;
            c[1] = 10.0f;

            float e = 0.001f;

            AssertEqualFloat(5.000f, c[-0.2f], e);
            AssertEqualFloat(5.000f, c[0.0f], e);
            AssertEqualFloat(5.477f, c[0.2f], e);
            AssertEqualFloat(6.727f, c[0.4f], e);
            AssertEqualFloat(8.272f, c[0.6f], e);
            AssertEqualFloat(9.522f, c[0.8f], e);
            AssertEqualFloat(10.00f, c[1.0f], e);
            AssertEqualFloat(10.00f, c[1.2f], e);
        }

        [Test]
        public void CubicInterpolation () {
            var c = new Curve<float>();
            c.Interpolator = Interpolators<float>.Cubic;
            c[-1] = 10.0f;
            c[0] = 5.0f;
            c[1] = 10.0f;
            c[2] = 5.0f;

            float e = 0.001f;

            AssertEqualFloat(10.00f, c[-1.2f], e);
            AssertEqualFloat(10.00f, c[-1f], e);
            AssertEqualFloat(5.360f, c[-0.2f], e);
            AssertEqualFloat(5.000f, c[0.0f], e);
            AssertEqualFloat(5.520f, c[0.2f], e);
            AssertEqualFloat(6.760f, c[0.4f], e);
            AssertEqualFloat(8.240f, c[0.6f], e);
            AssertEqualFloat(9.480f, c[0.8f], e);
            AssertEqualFloat(10.00f, c[1.0f], e);
            AssertEqualFloat(9.639f, c[1.2f], e);
            AssertEqualFloat(5.000f, c[2.0f], e);
            AssertEqualFloat(5.000f, c[2.2f], e);
        }

        [Test]
        public void Clamp () {
            var c = new Curve<float>();
            c.Interpolator = Interpolators<float>.Linear;
            c[0] = 1.0f;
            c[10] = 2.0f;
            c[20] = 3.0f;
            c[30] = 4.0f;

            c.Clamp(5, 25);

            AssertEqualFloat(1.5f, c[0]);
            AssertEqualFloat(2.0f, c[10]);
            AssertEqualFloat(3.0f, c[20]);
            AssertEqualFloat(3.5f, c[30]);

            c.Clamp(10, 20);

            AssertEqualFloat(2.0f, c[0]);
            AssertEqualFloat(2.0f, c[10]);
            AssertEqualFloat(3.0f, c[20]);
            AssertEqualFloat(3.0f, c[30]);

            c.Clamp(15, 15);

            AssertEqualFloat(2.5f, c[0]);
            AssertEqualFloat(2.5f, c[10]);
            AssertEqualFloat(2.5f, c[20]);
            AssertEqualFloat(2.5f, c[30]);
        }

        [Test]
        public void MultipleInterpolatorTypes () {
            Interpolator<float> lerpFloat = Interpolators<float>.Linear;
            Interpolator<double> lerpDouble = Interpolators<double>.Linear;

            var floats = new float[] { 0.0f, 1.0f };
            var doubles = new double[] { 0.0, 1.0 };

            InterpolatorSource<float> floatWindow = (i) => floats[i];
            InterpolatorSource<double> doubleWindow = (i) => doubles[i];

            Assert.AreEqual(
                0.5f,
                lerpFloat(floatWindow, 0, 0.5f)
            );

            Assert.AreEqual(
                0.5,
                lerpDouble(doubleWindow, 0, 0.5f)
            );
        }

        [Test]
        public void InterpolatorEquality () {
            Interpolator<float> a = Interpolators<float>.Linear;
            Interpolator<float> b = Interpolators<float>.Linear;
            Interpolator<float> c = Interpolators<float>.Null;

            Assert.AreEqual(a, b);
            Assert.AreNotEqual(a, c);
        }

        [Test]
        public void InterpolatorByName () {
            Assert.AreEqual(Interpolators<float>.GetByName("Linear"), (Interpolator<float>)Interpolators<float>.Linear);
            Assert.AreEqual(Interpolators<float>.GetByName("Foozle"), (Interpolator<float>)Interpolators<float>.Null);
        }

        [Test]
        public void PerformanceTest () {
            int numIterations = 50000;
            float[] r = new float[numIterations];
            float numIterationsF = numIterations;
            Curve<float> curve = new Curve<float>();
            float c;

            curve[0.0f] = 0.0f;
            curve[0.5f] = 1.0f;
            curve[1.0f] = 2.0f;

            curve.Interpolator = Interpolators<float>.Linear;

            long start = Time.Ticks;
            for (int i = 0; i < numIterations; i++) {
                c = (i / numIterationsF);
                r[i] = curve[c];
            }
            long end = Time.Ticks;
            Console.WriteLine("Execution time (linear interpolation): {0} ticks for {1} iterations ({2:0.000} ticks/iter)", end - start, numIterations, (end - start) / numIterationsF);

            curve.Interpolator = Interpolators<float>.Null;

            start = Time.Ticks;
            for (int i = 0; i < numIterations; i++) {
                c = (i / numIterationsF);
                r[i] = curve[c];
            }
            end = Time.Ticks;
            Console.WriteLine("Execution time (no interpolation): {0} ticks for {1} iterations ({2:0.000} ticks/iter)", end - start, numIterations, (end - start) / numIterationsF);
        }
    }
}
