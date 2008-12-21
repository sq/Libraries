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
    public class Vector2Comparer : IEqualityComparer<Vector2> {
        public bool Equals (Vector2 x, Vector2 y) {
            return (x.X == y.X) && (x.Y == y.Y);
        }

        public int GetHashCode (Vector2 obj) {
            return obj.X.GetHashCode() + obj.Y.GetHashCode();
        }
    }

    public class Polygon {
        private Vector2 _Position = new Vector2(0, 0);
        protected Vector2[] _Vertices;
        protected Vector2[] _TranslatedVertices;
        protected bool _Dirty = true;

        internal Vector2[] AxisCache = null;
        
        public Polygon (Vector2[] vertices) {
            _Vertices = vertices;
            _TranslatedVertices = new Vector2[vertices.Length];
        }

        protected virtual void ClearDirtyFlag () {
            for (int i = 0; i < _Vertices.Length; i++) {
                _TranslatedVertices[i] = _Vertices[i] + _Position;
            }

            _Dirty = false;
        }

        public int Count {
            get {
                return _Vertices.Length;
            }
        }

        public Vector2 Position {
            get {
                return _Position;
            }
            set {
                _Position = value;
                _Dirty = true;
            }
        }

        public Vector2 this[int index] {
            get {
                if (_Dirty)
                    ClearDirtyFlag();
                
                return _TranslatedVertices[index];
            }
        }

        public void SetVertex (int index, Vector2 newVertex) {
            _Vertices[index] = newVertex;
            AxisCache = null;
            _Dirty = true;
        }

        internal Vector2[] GetRawVertices () {
            return _Vertices;
        }

        public Vector2[] GetVertices () {
            if (_Dirty)
                ClearDirtyFlag();

            return _TranslatedVertices;
        }
    }

    public static class Geometry {
        public static Interval ProjectOntoAxis (Vector2 axis, Polygon polygon) {
            Interval result;
            var vertices = polygon.GetVertices();
            float d = Vector2.Dot(axis, vertices[0]);
            result = new Interval(d, d);

            int length = polygon.Count;
            for (int i = 1; i < length; i++) {
                d = Vector2.Dot(vertices[i], axis);

                if (d < result.Min)
                    result.Min = d;
                if (d > result.Max)
                    result.Max = d;
            }

            return result;
        }

        public static void GetPolygonAxes (Vector2[] buffer, ref int bufferCount, Polygon polygon) {
            int length = polygon.Count;

            if ((buffer.Length - bufferCount) < length)
                throw new ArgumentException(
                    String.Format(
                        "Not enough remaining space in the buffer ({0}/{1}) for all the polygon's potential axes ({2}).",
                        (buffer.Length - bufferCount), buffer.Length, length
                    ),
                    "buffer"
                );

            if (polygon.AxisCache != null) {
                Array.Copy(polygon.AxisCache, 0, buffer, bufferCount, length);
                bufferCount += polygon.Count;
                return;
            }

            bool done = false;
            int i = 0;
            Vector2 firstPoint = new Vector2(), current = new Vector2();
            Vector2 previous, axis = new Vector2();

            while (!done) {
                previous = current;

                if (i >= length) {
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

                axis.X = -(current.Y - previous.Y);
                axis.Y = current.X - previous.X;
                axis.Normalize();

                buffer[bufferCount] = axis;
                bufferCount += 1;

                i += 1;
            }

            polygon.AxisCache = new Vector2[length];
            Array.Copy(buffer, bufferCount - length, polygon.AxisCache, 0, length);
        }

        public static bool DoPolygonsIntersect (Polygon polygonA, Polygon polygonB) {
            using (var axisBuffer = BufferPool<Vector2>.Allocate(polygonA.Count + polygonB.Count)) {
                int axisCount = 0;
                GetPolygonAxes(axisBuffer.Data, ref axisCount, polygonA);
                GetPolygonAxes(axisBuffer.Data, ref axisCount, polygonB);

                for (int i = 0; i < axisCount; i++) {
                    var axis = axisBuffer.Data[i];

                    var intervalA = ProjectOntoAxis(axis, polygonA);
                    var intervalB = ProjectOntoAxis(axis, polygonB);

                    bool intersects = intervalA.Intersects(intervalB);

                    if (!intersects)
                        return false;
                }
            }

            return true;
        }

        public static bool DoLinesIntersect (Vector2 startA, Vector2 endA, Vector2 startB, Vector2 endB, out Vector2 intersection) {
            intersection = new Vector2();

            var lengthA = endA - startA;
            var lengthB = endB - startB;

            float q = (startA.Y - startB.Y) * (endB.X - startB.X) - (startA.X - startB.X) * (endB.Y - startB.Y);
            float d = lengthA.X * (endB.Y - startB.Y) - lengthA.Y * (endB.X - startB.X);

            if (d == 0.0f) return false;

            d = 1 / d;
            float r = q * d;

            if (r < 0.0f || r > 1.0f) return false;

            q = (startA.Y - startB.Y) * lengthA.X - (startA.X - startB.X) * lengthA.Y;
            float s = q * d;

            if (s < 0.0f || s > 1.0f ) return false;

            intersection = startA + (r * lengthA);

            return true;
        }

        public static float? LineIntersectPolygon (Vector2 start, Vector2 end, Polygon polygon) {
            float? result = null;

            bool done = false;
            int i = 0, length = polygon.Count;
            float minDistance = float.MaxValue;
            Vector2 firstPoint = new Vector2(), current = new Vector2();
            Vector2 previous, intersection;

            while (!done) {
                previous = current;

                if (i >= length) {
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

                if (DoLinesIntersect(start, end, previous, current, out intersection)) {
                    var distance = (intersection - start).LengthSquared();
                    if (distance < minDistance) {
                        minDistance = distance;
                        result = (float)Math.Sqrt(distance);
                    }
                }

                i += 1;
            }

            return result;
        }

        public struct ResolvedMotion {
            public bool AreIntersecting;
            public bool WouldHaveIntersected;
            public bool WillBeIntersecting;
            public Vector2 ResultVelocity;
        }

        public static ResolvedMotion ResolvePolygonMotion (Polygon polygonA, Polygon polygonB, Vector2 velocityA) {
            var result = new ResolvedMotion();
            result.AreIntersecting = true;
            result.WouldHaveIntersected = true;
            result.WillBeIntersecting = true;

            float velocityProjection;
            var velocityDistance = velocityA.Length();
            var velocityAxis = Vector2.Normalize(velocityA);

            Interval intervalA, intervalB;
            float minDistance = float.MaxValue;

            int bufferSize = polygonA.Count + polygonB.Count + 4;
            using (var axisBuffer = BufferPool<Vector2>.Allocate(bufferSize)) {
                int axisCount = 0;

                if (velocityA.LengthSquared() > 0) {
                    axisCount += 4;
                    axisBuffer.Data[0] = Vector2.Normalize(velocityA);
                    axisBuffer.Data[1] = new Vector2(-axisBuffer.Data[0].X, axisBuffer.Data[0].Y);
                    axisBuffer.Data[2] = new Vector2(axisBuffer.Data[0].X, -axisBuffer.Data[0].Y);
                    axisBuffer.Data[3] = new Vector2(-axisBuffer.Data[0].X, -axisBuffer.Data[0].Y);
                }

                GetPolygonAxes(axisBuffer.Data, ref axisCount, polygonA);
                GetPolygonAxes(axisBuffer.Data, ref axisCount, polygonB);

                for (int i = 0; i < axisCount; i++) {
                    var axis = axisBuffer.Data[i];

                    intervalA = ProjectOntoAxis(axis, polygonA);
                    intervalB = ProjectOntoAxis(axis, polygonB);

                    bool intersects = intervalA.Intersects(intervalB);
                    if (!intersects)
                        result.AreIntersecting = false;

                    Vector2.Dot(ref axis, ref velocityA, out velocityProjection);

                    var newIntervalA = new Interval(intervalA.Min + velocityProjection, intervalA.Max + velocityProjection);

                    var intersectionDistance = newIntervalA.GetDistance(intervalB);
                    intersects = intersectionDistance < 0;
                    if (!intersects)
                        result.WouldHaveIntersected = false;

                    if (result.WouldHaveIntersected == false) {
                        result.WillBeIntersecting = false;
                        result.ResultVelocity = velocityA;
                        break;
                    }

                    if ((velocityDistance > 0) && (intersectionDistance < minDistance)) {
                        var minVect = velocityAxis * intersectionDistance;
                        var newVelocity = velocityA + minVect;

                        if (newVelocity.LengthSquared() > velocityA.LengthSquared())
                            continue;
                        if (Vector2.Dot(velocityA, newVelocity) < 0.0f)
                            continue;

                        result.ResultVelocity = newVelocity;
                        result.WillBeIntersecting = false;
                        minDistance = intersectionDistance;
                    }
                }
            }

            return result;
        }
    }
}
