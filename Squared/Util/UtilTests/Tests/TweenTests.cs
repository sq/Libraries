using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Squared.Util;

namespace Squared.Util {
    [TestFixture]
    public class TweenTests {
        [Test]
        public void Constant () {
            var t0 = (Tween<double>)0;
            var t1 = (Tween<double>)1;
            Assert.AreEqual(0, t0.Get(0));
            Assert.AreEqual(0, t0.Get(1));
            Assert.AreEqual(0, t0.Get(long.MaxValue));
            Assert.AreEqual(0, t0.Get(long.MinValue));
            Assert.AreEqual(1, t1.Get(0));
            Assert.AreEqual(1, t1.Get(1));
            Assert.AreEqual(1, t1.Get(long.MaxValue));
            Assert.AreEqual(1, t1.Get(long.MinValue));
        }

        [Test]
        public void OneShot () {
            var t = Tween<double>.StartNow(0, 2, 1024L, 256L, 0L);
            Assert.AreEqual(0, t.Get(long.MinValue));
            Assert.AreEqual(0, t.Get(-256L));
            Assert.AreEqual(0, t.Get(0L));
            Assert.AreEqual(0, t.Get(256L));
            Assert.AreEqual(2, t.Get(1024L + 256L));
            Assert.AreEqual(2, t.Get(4096L));
            Assert.AreEqual(2, t.Get(long.MaxValue));
        }
    }
}
