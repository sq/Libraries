using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;

namespace Squared.Util {
    public static class Fixed {
        public const Int32 Q = 14;
        public const Int64 FactorI = 16384; // Math.Pow(2, Q)
        public const float FactorF = FactorI;
        public const double FactorD = FactorI;
        public const Int64 K = (1 << (Q - 1));

        public static Int64 FromInteger (Int32 value) {
            unchecked {
                return value * FactorI;
            }
        }

        public static Int64 FromFloat (float value) {
            unchecked {
                return (Int64)Math.Round(value * FactorF);
            }
        }

        public static Int64 FromDouble (double value) {
            unchecked {
                return (Int64)Math.Round(value * FactorD);
            }
        }

        public static Int32 ToInteger (Int64 fixd) {
            unchecked {
                return (Int32)(fixd / FactorI);
            }
        }

        public static float ToFloat (Int64 fixd) {
            unchecked {
                return fixd / FactorF;
            }
        }

        public static double ToDouble (Int64 fixd) {
            unchecked {
                return fixd / FactorD;
            }
        }

        public static Int64 Add (Int64 lhs, Int64 rhs) {
            unchecked {
                return lhs + rhs;
            }
        }

        public static Int64 Subtract (Int64 lhs, Int64 rhs) {
            unchecked {
                return lhs - rhs;
            }
        }

        public static Int64 Multiply (Int64 lhs, Int64 rhs) {
            unchecked {
                return ((lhs * rhs) + K) >> Q;
            }
        }

        public static Int64 Divide (Int64 lhs, Int64 rhs) {
            unchecked {
                return ((lhs << Q) + (rhs / 2)) / rhs;
            }
        }
    }
}
