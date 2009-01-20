using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Squared.Util;

namespace Squared.Game.Animation {
    public interface AnimCmd {
    }

    public class SetAnimation : AnimCmd {
        public IEnumerator<AnimCmd> Animation;
    }

    public class SetFrame : AnimCmd {
        public int Group;
        public int Frame;
    }

    public class Delay : AnimCmd {
        public long Duration;
    }

    public class Animator {
        public ITimeProvider TimeProvider = Time.DefaultTimeProvider;
        private IEnumerator<AnimCmd> _ActiveAnimation = null;
        private int _Group = 0, _Frame = 0;
        private long _SuspendUntil = 0;

        public void SetAnimation (IEnumerator<AnimCmd> animation) {
            if (_ActiveAnimation != null)
                _ActiveAnimation.Dispose();

            _ActiveAnimation = animation;
            _SuspendUntil = TimeProvider.Ticks;
        }

        public void SetFrame (int group, int frame) {
            _Group = group;
            _Frame = frame;
        }

        public int Group {
            get { return _Group; }
        }

        public int Frame {
            get { return _Frame; }
        }

        public void Update () {
            long now = TimeProvider.Ticks;
            while (now >= _SuspendUntil) {
                if (!_ActiveAnimation.MoveNext())
                    throw new Exception("Animation terminated prematurely");

                var item = _ActiveAnimation.Current;

                if (item is SetAnimation) {
                    var _ = (SetAnimation)item;
                    SetAnimation(_.Animation);
                } else if (item is SetFrame) {
                    var _ = (SetFrame)item;
                    SetFrame(_.Group, _.Frame);
                } else if (item is Delay) {
                    var _ = (Delay)item;
                    _SuspendUntil = _SuspendUntil + _.Duration;
                } else {
                    throw new Exception("Invalid animation command");
                }
            }
        }
    }
}
