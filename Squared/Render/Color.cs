using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Squared.Util;
using Microsoft.Xna.Framework;

namespace Squared.Render {
    public static class Colors {
        public static Vector4 Transparent {
            get {
                return new Vector4(0.0f, 0.0f, 0.0f, 0.0f);
            }
        }

        public static Vector4 Black {
            get {
                return new Vector4(0.0f, 0.0f, 0.0f, 1.0f);
            }
        }

        public static Vector4 White {
            get {
                return new Vector4(1.0f, 1.0f, 1.0f, 1.0f);
            }
        }

        public static Vector4 Red {
            get {
                return new Vector4(1.0f, 0.0f, 0.0f, 1.0f);
            }
        }

        public static Vector4 Green {
            get {
                return new Vector4(0.0f, 1.0f, 0.0f, 1.0f);
            }
        }

        public static Vector4 Blue {
            get {
                return new Vector4(0.0f, 0.0f, 1.0f, 1.0f);
            }
        }
    }
}
