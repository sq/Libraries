using System;
using System.Collections.Generic;
using System.Linq;
using Squared.Util;
using Microsoft.Xna.Framework;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Threading;
using System.Text;
using System.Runtime.InteropServices;

namespace Squared.Game {
    public sealed class Vector2Comparer : IEqualityComparer<Vector2> {
        public bool Equals (Vector2 x, Vector2 y) {
            return (x.X == y.X) && (x.Y == y.Y);
        }

        public int GetHashCode (Vector2 obj) {
            return obj.X.GetHashCode() + obj.Y.GetHashCode();
        }
    }

    public interface IHasAnchor {
        Vector2 Anchor { get; }
    }

    public interface IHasBounds {
        Bounds Bounds { get; }
    }

    [Serializable]
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct Bounds : ISerializable {
        public static readonly Bounds Unit = FromPositionAndSize(Vector2.Zero, Vector2.One);

        public Vector2 TopLeft;
        public Vector2 BottomRight;

        public Vector2 TopRight {
            get {
                return new Vector2(BottomRight.X, TopLeft.Y);
            }
        }

        public Vector2 BottomLeft {
            get {
                return new Vector2(TopLeft.X, BottomRight.Y);
            }
        }

        public Vector2 Center {
            get {
                return new Vector2(
                    TopLeft.X + (BottomRight.X - TopLeft.X) * 0.5f,
                    TopLeft.Y + (BottomRight.Y - TopLeft.Y) * 0.5f
                );
            }
        }

        public Vector2 Size {
            get {
                return new Vector2(
                    BottomRight.X - TopLeft.X,
                    BottomRight.Y - TopLeft.Y
                );
            }
            set {
                BottomRight = TopLeft + value;
            }
        }

        public Interval X {
            get {
                return new Interval(TopLeft.X, BottomRight.X);
            }
        }

        public Interval Y {
            get {
                return new Interval(TopLeft.Y, BottomRight.Y);
            }
        }

        public Bounds (in Rectangle rectangle, float scaleX = 1, float scaleY = 1)
            : this(
                new Vector2(rectangle.Left * scaleX, rectangle.Top * scaleY),
                new Vector2(rectangle.Right * scaleX, rectangle.Bottom * scaleY)
            )
        {
        }

        public Bounds (in Vector2 a, in Vector2 b) {
            TopLeft = new Vector2(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y));
            BottomRight = new Vector2(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
        }

        public bool Contains (in Vector2 point) {
            return (point.X >= TopLeft.X) && (point.Y >= TopLeft.Y) &&
                (point.X <= BottomRight.X) && (point.Y <= BottomRight.Y);
        }

        public bool Contains (in Bounds other) {
            return (other.TopLeft.X >= TopLeft.X) && (other.TopLeft.Y >= TopLeft.Y) &&
                (other.BottomRight.X <= BottomRight.X) && (other.BottomRight.Y <= BottomRight.Y);
        }

        public override string ToString () {
            return String.Format("{{{0}, {1}}} - {{{2}, {3}}}", TopLeft.X, TopLeft.Y, BottomRight.X, BottomRight.Y);
        }

        public Bounds ApplyVelocity (in Vector2 velocity) {
            var bounds = this;

            if (velocity.X < 0)
                bounds.TopLeft.X += velocity.X;
            else
                bounds.BottomRight.X += velocity.X;

            if (velocity.Y < 0)
                bounds.TopLeft.Y += velocity.Y;
            else
                bounds.BottomRight.Y += velocity.Y;

            return bounds;
        }

        public bool Intersects (in Bounds rhs) {
            return Intersect(this, in rhs);
        }

        public static bool Intersect (in Bounds lhs, in Bounds rhs) {
            return (rhs.TopLeft.X <= lhs.BottomRight.X) &&
                   (lhs.TopLeft.X <= rhs.BottomRight.X) &&
                   (rhs.TopLeft.Y <= lhs.BottomRight.Y) &&
                   (lhs.TopLeft.Y <= rhs.BottomRight.Y);
        }

        public static Bounds? FromIntersection (in Bounds lhs, in Bounds rhs) {
            Vector2 tl = Vector2.Zero, br = Vector2.Zero;
            tl.X = Math.Max(lhs.TopLeft.X, rhs.TopLeft.X);
            tl.Y = Math.Max(lhs.TopLeft.Y, rhs.TopLeft.Y);
            br.X = Math.Min(lhs.BottomRight.X, rhs.BottomRight.X);
            br.Y = Math.Min(lhs.BottomRight.Y, rhs.BottomRight.Y);

            if ((br.X > tl.X) && (br.Y > tl.Y)) {
                return new Bounds(tl, br);
            } else {
                return null;
            }
        }

        public static Bounds FromUnion (in Bounds lhs, in Bounds rhs) {
            Vector2 tl = Vector2.Zero, br = Vector2.Zero;
            tl.X = Math.Min(lhs.TopLeft.X, rhs.TopLeft.X);
            tl.Y = Math.Min(lhs.TopLeft.Y, rhs.TopLeft.Y);
            br.X = Math.Max(lhs.BottomRight.X, rhs.BottomRight.X);
            br.Y = Math.Max(lhs.BottomRight.Y, rhs.BottomRight.Y);

            return new Bounds(tl, br);
        }

        public static Bounds FromPoints (params Vector2[] points) {
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;

            foreach (var point in points) {
                minX = Math.Min(minX, point.X);
                minY = Math.Min(minY, point.Y);
                maxX = Math.Max(maxX, point.X);
                maxY = Math.Max(maxY, point.Y);
            }

            return new Bounds { TopLeft = new Vector2(minX, minY), BottomRight = new Vector2(maxX, maxY) };
        }

        public Bounds Expand (float x, float y) {
            return new Bounds { 
                TopLeft = new Vector2(TopLeft.X - x, TopLeft.Y - y), 
                BottomRight = new Vector2(BottomRight.X + x, BottomRight.Y + y) 
            };
        }

        public Bounds Translate (in Vector2 velocity) {
            return new Bounds {
                TopLeft = TopLeft + velocity,
                BottomRight = BottomRight + velocity
            };
        }

        public Bounds Round () {
            return new Bounds {
                TopLeft = new Vector2((float)Math.Round(TopLeft.X), (float)Math.Round(TopLeft.Y)),
                BottomRight = new Vector2((float)Math.Round(BottomRight.X), (float)Math.Round(BottomRight.Y)),
            };
        }

        public static Bounds Uninitialized {
            get {
                return new Bounds { TopLeft = new Vector2(float.MaxValue, float.MaxValue), BottomRight = new Vector2(float.MinValue, float.MinValue) };
            }
        }

        public bool HasValue {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                return (BottomRight.X != TopLeft.X) || (BottomRight.Y != TopLeft.Y);
            }
        }

        public Bounds Scale (float scale) {
            return new Bounds {
                TopLeft = TopLeft * scale,
                BottomRight = BottomRight * scale
            };
        }

        public Bounds Scale (in Vector2 scale) {
            return new Bounds {
                TopLeft = TopLeft * scale,
                BottomRight = BottomRight * scale
            };
        }

        public bool Intersection (in Bounds lhs, in Bounds rhs, out Bounds result) {
            var x1 = Math.Max(lhs.TopLeft.X, rhs.TopLeft.X);
            var y1 = Math.Max(lhs.TopLeft.Y, rhs.TopLeft.Y);
            var x2 = Math.Min(lhs.BottomRight.X, rhs.BottomRight.X);
            var y2 = Math.Min(lhs.BottomRight.Y, rhs.BottomRight.Y);

            if (x2 >= x1 && y2 >= y1) {
                result = new Bounds(
                    new Vector2(x1, y1),
                    new Vector2(x2, y2)
                );
                return true;
            }

            result = default(Bounds);
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Bounds FromPositionAndSize (in Vector2 position, in Vector2 size) {
            return new Bounds(
                position, position + size
            );
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Bounds FromPositionAndSize (float x, float y, float width, float height) {
            return new Bounds(
                new Vector2(x, y), new Vector2(x + width, y + height)
            );
        }

        public bool Equals (in Bounds rhs) {
            return (TopLeft == rhs.TopLeft) && (BottomRight == rhs.BottomRight);
        }

        public override bool Equals (object rhs) {
            if (rhs is Bounds brhs)
                return Equals(in brhs);
            else
                return false;
        }

        public static bool operator == (Bounds lhs, Bounds rhs) {
            return lhs.Equals(rhs);
        }

        public static bool operator != (Bounds lhs, Bounds rhs) {
            return !lhs.Equals(rhs);
        }

        public override int GetHashCode () {
            // FIXME
            return 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector4 ToVector4 () {
            return new Vector4(TopLeft.X, TopLeft.Y, BottomRight.X, BottomRight.Y);
        }

        internal Bounds (SerializationInfo info, StreamingContext context) {
            TopLeft = (Vector2)info.GetValue("TopLeft", typeof(Vector2));
            BottomRight = (Vector2)info.GetValue("BottomRight", typeof(Vector2));
        }

        void ISerializable.GetObjectData (SerializationInfo info, StreamingContext context) {
            info.AddValue("TopLeft", TopLeft);
            info.AddValue("BottomRight", BottomRight);
        }
    }

    public class Polygon : IEnumerable<Vector2>, IHasBounds {
        public struct Edge {
            public Vector2 Start, End;
        }

        private Vector2 _Position = new Vector2(0, 0);
        protected Vector2[] _Vertices;
        protected Vector2[] _TranslatedVertices;
        protected bool _Dirty = true, _BoundsDirty = true, _SizeDirty = true;
        protected Vector2 _Size;
        protected Bounds _Bounds;
        public readonly int Count;
        
        public Polygon (Vector2[] vertices) {
            _Vertices = vertices;
            _TranslatedVertices = new Vector2[vertices.Length];
            Count = vertices.Length;
        }

        protected virtual void ClearDirtyFlag () {
            for (int i = 0; i < _Vertices.Length; i++)
                _TranslatedVertices[i] = (_Vertices[i] + _Position).Round(Geometry.RoundingDecimals);

            _Dirty = false;
        }

        public Vector2 Position {
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

        public Vector2 this[int index] {
            get {
                if (_Dirty)
                    ClearDirtyFlag();
                
                return _TranslatedVertices[index];
            }
        }

        public void SetVertex (int index, Vector2 newVertex) {
            _Vertices[index] = newVertex;
            _Dirty = _SizeDirty = _BoundsDirty = true;
        }

        internal Vector2[] GetRawVertices () {
            return _Vertices;
        }

        public Vector2[] GetVertices () {
            if (_Dirty)
                ClearDirtyFlag();

            return _TranslatedVertices;
        }

        public Vector2 Size {
            get {
                if (_SizeDirty) {
                    float minX = float.MaxValue;
                    float maxX = float.MinValue;
                    float minY = float.MaxValue;
                    float maxY = float.MinValue;

                    foreach (var p in _Vertices) {
                        minX = Math.Min(minX, p.X);
                        maxX = Math.Max(maxX, p.X);
                        minY = Math.Min(minY, p.Y);
                        maxY = Math.Max(maxY, p.Y);
                    }

                    _Size = new Vector2(maxX - minX, maxY - minY);
                    _SizeDirty = false;
                }

                return _Size;
            }
        }

        public Bounds Bounds {
            get {
                if (_BoundsDirty) {
                    _Bounds = Bounds.FromPoints(_Vertices);
                    _BoundsDirty = false;
                }

                var result = _Bounds;
                result.TopLeft = result.TopLeft + _Position;
                result.BottomRight = result.BottomRight + _Position;
                return result;
            }
        }

        public IEnumerator<Vector2> GetEnumerator () {
            if (_Dirty)
                ClearDirtyFlag();

            return ((IEnumerable<Vector2>)_TranslatedVertices).GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator () {
            if (_Dirty)
                ClearDirtyFlag();

            return _TranslatedVertices.GetEnumerator();
        }

        public Edge GetEdge (int i) {
            i = Arithmetic.Wrap(i, 0, _Vertices.Length - 1);
            int j = Arithmetic.Wrap(i + 1, 0, _Vertices.Length - 1);

            return new Edge { Start = _TranslatedVertices[i], End = _TranslatedVertices[j] };
        }

        public static Polygon FromBounds (Bounds bounds) {
            return FromBounds(ref bounds);
        }

        public static Polygon FromBounds (ref Bounds bounds) {
            return new Polygon(new Vector2[] { 
                bounds.TopLeft, bounds.TopRight, bounds.BottomRight, bounds.BottomLeft
            });
        }
    }

    public static partial class Geometry {
        public const int RoundingDecimals = 2;
        public const float IntersectionEpsilon = (float)0.001;

        // FIXME: Optimize this
        static readonly Regex VectorRegex = new Regex(
            @"{\s*(X:)?\s*(?<x>[0-9,\-]*[0-9](.[0-9]+)?)\s*(Y:|,)?\s*(?<y>[0-9,\-]*[0-9](.[0-9]+)?)\s*((Z:|,)?\s*(?<z>[0-9,\-]*[0-9](.[0-9]+)?))?(\s*(W:|,)?\s*(?<w>[0-9,\-]*[0-9](.[0-9]+)?))?\s*}", 
            RegexOptions.ExplicitCapture | RegexOptions.IgnoreCase
        );

        static ThreadLocal<StringBuilder> VectorBuilders = new ThreadLocal<StringBuilder>(() => new StringBuilder());

        private static StringBuilder GetVectorBuilder () {
            var result = VectorBuilders.Value;
            result.Clear(); 
            return result;
        }

        public static string ToString (this Vector2 v2, IFormatProvider provider) {
            if (provider == null)
                provider = CultureInfo.InvariantCulture.NumberFormat;

            var sb = GetVectorBuilder();
            sb.Append('{');
            sb.Append(v2.X.ToString("F", provider));
            sb.Append(", ");
            sb.Append(v2.Y.ToString("F", provider));
            sb.Append('}');
            return sb.ToString();
        }

        public static string ToString (this Vector3 v3, IFormatProvider provider) {
            if (provider == null)
                provider = CultureInfo.InvariantCulture.NumberFormat;

            var sb = GetVectorBuilder();
            sb.Append('{');
            sb.Append(v3.X.ToString("F", provider));
            sb.Append(", ");
            sb.Append(v3.Y.ToString("F", provider));
            sb.Append(", ");
            sb.Append(v3.Z.ToString("F", provider));
            sb.Append('}');
            return sb.ToString();
        }

        public static string ToString (this Vector4 v4, IFormatProvider provider) {
            if (provider == null)
                provider = CultureInfo.InvariantCulture.NumberFormat;

            var isIntegral = ((int)v4.X == v4.X) &&
                ((int)v4.Y == v4.Y) &&
                ((int)v4.Z == v4.Z) &&
                ((int)v4.W == v4.W);
            var fs = isIntegral ? "########0" : "F";

            var sb = GetVectorBuilder();
            sb.Append('{');
            sb.Append(v4.X.ToString(fs, provider));
            sb.Append(", ");
            sb.Append(v4.Y.ToString(fs, provider));
            sb.Append(", ");
            sb.Append(v4.Z.ToString(fs, provider));
            sb.Append(", ");
            sb.Append(v4.W.ToString(fs, provider));
            sb.Append('}');
            return sb.ToString();
        }

        // HACK: Because our regex can handle commas in some places, this ensures that they will be parsed
        // Please don't rely on this though.
        const NumberStyles VectorStyle = NumberStyles.Float | NumberStyles.AllowThousands;

        public static bool TryParse (string text, out Vector2 result) => TryParse(text, null, out result);

        public static bool TryParse (string text, IFormatProvider provider, out Vector2 result) {
            if (provider == null)
                provider = CultureInfo.InvariantCulture.NumberFormat;

            result = default;
            if (float.TryParse(text, NumberStyles.Float, provider, out float v)) {
                result = new Vector2(v);
                return true;
            }

            var m = VectorRegex.Match(text);
            if (!m.Success)
                return false;
            Group x = m.Groups["x"], y = m.Groups["y"];
            if (!x.Success || !y.Success)
                return false;
            if (!float.TryParse(x.Value, VectorStyle, provider, out result.X) || !float.TryParse(y.Value, VectorStyle, provider, out result.Y))
                return false; 
            return true;
        }

        public static bool TryParse (string text, out Vector3 result) => TryParse(text, null, out result);

        public static bool TryParse (string text, IFormatProvider provider, out Vector3 result) {
            if (provider == null)
                provider = CultureInfo.InvariantCulture.NumberFormat;

            result = default;
            if (float.TryParse(text, NumberStyles.Float, provider, out float v)) {
                result = new Vector3(v);
                return true;
            }

            var m = VectorRegex.Match(text);
            if (!m.Success)
                return false;
            Group x = m.Groups["x"], y = m.Groups["y"], z = m.Groups["z"];
            if (!x.Success || !y.Success || !z.Success)
                return false;
            if (!float.TryParse(x.Value, VectorStyle, provider, out result.X) || !float.TryParse(y.Value, VectorStyle, provider, out result.Y) ||
                !float.TryParse(z.Value, VectorStyle, provider, out result.Z))
                return false;
            return true;
        }

        public static bool TryParse (string text, out Vector4 result) => TryParse(text, null, out result);

        public static bool TryParse (string text, IFormatProvider provider, out Vector4 result) {
            if (provider == null)
                provider = CultureInfo.InvariantCulture.NumberFormat;

            result = default;
            if (float.TryParse(text, NumberStyles.Float, provider, out float v)) {
                result = new Vector4(v);
                return true;
            }

            var m = VectorRegex.Match(text);
            if (!m.Success)
                return false;
            Group x = m.Groups["x"], y = m.Groups["y"], z = m.Groups["z"], w = m.Groups["w"];
            if (!x.Success || !y.Success || !z.Success || !w.Success)
                return false;
            if (!float.TryParse(x.Value, VectorStyle, provider, out result.X) || !float.TryParse(y.Value, VectorStyle, provider, out result.Y) ||
                !float.TryParse(z.Value, VectorStyle, provider, out result.Z) || !float.TryParse(w.Value, VectorStyle, provider, out result.W))
                return false;
            return true;
        }

        internal static double DotProduct (double x1, double y1, double x2, double y2) {
            return (x1 * x2 + y1 * y2);
        }

        internal static float VectorDot (Vector2 lhs, Vector2 rhs) {
            return (float)((lhs.X * rhs.X) + (lhs.Y * rhs.Y));
        }

        public static bool PointInPolygon (Vector2 pt, Polygon polygon) {
            int numIntersections = 0;
            var rayStart = pt;
            var rayEnd = new Vector2(pt.X + 99999, pt.Y);

            float intersection;
            for (int i = 0; i < polygon.Count; i++) {
                var edge = polygon.GetEdge(i);
                var a = edge.Start;
                var b = edge.End;

                if (DoLinesIntersect(rayStart, rayEnd, a, b, out intersection)) {
                    // If the ray crosses directly over a vertex, this can produce two
                    //  intersections instead of one, which causes the point-in-polygon
                    //  test to erroneously return true.
                    if (
                        (Math.Abs(a.Y - pt.Y) < IntersectionEpsilon) ||
                        (Math.Abs(b.Y - pt.Y) < IntersectionEpsilon) ||
                        (Math.Abs(a.X - pt.X) < IntersectionEpsilon) ||
                        (Math.Abs(b.X - pt.X) < IntersectionEpsilon)
                    ) {
                        // By discarding intersections where the endpoint of the edge lies
                        //  above the test point, we ensure that only one intersection is
                        //  recorded.
                        if (b.Y > pt.Y)
                            numIntersections += 1;
                    } else {
                        numIntersections += 1;
                    }
                }
            }

            var isInside = (numIntersections % 2) == 1;
            if (isInside) {
                // FIXME: Still busted. WHAT.
#if what
                var bounds = polygon.Bounds;

                if (!bounds.Contains(pt))
                    throw new InvalidOperationException();
#endif

                return true;
            } else
                return false;
        }

        public static bool PointInTriangle (Vector2 pt, params Vector2[] triangle) {
            if (triangle.Length != 3)
                throw new ArgumentException("Triangle must contain 3 vertices", "triangle");

            return PointInTriangle(pt, triangle[0], triangle[1], triangle[2]);
        }

        public static bool PointInTriangle (Vector2 pt, Vector2 a, Vector2 b, Vector2 c) {
            double x0 = c.X - a.X, y0 = c.Y - a.Y;
            double x1 = b.X - a.X, y1 = b.Y - a.Y;
            double x2 = pt.X - a.X, y2 = pt.Y - a.Y;
            double dot00 = DotProduct(x0, y0, x0, y0), dot01 = DotProduct(x0, y0, x1, y1);
            double dot02 = DotProduct(x0, y0, x2, y2), dot11 = DotProduct(x1, y1, x1, y1);
            double dot12 = DotProduct(x1, y1, x2, y2);

            double invDenom = 1.0 / (dot00 * dot11 - dot01 * dot01);
            double u = (dot11 * dot02 - dot01 * dot12) * invDenom;
            double v = (dot00 * dot12 - dot01 * dot02) * invDenom;

            return (u > 0.0) && (v > 0.0) && ((u + v) < 1.0);
        }

        public static IEnumerable<Vector2[]> Triangulate (IEnumerable<Vector2> polygon) {
            var ll = new LinkedList<Vector2>();
            foreach (var pt in polygon)
                ll.AddLast(pt);

            if (ll.Count < 3)
                yield break;
            else if (ll.Count == 3) {
                yield return ll.ToArray();
                yield break;
            }

            int steps = 0;
            bool done = false;
            while (!done) {
                var prev = ll.Last;
                var current = ll.First;
                var next = current.Next;

                while (current != null) {
                    bool isEar = true;
                    foreach (var pt in ll) {
                        if (PointInTriangle(pt, prev.Value, current.Value, next.Value)) {
                            isEar = false;
                            break;
                        }
                    }

                    if (isEar) {
                        yield return new Vector2[] { prev.Value, current.Value, next.Value };
                        ll.Remove(current);
                        steps = 0;
                        break;
                    } else {
                        steps += 1;
                    }

                    if (next == ll.First)
                        break;

                    if (steps > ll.Count) {
                        throw new System.TimeoutException("Triangulate made a complete pass over its remaining vertices without finding an ear");
                    }

                    prev = current;
                    current = next;
                    next = current.Next ?? ll.First;
                }

                done |= (ll.Count <= 3);
            }

            yield return ll.ToArray();
        }

        public static Interval ProjectOntoAxis (Vector2 axis, Polygon polygon) {
            return ProjectOntoAxis(axis, polygon, Vector2.Zero);
        }

        public static Interval ProjectOntoAxis (Vector2 axis, Polygon polygon, Vector2 offset) {
            var vertices = polygon.GetVertices();
            float d = Vector2.Dot(axis, vertices[0] + offset);
            Interval result = new Interval();
            result.Min = result.Max = d;

            int length = polygon.Count;
            for (int i = 1; i < length; i++) {
                d = Vector2.Dot(vertices[i] + offset, axis);

                if (d < result.Min)
                    result.Min = d;
                if (d > result.Max)
                    result.Max = d;
            }

            return result;
        }

        public static Interval ProjectOntoAxis (Vector2 axis, Vector2 lineStart, Vector2 lineEnd) {
            float d = Vector2.Dot(axis, lineStart);

            Interval result = new Interval();
            result.Min = result.Max = d;

            d = Vector2.Dot(lineEnd, axis);

            if (d < result.Min)
                result.Min = d;
            if (d > result.Max)
                result.Max = d;

            return result;
        }

        public static void GetPolygonAxes (Vector2[] buffer, ref int bufferCount, Polygon polygon) {
            int length = polygon.Count;
            int numAxes = 0;

            if ((buffer.Length - bufferCount) < length)
                throw new ArgumentException(
                    String.Format(
                        "Not enough remaining space in the buffer ({0}/{1}) for all the polygon's potential axes ({2}).",
                        (buffer.Length - bufferCount), buffer.Length, length
                    ),
                    "buffer"
                );

            bool done = false;
            int i = 0;
            Vector2 firstPoint = new Vector2(), current = new Vector2();
            Vector2 previous, axis = new Vector2();
            var vertices = polygon.GetVertices();

            while (!done) {
                previous = current;

                if (i >= length) {
                    done = true;
                    current = firstPoint;
                } else {
                    current = vertices[i];
                }

                if (i == 0) {
                    firstPoint = current;
                    i += 1;
                    continue;
                }

                axis.X = -(current.Y - previous.Y);
                axis.Y = current.X - previous.X;
                var ls = axis.LengthSquared();
                if (ls > 0 && !float.IsNaN(ls)) {
                    axis /= (float)Math.Sqrt(ls);
                    buffer[bufferCount] = axis;
                    bufferCount += 1;
                    numAxes += 1;
                }

                i += 1;
            }
        }

        public static Vector2 ComputeComparisonOffset (Polygon polygonA, Polygon polygonB) {
            var boundsA = polygonA.Bounds;
            var boundsB = polygonB.Bounds;
            float fixedBoundary = 64.0f;

            return new Vector2(
                (float)-Math.Min(boundsA.TopLeft.X, boundsB.TopLeft.X) + fixedBoundary,
                (float)-Math.Min(boundsA.TopLeft.Y, boundsB.TopLeft.Y) + fixedBoundary
            ).Round(RoundingDecimals);
        }

        public static bool DoPolygonsIntersect (Polygon polygonA, Polygon polygonB) {
            var offset = ComputeComparisonOffset(polygonA, polygonB);

            using (var axisBuffer = BufferPool<Vector2>.Allocate(polygonA.Count + polygonB.Count)) {
                int axisCount = 0;
                GetPolygonAxes(axisBuffer.Data, ref axisCount, polygonA);
                GetPolygonAxes(axisBuffer.Data, ref axisCount, polygonB);

                for (int i = 0; i < axisCount; i++) {
                    var axis = axisBuffer.Data[i];

                    var intervalA = ProjectOntoAxis(axis, polygonA, offset);
                    var intervalB = ProjectOntoAxis(axis, polygonB, offset);

                    bool intersects = intervalA.Intersects(intervalB, Geometry.IntersectionEpsilon);

                    if (!intersects)
                        return false;
                }
            }

            return true;
        }

        public static bool DoLinesIntersect (Vector2 startA, Vector2 endA, Vector2 startB, Vector2 endB) {
            var lengthAX = endA.X - startA.X;
            var lengthAY = endA.Y - startA.Y;
            var lengthBX = endB.X - startB.X;
            var lengthBY = endB.Y - startB.Y;
            var xDelta = (startA.X - startB.X);
            var yDelta = (startA.Y - startB.Y);

            float q = yDelta * lengthBX - xDelta * lengthBY;
            float d = lengthAX * lengthBY - lengthAY * lengthBX;

            if (d == 0.0f)
                return false;

            {
                d = 1 / d;
                float r = q * d;

                if (r < 0.0f || r > 1.0f)
                    return false;

                {
                    var q2 = yDelta * lengthAX - xDelta * lengthAY;
                    float s = q2 * d;

                    if (s < 0.0f || s > 1.0f)
                        return false;
                }

                return true;
            }
        }

        /// <param name="distanceAlongA">The distance along 'A' at which the intersection occurs (0.0 - 1.0)</param>
        public static bool DoLinesIntersect (Vector2 startA, Vector2 endA, Vector2 startB, Vector2 endB, out float distanceAlongA) {
            distanceAlongA = 0f;

            var lengthAX = endA.X - startA.X;
            var lengthAY = endA.Y - startA.Y;
            var lengthBX = endB.X - startB.X;
            var lengthBY = endB.Y - startB.Y;
            var xDelta = (startA.X - startB.X);
            var yDelta = (startA.Y - startB.Y);

            float q = yDelta * lengthBX - xDelta * lengthBY;
            float d = lengthAX * lengthBY - lengthAY * lengthBX;

            if (d == 0.0f)
                return false;

            {
                d = 1 / d;
                float r = q * d;

                if (r < 0.0f || r > 1.0f)
                    return false;

                {
                    var q2 = yDelta * lengthAX - xDelta * lengthAY;
                    float s = q2 * d;

                    if (s < 0.0f || s > 1.0f)
                        return false;
                }

                distanceAlongA = r;
                return true;
            }
        }

        public static bool DoLinesIntersect (Vector2 startA, Vector2 endA, Vector2 startB, Vector2 endB, out Vector2 intersection) {
            float r;
            if (!DoLinesIntersect(startA, endA, startB, endB, out r)) {
                intersection = default(Vector2);
                return false;
            }

            var lengthA = endA - startA;
            intersection = startA + (r * lengthA);

            return true;
        }

        public static bool DoesLineIntersectRectangle (Vector2 start, Vector2 end, Bounds rectangle) {
            var lineProjectionX = ProjectOntoAxis(Vector2.UnitX, start, end);
            var rectProjectionX = new Interval(rectangle.TopLeft.X, rectangle.BottomRight.X);

            if (lineProjectionX.Intersects(rectProjectionX))
                return true;

            var lineProjectionY = ProjectOntoAxis(Vector2.UnitY, start, end);
            var rectProjectionY = new Interval(rectangle.TopLeft.Y, rectangle.BottomRight.Y);

            return lineProjectionY.Intersects(rectProjectionY);
        }

        public static float? LineIntersectPolygon (Vector2 start, Vector2 end, Polygon polygon) {
            Vector2 temp;
            return LineIntersectPolygon(start, end, polygon, out temp);
        }

        public static float? LineIntersectPolygon (Vector2 start, Vector2 end, Polygon polygon, out Vector2 surfaceNormal) {
            float? result = null;

            bool done = false;
            int i = 0, length = polygon.Count;
            float minDistance = float.MaxValue;
            Vector2 firstPoint = new Vector2(), current = new Vector2();
            Vector2 previous, intersection;
            var vertices = polygon.GetVertices();

            surfaceNormal = default(Vector2);

            while (!done) {
                previous = current;

                if (i >= length) {
                    done = true;
                    current = firstPoint;
                } else {
                    current = vertices[i];
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
                        surfaceNormal = (current - previous);
                    }
                }

                i += 1;
            }

            surfaceNormal = Vector2.Normalize(surfaceNormal.PerpendicularLeft());
            return result;
        }

        public struct ResolvedMotion {
            public bool AreIntersecting;
            public bool WouldHaveIntersected;
            public bool WillBeIntersecting;
            public Vector2 ResultVelocity;
        }

        public static ResolvedMotion ResolvePolygonMotion (Polygon polygonA, Polygon polygonB, Vector2 velocityA) {
            var offset = ComputeComparisonOffset(polygonA, polygonB);

            var result = new ResolvedMotion();
            result.AreIntersecting = true;
            result.WouldHaveIntersected = true;
            result.WillBeIntersecting = true;

            float velocityProjection;
            var velocityDistance = velocityA.Length();
            var velocityAxis = Vector2.Normalize(velocityA);

            Interval intervalA, intervalB, newIntervalA;
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

                    intervalA = ProjectOntoAxis(axis, polygonA, offset);
                    intervalB = ProjectOntoAxis(axis, polygonB, offset);

                    bool intersects = intervalA.Intersects(intervalB, Geometry.IntersectionEpsilon);
                    if (!intersects)
                        result.AreIntersecting = false;
                    
                    velocityProjection = axis.Dot(ref velocityA);

                    newIntervalA = intervalA;
                    newIntervalA.Min += velocityProjection;
                    newIntervalA.Max += velocityProjection;

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
                        var minVect = axis * intersectionDistance;
                        var newVelocity = velocityA + minVect;
                        var newLength = Vector2.Dot(velocityAxis, newVelocity);
                        newVelocity = velocityAxis * newLength;

                        if (newVelocity.LengthSquared() > velocityA.LengthSquared())
                            continue;
                        if (Vector2.Dot(velocityA, newVelocity) < 0.0f)
                            newVelocity = Vector2.Zero;
                        
                        velocityProjection = axis.Dot(ref newVelocity);
                        newIntervalA.Min = (intervalA.Min + velocityProjection);
                        newIntervalA.Max = (intervalA.Max + velocityProjection);
                        intersectionDistance = newIntervalA.GetDistance(intervalB);

                        if (intersectionDistance < -IntersectionEpsilon)
                            continue;

                        result.ResultVelocity = newVelocity;
                        result.WillBeIntersecting = false;
                        minDistance = intersectionDistance;
                    }
                }
            }

            return result;
        }

        public static Vector2 ClosestPointOnLine (Vector2 sourcePoint, Vector2 lineStart, Vector2 lineEnd) {
            var lineDelta = lineEnd - lineStart;
            var u = (((sourcePoint.X - lineStart.X) * lineDelta.X) + ((sourcePoint.Y - lineStart.Y) * lineDelta.Y)) / lineDelta.LengthSquared();
            return lineStart + (lineDelta * Arithmetic.Saturate(u));
        }

        public static float LengthOfBezier (Vector2 a, Vector2 b, Vector2 c) {
            Vector2 v = 2 * (b - a),
                w = c - (2*b) + a;

            double uu = 4 * Vector2.Dot(w, w);
            if (uu < 0.0001) {
                Vector2 ca = (c - a);
                return (float)Math.Sqrt(Vector2.Dot(ca, ca));
            }

            double vv = 4 * Vector2.Dot(v, w),
                ww = Vector2.Dot(v, v),
                t1 = 2*Math.Sqrt(uu*(uu + vv + ww)),
                t2 = 2*uu+vv,
                t3 = vv*vv - 4*uu*ww,
                t4 = 2*Math.Sqrt(uu*ww);

            return (float)(
                (t1 * t2 - t3 * Math.Log(t2+t1) - 
                (vv * t4 - t3 * Math.Log(vv+t4))) / (8 * Math.Pow(uu, 1.5))
            );
        }
    }
}
