using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.GamerServices;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Media;
using Microsoft.Xna.Framework.Net;
using Microsoft.Xna.Framework.Storage;
using Squared.Util;

namespace Squared.Game {
    public static class Geometry {
        public static void GetEdgeNormal (ref Vector2 first, ref Vector2 second, out Vector2 result) {
            var edgeVector = second - first;
            result = new Vector2(-edgeVector.Y, edgeVector.X);
            result.Normalize();
        }

        public static Interval<float> ComputeInterval (Vector2 axis, Vector2[] vertices) {
            var result = new Interval<float>(0.0f, 0.0f);
            float d = 0.0f;

            result.Min = float.MaxValue;
            result.Max = float.MinValue;

            for (int i = 0; i < vertices.Length; i++) {
                Vector2.Dot(ref vertices[i], ref axis, out d);

                if (d < result.Min)
                    result.Min = d;
                if (d > result.Max)
                    result.Max = d;
            }

            return result;
        }

        public static void GetPolygonAxes (Vector2[] buffer, ref int bufferCount, Vector2[] polygon) {
            if ((buffer.Length - bufferCount) < polygon.Length)
                throw new ArgumentException(
                    String.Format(
                        "Not enough remaining space in the buffer ({0}/{1}) for all the polygon's potential axes ({2}).",
                        (buffer.Length - bufferCount), buffer.Length, polygon.Length
                    ),
                    "buffer"
                );

            bool done = false;
            int i = 0;
            Vector2 firstPoint = new Vector2(), current = new Vector2();
            Vector2 previous, axis;

            while (!done) {
                previous = current;

                if (i >= polygon.Length) {
                    done = true;
                    current = firstPoint;
                } else {
                    current = polygon[i];
                }

                if (i == 0) {
                    firstPoint = current;
                    i += 1;
                    continue;
                }

                GetEdgeNormal(ref previous, ref current, out axis);

                if (Array.IndexOf(buffer, axis, 0, bufferCount) == -1) {
                    buffer[bufferCount] = axis;
                    bufferCount += 1;
                }

                i += 1;
            }
        }

        public static bool DoPolygonsIntersect (Vector2[] verticesA, Vector2[] verticesB) {
            bool result = true;

            using (var axisBuffer = BufferPool<Vector2>.Allocate(verticesA.Length + verticesB.Length)) {
                int axisCount = 0;
                GetPolygonAxes(axisBuffer.Data, ref axisCount, verticesA);
                GetPolygonAxes(axisBuffer.Data, ref axisCount, verticesB);

                for (int i = 0; i < axisCount; i++) {
                    var axis = axisBuffer.Data[i];

                    var intervalA = ComputeInterval(axis, verticesA);
                    var intervalB = ComputeInterval(axis, verticesB);

                    bool intersects = intervalA.Intersects(intervalB);

                    if (!intersects)
                        result = false;
                }
            }

            return result;
        }

        public struct IntersectionInfo {
            public bool AreIntersecting;
            public bool WillBeIntersecting;
        }

        public static IntersectionInfo WillPolygonsIntersect (Vector2[] verticesA, Vector2[] verticesB, Vector2 relativeTranslationA) {
            var result = new IntersectionInfo();
            result.AreIntersecting = true;
            result.WillBeIntersecting = true;

            Interval<float> intervalA, intervalB;
            float translationProjection;

            using (var axisBuffer = BufferPool<Vector2>.Allocate(verticesA.Length + verticesB.Length)) {
                int axisCount = 0;
                GetPolygonAxes(axisBuffer.Data, ref axisCount, verticesA);
                GetPolygonAxes(axisBuffer.Data, ref axisCount, verticesB);

                for (int i = 0; i < axisCount; i++) {
                    var axis = axisBuffer.Data[i];

                    intervalA = ComputeInterval(axis, verticesA);
                    intervalB = ComputeInterval(axis, verticesB);

                    bool intersects = intervalA.Intersects(intervalB);
                    if (!intersects)
                        result.AreIntersecting = false;

                    Vector2.Dot(ref axis, ref relativeTranslationA, out translationProjection);

                    intervalA.Min += translationProjection;
                    intervalA.Max += translationProjection;

                    intersects = intervalA.Intersects(intervalB);
                    if (!intersects)
                        result.WillBeIntersecting = false;

                    if ((result.WillBeIntersecting == false) && (result.AreIntersecting == false))
                        break;
                }
            }

            return result;
        }
    }
}
