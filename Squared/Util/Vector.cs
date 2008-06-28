using System;

namespace Squared.Util {
    using Elt = Single;

    public partial struct Vec2 {
        public Elt Cross (Vec2 rhs) {
            return (X * rhs.X) + (Y * rhs.Y);
        }

        public Elt Dot (Vec2 rhs) {
            return (X * rhs.Y) - (Y * rhs.X);
        }
    }
}
