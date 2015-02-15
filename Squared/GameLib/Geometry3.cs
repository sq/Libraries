using System;
using System.Collections.Generic;
using System.Linq;
using Squared.Util;
using Microsoft.Xna.Framework;

namespace Squared.Game {
    public interface IHasBounds3 {
        Bounds3 Bounds { get; }
    }

    public struct Bounds3 {
        public Vector3 Minimum;
        public Vector3 Maximum;

        public Vector3 Center {
            get {
                return new Vector3(
                    Minimum.X + (Maximum.X - Minimum.X) * 0.5f,
                    Minimum.Y + (Maximum.Y - Minimum.Y) * 0.5f,
                    Minimum.Z + (Maximum.Z - Minimum.Z) * 0.5f
                );
            }
        }

        public Vector3 Size {
            get {
                return new Vector3(
                    Maximum.X - Minimum.X,
                    Maximum.Y - Minimum.Y,
                    Maximum.Z - Minimum.Z
                );
            }
        }

        public Bounds XY {
            get {
                return new Bounds(
                    new Vector2(Minimum.X, Minimum.Y),
                    new Vector2(Maximum.X, Maximum.Y)
                );
            }
        }

        public Bounds3 (Vector3 a, Vector3 b) {
            Minimum = new Vector3(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z));
            Maximum = new Vector3(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));
        }

        public bool Contains (Vector3 point) {
            return (point.X >= Minimum.X) && (point.Y >= Minimum.Y) && (point.Z >= Minimum.Z) &&
                (point.X <= Maximum.X) && (point.Y <= Maximum.Y) && (point.Z <= Maximum.Z);
        }

        public bool Contains (ref Vector3 point) {
            return (point.X >= Minimum.X) && (point.Y >= Minimum.Y) && (point.Z >= Minimum.Z) &&
                (point.X <= Maximum.X) && (point.Y <= Maximum.Y) && (point.Z <= Maximum.Z);
        }

        public bool Contains (Bounds3 other) {
            return (other.Minimum.X >= Minimum.X) && (other.Minimum.Y >= Minimum.Y) && (other.Minimum.Z >= Minimum.Z) &&
                (other.Maximum.X <= Maximum.X) && (other.Maximum.Y <= Maximum.Y) && (other.Maximum.Z <= Maximum.Z);
        }

        public bool Contains (ref Bounds3 other) {
            return (other.Minimum.X >= Minimum.X) && (other.Minimum.Y >= Minimum.Y) && (other.Minimum.Z >= Minimum.Z) &&
                (other.Maximum.X <= Maximum.X) && (other.Maximum.Y <= Maximum.Y) && (other.Maximum.Z <= Maximum.Z);
        }

        public override string ToString () {
            return String.Format(
                "{{{0}, {1}, {2}}} - {{{3}, {4}, {5}}}", 
                Minimum.X, Minimum.Y, Minimum.Z,
                Maximum.X, Maximum.Y, Maximum.Z
            );
        }

        public bool Intersects (Bounds3 rhs) {
            return Intersect(ref this, ref rhs);
        }

        public static bool Intersect (ref Bounds3 lhs, ref Bounds3 rhs) {
            return (rhs.Minimum.X <= lhs.Maximum.X) &&
                   (lhs.Minimum.X <= rhs.Maximum.X) &&
                   (rhs.Minimum.Y <= lhs.Maximum.Y) &&
                   (lhs.Minimum.Y <= rhs.Maximum.Y);
        }

        public static Bounds3? FromIntersection (Bounds3 lhs, Bounds3 rhs) {
            return FromIntersection(ref lhs, ref rhs);
        }

        public static Bounds3? FromIntersection (ref Bounds3 lhs, ref Bounds3 rhs) {
            Vector3 a = Vector3.Zero, b = Vector3.Zero;
            a.X = Math.Max(lhs.Minimum.X, rhs.Minimum.X);
            a.Y = Math.Max(lhs.Minimum.Y, rhs.Minimum.Y);
            a.Z = Math.Max(lhs.Minimum.Z, rhs.Minimum.Z);
            b.X = Math.Min(lhs.Maximum.X, rhs.Maximum.X);
            b.Y = Math.Min(lhs.Maximum.Y, rhs.Maximum.Y);
            b.Z = Math.Min(lhs.Maximum.Z, rhs.Maximum.Z);

            if (
                (b.X > a.X) && 
                (b.Y > a.Y) &&
                (b.Z > a.Z)
            ) {
                return new Bounds3(a, b);
            } else {
                return null;
            }
        }

        public static Bounds3 FromPoints (params Vector3[] points) {
            float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;

            foreach (var point in points) {
                minX = Math.Min(minX, point.X);
                minY = Math.Min(minY, point.Y);
                minZ = Math.Min(minZ, point.Z);
                maxX = Math.Max(maxX, point.X);
                maxY = Math.Max(maxY, point.Y);
                maxZ = Math.Max(maxZ, point.Z);
            }

            return new Bounds3 { 
                Minimum = new Vector3(minX, minY, minZ), 
                Maximum = new Vector3(maxX, maxY, maxZ) 
            };
        }

        public Bounds3 Expand (float x, float y, float z = 0) {
            return new Bounds3 {
                Minimum = new Vector3(Minimum.X - x, Minimum.Y - y, Minimum.Z - z),
                Maximum = new Vector3(Maximum.X + x, Maximum.Y + y, Maximum.Z + z)
            };
        }

        public Bounds3 Translate (Vector3 velocity) {
            return new Bounds3 {
                Minimum = Minimum + velocity,
                Maximum = Maximum + velocity
            };
        }

        public static Bounds3 Uninitialized {
            get {
                return new Bounds3 { 
                    Minimum = new Vector3(float.MaxValue), 
                    Maximum = new Vector3(float.MinValue) 
                };
            }
        }

        public Bounds3 Scale (float scale) {
            return new Bounds3 {
                Minimum = Minimum * scale,
                Maximum = Maximum * scale
            };
        }

        public bool Intersection (ref Bounds3 lhs, ref Bounds3 rhs, out Bounds3 result) {
            var x1 = Math.Max(lhs.Minimum.X, rhs.Minimum.X);
            var y1 = Math.Max(lhs.Minimum.Y, rhs.Minimum.Y);
            var z1 = Math.Max(lhs.Minimum.Z, rhs.Minimum.Z);
            var x2 = Math.Min(lhs.Maximum.X, rhs.Maximum.X);
            var y2 = Math.Min(lhs.Maximum.Y, rhs.Maximum.Y);
            var z2 = Math.Max(lhs.Maximum.Z, rhs.Maximum.Z);

            if (x2 >= x1 && y2 >= y1 && z2 >= z1) {
                result = new Bounds3(
                    new Vector3(x1, y1, z1),
                    new Vector3(x2, y2, z2)
                );
                return true;
            }

            result = default(Bounds3);
            return false;
        }

        public static Bounds3 FromPositionAndSize (Vector3 position, Vector3 size) {
            return new Bounds3(
                position, position + size
            );
        }
    }

    public class Polygon3 : IEnumerable<Vector3>, IHasBounds3 {
        public struct Edge {
            public Vector3 Start, End;
        }

        private Vector3 _Position = Vector3.Zero;
        protected Vector3[] _Vertices;
        protected Vector3[] _TranslatedVertices;
        protected bool _Dirty = true, _BoundsDirty = true;
        protected Bounds3 _Bounds;
        public readonly int Count;
        
        public Polygon3 (Vector3[] vertices) {
            _Vertices = vertices;
            _TranslatedVertices = new Vector3[vertices.Length];
            Count = vertices.Length;
        }

        protected virtual void ClearDirtyFlag () {
            for (int i = 0; i < _Vertices.Length; i++)
                _TranslatedVertices[i] = (_Vertices[i] + _Position).Round(Geometry.RoundingDecimals);

            _Dirty = false;
        }

        public Vector3 Position {
            get {
                return _Position;
            }
            set {
                value = value.Round(Geometry.RoundingDecimals);

                if (_Position != value) {
                    _Position = value;
                    _Dirty = true;
                }
            }
        }

        public Vector3 this[int index] {
            get {
                if (_Dirty)
                    ClearDirtyFlag();
                
                return _TranslatedVertices[index];
            }
        }

        public void SetVertex (int index, Vector3 newVertex) {
            _Vertices[index] = newVertex;
            _Dirty = true;
            _BoundsDirty = true;
        }

        internal Vector3[] GetRawVertices () {
            return _Vertices;
        }

        public Vector3[] GetVertices () {
            if (_Dirty)
                ClearDirtyFlag();

            return _TranslatedVertices;
        }

        public Bounds3 Bounds {
            get {
                if (_BoundsDirty) {
                    _Bounds = Bounds3.FromPoints(_Vertices);
                    _BoundsDirty = false;
                }

                var result = _Bounds;
                result.Minimum = result.Minimum + _Position;
                result.Maximum = result.Maximum + _Position;
                return result;
            }
        }

        public IEnumerator<Vector3> GetEnumerator () {
            if (_Dirty)
                ClearDirtyFlag();

            return ((IEnumerable<Vector3>)_TranslatedVertices).GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator () {
            if (_Dirty)
                ClearDirtyFlag();

            return _TranslatedVertices.GetEnumerator();
        }

        public Edge GetEdge (int i) {
            int j = i + 1;
            if (j >= _Vertices.Length)
                j = 0;

            return new Edge { Start = _TranslatedVertices[i], End = _TranslatedVertices[j] };
        }

        public static Polygon3 FromBounds (Bounds3 bounds) {
            return FromBounds(ref bounds);
        }

        public static Polygon3 FromBounds (ref Bounds3 bounds) {
            var points = new [] {
                new Vector3(bounds.Minimum.X, bounds.Minimum.Y, bounds.Minimum.Z),
                new Vector3(bounds.Minimum.X, bounds.Minimum.Y, bounds.Maximum.Z),

                new Vector3(bounds.Minimum.X, bounds.Maximum.Y, bounds.Minimum.Z),
                new Vector3(bounds.Minimum.X, bounds.Maximum.Y, bounds.Maximum.Z),

                new Vector3(bounds.Maximum.X, bounds.Minimum.Y, bounds.Minimum.Z),
                new Vector3(bounds.Maximum.X, bounds.Minimum.Y, bounds.Maximum.Z),

                new Vector3(bounds.Maximum.X, bounds.Maximum.Y, bounds.Minimum.Z),

                new Vector3(bounds.Maximum.X, bounds.Minimum.Y, bounds.Maximum.Z),

                new Vector3(bounds.Maximum.X, bounds.Maximum.Y, bounds.Maximum.Z),
            };            

            return new Polygon3(points);
        }
    }

    public static partial class Geometry {
        public static Interval ProjectOntoAxis (Vector3 axis, Vector3 lineStart, Vector3 lineEnd) {
            float d = Vector3.Dot(axis, lineStart);

            Interval result = new Interval();
            result.Min = result.Max = d;

            d = Vector3.Dot(lineEnd, axis);

            if (d < result.Min)
                result.Min = d;
            if (d > result.Max)
                result.Max = d;

            return result;
        }

        private static float Norm2 (Vector3 v) {
            return v.X * v.X + v.Y * v.Y + v.Z * v.Z;
        }

        public static bool DoLinesIntersect (Vector3 startA, Vector3 endA, Vector3 startB, Vector3 endB) {
            float temp;
            return DoLinesIntersect(startA, endA, startB, endB, out temp);
        }

        /// <param name="distanceAlongA">The distance along 'A' at which the intersection occurs (0.0 - 1.0)</param>
        public static bool DoLinesIntersect (Vector3 startA, Vector3 endA, Vector3 startB, Vector3 endB, out float distanceAlongA) {
            // FIXME: Is this correct?
            distanceAlongA = 0f;

            var da = endA - startA;
            var db = endB - startB;
            var dc = startB - startA;

            if (Vector3.Dot(dc, Vector3.Cross(da, db)) != 0.0) {
                // lines are not coplanar
                distanceAlongA = float.NaN;
                return false;
            }

            var s = Vector3.Dot(Vector3.Cross(dc, db), Vector3.Cross(da, db)) / Norm2(Vector3.Cross(da, db));
            if (s >= 0.0 && s <= 1.0) {
                distanceAlongA = s;
                return true;
            }

            return false;
        }

        public static bool DoLinesIntersect (Vector3 startA, Vector3 endA, Vector3 startB, Vector3 endB, out Vector3 intersection) {
            float r;
            if (!DoLinesIntersect(startA, endA, startB, endB, out r)) {
                intersection = default(Vector3);
                return false;
            }

            var lengthA = endA - startA;
            intersection = startA + (r * lengthA);

            return true;
        }

        public static bool DoesLineIntersectCube (Vector3 start, Vector3 end, Bounds3 cube) {
            var lineProjectionX = ProjectOntoAxis(Vector3.UnitX, start, end);
            var rectProjectionX = new Interval(cube.Minimum.X, cube.Maximum.X);

            if (lineProjectionX.Intersects(rectProjectionX))
                return true;

            var lineProjectionY = ProjectOntoAxis(Vector3.UnitY, start, end);
            var rectProjectionY = new Interval(cube.Minimum.Y, cube.Maximum.Y);

            if (lineProjectionY.Intersects(rectProjectionY))
                return true;

            var lineProjectionZ = ProjectOntoAxis(Vector3.UnitY, start, end);
            var rectProjectionZ = new Interval(cube.Minimum.Z, cube.Maximum.Z);

            return lineProjectionZ.Intersects(rectProjectionZ);
        }
    }
}
