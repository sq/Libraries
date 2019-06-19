using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Game;
using Squared.Render.Internal;
using Squared.Util;
using GeometryVertex = Microsoft.Xna.Framework.Graphics.VertexPositionColor;

namespace Squared.Render {
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct RasterShapeVertex : IVertexType {
        public Vector4 PointsAB, PointsCD;
        public Color CenterColor, EdgeColor, OutlineColor;
        public Vector3 OutlineSizeMiterAndType;

        public static readonly VertexElement[] Elements;
        static readonly VertexDeclaration _VertexDeclaration;

        static RasterShapeVertex () {
            var tThis = typeof(RasterShapeVertex);

            Elements = new VertexElement[] {
                new VertexElement( Marshal.OffsetOf(tThis, "PointsAB").ToInt32(), 
                    VertexElementFormat.Vector4, VertexElementUsage.Position, 0 ),
                new VertexElement( Marshal.OffsetOf(tThis, "PointsCD").ToInt32(), 
                    VertexElementFormat.Vector4, VertexElementUsage.Position, 1 ),
                new VertexElement( Marshal.OffsetOf(tThis, "OutlineSizeMiterAndType").ToInt32(), 
                    VertexElementFormat.Vector3, VertexElementUsage.TextureCoordinate, 0 ),
                new VertexElement( Marshal.OffsetOf(tThis, "CenterColor").ToInt32(), 
                    VertexElementFormat.Color, VertexElementUsage.Color, 0 ),
                new VertexElement( Marshal.OffsetOf(tThis, "EdgeColor").ToInt32(), 
                    VertexElementFormat.Color, VertexElementUsage.Color, 1 ),
                new VertexElement( Marshal.OffsetOf(tThis, "OutlineColor").ToInt32(), 
                    VertexElementFormat.Color, VertexElementUsage.Color, 2 ),
            };

            _VertexDeclaration = new VertexDeclaration(Elements);
        }

        public VertexDeclaration VertexDeclaration {
            get { return _VertexDeclaration; }
        }
    }

    public enum RasterShapeType : int {
        Ellipse,
        LineSegment
    }

    public struct RasterShapeDrawCall {
        public RasterShapeType Type;
        public Vector2 A, B, C, D;
        public float OutlineSize, Miter;
        public Color CenterColor, EdgeColor, OutlineColor;
    }

    public class RasterShapeBatch : ListBatch<RasterShapeDrawCall> {
        private BufferGenerator<RasterShapeVertex> _BufferGenerator = null;

        // 0   1   2   3
        // tl, tr, bl, br
        internal static readonly ushort[] QuadIndices = new ushort[] { 
            0, 1, 3, 
            0, 3, 2 
        };

        internal ArrayPoolAllocator<RasterShapeVertex> VertexAllocator;
        internal ArrayPoolAllocator<short> IndexAllocator;
        internal ISoftwareBuffer _SoftwareBuffer;

        const int MaxVertexCount = 65535;

        public void Initialize (IBatchContainer container, int layer, Material material) {
            base.Initialize(container, layer, material, true);

            if (VertexAllocator == null)
                VertexAllocator = container.RenderManager.GetArrayAllocator<RasterShapeVertex>();
            if (IndexAllocator == null)
                IndexAllocator = container.RenderManager.GetArrayAllocator<short>();
        }

        protected override void Prepare (PrepareManager manager) {
            var count = _DrawCalls.Count;
            var vertexCount = count * 4;
            var indexCount = count * 6;
            if (count > 0) {
                _BufferGenerator = Container.RenderManager.GetBufferGenerator<BufferGenerator<RasterShapeVertex>>();
                var swb = _BufferGenerator.Allocate(vertexCount, indexCount, true);
                _SoftwareBuffer = swb;

                var vb = new Internal.VertexBuffer<RasterShapeVertex>(swb.Vertices);
                var vw = vb.GetWriter(4 * count);

                for (int i = 0, j = 0; i < count; i++, j+=4) {
                    var dc = _DrawCalls[i];
                    var vert = new RasterShapeVertex {
                        PointsAB = new Vector4(dc.A.X, dc.A.Y, dc.B.X, dc.B.Y),
                        PointsCD = new Vector4(dc.C.X, dc.C.Y, dc.D.X, dc.D.Y),
                        CenterColor = dc.CenterColor,
                        OutlineColor = dc.OutlineColor,
                        EdgeColor = dc.EdgeColor,
                        OutlineSizeMiterAndType = new Vector3(dc.OutlineSize, dc.Miter, (int)dc.Type)
                    };
                    vw.Write(vert);
                }

                NativeBatch.RecordPrimitives(count * 2);
            }
        }

        public override void Issue (DeviceManager manager) {
            var count = _DrawCalls.Count;
            if (count > 0) {
                manager.ApplyMaterial(Material);
                var hwb = _SoftwareBuffer.HardwareBuffer;
                if (hwb == null)
                    throw new ThreadStateException("Could not get a hardware buffer for this batch");

                hwb.SetActiveAndApply(manager.Device);
                manager.Device.DrawIndexedPrimitives(
                    PrimitiveType.TriangleList, 0, 0, count * 4, 0, count * 2
                );
                Squared.Render.NativeBatch.RecordCommands(1);
                hwb.SetInactiveAndUnapply(manager.Device);
            }

            _SoftwareBuffer = null;

            base.Issue(manager);
        }

        new public void Add (RasterShapeDrawCall dc) {
            _DrawCalls.Add(ref dc);
        }

        new public void Add (ref RasterShapeDrawCall dc) {
            _DrawCalls.Add(ref dc);
        }

        public static RasterShapeBatch New (IBatchContainer container, int layer, Material material) {
            if (container == null)
                throw new ArgumentNullException("container");
            if (material == null)
                throw new ArgumentNullException("material");

            var result = container.RenderManager.AllocateBatch<RasterShapeBatch>();
            result.Initialize(container, layer, material);
            result.CaptureStack(0);
            return result;
        }
    }
}
