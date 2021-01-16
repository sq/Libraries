using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Squared.Game;
using Squared.Util;

namespace Squared.Render {
    public class GammaRamp {
        public readonly double Gamma;
        public readonly byte[] GammaTable, InvGammaTable;

        public GammaRamp (double gamma) {
            GammaTable = new byte[256];
            InvGammaTable = new byte[256];

            double g = Gamma = Math.Round(gamma, 3, MidpointRounding.AwayFromZero);
            double gInv = 1.0 / g;
            for (int i = 0; i < 256; i++) {
                if (g == 1) {
                    InvGammaTable[i] = GammaTable[i] = (byte)i;
                } else {
                    var gD = i / 255.0;
                    var inv = (byte)(Math.Pow(gD, gInv) * 255.0);
                    GammaTable[i] = inv;
                    InvGammaTable[inv] = (byte)i;
                }
            }
        }
    }

    public struct pSRGBColor {
        public bool IsVector4;
        public Vector4 Vector4;
        public Color Color;

        public pSRGBColor (int r, int g, int b, float a = 1f) {
            IsVector4 = true;
            Vector4 = new Vector4(r * a, g * a, b * a, a);
            Color = default(Color);
        }

        public pSRGBColor (int r, int g, int b, int _a) {
            IsVector4 = true;
            float a = _a / 255.0f;
            Vector4 = new Vector4(r * a, g * a, b * a, a);
            Color = default(Color);
        }

        public pSRGBColor (float r, float g, float b, float a = 1f) {
            IsVector4 = true;
            Vector4 = new Vector4(r * a, g * a, b * a, a);
            Color = default(Color);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public pSRGBColor (Color c, bool issRGB = true) {
            if (issRGB) {
                IsVector4 = false;
                Vector4 = default(Vector4);
                Color = c;
            } else {
                IsVector4 = true;
                Vector4 = new Vector4(
                    ColorSpace.sRGBByteToLinearFloatTable[c.R],
                    ColorSpace.sRGBByteToLinearFloatTable[c.G],
                    ColorSpace.sRGBByteToLinearFloatTable[c.B],
                    ColorSpace.sRGBByteToLinearFloatTable[c.A]
                );
                Color = default(Color);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public pSRGBColor (Vector4 v4, bool isPremultiplied = true) {
            IsVector4 = true;
            if (!isPremultiplied) {
                float a = v4.W;
                Vector4 = v4 * a;
                Vector4.W = a;
            } else {
                Vector4 = v4;
            }
            Color = default(Color);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public pSRGBColor (ref Vector4 v4, bool isPremultiplied = true) {
            IsVector4 = true;
            if (!isPremultiplied) {
                float a = v4.W;
                Vector4 = v4;
                Vector4 *= a;
                Vector4.W = a;
            } else {
                Vector4 = v4;
            }
            Color = default(Color);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Color ToLinearColor () {
            var v = ToPLinear();
            return new Color(v.X, v.Y, v.Z, v.W);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Color ToColor (bool sRGB = true) {
            if (!sRGB) {
                var linear = ToPLinear();
                return new Color(linear.X, linear.Y, linear.Z, linear.W);
            } else if (!IsVector4) {
                return Color;
            } else {
                var v = ToVector4();
                return new Color(v.X, v.Y, v.Z, v.W);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Color ToColor () {
            if (!IsVector4)
                return Color;
            else {
                var v = ToVector4();
                return new Color(v.X, v.Y, v.Z, v.W);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector4 ToVector4 () {
            if (IsVector4)
                return Vector4;
            else
                return new Vector4(Color.R / 255f, Color.G / 255f, Color.B / 255f, Color.A / 255f);
        }

        public Vector4 ToPLinear () {
            var v4 = ToVector4();
            float alpha = v4.W;
            if (alpha <= 0)
                return Vector4.Zero;

            // Unpremultiply
            v4 *= (1.0f / alpha);

            // Compute low/high linear pairs from the sRGB values
            var low = v4 / 12.92f;
            var preHigh = (v4 + new Vector4(0.055f)) / 1.055f;
            var high = new Vector3(
                (float)Math.Pow(preHigh.X, 2.4),
                (float)Math.Pow(preHigh.Y, 2.4),
                (float)Math.Pow(preHigh.Z, 2.4)
            );
            // Select low/high value based on threshold
            var result = new Vector4(
                v4.X <= 0.04045f ? low.X : high.X,
                v4.Y <= 0.04045f ? low.Y : high.Y,
                v4.Z <= 0.04045f ? low.Z : high.Z,
                1
            );

            result *= alpha;
            return result;
        }

        public pSRGBColor AdjustBrightness (float factor) {
            var result = ToVector4();
            result.X *= factor;
            result.Y *= factor;
            result.Z *= factor;
            return new pSRGBColor(result, isPremultiplied: true);
        }

        public static pSRGBColor FromPLinear (ref Vector4 pLinear) {
            if (pLinear.W <= 0)
                return new pSRGBColor(ref pLinear, true);

            var linear = pLinear / pLinear.W;
            linear.W = pLinear.W;

            var low = linear / 12.92f;
            var inv2_4 = 1.0 / 2.4;
            var high = new Vector4(
                (float)((1.055 * Math.Pow(linear.X, inv2_4)) - 0.055),
                (float)((1.055 * Math.Pow(linear.Y, inv2_4)) - 0.055),
                (float)((1.055 * Math.Pow(linear.Z, inv2_4)) - 0.055),
                pLinear.W
            );
            var result = new Vector4(
                linear.X < 0.0031308f ? low.X : high.X,
                linear.Y < 0.0031380f ? low.Y : high.Y,
                linear.Z < 0.0031380f ? low.Z : high.Z,
                1f
            );
            result *= pLinear.W;
            return result;
        }

        public float ColorDelta {
            get {
                var v = ToVector4();
                return Math.Abs(v.X - v.Y) + Math.Abs(v.Z - v.Y) + Math.Abs(v.Z - v.X);
            }
        }

        public static pSRGBColor FromPLinear (Vector4 v) {
            return FromPLinear(ref v);
        }

        public static pSRGBColor operator - (pSRGBColor a, pSRGBColor b) {
            return new pSRGBColor(a.ToVector4() - b.ToVector4());
        }

        public static pSRGBColor operator - (pSRGBColor color, float inverseDelta) {
            return color + (-inverseDelta);
        }

        public static pSRGBColor operator + (pSRGBColor color, float delta) {
            if (Math.Abs(delta) < 0.001f)
                return color;

            var result = color.ToVector4();
            var alpha = Math.Max(result.W, 0.001f);
            result.X /= alpha; result.Y /= alpha; result.Z /= alpha;
            result.X += delta; result.Y += delta; result.Z += delta;
            alpha = Arithmetic.Saturate(alpha + delta);
            result.X *= alpha; result.Y *= alpha; result.Z *= alpha;
            result.W = alpha;
            return new pSRGBColor(result);
        }

        public static pSRGBColor operator + (pSRGBColor a, pSRGBColor b) {
            return new pSRGBColor(a.ToVector4() + b.ToVector4());
        }

        public static pSRGBColor operator * (pSRGBColor color, float multiplier) {
            var result = color.ToVector4();
            result *= multiplier;
            return new pSRGBColor(result, true);
        }

        public static implicit operator pSRGBColor (Vector4 v4) {
            return new pSRGBColor(v4);
        }

        public static implicit operator pSRGBColor (Color c) {
            return new pSRGBColor(c);
        }

        public bool IsTransparent {
            get {
                if (IsVector4)
                    return Vector4.W <= 0;
                else
                    return Color.PackedValue == 0;
            }
        }

        public bool Equals (pSRGBColor rhs) {
            if (IsVector4 != rhs.IsVector4)
                return false;

            if (IsVector4) {
                return Vector4.FastEquals(ref rhs.Vector4);
            } else {
                return Color == rhs.Color;
            }
        }

        public override bool Equals (object obj) {
            if (obj is pSRGBColor)
                return Equals((pSRGBColor)obj);
            else if (obj is Vector4)
                return Equals((pSRGBColor)(Vector4)obj);
            else if (obj is Color)
                return Equals((pSRGBColor)(Color)obj);
            else
                return false;
        }

        public static bool operator == (pSRGBColor lhs, pSRGBColor rhs) {
            return lhs.Equals(rhs);
        }

        public static bool operator != (pSRGBColor lhs, pSRGBColor rhs) {
            return !lhs.Equals(rhs);
        }

        public override int GetHashCode () {
            return (
                IsVector4 
                    ? Vector4.GetHashCode()
                    : Color.GetHashCode()
            );
        }
    }
}
