using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Squared.Game;
using Squared.Util;

namespace Squared.Render {
    public sealed class GammaRamp {
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

    public static class NamedColor {
        private static readonly Dictionary<string, Color?> SystemNamedColorCache = 
            new Dictionary<string, Color?>(StringComparer.OrdinalIgnoreCase);

        public static bool TryParse (string text, out Color result) {
            if (string.IsNullOrWhiteSpace(text)) {
                result = default;
                return false;
            }

            lock (SystemNamedColorCache) {
                if (SystemNamedColorCache.TryGetValue(text, out Color? systemNamedColor)) {
                    result = systemNamedColor ?? default;
                    return systemNamedColor != null;
                }
            }

            var tColor = typeof(Color);
            var prop = tColor.GetProperty(text, BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase);
            if (prop != null) {
                result = (Color)prop.GetValue(null);
                lock (SystemNamedColorCache)
                    SystemNamedColorCache[text] = result;
                return true;
            } else {
                result = default;
                lock (SystemNamedColorCache)
                    SystemNamedColorCache[text] = null;
                return false;
            }
        }
    }

    public struct pSRGBColor : IEquatable<pSRGBColor>, IComparable<pSRGBColor> {
        public static readonly pSRGBColor Transparent = new pSRGBColor(Vector4.Zero, true);
        private static readonly pSRGBColor _Black = new pSRGBColor(new Vector4(0, 0, 0, 1), true),
            _White = new pSRGBColor(Vector4.One, true);

        private bool IsVector4;
        private Vector4 _Vector4;
        private Color _Color;

        public pSRGBColor (int r, int g, int b, float a = 1f) {
            IsVector4 = true;
            // FIXME: sRGB
            _Vector4 = new Vector4(r * a / 255f, g * a / 255f, b * a / 255f, a);
            _Color = default(Color);
        }

        public pSRGBColor (int r, int g, int b, int _a, bool isPremultiplied = false) {
            IsVector4 = true;
            // FIXME: sRGB
            if (isPremultiplied)
                _Vector4 = new Vector4(r / 255f, g / 255f, b / 255f, 1);
            else {
                float a = _a / 255f;
                _Vector4 = new Vector4(r * a / 255f, g * a / 255f, b * a / 255f, a);
            }
            _Color = default(Color);
        }

        public pSRGBColor (float r, float g, float b, float a = 1f, bool isPremultiplied = false) {
            IsVector4 = true;
            // FIXME: sRGB
            if (isPremultiplied)
                _Vector4 = new Vector4(r, g, b, a);
            else
                _Vector4 = new Vector4(r * a, g * a, b * a, a);
            _Color = default(Color);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public pSRGBColor (Color c, bool issRGB = true) {
            if (issRGB) {
                IsVector4 = false;
                _Vector4 = default(Vector4);
                _Color = c;
            } else {
                IsVector4 = true;
                _Vector4 = new Vector4(
                    (float)ColorSpace.LinearByteTosRGBTable[c.R],
                    (float)ColorSpace.LinearByteTosRGBTable[c.G],
                    (float)ColorSpace.LinearByteTosRGBTable[c.B],
                    c.A / 255f
                );
                _Color = default(Color);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public pSRGBColor (Color c, float alpha, bool issRGB = true) {
            if (issRGB && (alpha == 1.0f)) {
                IsVector4 = false;
                _Vector4 = default(Vector4);
                _Color = c;
            } else {
                IsVector4 = true;
                _Color = default(Color);
                if (issRGB) {
                    // FIXME: The alpha/srgb relationship is a bit messed up here
                    _Vector4 = new Vector4(
                        c.R / 255f * alpha,
                        c.G / 255f * alpha,
                        c.B / 255f * alpha,
                        c.A / 255f * alpha
                    );
                } else {
                    _Vector4 = new Vector4(
                        (float)ColorSpace.LinearByteTosRGBTable[c.R] * alpha,
                        (float)ColorSpace.LinearByteTosRGBTable[c.G] * alpha,
                        (float)ColorSpace.LinearByteTosRGBTable[c.B] * alpha,
                        c.A / 255f * alpha
                    );
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public pSRGBColor (Vector4 v4, bool isPremultiplied = true) {
            IsVector4 = true;
            if (!isPremultiplied) {
                float a = v4.W;
                _Vector4 = v4 * a;
                _Vector4.W = a;
            } else {
                _Vector4 = v4;
            }
            _Color = default(Color);
        }

        public static pSRGBColor Black (float opacity = 1.0f) {
            if (opacity == 1.0f)
                return _Black;
            return new pSRGBColor(0, 0, 0, opacity, true);
        }

        public static pSRGBColor White (float opacity = 1.0f) {
            if (opacity == 1.0f)
                return _White;
            float g = (float)ColorSpace.LinearTosRGB(opacity);
            return new pSRGBColor(g, g, g, opacity, true);
        }

        public static pSRGBColor LinearLerp (pSRGBColor a, pSRGBColor b, float t) {
            Vector4 vA = a.ToPLinear(), vB = b.ToPLinear(), result;
            Vector4.Lerp(ref vA, ref vB, t, out result);
            return FromPLinear(in result);
        }

        public static pSRGBColor Lerp (pSRGBColor a, pSRGBColor b, float t) {
            Vector4 vA = a.ToVector4(), vB = b.ToVector4(), result;
            Vector4.Lerp(ref vA, ref vB, t, out result);
            return new pSRGBColor(result, isPremultiplied: true);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public pSRGBColor (in Vector4 v4, bool isPremultiplied = true) {
            IsVector4 = true;
            if (!isPremultiplied) {
                float a = v4.W;
                _Vector4 = v4;
                _Vector4 *= a;
                _Vector4.W = a;
            } else {
                _Vector4 = v4;
            }
            _Color = default(Color);
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
                return _Color;
            } else {
                var v = ToVector4();
                return new Color(v.X, v.Y, v.Z, v.W);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Color ToColor () {
            if (!IsVector4)
                return _Color;
            else {
                var v = ToVector4();
                return new Color(v.X, v.Y, v.Z, v.W);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector4 ToVector4 () {
            if (IsVector4)
                return _Vector4;
            else
                return new Vector4(_Color.R / 255f, _Color.G / 255f, _Color.B / 255f, _Color.A / 255f);
        }

        const float ToGrayR = 0.299f, ToGrayG = 0.587f, ToGrayB = 0.144f;

        public pSRGBColor ToGrayscale (float opacity = 1.0f) {
            var plinear = ToPLinear();
            var g = (plinear.X * ToGrayR) + (plinear.Y * ToGrayG) + (plinear.Z * ToGrayB);
            plinear.X = plinear.Y = plinear.Z = g;
            plinear *= opacity;
            return FromPLinear(plinear);
        }

        public Vector4 ToPLinear (float opacity = 1.0f) {
            var v4 = ToVector4();
            float alpha = v4.W;
            if ((alpha * opacity) <= 0)
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

            result *= alpha * opacity;
            return result;
        }

        public pSRGBColor AdjustBrightness (float factor, bool clamp = true) {
            var vec = ToVector4();
            var newVec = vec * factor;

            float newX = clamp ? Arithmetic.Saturate(newVec.X) : newVec.X,
                newY = clamp ? Arithmetic.Saturate(newVec.Y) : newVec.Y,
                newZ = clamp ? Arithmetic.Saturate(newVec.Z) : newVec.Z,
                excess = Math.Abs(newVec.X - newX) +
                    Math.Abs(newVec.Y - newY) +
                    Math.Abs(newVec.Z - newZ);

            int excessComponents = 3;
            if (newX < newVec.X)
                excessComponents--;
            if (newY < newVec.Y)
                excessComponents--;
            if (newZ < newVec.Z)
                excessComponents--;

            if (excessComponents > 0)
                excess /= excessComponents;
            else
                excess = 0f;

            // HACK: If adjusting the brightness rammed up against the 1.0 limit,
            //  distribute the remaining energy to the other channels and push the
            //  result closer to white to try and approximate the desired increase
            newX = clamp ? Arithmetic.Saturate(newX + excess) : newX + excess;
            newY = clamp ? Arithmetic.Saturate(newY + excess) : newY + excess;
            newZ = clamp ? Arithmetic.Saturate(newZ + excess) : newZ + excess;

            var result = new Vector4(newX, newY, newZ, vec.W);
            return new pSRGBColor(
                result, isPremultiplied: true
            );
        }

        public static pSRGBColor FromPLinear (in Vector4 pLinear) {
            if (pLinear.W <= 0)
                return new pSRGBColor(in pLinear, true);

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
            var result = color.ToVector4() * multiplier;
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
                    return _Vector4.W <= 0;
                else
                    return _Color.PackedValue == 0;
            }
        }

        public bool Equals (pSRGBColor rhs) {
            if (IsVector4 != rhs.IsVector4)
                return ToVector4().FastEquals(rhs.ToVector4());

            if (IsVector4) {
                return _Vector4.FastEquals(in rhs._Vector4);
            } else {
                return _Color == rhs._Color;
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
                    ? _Vector4.GetHashCode()
                    : _Color.GetHashCode()
            );
        }

        public override string ToString () {
            var v4 = ToVector4();
            const int digits = 3;
            return $"{{{Math.Round(v4.X, digits, MidpointRounding.AwayFromZero)}, {Math.Round(v4.Y, digits, MidpointRounding.AwayFromZero)}, {Math.Round(v4.Z, digits, MidpointRounding.AwayFromZero)}, {Math.Round(v4.W, digits, MidpointRounding.AwayFromZero)}}}";
        }

        public static bool TryParse (string text, out object result) {
            if (TryParse(text, out pSRGBColor _result)) {
                result = _result;
                return true;
            }

            result = null;
            return false;
        }

        public static bool TryParse (string text, out pSRGBColor result) {
            if (TryParseNumeric(text, out result))
                return true;
            if (NamedColor.TryParse(text, out Color namedColor)) {
                result = new pSRGBColor(namedColor, true);
                return true;
            }
            result = default;
            return false;
        }

        private static bool TryParseNumeric (string text, out pSRGBColor result) {
            result = default;
            if (string.IsNullOrWhiteSpace(text))
                return false;
            int o1 = 0, o2 = 0;
            text = text.Trim();
            if (text.StartsWith("{"))
                o1 += 1;
            if (text.EndsWith("}"))
                o2 += 1;
            text = text.Substring(o1, text.Length - o1 - o2).Trim();
            if (string.IsNullOrWhiteSpace(text))
                return false;
            var values = text.Split(',');
            if ((values.Length < 3) || (values.Length > 4))
                return false;
            if (!float.TryParse(values[0], out float r))
                return false;
            if (!float.TryParse(values[1], out float g))
                return false;
            if (!float.TryParse(values[2], out float b))
                return false;

            float a = 1;
            bool isPremultiplied = true;
            if (values.Length > 3) {
                var v3 = values[3].Trim();
                if (v3.StartsWith("*")) {
                    isPremultiplied = false;
                    v3 = v3.Substring(1);
                }
                if (!float.TryParse(v3, out a))
                    return false;
            }

            result = new pSRGBColor(r, g, b, a, isPremultiplied);
            return true;
        }

        public int CompareTo (pSRGBColor other) {
            var v4l = ToVector4();
            var v4r = other.ToVector4();
            var result = v4l.X.CompareTo(v4r.X);
            if (result == 0)
                result = v4l.Y.CompareTo(v4r.Y);
            if (result == 0)
                result = v4l.Z.CompareTo(v4r.Z);
            if (result == 0)
                result = v4l.W.CompareTo(v4r.W);
            return result;
        }
    }
}
