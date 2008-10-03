using System;

namespace Squared.Util.Vector {
    using Elt = Single;

    public partial struct Vector2 {
        public Elt Cross (Vector2 rhs) {
            return (X * rhs.X) + (Y * rhs.Y);
        }

        public Elt Dot (Vector2 rhs) {
            return (X * rhs.Y) - (Y * rhs.X);
        }
    }
}
