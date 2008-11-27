using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Squared.Util {
    public delegate float DotProduct<T> (T lhs, T rhs);
    public delegate T GetEdgeNormal<T> (T first, T second);

    public static class Geometry {
        public static Interval<float> ComputeInterval<T> (T axis, IEnumerable<T> vertices, DotProduct<T> dotProduct) {
            using (var iter = vertices.GetEnumerator()) {
                var result = new Interval<float>(0.0f, 0.0f);
                float d = 0.0f;
                int count = 0;

                result.Min = float.MaxValue;
                result.Max = float.MinValue;

                while (iter.MoveNext()) {
                    d = dotProduct(iter.Current, axis);

                    if (d < result.Min)
                        result.Min = d;
                    else if (d > result.Max)
                        result.Max = d;

                    count += 1;
                }

                return result;
            }
        }

        public static T[] GetPolygonAxes<T> (GetEdgeNormal<T> getEdgeNormal, params IEnumerable<T>[] vertexSets) {
            var axes = new List<T>();

            foreach (var vertexSet in vertexSets) {
                using (var iter = vertexSet.GetEnumerator()) {
                    int count = 0;
                    bool done = false;
                    T firstPoint = default(T), previous, current = default(T);

                    while (!done) {
                        previous = current;

                        if (!iter.MoveNext()) {
                            done = true;
                            current = firstPoint;
                        } else {
                            current = iter.Current;
                        }

                        count += 1;

                        if (count == 1) {
                            firstPoint = current;
                            continue;
                        }

                        var axis = getEdgeNormal(previous, current);
                        if (!axes.Contains(axis))
                            axes.Add(axis);
                    }
                }
            }

            return axes.ToArray();
        }

        public static bool DoPolygonsIntersect<T> (IEnumerable<T> verticesA, IEnumerable<T> verticesB, DotProduct<T> dotProduct, GetEdgeNormal<T> getEdgeNormal) {
            var axes = GetPolygonAxes<T>(getEdgeNormal, verticesA, verticesB);

            foreach (var axis in axes) {
                var intervalA = ComputeInterval<T>(axis, verticesA, dotProduct);
                var intervalB = ComputeInterval<T>(axis, verticesB, dotProduct);

                bool intersects = intervalA.Intersects(intervalB);

                if (!intersects)
                    return false;
            }

            return true;
        }

        public struct IntersectionInfo {
            public bool AreIntersecting;
            public bool WillBeIntersecting;
        }

        public static IntersectionInfo WillPolygonsIntersect<T> (IEnumerable<T> verticesA, IEnumerable<T> verticesB, T relativeTranslationA, DotProduct<T> dotProduct, GetEdgeNormal<T> getEdgeNormal) {
            var result = new IntersectionInfo();
            result.AreIntersecting = true;
            result.WillBeIntersecting = true;

            var axes = GetPolygonAxes<T>(getEdgeNormal, verticesA, verticesB);

            foreach (var axis in axes) {
                var intervalA = ComputeInterval<T>(axis, verticesA, dotProduct);
                var intervalB = ComputeInterval<T>(axis, verticesB, dotProduct);

                bool intersects = intervalA.Intersects(intervalB);
                if (!intersects)
                    result.AreIntersecting = false;

                // Console.WriteLine("axis={0} iA={1} iB={2} intersects={3}", axis, intervalA, intervalB, intersects);

                var translationProjection = dotProduct(axis, relativeTranslationA);

                if (translationProjection > 0) {
                    intervalA.Min -= translationProjection;
                } else {
                    intervalA.Max -= translationProjection;
                }

                intersects = intervalA.Intersects(intervalB);
                if (!intersects)
                    result.WillBeIntersecting = false;

                // Console.WriteLine("axis={0} iA={1} iB={2} willIntersect={3}", axis, intervalA, intervalB, intersects);

                if ((result.WillBeIntersecting == false) && (result.AreIntersecting == false))
                    break;
            }

            return result;
        }
    }
}
