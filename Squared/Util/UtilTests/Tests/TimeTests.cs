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
    }
}
