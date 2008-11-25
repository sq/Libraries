using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Pos = Microsoft.Xna.Framework.Vector2;

namespace Squared.Render {
    public interface IGeometryWriter<Vert> {
        void Write (Vert vertex);
        int VertexCount { get; }
        int TriangleCount { get; }
    }

    public class GeometryStream<Vert> : IGeometryWriter<Vert>, IDisposable {
        List<Vert> _Stream = new List<Vert>();

        public GeometryStream () {
        }

        public void Write (Vert vertex) {
            _Stream.Add(vertex);
        }

        public Vert[] GetVertices () {
            return _Stream.ToArray();
        }

        public void Dispose () {
            _Stream.Clear();
            _Stream = null;
        }

        public int VertexCount {
            get {
                return _Stream.Count;
            }
        }

        public int TriangleCount {
            get {
                return _Stream.Count / 3;
            }
        }
    }

    public class GeometryBuilder<Vert> {
        public delegate Vert VertexBuilder (Pos position);

        public GeometryStream<Vert> CreateStream () {
            return new GeometryStream<Vert>();
        }

        public GeometryBuilder () {
        }

        public int BuildQuad (Pos topLeft, Pos bottomRight, VertexBuilder builder, IGeometryWriter<Vert> writer) {
            int count = 2;

            var topRight = new Pos(bottomRight.X, topLeft.Y);
            var bottomLeft = new Pos(topLeft.X, bottomRight.Y);

            writer.Write(builder(topLeft));
            writer.Write(builder(topRight));
            writer.Write(builder(bottomRight));
            writer.Write(builder(bottomRight));
            writer.Write(builder(bottomLeft));
            writer.Write(builder(topLeft));

            return count;
        }
    }
}
