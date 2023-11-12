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
    public delegate void GeometryDrawCallPreparer (ref Internal.VertexBuffer<GeometryVertex> vb, ref Internal.IndexBuffer ib, ref GeometryDrawCall call);

    public struct GeometryDrawCall {
        internal GeometryDrawCallPreparer _Preparer;
        public GeometryDrawCallPreparer Preparer {
            get {
                return _Preparer;
            }
            set {
                _Preparer = value;
                PreparerHash = value.GetHashCode();
            }
        }
        internal int PreparerHash;
        internal PrimitiveType PrimitiveType;

        public float Z, Quality;
        public Vector2 Vector0, Vector1, Vector2;
        public Color Color0, Color1, Color2, Color3;
        public Vector2 TexCoord0, TexCoord1, TexCoord2, TexCoord3;
        public float Scalar0, Scalar1;
    }

    public sealed class GeometryDrawCallSorter : IRefComparer<GeometryDrawCall> {
        public int Compare (ref GeometryDrawCall lhs, ref GeometryDrawCall rhs) {
            return lhs.PreparerHash.CompareTo(rhs.PreparerHash);
        }
    }
    
    public sealed class GeometryBatch : Batch {
        #region Primitive Infrastructure

        internal struct DrawArguments {
            public PrimitiveType PrimitiveType;
            public int VertexOffset;
            public int VertexCount;
            public int IndexOffset;
            public int IndexCount;
            public int PrimitiveCount;
        }

        internal static GeometryDrawCallSorter _DrawCallSorter = new GeometryDrawCallSorter();

        // 0   1   2   3
        // tl, tr, bl, br
        internal static readonly ushort[] OutlinedQuadIndices = new ushort[] { 
            0, 1,
            1, 3,
            3, 2,
            2, 0
        };

        // 0   1   2   3
        // tl, tr, bl, br
        internal static readonly ushort[] QuadIndices = new ushort[] { 
            0, 1, 3, 
            0, 3, 2 
        };

        // 0        1        2        3        4        5        6        7
        // tlInner, tlOuter, trInner, trOuter, brInner, brOuter, blInner, blOuter
        internal static readonly ushort[] QuadBorderIndices = new ushort[] { 
            // left
            1, 0, 7,
            0, 6, 7,

            // right
            3, 5, 2,
            2, 5, 4,

            // top
            1, 3, 0,
            3, 2, 0,

            // bottom
            6, 4, 5,
            6, 5, 7
        };

        internal static readonly ushort[] LineIndices = new ushort[] {
            0, 1
        };

        internal static GeometryDrawCallPreparer
            PrepareOutlinedQuad, PrepareQuad, PrepareGradientQuad, 
            PrepareQuadBorder, PrepareLine, PrepareRing;

        static GeometryBatch () {
            PrepareQuad = _PrepareQuad;
            PrepareGradientQuad = _PrepareGradientQuad;
            PrepareOutlinedQuad = _PrepareOutlinedQuad;
            PrepareQuadBorder = _PrepareQuadBorder;
            PrepareLine = _PrepareLine;
            
#if !PSM
            PrepareRing = _PrepareRing;
#else
            PSMBufferGenerator<VertexPositionColor>.VertexFormat = new Sce.PlayStation.Core.Graphics.VertexFormat[] {
                Sce.PlayStation.Core.Graphics.VertexFormat.Float3, 
                Sce.PlayStation.Core.Graphics.VertexFormat.UByte4N
            };
#endif
        }

        #endregion

        #region Implementation

        private static readonly ListPool<GeometryDrawCall> _ListPool = new ListPool<GeometryDrawCall>(
            256, 4, 128, 1024, 10240
        );

        private static readonly ListPool<DrawArguments> _DrawArgumentsListPool = new ListPool<DrawArguments>(
            256, 4, 128, 1024, 10240
        );

        internal Dictionary<PrimitiveType, UnorderedList<GeometryDrawCall>> Lists = new Dictionary<PrimitiveType, UnorderedList<GeometryDrawCall>>();

        private BufferGenerator<GeometryVertex> _BufferGenerator = null;
        internal UnorderedList<DrawArguments> _DrawArguments; 

        internal ArrayPoolAllocator<GeometryVertex> VertexAllocator;
        internal ArrayPoolAllocator<short> IndexAllocator;
        internal int VertexCount = 0, IndexCount = 0, Count = 0;
        internal IGeometryBuffer _SoftwareBuffer;

        const int MaxVertexCount = 65535;

        public void Initialize (IBatchContainer container, int layer, Material material) {
            base.Initialize(container, layer, material, true);

            if (VertexAllocator == null)
                VertexAllocator = container.RenderManager.GetArrayAllocator<GeometryVertex>();
            if (IndexAllocator == null)
                IndexAllocator = container.RenderManager.GetArrayAllocator<short>();

            Count = VertexCount = IndexCount = 0;
        }

        protected void Add (in GeometryDrawCall drawCall, int vertexCount, int indexCount) {
            Count += 1;
            VertexCount += vertexCount;
            IndexCount += indexCount;

            if (VertexCount >= MaxVertexCount)
                throw new InternalBufferOverflowException("This GeometryBatch contains too many primitives. Split your primitives into multiple batches.");

            UnorderedList<GeometryDrawCall> list;
            if (!Lists.TryGetValue(drawCall.PrimitiveType, out list))
                list = Lists[drawCall.PrimitiveType] = _ListPool.Allocate(null);

            list.Add(drawCall);
        }

        protected void MakeDrawArguments (
            PrimitiveType primitiveType, 
            ref Internal.VertexBuffer<GeometryVertex> vb, ref Internal.IndexBuffer ib, 
            ref int vertexOffset, ref int indexOffset, out int primCount,
            int vertexCount, int indexCount
        ) {
            if ((vertexCount == 0) || (indexCount == 0)) {
                primCount = 0;
                return;
            }

            primCount = primitiveType.ComputePrimitiveCount(indexCount);

            _DrawArguments.Add(new DrawArguments {
                PrimitiveType = primitiveType,
                VertexOffset = vertexOffset,
                VertexCount = vertexCount,
                IndexOffset = indexOffset,
                IndexCount = indexCount,
                PrimitiveCount = primCount
            });

            vertexOffset += vertexCount;
            indexOffset += indexCount;
        }

        protected override void Prepare (PrepareManager manager) {
            if (Count > 0) {
                _BufferGenerator = Container.RenderManager.GetBufferGenerator<BufferGenerator<GeometryVertex>>();

                _DrawArguments = _DrawArgumentsListPool.Allocate(null);
                var swb = _BufferGenerator.Allocate(VertexCount, IndexCount);
                _SoftwareBuffer = swb;

                var vb = new Internal.VertexBuffer<GeometryVertex>(swb);
                var ib = new Internal.IndexBuffer(swb);
                int vertexOffset = 0, indexOffset = 0;
                int totalPrimCount = 0;

                foreach (var kvp in Lists) {
                    var l = kvp.Value;
                    var c = l.Count;

                    l.FastCLRSortRef(_DrawCallSorter);

                    int vertexCount = vb.Count, indexCount = ib.Count;

                    var _l = l.GetBuffer();
                    for (int i = 0; i < c; i++) {
                        var idx = i + _l.Offset;
                        _l.Array[idx].Preparer(ref vb, ref ib, ref _l.Array[idx]);
                    }

                    vertexCount = vb.Count - vertexCount;
                    indexCount = ib.Count - indexCount;

                    int primCount;
                    MakeDrawArguments(kvp.Key, ref vb, ref ib, ref vertexOffset, ref indexOffset, out primCount, vertexCount, indexCount);

                    totalPrimCount += primCount;
                }

                NativeBatch.RecordPrimitives(totalPrimCount);
            }
        }

        public override void Issue (DeviceManager manager) {
            base.Issue(manager);

            IGeometryBuffer hwb = null;
            try {
                if (Count > 0) {
                    manager.ApplyMaterial(Material, ref MaterialParameters);

                    hwb = _SoftwareBuffer;
                    if (hwb == null)
                        throw new ThreadStateException("Could not get a hardware buffer for this batch");

                    hwb.SetActiveAndApply(manager.Device);
                    foreach (var da in _DrawArguments) {
                        manager.Device.DrawIndexedPrimitives(da.PrimitiveType, 0, da.VertexOffset, da.VertexCount, da.IndexOffset, da.PrimitiveCount);
                    }
                    Squared.Render.NativeBatch.RecordCommands(_DrawArguments.Count);
                    hwb.SetInactiveAndUnapply(manager.Device);
                }
            } finally {
                if (hwb != null)
                    hwb.TrySetInactive();
            }

            _DrawArgumentsListPool.Release(ref _DrawArguments, Threading.WorkQueueNotifyMode.Never);
            _SoftwareBuffer = null;
        }

        protected override void OnReleaseResources () {
            foreach (var kvp in Lists) {
                var l = kvp.Value;
                _ListPool.Release(ref l, Threading.WorkQueueNotifyMode.Never);
            }

            Lists.Clear();

            base.OnReleaseResources();
        }

        public static GeometryBatch New (IBatchContainer container, int layer, Material material) {
            if (container == null)
                throw new ArgumentNullException("container");
            if (material == null)
                throw new ArgumentNullException("material");

            var result = container.RenderManager.AllocateBatch<GeometryBatch>();
            result.Initialize(container, layer, material);
            result.CaptureStack(0);
            return result;
        }

        #endregion

        #region Primitives

        private static VertexPositionColor MakeVertex (float x, float y, float z, Color c) {
            return new VertexPositionColor(new Vector3(x, y, z), c);
        }

        public void AddOutlinedQuad (Bounds bounds, Color outlineColor) {
            AddOutlinedQuad(bounds.TopLeft, bounds.BottomRight, outlineColor);
        }

        public void AddOutlinedQuad (Vector2 topLeft, Vector2 bottomRight, Color outlineColor) {
            var dc = new GeometryDrawCall {
                Preparer = PrepareOutlinedQuad,
                PrimitiveType = PrimitiveType.LineList,
                Vector0 = topLeft,
                Vector1 = bottomRight,
                Color0 = outlineColor
            };

            Add(in dc, 4, OutlinedQuadIndices.Length);
        }

        protected static void _PrepareOutlinedQuad (ref Internal.VertexBuffer<GeometryVertex> vb, ref Internal.IndexBuffer ib, ref GeometryDrawCall dc) {
            var vw = vb.GetWriter(4);
            var iw = ib.GetWriter(OutlinedQuadIndices.Length, ref vw);

            vw.Write(MakeVertex(dc.Vector0.X, dc.Vector0.Y, dc.Z, dc.Color0));
            vw.Write(MakeVertex(dc.Vector1.X, dc.Vector0.Y, dc.Z, dc.Color0));
            vw.Write(MakeVertex(dc.Vector0.X, dc.Vector1.Y, dc.Z, dc.Color0));
            vw.Write(MakeVertex(dc.Vector1.X, dc.Vector1.Y, dc.Z, dc.Color0));

            iw.Write(OutlinedQuadIndices);
        }

        public void AddFilledQuad (Bounds bounds, Color fillColor) {
            AddFilledQuad(bounds.TopLeft, bounds.BottomRight, fillColor);
        }

        public void AddFilledQuad (Vector2 topLeft, Vector2 bottomRight, Color fillColor) {
            var dc = new GeometryDrawCall {
                Preparer = PrepareQuad,
                PrimitiveType = PrimitiveType.TriangleList,
                Vector0 = topLeft,
                Vector1 = bottomRight,
                Color0 = fillColor
            };

            Add(in dc, 4, QuadIndices.Length);
        }

        protected static void _PrepareQuad (ref Internal.VertexBuffer<GeometryVertex> vb, ref Internal.IndexBuffer ib, ref GeometryDrawCall dc) {
            var vw = vb.GetWriter(4);
            var iw = ib.GetWriter(QuadIndices.Length, ref vw);

            vw.Write(MakeVertex(dc.Vector0.X, dc.Vector0.Y, dc.Z, dc.Color0));
            vw.Write(MakeVertex(dc.Vector1.X, dc.Vector0.Y, dc.Z, dc.Color0));
            vw.Write(MakeVertex(dc.Vector0.X, dc.Vector1.Y, dc.Z, dc.Color0));
            vw.Write(MakeVertex(dc.Vector1.X, dc.Vector1.Y, dc.Z, dc.Color0));

            iw.Write(QuadIndices);
        }

        public void AddGradientFilledQuad (Bounds bounds, Color topLeft, Color topRight, Color bottomLeft, Color bottomRight) {
            AddGradientFilledQuad(bounds.TopLeft, bounds.BottomRight, topLeft, topRight, bottomLeft, bottomRight);
        }

        public void AddGradientFilledQuad (Vector2 topLeft, Vector2 bottomRight, Color topLeftColor, Color topRightColor, Color bottomLeftColor, Color bottomRightColor) {
            var dc = new GeometryDrawCall {
                Preparer = PrepareGradientQuad,
                PrimitiveType = PrimitiveType.TriangleList,
                Vector0 = topLeft,
                Vector1 = bottomRight,
                Color0 = topLeftColor,
                Color1 = topRightColor,
                Color2 = bottomLeftColor,
                Color3 = bottomRightColor
            };

            Add(in dc, 4, QuadIndices.Length);
        }

        protected static void _PrepareGradientQuad (ref Internal.VertexBuffer<GeometryVertex> vb, ref Internal.IndexBuffer ib, ref GeometryDrawCall dc) {
            var vw = vb.GetWriter(4);
            var iw = ib.GetWriter(QuadIndices.Length, ref vw);

            vw.Write(MakeVertex(dc.Vector0.X, dc.Vector0.Y, dc.Z, dc.Color0));
            vw.Write(MakeVertex(dc.Vector1.X, dc.Vector0.Y, dc.Z, dc.Color1));
            vw.Write(MakeVertex(dc.Vector0.X, dc.Vector1.Y, dc.Z, dc.Color2));
            vw.Write(MakeVertex(dc.Vector1.X, dc.Vector1.Y, dc.Z, dc.Color3));

            iw.Write(QuadIndices);
        }

        public void AddQuadBorder (Vector2 topLeft, Vector2 bottomRight, Color colorInner, Color colorOuter, float borderSize) {
            var dc = new GeometryDrawCall {
                Preparer = PrepareQuadBorder,
                PrimitiveType = PrimitiveType.TriangleList,
                Vector0 = topLeft,
                Vector1 = bottomRight,
                Color0 = colorInner,
                Color1 = colorOuter,
                Scalar0 = borderSize
            };

            Add(in dc, 8, QuadBorderIndices.Length);
        }

        protected static void _PrepareQuadBorder (ref Internal.VertexBuffer<GeometryVertex> vb, ref Internal.IndexBuffer ib, ref GeometryDrawCall dc) {
            var vw = vb.GetWriter(8);
            var iw = ib.GetWriter(QuadBorderIndices.Length, ref vw);

            var tl = dc.Vector0;
            var br = dc.Vector1;

            var border = dc.Scalar0;

            var vInner = new GeometryVertex(new Vector3(tl.X, tl.Y, dc.Z), dc.Color0);
            var vOuter = new GeometryVertex(new Vector3(tl.X - border, tl.Y - border, dc.Z), dc.Color1);

            vw.Write(ref vInner);
            vw.Write(ref vOuter);

            vInner.Position.X = br.X;
            vOuter.Position.X = br.X + border;

            vw.Write(ref vInner);
            vw.Write(ref vOuter);

            vInner.Position.Y = br.Y;
            vOuter.Position.Y = br.Y + border;

            vw.Write(ref vInner);
            vw.Write(ref vOuter);

            vInner.Position.X = tl.X;
            vOuter.Position.X = tl.X - border;

            vw.Write(ref vInner);
            vw.Write(ref vOuter);

            iw.Write(QuadBorderIndices);
        }

        public void AddFilledBorderedQuad (Bounds bounds, Color colorInner, Color colorOuter, float borderSize) {
            AddFilledQuad(bounds.TopLeft, bounds.BottomRight, colorInner);
            AddQuadBorder(bounds.TopLeft, bounds.BottomRight, colorInner, colorOuter, borderSize);
        }

        public void AddFilledBorderedQuad (Vector2 topLeft, Vector2 bottomRight, Color colorInner, Color colorOuter, float borderSize) {
            AddFilledQuad(topLeft, bottomRight, colorInner);
            AddQuadBorder(topLeft, bottomRight, colorInner, colorOuter, borderSize);
        }

        public void AddLine (Vector2 start, Vector2 end, Color color) {
            AddLine(start, end, color, color);
        }

        public void AddLine (Vector2 start, Vector2 end, Color firstColor, Color secondColor) {
            var dc = new GeometryDrawCall {
                Preparer = PrepareLine,
                PrimitiveType = PrimitiveType.LineList,
                Vector0 = start,
                Vector1 = end,
                Color0 = firstColor,
                Color1 = secondColor
            };

            Add(in dc, 2, LineIndices.Length);
        }

        protected static void _PrepareLine (ref Internal.VertexBuffer<GeometryVertex> vb, ref Internal.IndexBuffer ib, ref GeometryDrawCall dc) {
            var vw = vb.GetWriter(2);
            var iw = ib.GetWriter(LineIndices.Length, ref vw);

            vw.Write(MakeVertex(dc.Vector0.X, dc.Vector0.Y, dc.Z, dc.Color0));
            vw.Write(MakeVertex(dc.Vector1.X, dc.Vector1.Y, dc.Z, dc.Color1));

            iw.Write(LineIndices);
        }
        
#if !PSM
        public void AddFilledRing (Vector2 center, float innerRadius, float outerRadius, Color innerColor, Color outerColor, float quality = 0) {
            AddFilledRing(
                center, 
                new Vector2(innerRadius, innerRadius), 
                new Vector2(outerRadius, outerRadius), 
                innerColor, outerColor, innerColor, outerColor, quality: quality
            );
        }

        protected static int ComputeRingPoints (ref Vector2 radius, float quality) {
            float scale = Arithmetic.Clamp(3.75f - quality, 2, 5);
            var result = (int)Math.Ceiling(Math.Abs(radius.X + radius.Y) / scale) + 8;
            if (result < 8)
                result = 8;
            if (result > 2048)
                result = 2048;
            return result;
        }

        public void AddFilledRing (
            Vector2 center, Vector2 innerRadius, Vector2 outerRadius, 
            Color innerColorStart, Color outerColorStart, 
            Color? innerColorEnd = null, Color? outerColorEnd = null, 
            float startAngle = 0, float endAngle = (float)(Math.PI * 2),
            float quality = 0
        ) {
            var dc = new GeometryDrawCall {
                Preparer = PrepareRing,
                PrimitiveType = PrimitiveType.TriangleList,
                Vector0 = center,
                Vector1 = innerRadius,
                Vector2 = outerRadius,
                Color0 = innerColorStart,
                Color1 = outerColorStart,
                Color2 = innerColorEnd.GetValueOrDefault(innerColorStart),
                Color3 = outerColorEnd.GetValueOrDefault(outerColorStart),
                Scalar0 = startAngle,
                Scalar1 = endAngle,
                Quality = quality
            };

            int numPoints = ComputeRingPoints(ref outerRadius, quality);

            Add(in dc, numPoints * 2, (numPoints - 1) * 6);
        }

        public static unsafe void _PrepareRing (ref Internal.VertexBuffer<GeometryVertex> vb, ref Internal.IndexBuffer ib, ref GeometryDrawCall dc) {
            int numPoints = ComputeRingPoints(ref dc.Vector2, dc.Quality);

            const int vertexStride = 2;
            const int indexStride = 6;

            var vw = vb.GetWriter(numPoints * vertexStride);
            var iw = ib.GetWriter((numPoints - 1) * indexStride, ref vw);

            float a = dc.Scalar0;
            float step = (float)((dc.Scalar1 - dc.Scalar0) / (numPoints - 1));
            float cos, sin;
            float colorA = 0, colorStep = 1.0f / (numPoints - 1);
            var vertexInner = new GeometryVertex(new Vector3(0, 0, dc.Z), dc.Color0);
            var vertexOuter = new GeometryVertex(new Vector3(0, 0, dc.Z), dc.Color1);

            GeometryVertex* pVertices = vw.Storage;
            ushort* pIndices = iw.Storage;
            for (int i = 0, j = 0, k = 0; i < numPoints; i++, j += vertexStride, k += indexStride) {
                cos = (float)Math.Cos(a);
                sin = (float)Math.Sin(a);

                vertexInner.Position.X = dc.Vector0.X + (float)(cos * dc.Vector1.X);
                vertexInner.Position.Y = dc.Vector0.Y + (float)(sin * dc.Vector1.Y);
                vertexInner.Color = Color.Lerp(dc.Color0, dc.Color2, colorA);
                pVertices[j] = vertexInner;

                vertexOuter.Position.X = dc.Vector0.X + (float)(cos * dc.Vector2.X);
                vertexOuter.Position.Y = dc.Vector0.Y + (float)(sin * dc.Vector2.Y);
                vertexOuter.Color = Color.Lerp(dc.Color1, dc.Color3, colorA);
                pVertices[j + 1] = vertexOuter;

                if (i == (numPoints - 1))
                    break;

                pIndices[k]     = (ushort)(j +     vw.IndexOffset);
                pIndices[k + 1] = (ushort)(j + 1 + vw.IndexOffset);
                pIndices[k + 2] = (ushort)(j + 3 + vw.IndexOffset);
                pIndices[k + 3] = (ushort)(j + 2 + vw.IndexOffset);
                pIndices[k + 4] = (ushort)(j +     vw.IndexOffset);
                pIndices[k + 5] = (ushort)(j + 3 + vw.IndexOffset);

                a += step;
                colorA += colorStep;
            }
        }
#endif

        #endregion
    }
}
