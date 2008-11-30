using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Squared.Util;
using Squared.Game;
using Microsoft.Xna.Framework;

namespace Squared.Game {
    [TestFixture]
    public class GeometryTests {
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
        public void ProjectOntoAxisTest () {
            var vertices = MakeSquare(0, 0, 5);

            var expected = new Interval<float>(-2.5f, 2.5f);
            var interval = Geometry.ProjectOntoAxis(new Vector2(0.0f, 1.0f), vertices);

            Assert.AreEqual(expected, interval);

            expected = new Interval<float>(-2.5f, 2.5f);
            interval = Geometry.ProjectOntoAxis(new Vector2(1.0f, 0.0f), vertices);

            Assert.AreEqual(expected, interval);

            var distance = (vertices[2] - vertices[0]).Length();
            expected = new Interval<float>(-distance / 2.0f, distance / 2.0f);
            var vec = new Vector2(1.0f, 1.0f);
            vec.Normalize();
            interval = Geometry.ProjectOntoAxis(vec, vertices);

            Assert.AreEqual(expected, interval);
        }

        [Test]
        public void DoPolygonsIntersectTest () {
            Assert.IsTrue(Geometry.DoPolygonsIntersect(MakeSquare(0, 0, 5), MakeSquare(0, 0, 5)));
            Assert.IsTrue(Geometry.DoPolygonsIntersect(MakeSquare(0, 0, 5), MakeSquare(2, 0, 5)));
            Assert.IsTrue(Geometry.DoPolygonsIntersect(MakeSquare(0, 0, 5), MakeSquare(2, 2, 5)));
            Assert.IsTrue(Geometry.DoPolygonsIntersect(MakeSquare(2, 2, 5), MakeSquare(2, 0, 5)));
            Assert.IsTrue(Geometry.DoPolygonsIntersect(MakeSquare(2, 2, 5), MakeSquare(0, 0, 5)));
            Assert.IsTrue(Geometry.DoPolygonsIntersect(MakeSquare(1.9f, 1.9f, 2), MakeSquare(0, 0, 2)));

            Assert.IsFalse(Geometry.DoPolygonsIntersect(MakeSquare(2, 2, 2), MakeSquare(0, 0, 2)));
            Assert.IsFalse(Geometry.DoPolygonsIntersect(MakeSquare(3, 3, 5), MakeSquare(-3, -3, 5)));
            Assert.IsFalse(Geometry.DoPolygonsIntersect(MakeSquare(3, 3, 5), MakeSquare(-3, 0, 5)));
            Assert.IsFalse(Geometry.DoPolygonsIntersect(MakeSquare(3, 3, 5), MakeSquare(0, -3, 5)));
        }

        [Test]
        public void ResolvePolygonMotionTest () {
            var predTrue = (ResolveMotionPredicate)((o, n) => true);
            var predFalse = (ResolveMotionPredicate)((o, n) => false);

            var result = Geometry.ResolvePolygonMotion(MakeSquare(0, 0, 5), MakeSquare(0, 0, 5), new Vector2(0, 0));
            Assert.IsTrue(result.AreIntersecting);
            Assert.IsTrue(result.WouldHaveIntersected);
            Assert.IsTrue(result.WillBeIntersecting);

            result = Geometry.ResolvePolygonMotion(MakeSquare(0, 0, 5), MakeSquare(5.1f, 0, 5), new Vector2(-5, 0), predTrue);
            Assert.IsFalse(result.AreIntersecting);
            Assert.IsFalse(result.WouldHaveIntersected);
            Assert.IsFalse(result.WillBeIntersecting);

            Assert.AreEqual(result.ResultVelocity, new Vector2(-5, 0));
            Assert.IsFalse(Geometry.DoPolygonsIntersect(MakeSquare(result.ResultVelocity.X, result.ResultVelocity.Y, 5), MakeSquare(5.1f, 0, 5)));

            result = Geometry.ResolvePolygonMotion(MakeSquare(0, 0, 5), MakeSquare(5.1f, 0, 5), new Vector2(5, 0), predTrue);
            Assert.IsFalse(result.AreIntersecting);
            Assert.IsTrue(result.WouldHaveIntersected);
            Assert.IsFalse(result.WillBeIntersecting);

            Assert.IsFalse(Geometry.DoPolygonsIntersect(MakeSquare(result.ResultVelocity.X, result.ResultVelocity.Y, 5), MakeSquare(5.1f, 0, 5)));

            result = Geometry.ResolvePolygonMotion(MakeSquare(0, 0, 5), MakeSquare(5.1f, 0, 5), new Vector2(5, 0), predFalse);
            Assert.IsFalse(result.AreIntersecting);
            Assert.IsTrue(result.WouldHaveIntersected);
            Assert.IsTrue(result.WillBeIntersecting);

            Assert.IsFalse(Geometry.DoPolygonsIntersect(MakeSquare(result.ResultVelocity.X, result.ResultVelocity.Y, 5), MakeSquare(5.1f, 0, 5)));

            result = Geometry.ResolvePolygonMotion(MakeSquare(0, 0, 5), MakeSquare(5.1f, 5.1f, 5), new Vector2(5, 5));
            Assert.IsFalse(result.AreIntersecting);
            Assert.IsTrue(result.WouldHaveIntersected);
            Assert.IsFalse(result.WillBeIntersecting);

            Assert.IsFalse(Geometry.DoPolygonsIntersect(MakeSquare(result.ResultVelocity.X, result.ResultVelocity.Y, 5), MakeSquare(5.1f, 5.1f, 5)));

            result = Geometry.ResolvePolygonMotion(MakeSquare(0, 0, 5), MakeSquare(5.1f, 5.1f, 5), new Vector2(5, 5), predFalse);
            Assert.IsFalse(result.AreIntersecting);
            Assert.IsTrue(result.WouldHaveIntersected);
            Assert.IsTrue(result.WillBeIntersecting);

            Assert.IsFalse(Geometry.DoPolygonsIntersect(MakeSquare(result.ResultVelocity.X, result.ResultVelocity.Y, 5), MakeSquare(5.1f, 5.1f, 5)));
        }
    }
}
