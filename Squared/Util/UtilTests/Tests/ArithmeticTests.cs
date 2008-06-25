using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Squared.Util;

namespace Squared.Util {
    public struct ValueType {
        public float A, B;

        public ValueType (float a, float b) {
            A = a;
            B = b;
        }

        public static ValueType operator + (ValueType lhs, ValueType rhs) {
            return new ValueType(lhs.A + rhs.A, lhs.B + rhs.B);
        }

        public static ValueType operator - (ValueType lhs, ValueType rhs) {
            return new ValueType(lhs.A - rhs.A, lhs.B - rhs.B);
        }

        public static ValueType operator * (ValueType lhs, ValueType rhs) {
            return new ValueType(lhs.A * rhs.A, lhs.B * rhs.B);
        }

        public static ValueType operator / (ValueType lhs, ValueType rhs) {
            return new ValueType(lhs.A / rhs.A, lhs.B / rhs.B);
        }

        public static ValueType operator * (ValueType lhs, float rhs) {
            return new ValueType(lhs.A * rhs, lhs.B * rhs);
        }
    }

    [TestFixture]
    public class ArithmeticTests {
        [Test]
        public void ClampInt () {
            Assert.AreEqual(Arithmetic.Clamp(1, 0, 2), 1);
            Assert.AreEqual(Arithmetic.Clamp(0, 0, 2), 0);
            Assert.AreEqual(Arithmetic.Clamp(2, 0, 2), 2);
            Assert.AreEqual(Arithmetic.Clamp(-1, 0, 2), 0);
            Assert.AreEqual(Arithmetic.Clamp(3, 0, 2), 2);
        }

        [Test]
        public void ClampFloat () {
            Assert.AreEqual(Arithmetic.Clamp(1.0f, 0.0f, 2.0f), 1.0f);
            Assert.AreEqual(Arithmetic.Clamp(0.0f, 0.0f, 2.0f), 0.0f);
            Assert.AreEqual(Arithmetic.Clamp(2.0f, 0.0f, 2.0f), 2.0f);
            Assert.AreEqual(Arithmetic.Clamp(-0.5f, 0.0f, 2.0f), 0.0f);
            Assert.AreEqual(Arithmetic.Clamp(2.5f, 0.0f, 2.0f), 2.0f);
        }

        [Test]
        public void Add () {
            Assert.AreEqual(5, Arithmetic.Add(2, 3));
            Assert.AreEqual(5.5f, Arithmetic.Add(2.5f, 3.0f));
            Assert.AreEqual(
                new ValueType(5.0f, 4.0f),
                Arithmetic.Add(new ValueType(3.0f, 2.0f), new ValueType(2.0f, 2.0f))
            );
        }

        [Test]
        public void Subtract () {
            Assert.AreEqual(3, Arithmetic.Subtract(5, 2));
            Assert.AreEqual(3.5f, Arithmetic.Subtract(5.5f, 2.0f));
            Assert.AreEqual(
                new ValueType(1.0f, 2.0f),
                Arithmetic.Subtract(new ValueType(4.0f, 4.0f), new ValueType(3.0f, 2.0f))
            );
        }

        [Test]
        public void Multiply () {
            Assert.AreEqual(4, Arithmetic.Multiply(2, 2));
            Assert.AreEqual(4.5f, Arithmetic.Multiply(2.25f, 2.0f));
            Assert.AreEqual(
                new ValueType(4.5f, 4.0f),
                Arithmetic.Multiply(new ValueType(2.25f, 2.0f), new ValueType(2.0f, 2.0f))
            );
        }

        [Test]
        public void Divide () {
            Assert.AreEqual(2, Arithmetic.Divide(4, 2));
            Assert.AreEqual(2.25f, Arithmetic.Divide(4.5f, 2.0f));
            Assert.AreEqual(
                new ValueType(2.25f, 2.0f),
                Arithmetic.Divide(new ValueType(4.5f, 4.0f), new ValueType(2.0f, 2.0f))
            );
        }

        [Test]
        public void MultiplyMixedTypes () {
            Assert.AreEqual(
                new ValueType(4.5f, 4.0f),
                Arithmetic.Multiply(new ValueType(2.25f, 2.0f), 2.0f)
            );
        }

        [Test]
        public void ThrowsIfLeftIsPrimitiveButRightIsNot () {
            try {
                Arithmetic.Add(2.0f, new ValueType(1.0f, 1.0f));
                Assert.Fail("Did not throw");
            } catch (InvalidOperationException ex) {
                Assert.IsTrue(ex.Message.Contains("GenerateOperatorIL failed"));
            }
        }
    }
}
