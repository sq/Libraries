using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Game;

namespace Squared.Render {
    public delegate void GeometryDrawCallPreparer<T> (GeometryBatch<T> batch, ref Internal.VertexBuffer<GeometryVertex> vb, ref Internal.IndexBuffer ib, ref GeometryDrawCall<T> call)
        where T : struct, IVertexType;

    public struct GeometryVertex {
        public Vector3 Position;
        public Color Color;

        public GeometryVertex (Vector3 position, Color color) {
            Position = position;
            Color = color;
        }

        public GeometryVertex (float x, float y, float z, Color color) {
            Position.X = x;
            Position.Y = y;
            Position.Z = z;
            Color = color;
        }
    }

    public delegate void VertexBuilder<T> (GeometryVertex[] source, T[] destination, int offset, int count)
        where T : struct, IVertexType;

    public struct GeometryDrawCall<T>
        where T : struct, IVertexType {

        internal GeometryDrawCallPreparer<T> _Preparer;
        public GeometryDrawCallPreparer<T> Preparer {
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

        public float Z;
        public Vector2 Vector0, Vector1, Vector2;
        public Color Color0, Color1, Color2, Color3;
        public float Scalar0;
    }

    public static class GeometryBatch {
        public static void SetVertexBuilder<T> (VertexBuilder<T> builder)
            where T : struct, IVertexType {
            GeometryBatch<T>._VertexBuilder = builder;
        }
    }

    public class GeometryBatch<T> : ListBatch<GeometryDrawCall<T>>
        where T : struct, IVertexType {

        #region Primitive Infrastructure

        internal static VertexBuilder<T> _VertexBuilder = null;
        internal static Comparison<GeometryDrawCall<T>> _DrawCallSorter;

        // 0   1   2   3
        // tl, tr, bl, br
        internal static readonly short[] OutlinedQuadIndices = new short[] { 
            0, 1,
            1, 3,
            3, 2,
            2, 0
        };

        // 0   1   2   3
        // tl, tr, bl, br
        internal static readonly short[] QuadIndices = new short[] { 
            0, 1, 3, 
            0, 3, 2 
        };

        // 0        1        2        3        4        5        6        7
        // tlInner, tlOuter, trInner, trOuter, brInner, brOuter, blInner, blOuter
        internal static readonly short[] QuadBorderIndices = new short[] { 
            1, 3, 0,
            3, 2, 0,
            3, 5, 2,
            2, 5, 4,
            6, 4, 5,
            6, 5, 7,
            1, 0, 6,
            1, 6, 7
        };

        internal static readonly short[] LineIndices = new short[] {
            0, 1
        };

        internal static GeometryDrawCallPreparer<T> 
            PrepareOutlinedQuad, PrepareQuad, PrepareGradientQuad, 
            PrepareQuadBorder, PrepareLine, PrepareRing;

        static GeometryBatch () {
            if (typeof(T) == typeof(VertexPositionColor)) {
                GeometryBatch.SetVertexBuilder<VertexPositionColor>(
                    (src, dest, offset, count) => {
                        int end = offset + count;

                        for (int i = offset; i < end; i++) {
                            dest[i].Position = src[i].Position;
                            dest[i].Color = src[i].Color;
                        }
                    }
                );
            }

            _DrawCallSorter = (lhs, rhs) => {
                int result = ((int)lhs.PrimitiveType).CompareTo((int)rhs.PrimitiveType);
                if (result == 0)
                    result = lhs.PreparerHash.CompareTo(rhs.PreparerHash);

                return result;
            };

            PrepareQuad = _PrepareQuad;
            PrepareGradientQuad = _PrepareGradientQuad;
            PrepareOutlinedQuad = _PrepareOutlinedQuad;
            PrepareQuadBorder = _PrepareQuadBorder;
            PrepareLine = _PrepareLine;
            PrepareRing = _PrepareRing;
        }

        #endregion

        #region Implementation

        internal PrimitiveBatch<T> InnerBatch;
        internal ArrayPoolAllocator<GeometryVertex> VertexAllocator;
        internal ArrayPoolAllocator<short> IndexAllocator;
        internal int VertexCount = 0, IndexCount = 0;

        public override void Initialize (Frame frame, int layer, Material material) {
            if (_VertexBuilder == null)
                throw new InvalidOperationException("You must set a VertexBuilder for this vertex type before creating GeometryBatches");

            base.Initialize(frame, layer, material);

            InnerBatch = PrimitiveBatch<T>.New(frame, layer, material);

            if (VertexAllocator == null)
                VertexAllocator = frame.RenderManager.GetArrayAllocator<GeometryVertex>();
            if (IndexAllocator == null)
                IndexAllocator = frame.RenderManager.GetArrayAllocator<short>();

            VertexCount = IndexCount = 0;
        }

        protected void Add (ref GeometryDrawCall<T> drawCall, int vertexCount, int indexCount) {
            VertexCount += vertexCount;
            IndexCount += indexCount;
            base.Add(ref drawCall);
        }

        protected void CreateInternalBatch (PrimitiveType primitiveType, ref Internal.VertexBuffer<GeometryVertex> vb, ref Internal.VertexBuffer<T> outputVb, ref Internal.IndexBuffer ib, ref int vertexOffset, ref int indexOffset) {
            int vertexCount = vb.Count - vertexOffset;
            int indexCount = ib.Count - indexOffset;

            if ((vertexCount == 0) || (indexCount == 0))
                return;

            _VertexBuilder(vb.Buffer, outputVb.Buffer, vertexOffset, vertexCount);

            int primCount = primitiveType.ComputePrimitiveCount(indexCount);

            InnerBatch.Add(new PrimitiveDrawCall<T>(
                primitiveType, outputVb.Buffer, vertexOffset, vertexCount, ib.Buffer, indexOffset, primCount
            ));

            vertexOffset += vertexCount;
            indexOffset += indexCount;
        }

        public override void Prepare () {
            if (_DrawCalls.Count > 0) {
                _DrawCalls.Sort(_DrawCallSorter);

                int count = _DrawCalls.Count;
                GeometryDrawCall<T> dc;

                var vb = new Internal.VertexBuffer<GeometryVertex>(VertexAllocator, VertexCount);
                var outputVb = InnerBatch.CreateBuffer(VertexCount);
                var ib = new Internal.IndexBuffer(IndexAllocator, IndexCount);
                int vertexOffset = 0, indexOffset = 0;
                PrimitiveType primitiveType = _DrawCalls[0].PrimitiveType;

                for (int i = 0; i < count; i++) {
                    dc = _DrawCalls[i];
                    if (dc.PrimitiveType != primitiveType) {
                        CreateInternalBatch(primitiveType, ref vb, ref outputVb, ref ib, ref vertexOffset, ref indexOffset);

                        primitiveType = dc.PrimitiveType;
                    }

                    dc.Preparer(this, ref vb, ref ib, ref dc);
                }

                CreateInternalBatch(primitiveType, ref vb, ref outputVb, ref ib, ref vertexOffset, ref indexOffset);
            }

            InnerBatch.Prepare();
        }

        public override void Issue (DeviceManager manager) {
            InnerBatch.Issue(manager);
        }

        public override void Dispose () {
            base.Dispose();
        }

        public override void ReleaseResources () {
            if (InnerBatch != null)
                InnerBatch.ReleaseResources();

            InnerBatch = null;
            base.ReleaseResources();
        }

        public static GeometryBatch<T> New (Frame frame, int layer, Material material) {
            if (frame == null)
                throw new ArgumentNullException("frame");
            if (material == null)
                throw new ArgumentNullException("material");

            var result = frame.RenderManager.AllocateBatch<GeometryBatch<T>>();
            result.Initialize(frame, layer, material);
            return result;
        }

        #endregion

        #region Primitives

        public void AddOutlinedQuad (Bounds bounds, Color outlineColor) {
            AddOutlinedQuad(bounds.TopLeft, bounds.BottomRight, outlineColor);
        }

        public void AddOutlinedQuad (Vector2 topLeft, Vector2 bottomRight, Color outlineColor) {
            var dc = new GeometryDrawCall<T> {
                Preparer = PrepareOutlinedQuad,
                PrimitiveType = PrimitiveType.LineList,
                Vector0 = topLeft,
                Vector1 = bottomRight,
                Color0 = outlineColor
            };

            Add(ref dc, 4, OutlinedQuadIndices.Length);
        }

        protected static void _PrepareOutlinedQuad (GeometryBatch<T> batch, ref Internal.VertexBuffer<GeometryVertex> vb, ref Internal.IndexBuffer ib, ref GeometryDrawCall<T> dc) {
            var vw = vb.GetWriter(4);
            var iw = ib.GetWriter(OutlinedQuadIndices.Length, (short)vw.Offset);

            vw.Write(new GeometryVertex(dc.Vector0.X, dc.Vector0.Y, dc.Z, dc.Color0));
            vw.Write(new GeometryVertex(dc.Vector1.X, dc.Vector0.Y, dc.Z, dc.Color0));
            vw.Write(new GeometryVertex(dc.Vector0.X, dc.Vector1.Y, dc.Z, dc.Color0));
            vw.Write(new GeometryVertex(dc.Vector1.X, dc.Vector1.Y, dc.Z, dc.Color0));

            iw.Write(OutlinedQuadIndices);
        }

        public void AddFilledQuad (Bounds bounds, Color fillColor) {
            AddFilledQuad(bounds.TopLeft, bounds.BottomRight, fillColor);
        }

        public void AddFilledQuad (Vector2 topLeft, Vector2 bottomRight, Color fillColor) {
            var dc = new GeometryDrawCall<T> {
                Preparer = PrepareQuad,
                PrimitiveType = PrimitiveType.TriangleList,
                Vector0 = topLeft,
                Vector1 = bottomRight,
                Color0 = fillColor
            };

            Add(ref dc, 4, QuadIndices.Length);
        }

        protected static void _PrepareQuad (GeometryBatch<T> batch, ref Internal.VertexBuffer<GeometryVertex> vb, ref Internal.IndexBuffer ib, ref GeometryDrawCall<T> dc) {
            var vw = vb.GetWriter(4);
            var iw = ib.GetWriter(QuadIndices.Length, (short)vw.Offset);

            vw.Write(new GeometryVertex(dc.Vector0.X, dc.Vector0.Y, dc.Z, dc.Color0));
            vw.Write(new GeometryVertex(dc.Vector1.X, dc.Vector0.Y, dc.Z, dc.Color0));
            vw.Write(new GeometryVertex(dc.Vector0.X, dc.Vector1.Y, dc.Z, dc.Color0));
            vw.Write(new GeometryVertex(dc.Vector1.X, dc.Vector1.Y, dc.Z, dc.Color0));

            iw.Write(QuadIndices);
        }

        public void AddGradientFilledQuad (Bounds bounds, Color topLeft, Color topRight, Color bottomLeft, Color bottomRight) {
            AddGradientFilledQuad(bounds.TopLeft, bounds.BottomRight, topLeft, topRight, bottomLeft, bottomRight);
        }

        public void AddGradientFilledQuad (Vector2 topLeft, Vector2 bottomRight, Color topLeftColor, Color topRightColor, Color bottomLeftColor, Color bottomRightColor) {
            var dc = new GeometryDrawCall<T> {
                Preparer = PrepareGradientQuad,
                PrimitiveType = PrimitiveType.TriangleList,
                Vector0 = topLeft,
                Vector1 = bottomRight,
                Color0 = topLeftColor,
                Color1 = topRightColor,
                Color2 = bottomLeftColor,
                Color3 = bottomRightColor
            };

            Add(ref dc, 4, QuadIndices.Length);
        }

        protected static void _PrepareGradientQuad (GeometryBatch<T> batch, ref Internal.VertexBuffer<GeometryVertex> vb, ref Internal.IndexBuffer ib, ref GeometryDrawCall<T> dc) {
            var vw = vb.GetWriter(4);
            var iw = ib.GetWriter(QuadIndices.Length, (short)vw.Offset);

            vw.Write(new GeometryVertex(dc.Vector0.X, dc.Vector0.Y, dc.Z, dc.Color0));
            vw.Write(new GeometryVertex(dc.Vector1.X, dc.Vector0.Y, dc.Z, dc.Color1));
            vw.Write(new GeometryVertex(dc.Vector0.X, dc.Vector1.Y, dc.Z, dc.Color2));
            vw.Write(new GeometryVertex(dc.Vector1.X, dc.Vector1.Y, dc.Z, dc.Color3));

            iw.Write(QuadIndices);
        }

        public void AddQuadBorder (Vector2 topLeft, Vector2 bottomRight, Color colorInner, Color colorOuter, float borderSize) {
            var dc = new GeometryDrawCall<T> {
                Preparer = PrepareQuadBorder,
                PrimitiveType = PrimitiveType.TriangleList,
                Vector0 = topLeft,
                Vector1 = bottomRight,
                Color0 = colorInner,
                Color1 = colorOuter,
                Scalar0 = borderSize
            };

            Add(ref dc, 8, QuadBorderIndices.Length);
        }

        protected static void _PrepareQuadBorder (GeometryBatch<T> batch, ref Internal.VertexBuffer<GeometryVertex> vb, ref Internal.IndexBuffer ib, ref GeometryDrawCall<T> dc) {
            var vw = vb.GetWriter(8);
            var iw = ib.GetWriter(QuadBorderIndices.Length, (short)vw.Offset);

            var tl = dc.Vector0;
            var br = dc.Vector1;

            var border = dc.Scalar0;

            var vInner = new GeometryVertex(tl.X, tl.Y, dc.Z, dc.Color0);
            var vOuter = new GeometryVertex(tl.X - border, tl.Y - border, dc.Z, dc.Color1);

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
            var dc = new GeometryDrawCall<T> {
                Preparer = PrepareLine,
                PrimitiveType = PrimitiveType.LineList,
                Vector0 = start,
                Vector1 = end,
                Color0 = firstColor,
                Color1 = secondColor
            };

            Add(ref dc, 2, LineIndices.Length);
        }

        protected static void _PrepareLine (GeometryBatch<T> batch, ref Internal.VertexBuffer<GeometryVertex> vb, ref Internal.IndexBuffer ib, ref GeometryDrawCall<T> dc) {
            var vw = vb.GetWriter(2);
            var iw = ib.GetWriter(LineIndices.Length, (short)vw.Offset);

            vw.Write(new GeometryVertex(dc.Vector0.X, dc.Vector0.Y, dc.Z, dc.Color0));
            vw.Write(new GeometryVertex(dc.Vector1.X, dc.Vector1.Y, dc.Z, dc.Color1));

            iw.Write(LineIndices);
        }

        public void AddFilledRing (Vector2 center, float innerRadius, float outerRadius, Color innerColor, Color outerColor) {
            AddFilledRing(
                center, 
                new Vector2(innerRadius, innerRadius), 
                new Vector2(outerRadius, outerRadius), 
                innerColor, outerColor
            );
        }

        protected static int ComputeRingPoints (ref Vector2 radius) {
            return (int)Math.Ceiling(Math.Abs(radius.X + radius.Y) / 2) + 8;
        }

        public void AddFilledRing (Vector2 center, Vector2 innerRadius, Vector2 outerRadius, Color innerColor, Color outerColor) {
            var dc = new GeometryDrawCall<T> {
                Preparer = PrepareRing,
                PrimitiveType = PrimitiveType.TriangleList,
                Vector0 = center,
                Vector1 = innerRadius,
                Vector2 = outerRadius,
                Color0 = innerColor,
                Color1 = outerColor
            };

            int numPoints = ComputeRingPoints(ref outerRadius);

            Add(ref dc, numPoints * 2, (numPoints - 1) * 3);
        }

        public static void _PrepareRing (GeometryBatch<T> batch, ref Internal.VertexBuffer<GeometryVertex> vb, ref Internal.IndexBuffer ib, ref GeometryDrawCall<T> dc) {
            int numPoints = ComputeRingPoints(ref dc.Vector2);

            var vw = vb.GetWriter(numPoints * 2);
            var iw = ib.GetWriter((numPoints - 1) * 3, (short)vw.Offset);

            float a = 0;
            float step = (float)(Math.PI * 2.0 / (numPoints - 1));
            float cos, sin;
            var vertexInner = new GeometryVertex(0, 0, dc.Z, dc.Color0);
            var vertexOuter = new GeometryVertex(0, 0, dc.Z, dc.Color1);

            for (int i = 0; i < numPoints; i++) {
                cos = (float)Math.Cos(a);
                sin = (float)Math.Sin(a);

                vertexInner.Position.X = dc.Vector0.X + (float)(cos * dc.Vector1.X);
                vertexInner.Position.Y = dc.Vector0.Y + (float)(sin * dc.Vector1.Y);
                vw.Write(ref vertexInner);

                vertexOuter.Position.X = dc.Vector0.X + (float)(cos * dc.Vector2.X);
                vertexOuter.Position.Y = dc.Vector0.Y + (float)(sin * dc.Vector2.Y);
                vw.Write(ref vertexOuter);

                if (i == (numPoints - 1))
                    break;

                int j = i * 2;
                iw.Write((short)j);
                iw.Write((short)(j + 1));
                iw.Write((short)(j + 3));

                a += step;
            }
        }

        #endregion
    }
}
