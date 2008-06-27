using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Squared.Util;
using System.Linq.Expressions;

namespace Squared.Util {
    [TestFixture]
    public class Vec2Tests {
        [Test]
        public void Add () {
            var a = new Vec2(2.0f, 1.0f);
            var b = new Vec2(1.0f, 3.0f);
            Assert.AreEqual(new Vec2(3.0f, 4.0f), a + b);
        }

        [Test]
        public void AddScalar () {
            var a = new Vec2(2.0f, 1.0f);
            Assert.AreEqual(new Vec2(3.0f, 2.0f), a + 1.0f);
        }

        [Test]
        public void Subtract () {
            var a = new Vec2(2.0f, 1.0f);
            var b = new Vec2(1.0f, 3.0f);
            Assert.AreEqual(new Vec2(1.0f, -2.0f), a - b);
        }

        [Test]
        public void SubtractScalar () {
            var a = new Vec2(2.0f, 1.0f);
            Assert.AreEqual(new Vec2(1.0f, 0.0f), a - 1.0f);
        }

        [Test]
        public void Multiply () {
            var a = new Vec2(2.0f, 1.0f);
            var b = new Vec2(1.0f, 3.0f);
            Assert.AreEqual(new Vec2(2.0f, 3.0f), a * b);
        }

        [Test]
        public void MultiplyScalar () {
            var a = new Vec2(2.0f, 1.0f);
            Assert.AreEqual(new Vec2(4.0f, 2.0f), a * 2.0f);
        }

        [Test]
        public void Divide () {
            var a = new Vec2(2.0f, 1.0f);
            var b = new Vec2(1.0f, 3.0f);
            Assert.AreEqual(new Vec2(2.0f, 1.0f / 3.0f), a / b);
        }

        [Test]
        public void DivideScalar () {
            var a = new Vec2(2.0f, 1.0f);
            Assert.AreEqual(new Vec2(1.0f, 0.5f), a / 2.0f);
        }
    }
}
