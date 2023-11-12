using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Render.RasterShape;

namespace Squared.Render {
    internal class PolygonBuffer {
        const int PolygonVertexTextureSize = 1024;

        public readonly object Lock = new object();
        private bool FlushRequired = true;
        private int WriteOffset = 0, VertexCount = 0;
        private Vector4[] VertexBuffer;

        public Texture2D Texture { get; private set; }
        public int TextureWidth => Texture?.Width ?? 0;
        public int TextureHeight => Texture?.Height ?? 0;

        public void Clear () {
            lock (Lock) {
                FlushRequired = true;
                VertexCount = 0;
                WriteOffset = 0;
            }
        }

        public void Flush (DeviceManager dm) {
            // HACK
            lock (Lock) {
                int w = PolygonVertexTextureSize,
                    h = Math.Max(1, VertexBuffer.Length / PolygonVertexTextureSize);

                if ((Texture == null) || (Texture.Width < w) || (Texture.Height < h)) {
                    dm.DisposeResource(Texture);
                    Texture = new Texture2D(dm.Device, w, h, false, SurfaceFormat.Vector4) {
                        Name = "PolygonBuffer",
                    };
                    FlushRequired = true;
                }

                if (!FlushRequired)
                    return;

                Texture.SetData(0, new Rectangle(0, 0, w, h), VertexBuffer, 0, VertexBuffer.Length);
                FlushRequired = false;
            }
        }

        public void AddVertices (
            ArraySegment<RasterPolygonVertex> vertices, out int offset, out int count, bool closed = false,
            Matrix? vertexTransform = null, Func<RasterPolygonVertex, RasterPolygonVertex> vertexModifier = null
        ) {
            if ((vertices.Count < 1) || (vertices.Count > 4096))
                throw new ArgumentOutOfRangeException("vertices.Count");

            lock (Lock) {
                FlushRequired = true;

                int allocSize = 0;
                for (int i = 0; i < vertices.Count; i++)
                    allocSize += vertices.Array[vertices.Offset + i].Type == RasterVertexType.Bezier ? 2 : 1;

                offset = WriteOffset;
                count = vertices.Count; // allocSize;

                // HACK: Padding somehow necessary for this to work consistently
                allocSize += 4;

                int newCount = VertexCount + allocSize,
                    newTexWidth = PolygonVertexTextureSize, newTexHeight = (int)Math.Ceiling(newCount / (double)PolygonVertexTextureSize),
                    newBufferSize = newTexWidth * newTexHeight;

                WriteOffset += allocSize;
                VertexCount = newCount;

                if ((VertexBuffer == null) || (newBufferSize != VertexBuffer.Length)) {
                    if (VertexBuffer == null)
                        VertexBuffer = new Vector4[newBufferSize];
                    else
                        Array.Resize(ref VertexBuffer, newBufferSize);
                }

                var prev = Vector2.Zero;
                var matrix = vertexTransform ?? Matrix.Identity;
                for (int i = 0, j = offset; i < vertices.Count; i++) {
                    var vert = vertices.Array[vertices.Offset + i];
                    if ((i == 0) && !closed)
                        vert.Type = RasterVertexType.StartNew;

                    if (vertexTransform.HasValue) {
                        var temp = new Vector4(vert.Position, 0, 1);
                        Vector4.Transform(ref temp, ref matrix, out var transformed);
                        transformed /= transformed.W;
                        vert.Position = new Vector2(transformed.X, transformed.Y);

                        temp = new Vector4(vert.ControlPoint, 0, 1);
                        Vector4.Transform(ref temp, ref matrix, out transformed);
                        transformed /= transformed.W;
                        vert.ControlPoint = new Vector2(transformed.X, transformed.Y);
                    }
                    if (vertexModifier != null)
                        vert = vertexModifier(vert);

                    VertexBuffer[j] = new Vector4(vert.Position.X, vert.Position.Y, (int)vert.Type, vert.LocalRadius);
                    j++;

                    if (vert.Type == RasterVertexType.Bezier) {
                        VertexBuffer[j] = new Vector4(
                            vert.ControlPoint.X, vert.ControlPoint.Y,
                            Game.Geometry.LengthOfBezier(prev, vert.ControlPoint, vert.Position), 0
                        );
                        j++;
                    }

                    prev = vert.Position;
                }
            }
        }
    }
}
