using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;

namespace Squared.Game {
    public static class GameExtensionMethods {
        public static double NextDouble (this Random random, double min, double max) {
            return (random.NextDouble() * (max - min)) + min;
        }

        public static float NextFloat (this Random random, float min, float max) {
            return ((float)random.NextDouble() * (max - min)) + min;
        }

        public static float Pulse (this float value, float min, float max) {
            value = value % 1.0f;
            float a;
            if (value >= 0.5f) {
                a = (value - 0.5f) / 0.5f;
                return MathHelper.Lerp(max, min, a);
            } else {
                a = value / 0.5f;
                return MathHelper.Lerp(min, max, a);
            }
        }

        public static float PulseExp (this float value, float min, float max) {
            value = value % 1.0f;
            float a;
            if (value >= 0.5f) {
                a = (value - 0.5f) / 0.5f;
                return MathHelper.Lerp(max, min, a * a);
            } else {
                a = value / 0.5f;
                return MathHelper.Lerp(min, max, a * a);
            }
        }

        public static Vector2 Perpendicular (this Vector2 vector) {
            return new Vector2(-vector.Y, vector.X);
        }

        public static Vector2 PerpendicularLeft (this Vector2 vector) {
            return new Vector2(vector.Y, -vector.X);
        }

        public static Vector2 Rotate (this Vector2 vector, float radians) {
            var cos = (float)Math.Cos(radians);
            var sin = (float)Math.Sin(radians);
            return new Vector2(
                (cos * vector.X - sin * vector.Y),
                (sin * vector.X + cos * vector.Y)
            );
        }
    }
}
