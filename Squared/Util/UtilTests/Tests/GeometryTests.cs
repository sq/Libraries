using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Squared.Util;
using Squared.Util.Vector;

namespace Squared.Util {
    [TestFixture]
    public class GeometryTests {
        public float Dot (Vector2 lhs, Vector2 rhs) {
            return lhs.Dot(rhs);
        }

        public Vector2 GetEdgeNormal (Vector2 first, Vector2 second) {
            var edgeVector = second - first;
            return new Vector2(-edgeVector.Y, edgeVector.X).Normalize();
        }

        public Vector2[] MakeSquare (float x, float y, float size) {
            size /= 2;

            return new Vector2[] {
                new Vector2(x - size, y - size),
                new Vector2(x + size, y - size),
                new Vector2(x + size, y + size),
                new Vector2(x - size, y + size)
            };
        }

        [Test]
        public void ComputeIntervalTest () {
            var vertices = MakeSquare(0, 0, 5);

            var expected = new Interval<float>(-2.5f, 2.5f);
            var interval = Geometry.ComputeInterval(new Vector2(0.0f, 1.0f), vertices, Dot);

            Assert.AreEqual(expected, interval);

            expected = new Interval<float>(-2.5f, 2.5f);
            interval = Geometry.ComputeInterval(new Vector2(1.0f, 0.0f), vertices, Dot);

            Assert.AreEqual(expected, interval);

            var distance = (vertices[2] - vertices[0]).Length();
            expected = new Interval<float>(-distance / 2.0f, distance / 2.0f);
            interval = Geometry.ComputeInterval((new Vector2(1.0f, 1.0f)).Normalize(), vertices, Dot);

            Assert.AreEqual(expected, interval);
        }

        [Test]
        public void DoPolygonsIntersectTest () {
            Assert.IsTrue(Geometry.DoPolygonsIntersect(MakeSquare(0, 0, 5), MakeSquare(0, 0, 5), Dot, GetEdgeNormal));
            Assert.IsTrue(Geometry.DoPolygonsIntersect(MakeSquare(0, 0, 5), MakeSquare(2, 0, 5), Dot, GetEdgeNormal));
            Assert.IsTrue(Geometry.DoPolygonsIntersect(MakeSquare(0, 0, 5), MakeSquare(2, 2, 5), Dot, GetEdgeNormal));
            Assert.IsTrue(Geometry.DoPolygonsIntersect(MakeSquare(2, 2, 5), MakeSquare(2, 0, 5), Dot, GetEdgeNormal));
            Assert.IsTrue(Geometry.DoPolygonsIntersect(MakeSquare(2, 2, 5), MakeSquare(0, 0, 5), Dot, GetEdgeNormal));
            Assert.IsTrue(Geometry.DoPolygonsIntersect(MakeSquare(1.9f, 1.9f, 2), MakeSquare(0, 0, 2), Dot, GetEdgeNormal));

            Assert.IsFalse(Geometry.DoPolygonsIntersect(MakeSquare(2, 2, 2), MakeSquare(0, 0, 2), Dot, GetEdgeNormal));
            Assert.IsFalse(Geometry.DoPolygonsIntersect(MakeSquare(3, 3, 5), MakeSquare(-3, -3, 5), Dot, GetEdgeNormal));
            Assert.IsFalse(Geometry.DoPolygonsIntersect(MakeSquare(3, 3, 5), MakeSquare(-3, 0, 5), Dot, GetEdgeNormal));
            Assert.IsFalse(Geometry.DoPolygonsIntersect(MakeSquare(3, 3, 5), MakeSquare(0, -3, 5), Dot, GetEdgeNormal));
        }

        [Test]
        public void WillPolygonsIntersectTest () {
            var result = Geometry.WillPolygonsIntersect(MakeSquare(0, 0, 5), MakeSquare(0, 0, 5), new Vector2(0, 0), Dot, GetEdgeNormal);
            Assert.IsTrue(result.AreIntersecting);
            Assert.IsTrue(result.WillBeIntersecting);

            result = Geometry.WillPolygonsIntersect(MakeSquare(0, 0, 5), MakeSquare(5.1f, 0, 5), new Vector2(-5, 0), Dot, GetEdgeNormal);
            Assert.IsFalse(result.AreIntersecting);
            Assert.IsFalse(result.WillBeIntersecting);

            result = Geometry.WillPolygonsIntersect(MakeSquare(0, 0, 5), MakeSquare(5.1f, 0, 5), new Vector2(5, 0), Dot, GetEdgeNormal);
            Assert.IsFalse(result.AreIntersecting);
            Assert.IsTrue(result.WillBeIntersecting);
        }
    }
}
