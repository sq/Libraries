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
    public class Polygon {
        public Polygon (Vector2[] vertices) {
        }
    }

    public delegate bool ResolveMotionPredicate (Vector2 oldVelocity, Vector2 newVelocity);

    public static class Geometry {
        public static void GetEdgeNormal (ref Vector2 first, ref Vector2 second, out Vector2 result) {
            var edgeVector = second - first;
            result = new Vector2(-edgeVector.Y, edgeVector.X);
            result.Normalize();
        }

        public static Interval<float> ProjectOntoAxis (Vector2 axis, Vector2[] vertices) {
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

                    var intervalA = ProjectOntoAxis(axis, verticesA);
                    var intervalB = ProjectOntoAxis(axis, verticesB);

                    bool intersects = intervalA.Intersects(intervalB);

                    if (!intersects)
                        result = false;
                }
            }

            return result;
        }

        public struct ResolvedMotion {
            public bool AreIntersecting;
            public bool WouldHaveIntersected;
            public bool WillBeIntersecting;
            public Vector2 ResultVelocity;
        }

        private static void FloatSubtractor (ref float lhs, ref float rhs, out float result) {
            result = lhs - rhs;
        }

        public static ResolvedMotion ResolvePolygonMotion (Vector2[] verticesA, Vector2[] verticesB, Vector2 velocityA, ResolveMotionPredicate predicate) {
            return ResolvePolygonMotion(verticesA, verticesB, velocityA, null, predicate);
        }

        public static ResolvedMotion ResolvePolygonMotion (Vector2[] verticesA, Vector2[] verticesB, Vector2 velocityA, Vector2[] checkAxes, ResolveMotionPredicate predicate) {
            var result = new ResolvedMotion();
            result.AreIntersecting = true;
            result.WouldHaveIntersected = true;
            result.WillBeIntersecting = true;

            float velocityProjection;
            var velocityLength = velocityA.Length();
            var velocityAxis = velocityA;
            velocityAxis.Normalize();

            Interval<float> intervalA, intervalB;
            float minDistance = float.MaxValue;

            int bufferSize = verticesA.Length + verticesB.Length;
            if (checkAxes != null)
                bufferSize += checkAxes.Length;

            using (var axisBuffer = BufferPool<Vector2>.Allocate(bufferSize)) {
                int axisCount = 0;
                if (checkAxes != null) {
                    Array.Copy(checkAxes, axisBuffer.Data, checkAxes.Length);
                    axisCount += checkAxes.Length;
                }

                GetPolygonAxes(axisBuffer.Data, ref axisCount, verticesA);
                GetPolygonAxes(axisBuffer.Data, ref axisCount, verticesB);

                for (int i = 0; i < axisCount; i++) {
                    var axis = axisBuffer.Data[i];

                    intervalA = ProjectOntoAxis(axis, verticesA);
                    intervalB = ProjectOntoAxis(axis, verticesB);

                    bool intersects = intervalA.Intersects(intervalB);
                    if (!intersects)
                        result.AreIntersecting = false;

                    Vector2.Dot(ref axis, ref velocityA, out velocityProjection);

                    var newIntervalA = new Interval<float>(intervalA.Min + velocityProjection, intervalA.Max + velocityProjection);

                    var intersectionDistance = newIntervalA.GetDistance(intervalB, FloatSubtractor);
                    intersects = intersectionDistance < 0;
                    if (!intersects)
                        result.WouldHaveIntersected = false;
                    else {
                        intersectionDistance = Math.Abs(intersectionDistance);
                        if (intersectionDistance < minDistance) {
                            var minVect = axis * intersectionDistance;
                            var newVelocity = velocityA + minVect;
                            bool accept = true;

                            if (newVelocity.Length() > velocityLength) {
                                accept = false;

                                /*
                                newVelocity.Normalize();
                                newVelocity *= velocityLength;

                                Vector2.Dot(ref axis, ref newVelocity, out velocityProjection);
                                newIntervalA = new Interval<float>(intervalA.Min + velocityProjection, intervalA.Max + velocityProjection);

                                if (newIntervalA.Intersects(intervalB)) {
                                    // Trivial rejection: This vector can only resolve the collision by moving object A through object B entirely, which is not desirable
                                    accept = false;
                                }
                                 */
                            }

                            if (accept)
                                accept &= predicate(velocityA, newVelocity);

                            if (accept) {
                                result.ResultVelocity = newVelocity;
                                result.WillBeIntersecting = false;
                                minDistance = intersectionDistance;
                            }

                            /*
                            Console.WriteLine(
                                "axis={0}, velocity={1}, newVelocity={2}, predResult={3}",
                                axis, velocityA, newVelocity, predResult
                            );
                            */
                        }
                    }

                    if ((result.WouldHaveIntersected == false) && (result.AreIntersecting == false)) {
                        result.WillBeIntersecting = false;
                        result.ResultVelocity = velocityA;
                        break;
                    }
                }
            }

            /*
                Console.WriteLine(
                    "{0} -> {{ wouldHave={1}, willBe={2}, resultVel={3} }}", velocityA, result.WouldHaveIntersected, result.WillBeIntersecting, result.ResultVelocity
                );
             */

            return result;
        }
    }
}
