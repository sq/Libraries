using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Util;

namespace Squared.Game {
    public static class GameExtensionMethods {
        public static double NextDouble (this Random random, double min, double max) {
            return (random.NextDouble() * (max - min)) + min;
        }

        public static float NextFloat (this Random random, float min, float max) {
            return ((float)random.NextDouble() * (max - min)) + min;
        }

        public static Vector2 Perpendicular (this Vector2 vector) {
            return new Vector2(-vector.Y, vector.X);
        }

        public static Vector2 PerpendicularLeft (this Vector2 vector) {
            return new Vector2(vector.Y, -vector.X);
        }

        public static Vector2 Floor (this Vector2 vector) {
            return new Vector2((float)Math.Floor(vector.X), (float)Math.Floor(vector.Y));
        }

        public static Vector2 Round (this Vector2 vector) {
            return new Vector2((float)Math.Round(vector.X), (float)Math.Round(vector.Y));
        }

        public static Vector2 Round (this Vector2 vector, int decimals) {
            return new Vector2((float)Math.Round(vector.X, decimals), (float)Math.Round(vector.Y, decimals));
        }

        public static Vector3 Round (this Vector3 vector) {
            return new Vector3((float)Math.Round(vector.X), (float)Math.Round(vector.Y), (float)Math.Round(vector.Z));
        }

        public static Vector3 Round (this Vector3 vector, int decimals) {
            return new Vector3((float)Math.Round(vector.X, decimals), (float)Math.Round(vector.Y, decimals), (float)Math.Round(vector.Z, decimals));
        }

        public static Vector2 Rotate (this Vector2 vector, float radians) {
            var cos = (float)Math.Cos(radians);
            var sin = (float)Math.Sin(radians);
            return new Vector2(
                (cos * vector.X - sin * vector.Y),
                (sin * vector.X + cos * vector.Y)
            );
        }

        public static float Dot (this Vector2 @this, ref Vector2 rhs) {
            float result;
            Vector2.Dot(ref @this, ref rhs, out result);
            return result;
        }

        public static Bounds Bounds (this Texture2D @this) {
            if (@this == null)
                return default(Bounds);

            Rectangle r = new Rectangle(0, 0, @this.Width, @this.Height);
            return BoundsFromRectangle(@this, ref r);
        }

        public static Bounds BoundsFromRectangle (int width, int height, ref Rectangle rectangle) {
            float fw = width;
            float fh = height;
            float rw = rectangle.Width;
            float rh = rectangle.Height;
            float xScale = 1f / fw, yScale = 1f / fh;
            float offsetX = xScale * -0.0f;
            float offsetY = yScale * -0.0f;
            var tl = new Vector2(rectangle.Left * xScale + offsetX, rectangle.Top * yScale + offsetY);
            var br = new Vector2(tl.X + (rw * xScale), tl.Y + (rh * yScale));
            return new Bounds(tl, br);
        }

        public static Bounds BoundsFromRectangle (this Texture2D @this, ref Rectangle rectangle) {
            if (@this == null)
                return default(Bounds);

            return BoundsFromRectangle(@this.Width, @this.Height, ref rectangle);
        }

        public static Bounds BoundsFromRectangle (this Texture2D @this, Rectangle rectangle) {
            return @this.BoundsFromRectangle(ref rectangle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsFinite (this Vector2 v) {
            return Arithmetic.IsFinite(v.X) && Arithmetic.IsFinite(v.Y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsFinite (this Vector3 v) {
            return Arithmetic.IsFinite(v.X) &&
                Arithmetic.IsFinite(v.Y) &&
                Arithmetic.IsFinite(v.Z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool AssertFinite (this Vector4 v) {
            return Arithmetic.IsFinite(v.X) &&
                Arithmetic.IsFinite(v.Y) &&
                Arithmetic.IsFinite(v.Z) &&
                Arithmetic.IsFinite(v.W);
        }

        const float ToGrayR = 0.299f / 255,
            ToGrayG = 0.587f / 255,
            ToGrayB = 0.144f / 255;

        public static Color ToGrayscale (this Color c, float alphaMultiplier = 1) {
            var sum = (c.R * ToGrayR + c.G * ToGrayG + c.B * ToGrayB) * alphaMultiplier;
            return new Color(
                sum, sum, sum, 
                c.A * alphaMultiplier
            );
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe bool FastEquals (this Vector2 lhs, ref Vector2 rhs) {
            return (lhs.X == rhs.X) &&
                (lhs.Y == rhs.Y);
            /*
            unchecked {
                var pLhs = (UInt32*)&lhs;
                fixed (Vector2* _pRhs = &rhs) {
                    var pRhs = (UInt32*)_pRhs;
                    return pLhs[0] == pRhs[0] &&
                        pLhs[1] == pRhs[1];
                }
            }
            */
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe bool FastEquals (this Vector3 lhs, ref Vector3 rhs) {
            return (lhs.X == rhs.X) &&
                (lhs.Y == rhs.Y) &&
                (lhs.Z == rhs.Z);
            /*
            unchecked {
                var pLhs = (UInt32*)&lhs;
                fixed (Vector3* _pRhs = &rhs) {
                    var pRhs = (UInt32*)_pRhs;
                    return pLhs[0] == pRhs[0] &&
                        pLhs[1] == pRhs[1] &&
                        pLhs[2] == pRhs[2];
                }
            }
            */
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe bool FastEquals (this Vector4 lhs, ref Vector4 rhs) {
            return (lhs.X == rhs.X) &&
                (lhs.Y == rhs.Y) &&
                (lhs.Z == rhs.Z) &&
                (lhs.W == rhs.W);
            /*
            unchecked {
                var pLhs = (UInt32*)&lhs;
                fixed (Vector4* _pRhs = &rhs) {
                    var pRhs = (UInt32*)_pRhs;
                    return pLhs[0] == pRhs[0] &&
                        pLhs[1] == pRhs[1] &&
                        pLhs[2] == pRhs[2] &&
                        pLhs[3] == pRhs[3];
                }
            }
            */
        }
    }
}
