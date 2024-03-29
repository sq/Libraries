﻿using System;
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

        [Test]
        public void RepeatWithoutDelay () {
            var t = Tween<double>.StartNow(0, 2, 1f, 0.25f, now: 0L, repeatCount: 4);
            Assert.AreEqual(0, t.Get(0f));
            Assert.AreEqual(0, t.Get(0.25f));
            Assert.AreEqual(1, t.Get(0.75f));
            Assert.AreEqual(2, t.Get(1.25f));
            Assert.AreEqual(1, t.Get(1.75f));
        }

        [Test]
        public void RepeatWithDelay () {
            var t = Tween<double>.StartNow(0, 2, 1f, 0.25f, now: 0L, repeatCount: 4, repeatDelay: 1f);
            Assert.AreEqual(0, t.Get(0f));
            Assert.AreEqual(0, t.Get(0.25f));
            Assert.AreEqual(1, t.Get(0.75f));
            Assert.AreEqual(2, t.Get(1.25f));
            Assert.AreEqual(2, t.Get(1.5f));
            Assert.AreEqual(2, t.Get(2.0f));
            Assert.AreEqual(1, t.Get(2.75f));
        }

        [Test]
        public void PulseWithDelay () {
            var t = Tween<double>.StartNow(0, 2, 1f, now: 0L, repeatCount: 4, repeatDelay: 1f, repeatMode: TweenRepeatMode.Pulse);
            Assert.AreEqual(0, t.Get(0f));
            Assert.AreEqual(1, t.Get(0.5f));
            Assert.AreEqual(2, t.Get(1f));
            Assert.AreEqual(2, t.Get(1.5f));
            Assert.AreEqual(2, t.Get(2.0f));
            Assert.AreEqual(1.5, t.Get(2.25f));
            Assert.AreEqual(0.5, t.Get(2.75f));
        }

        [Test]
        public void RepeatWithDelayAndExtension () {
            var t = Tween<double>.StartNow(0, 2, 1f, 0.25f, now: 0L, repeatCount: 4, repeatDelay: 1f, repeatExtraDuration: 1f);
            Assert.AreEqual(0, t.Get(0f));
            Assert.AreEqual(0, t.Get(0.25f));
            Assert.AreEqual(1, t.Get(0.75f));
            Assert.AreEqual(2, t.Get(1.25f));
            Assert.AreEqual(2, t.Get(1.5f));
            Assert.AreEqual(2, t.Get(2.0f));
            Assert.AreEqual(0.5, t.Get(2.75f));
            Assert.AreEqual(1, t.Get(3.25f));
        }
    }
}
