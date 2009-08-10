using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Squared.Util;

namespace Squared.Util {
    [TestFixture]
    public class TimeTests {
        [Test]
        public void TestPausableTimeProvider () {
            var realTime = new MockTimeProvider();
            var pausable = new PausableTimeProvider(realTime);

            Assert.AreEqual(realTime.Ticks, pausable.Ticks);

            realTime.Advance(100);
            Assert.AreEqual(realTime.Ticks, pausable.Ticks);

            pausable.Paused = true;

            realTime.Advance(100);
            Assert.AreEqual(realTime.Ticks - 100, pausable.Ticks);

            pausable.Paused = false;
            Assert.AreEqual(realTime.Ticks - 100, pausable.Ticks);

            realTime.Advance(100);
            Assert.AreEqual(realTime.Ticks - 100, pausable.Ticks);
        }

        [Test]
        public void TestScalableTimeProvider () {
            var realTime = new MockTimeProvider();
            var scalable = new ScalableTimeProvider(realTime);

            Assert.AreEqual(realTime.Ticks, scalable.Ticks);

            scalable.TimeScale = 0.5f;
            realTime.Advance(100);
            Assert.AreEqual(realTime.Ticks - 50, scalable.Ticks);

            scalable.TimeScale = 1.0f;
            realTime.Advance(100);
            Assert.AreEqual(realTime.Ticks - 50, scalable.Ticks);

            scalable.TimeScale = 2.0f;
            realTime.Advance(100);
            Assert.AreEqual(realTime.Ticks + 50, scalable.Ticks);

            scalable.TimeScale = 0.0f;
            Assert.AreEqual(realTime.Ticks + 50, scalable.Ticks);

            realTime.Advance(100);
            Assert.AreEqual(realTime.Ticks - 50, scalable.Ticks);

            scalable.TimeScale = 1.0f;
            Assert.AreEqual(realTime.Ticks - 50, scalable.Ticks);

            realTime.Advance(100);
            Assert.AreEqual(realTime.Ticks - 50, scalable.Ticks);
        }
    }
}
