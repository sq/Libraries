using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Squared.Util;
using Squared.Game;
using Microsoft.Xna.Framework;

namespace Squared.Game.Animation {
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

        public IEnumerator<AnimCmd> SingleAnim<T> (T group, int start, int end) {
            for (int i = start; i <= end; i++) {
                yield return new SetFrame { Group = group, Frame = i };
                yield return new WaitForUpdate();
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
        public void StringGroupTest () {
            var a = new Animator { TimeProvider = TimeProvider };
            a.SetAnimation(SingleAnim("group1", 0, 2));

            a.Update();
            Assert.AreEqual("group1", a.Group);
            Assert.AreEqual(0, a.Frame);

            TimeProvider.Advance(50);
            a.Update();
            Assert.AreEqual("group1", a.Group);
            Assert.AreEqual(1, a.Frame);

            TimeProvider.Advance(50);
            a.Update();
            Assert.AreEqual("group1", a.Group);
            Assert.AreEqual(2, a.Frame);
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

        [Test]
        public void ChainTest () {
            var a = new Animator { TimeProvider = TimeProvider };
            a.SetAnimation(SingleAnim(0, 0, 1).Chain(() => SingleAnim(1, 0, 1)));

            a.Update();
            Assert.AreEqual(0, a.Group);
            Assert.AreEqual(0, a.Frame);

            a.Update();
            Assert.AreEqual(0, a.Group);
            Assert.AreEqual(1, a.Frame);

            a.Update();
            Assert.AreEqual(1, a.Group);
            Assert.AreEqual(0, a.Frame);

            a.Update();
            Assert.AreEqual(1, a.Group);
            Assert.AreEqual(1, a.Frame);
        }

        [Test]
        public void SetAnimationAtEndTest () {
            var a = new Animator { TimeProvider = TimeProvider };
            var anim = SingleAnim(0, 0, 1).WatchPlayState(
                (playing) => {
                    if (playing == false) a.SetAnimation(SingleAnim(1, 0, 1));
            });

            a.SetAnimation(anim);

            a.Update();
            Assert.AreEqual(0, a.Group);
            Assert.AreEqual(0, a.Frame);

            a.Update();
            Assert.AreEqual(0, a.Group);
            Assert.AreEqual(1, a.Frame);

            a.Update();
            Assert.AreEqual(1, a.Group);
            Assert.AreEqual(0, a.Frame);
        }
    }
}
