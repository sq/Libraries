using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Squared.Util;

namespace Squared.Game.Animation {
    public interface AnimCmd {
        bool Invoke (Animator animator);
    }

    public class SetAnimation : AnimCmd {
        public Func<IEnumerator<AnimCmd>> Animation;

        public bool Invoke (Animator animator) {
            animator.SetAnimation(Animation);
            return false;
        }
    }

    public class SetFrame : AnimCmd {
        public object Group;
        public int Frame;

        public bool Invoke (Animator animator) {
            animator.SetFrame(Group, Frame);
            return false;
        }
    }

    public class Delay : AnimCmd {
        public long Duration;

        public bool Invoke (Animator animator) {
            animator.Delay(Duration);
            return false;
        }
    }

    public class WaitForUpdate : AnimCmd {
        public bool Invoke (Animator animator) {
            return true;
        }
    }

    public static class AnimationExtensions {
        public static IEnumerator<AnimCmd> WhenFinished (this IEnumerator<AnimCmd> animation, Action action) {
            using (animation)
                while (animation.MoveNext())
                    yield return animation.Current;

            action();
        }
        
        public static IEnumerator<AnimCmd> WatchPlayState (this IEnumerator<AnimCmd> animation, Action<bool> playStateChanged) {
            try {
                playStateChanged(true);
                while (animation.MoveNext())
                    yield return animation.Current;
            } finally {
                animation.Dispose();
                playStateChanged(false);
            }
        }

        public static IEnumerator<AnimCmd> Chain (this IEnumerator<AnimCmd> first, Func<IEnumerator<AnimCmd>> second) {
            using (first)
                while (first.MoveNext())
                    yield return first.Current;

            yield return new SetAnimation { Animation = second };
        }

        public static IEnumerator<AnimCmd> SwitchIf (this IEnumerator<AnimCmd> root, Func<IEnumerator<AnimCmd>> leaf, Func<bool> predicate) {
            using (root) {
                while (root.MoveNext()) {
                    if (predicate()) {
                        yield return new SetAnimation { Animation = leaf };
                        break;
                    } else {
                        yield return root.Current;
                    }
                }
            }
        }
    }

    public class Animator {
        public ITimeProvider TimeProvider = Time.DefaultTimeProvider;
        private IEnumerator<AnimCmd> _ActiveAnimation = null;
        private int _Frame = 0;
        private long _Speed = 10000;
        private object _Group = null;
        private long _SuspendSince = 0;
        private long _SuspendDuration = 0;
        private long _SuspendUntil = 0;

        public void SetAnimation (Func<IEnumerator<AnimCmd>> animation) {
            if (_ActiveAnimation != null)
                _ActiveAnimation.Dispose();

            if (animation != null)
                _ActiveAnimation = animation();
            else
                _ActiveAnimation = null;

            _SuspendSince = _SuspendUntil = TimeProvider.Ticks;
            _SuspendDuration = 0;
        }

        public void SetFrame (object group, int frame) {
            _Group = group;
            _Frame = frame;
        }

        public void Delay (long duration) {
            _SuspendSince = _SuspendUntil;
            _SuspendDuration = duration;
            _SuspendUntil = _SuspendUntil + (duration * 10000 / _Speed);
        }

        public object Group {
            get { return _Group; }
        }

        public int Frame {
            get { return _Frame; }
        }

        public void Update () {
            long now = TimeProvider.Ticks;
            while ((_ActiveAnimation != null) && (now >= _SuspendUntil)) {
                var a = _ActiveAnimation;
                if (!a.MoveNext()) {
                    if (a == _ActiveAnimation) {
                        SetAnimation(null);
                        break;
                    } else {
                        continue;
                    }
                }

                var item = _ActiveAnimation.Current;
                if (item == null || item.Invoke(this))
                    break;
            }
        }

        public void SetSpeed (float speed) {
            _Speed = (long)Math.Round(speed * 10000);
            _SuspendUntil = _SuspendSince + (_SuspendDuration * 10000 / _Speed);
        }
    }
}
