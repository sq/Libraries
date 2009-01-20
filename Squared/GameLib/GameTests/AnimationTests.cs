using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Squared.Util;
using Squared.Game;
using Microsoft.Xna.Framework;

namespace Squared.Game.Animation {
    public class MockTimeProvider : ITimeProvider {
        public long CurrentTime = 0;

        public long Ticks {
            get { return CurrentTime; }
        }

        public double Seconds {
            get { return CurrentTime / Squared.Util.Time.SecondInTicks; }
        }

        public void Advance (long ticks) {
            CurrentTime += ticks;
        }
    }

    [TestFixture]
    public class AnimationTests {
        MockTimeProvider TimeProvider = new MockTimeProvider();

        public IEnumerator<AnimCmd> BasicAnimation () {
            int i = 0;
            while (true) {
                yield return new SetFrame { Group = 0, Frame = i };
                yield return new Delay { Duration = 50 };
                i = (i + 1) % 3;
            }
        }

        public IEnumerator<AnimCmd> AnimString1 () {
            yield return new SetFrame { Group = 0, Frame = 0 };
            yield return new Delay { Duration = 50 };
            yield return new SetAnimation { Animation = AnimString2() };
        }

        public IEnumerator<AnimCmd> AnimString2 () {
            yield return new SetFrame { Group = 1, Frame = 0 };
            yield return new Delay { Duration = 50 };
            yield return new SetAnimation { Animation = AnimString3() };
        }

        public IEnumerator<AnimCmd> AnimString3 () {
            yield return new SetFrame { Group = 2, Frame = 0 };
            yield return new Delay { Duration = 50 };
            yield return new SetAnimation { Animation = AnimString1() };
        }

        public IEnumerator<AnimCmd> WaitForUpdateAnim () {
            int i = 0;
            while (true) {
                yield return new SetFrame { Group = 0, Frame = i };
                yield return new WaitForUpdate();
                i += 1;
            }
        }

        [Test]
        public void BasicAnimationTest () {
            var a = new Animator { TimeProvider = TimeProvider };
            a.SetAnimation(BasicAnimation());

            a.Update();
            Assert.AreEqual(0, a.Frame);

            TimeProvider.Advance(5);
            a.Update();
            Assert.AreEqual(0, a.Frame);

            TimeProvider.Advance(50);
            a.Update();
            Assert.AreEqual(1, a.Frame);

            TimeProvider.Advance(50);
            a.Update();
            Assert.AreEqual(2, a.Frame);

            TimeProvider.Advance(50);
            a.Update();
            Assert.AreEqual(0, a.Frame);

            TimeProvider.Advance(100);
            a.Update();
            Assert.AreEqual(2, a.Frame);
        }

        [Test]
        public void AnimationStringTest () {
            var a = new Animator { TimeProvider = TimeProvider };
            a.SetAnimation(AnimString1());

            a.Update();
            Assert.AreEqual(0, a.Group);

            TimeProvider.Advance(50);
            a.Update();
            Assert.AreEqual(1, a.Group);

            TimeProvider.Advance(50);
            a.Update();
            Assert.AreEqual(2, a.Group);

            TimeProvider.Advance(50);
            a.Update();
            Assert.AreEqual(0, a.Group);
        }

        [Test]
        public void WaitForUpdateTest () {
            var a = new Animator { TimeProvider = TimeProvider };
            a.SetAnimation(WaitForUpdateAnim());

            a.Update();
            Assert.AreEqual(0, a.Frame);

            a.Update();
            Assert.AreEqual(1, a.Frame);

            a.Update();
            Assert.AreEqual(2, a.Frame);
        }
    }
}
