using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Squared.Render;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework;
using Squared.Game;
using System.Reflection;
using Squared.Util;

namespace Squared.Render.Internal {
    public struct VertexBuffer<T> : IDisposable
        where T : struct {

        public readonly ArrayPoolAllocator<T>.Allocation Allocation;
        public int Count;

        public VertexBuffer(ArrayPoolAllocator<T> allocator, int capacity) {
            Allocation = allocator.Allocate(capacity);
            Count = 0;
        }

        public T[] Buffer {
            get {
                return Allocation.Buffer;
            }
        }

        public VertexWriter<T> GetWriter(int capacity) {
            var offset = Count;
            var newCount = Count + capacity;

            if (newCount >= Allocation.Buffer.Length)
                throw new InvalidOperationException();

            Count = newCount;
            return new VertexWriter<T>(this.Allocation.Buffer, offset, Count);
        }

        public void Dispose() {
            Count = 0;
        }
    }

    public struct VertexWriter<T>
        where T : struct {

        public readonly T[] Buffer;
        public readonly int Offset;
        public readonly int Size;
        public int Count;

        public VertexWriter(T[] buffer, int offset, int size) {
            Buffer = buffer;
            Offset = offset;
            Size = size;
            Count = 0;
        }

        public void Write(T newVertex) {
            if (Count >= Size)
                throw new InvalidOperationException();

            Buffer[Offset + Count] = newVertex;
            Count += 1;
        }

        public void Write(ref T newVertex) {
            if (Count >= Size)
                throw new InvalidOperationException();

            Buffer[Offset + Count] = newVertex;
            Count += 1;
        }

        public void Write(int index, T newVertex) {
            if (index >= Size)
                throw new InvalidOperationException();

            Count = Math.Max(Count, index + 1);
            index += Offset;

            Buffer[index] = newVertex;
        }

        public void Write(int index, ref T newVertex) {
            if (index >= Size)
                throw new InvalidOperationException();

            Count = Math.Max(Count, index + 1);
            index += Offset;

            Buffer[index] = newVertex;
        }

        public PrimitiveDrawCall<T> GetDrawCall(PrimitiveType type) {
            int primCount = type.ComputePrimitiveCount(Count);
            return new PrimitiveDrawCall<T>(
                type, Buffer, Offset, primCount
            );
        }
    }
}

namespace Squared.Render {
    public class PrimitiveBatch<T> : ListBatch<PrimitiveDrawCall<T>>
        where T : struct, IVertexType {

        private ArrayPoolAllocator<T> _Allocator;

        public override void Initialize (Frame frame, int layer, Material material) {
            base.Initialize(frame, layer, material);

            if (_Allocator == null)
                _Allocator = frame.RenderManager.GetArrayAllocator<T>();
        }

        public Internal.VertexBuffer<T> CreateBuffer (int capacity) {
            return new Internal.VertexBuffer<T>(_Allocator, capacity);
        }

        public override void Add (ref PrimitiveDrawCall<T> item) {
            if (item.Vertices == null)
                return;

            base.Add(ref item);
        }

        public override void Prepare () {
        }

        public override void Issue (DeviceManager manager) {
            if (_DrawCalls.Count == 0)
                return;

            using (manager.ApplyMaterial(Material)) {
                var device = manager.Device;

                foreach (var call in _DrawCalls) {
                    if (call.Indices != null) {
                        device.DrawUserIndexedPrimitives<T>(
                            call.PrimitiveType, call.Vertices, call.VertexOffset, call.VertexCount, call.Indices, call.IndexOffset, call.PrimitiveCount
                        );
                    } else {
                        device.DrawUserPrimitives<T>(
                            call.PrimitiveType, call.Vertices, call.VertexOffset, call.PrimitiveCount
                        );
                    }
                }
            }
        }

        public static PrimitiveBatch<T> New (Frame frame, int layer, Material material) {
            if (frame == null)
                throw new ArgumentNullException("frame");
            if (material == null)
                throw new ArgumentNullException("material");

            var result = frame.RenderManager.AllocateBatch<PrimitiveBatch<T>>();
            result.Initialize(frame, layer, material);
            return result;
        }
    }

    public static class PrimitiveDrawCall {
        public static PrimitiveDrawCall<T> New<T> (PrimitiveType primitiveType, T[] vertices)
            where T : struct {

            return New<T>(primitiveType, vertices, 0, primitiveType.ComputePrimitiveCount(vertices.Length));
        }

        public static PrimitiveDrawCall<T> New<T> (PrimitiveType primitiveType, T[] vertices, int vertexOffset, int primitiveCount)
            where T : struct {

            return new PrimitiveDrawCall<T>(
                primitiveType,
                vertices,
                vertexOffset,
                primitiveCount
            );
        }

        public static PrimitiveDrawCall<T> New<T> (PrimitiveType primitiveType, T[] vertices, int vertexOffset, int vertexCount, short[] indices, int indexOffset, int primitiveCount)
            where T : struct {

            return new PrimitiveDrawCall<T>(
                primitiveType,
                vertices,
                vertexOffset,
                vertexCount,
                indices,
                indexOffset,
                primitiveCount
            );
        }
    }

    public struct PrimitiveDrawCall<T> 
        where T : struct {

        public readonly PrimitiveType PrimitiveType;
        public readonly short[] Indices;
        public readonly int IndexOffset;
        public readonly T[] Vertices;
        public readonly int VertexOffset;
        public readonly int VertexCount;
        public readonly int PrimitiveCount;

        public static PrimitiveDrawCall<T> Null = new PrimitiveDrawCall<T>();

        public PrimitiveDrawCall (PrimitiveType primitiveType, T[] vertices, int vertexOffset, int primitiveCount)
            : this (primitiveType, vertices, vertexOffset, vertices.Length, null, 0, primitiveCount) {
        }

        public PrimitiveDrawCall (PrimitiveType primitiveType, T[] vertices, int vertexOffset, int vertexCount, short[] indices, int indexOffset, int primitiveCount) {
            if (primitiveCount <= 0)
                throw new ArgumentOutOfRangeException("primitiveCount", "At least one primitive must be drawn within a draw call.");
            if (vertexCount <= 0)
                throw new ArgumentOutOfRangeException("vertexCount", "At least one vertex must be provided.");

            PrimitiveType = primitiveType;
            Vertices = vertices;
            VertexOffset = vertexOffset;
            VertexCount = vertexCount;
            Indices = indices;
            IndexOffset = indexOffset;
            PrimitiveCount = primitiveCount;
        }
    }

    public static class Primitives {
        public static void OutlinedQuad (PrimitiveBatch<VertexPositionColor> batch, Bounds bounds, Color outlineColor) {
            OutlinedQuad(batch, bounds.TopLeft, bounds.BottomRight, outlineColor);
        }

        public static void OutlinedQuad (PrimitiveBatch<VertexPositionColor> batch, Vector2 topLeft, Vector2 bottomRight, Color outlineColor) {
            using (var buffer = batch.CreateBuffer(5)) {
                var vertices = buffer.Buffer;
                vertices[0] = new VertexPositionColor(new Vector3(topLeft.X, topLeft.Y, 0), outlineColor);
                vertices[1] = new VertexPositionColor(new Vector3(bottomRight.X, topLeft.Y, 0), outlineColor);
                vertices[2] = new VertexPositionColor(new Vector3(bottomRight.X, bottomRight.Y, 0), outlineColor);
                vertices[3] = new VertexPositionColor(new Vector3(topLeft.X, bottomRight.Y, 0), outlineColor);
                vertices[4] = vertices[0];

                batch.Add(PrimitiveDrawCall.New(
                    PrimitiveType.LineStrip,
                    vertices, 0, 4
                ));
            }
        }

        public static void FilledQuad (PrimitiveBatch<VertexPositionColor> batch, Bounds bounds, Color fillColor) {
            FilledQuad(batch, bounds.TopLeft, bounds.BottomRight, fillColor);
        }

        public static void FilledQuad (PrimitiveBatch<VertexPositionColor> batch, Vector2 topLeft, Vector2 bottomRight, Color fillColor) {
            using (var buffer = batch.CreateBuffer(6)) {
                var vertices = buffer.Buffer;

                vertices[0] = new VertexPositionColor(new Vector3(topLeft.X, topLeft.Y, 0), fillColor);
                vertices[1] = new VertexPositionColor(new Vector3(bottomRight.X, topLeft.Y, 0), fillColor);
                vertices[2] = new VertexPositionColor(new Vector3(topLeft.X, bottomRight.Y, 0), fillColor);
                vertices[3] = vertices[1];
                vertices[4] = vertices[2];
                vertices[5] = new VertexPositionColor(new Vector3(bottomRight.X, bottomRight.Y, 0), fillColor);

                batch.Add(PrimitiveDrawCall.New(
                    PrimitiveType.TriangleList,
                    vertices, 0, 2
                ));
            }
        }

        public static void GradientFilledQuad (PrimitiveBatch<VertexPositionColor> batch, Vector2 p1, Vector2 p2, Color tl, Color tr, Color bl, Color br) {
            GradientFilledQuad(
                batch,
                Math.Min(p1.X, p2.X), Math.Min(p1.Y, p2.Y),
                Math.Max(p1.X, p2.X), Math.Max(p1.Y, p2.Y),
                tl, tr, bl, br
            );
        }

        public static void GradientFilledQuad (PrimitiveBatch<VertexPositionColor> batch, float x1, float y1, float x2, float y2, Color tl, Color tr, Color bl, Color br) {
            using (var buffer = batch.CreateBuffer(6)) {
                var vertices = buffer.Buffer;

                vertices[0] = new VertexPositionColor(new Vector3(x1, y1, 0), tl);
                vertices[1] = new VertexPositionColor(new Vector3(x2, y1, 0), tr);
                vertices[2] = new VertexPositionColor(new Vector3(x2, y2, 0), br);
                vertices[3] = new VertexPositionColor(new Vector3(x1, y1, 0), tl);
                vertices[4] = new VertexPositionColor(new Vector3(x2, y2, 0), br);
                vertices[5] = new VertexPositionColor(new Vector3(x1, y2, 0), bl);

                batch.Add(PrimitiveDrawCall.New(
                    PrimitiveType.TriangleList,
                    vertices, 0, 2
                ));
            }
        }

        public static void FilledBorderedBox (PrimitiveBatch<VertexPositionColor> batch, Vector2 tl, Vector2 br, Color colorInner, Color colorOuter, float border) {
            FilledQuad(batch, tl, br, colorInner);
            BorderedBox(batch, tl, br, colorInner, colorOuter, border);
        }

        public static void BorderedBox (PrimitiveBatch<VertexPositionColor> batch, Vector2 tl, Vector2 br, Color colorInner, Color colorOuter, float border) {
            var tr = new Vector2(br.X, tl.Y);
            var bl = new Vector2(tl.X, br.Y);

            var tlOuter = tl + new Vector2(-border, -border);
            var trOuter = tr + new Vector2(border, -border);
            var brOuter = br + new Vector2(border, border);
            var blOuter = bl + new Vector2(-border, border);

            VertexPositionColor vOuter = new VertexPositionColor(new Vector3(tlOuter, 0), colorOuter);
            VertexPositionColor vInner = new VertexPositionColor(new Vector3(tl, 0), colorInner);

            using (var buffer = batch.CreateBuffer(16)) {
                var writer = buffer.GetWriter(16);

                writer.Write(ref vInner);
                writer.Write(ref vOuter);

                vOuter.Position = new Vector3(trOuter, 0);
                vInner.Position = new Vector3(tr, 0);
                writer.Write(ref vInner);
                writer.Write(ref vOuter);

                vOuter.Position = new Vector3(brOuter, 0);
                vInner.Position = new Vector3(br, 0);
                writer.Write(ref vInner);
                writer.Write(ref vOuter);

                vOuter.Position = new Vector3(blOuter, 0);
                vInner.Position = new Vector3(bl, 0);
                writer.Write(ref vInner);
                writer.Write(ref vOuter);

                vOuter.Position = new Vector3(tlOuter, 0);
                vInner.Position = new Vector3(tl, 0);
                writer.Write(ref vInner);
                writer.Write(ref vOuter);

                batch.Add(writer.GetDrawCall(PrimitiveType.TriangleStrip));
            }
        }

        public static void FilledRing (PrimitiveBatch<VertexPositionColor> batch, Vector2 center, float innerRadius, float outerRadius, Color innerColor, Color outerColor) {
            FilledRing(batch, center, new Vector2(innerRadius, innerRadius), new Vector2(outerRadius, outerRadius), innerColor, outerColor);
        }

        public static void FilledRing (PrimitiveBatch<VertexPositionColor> batch, Vector2 center, Vector2 innerRadius, Vector2 outerRadius, Color innerColor, Color outerColor) {
            int numPoints = (int)Math.Ceiling(Math.Abs(outerRadius.X + outerRadius.Y) / 2) * 2 + 8;
            float a = 0;
            float step = (float)(Math.PI * 4.0 / numPoints);
            float cos, sin;
            var vertex = new VertexPositionColor();

            using (var buffer = batch.CreateBuffer(numPoints + 2)) {
                var points = buffer.Buffer;

                for (int i = 0; i <= numPoints + 1; i++) {
                    cos = (float)Math.Cos(a);
                    sin = (float)Math.Sin(a);

                    vertex.Position.X = center.X + (float)(cos * innerRadius.X);
                    vertex.Position.Y = center.Y + (float)(sin * innerRadius.Y);
                    vertex.Color = innerColor;
                    points[i] = vertex;

                    i++;

                    vertex.Position.X = center.X + (float)(cos * outerRadius.X);
                    vertex.Position.Y = center.Y + (float)(sin * outerRadius.Y);
                    vertex.Color = outerColor;
                    points[i] = vertex;

                    a += step;
                }

                batch.Add(PrimitiveDrawCall.New(
                    PrimitiveType.TriangleStrip,
                    points, 0, numPoints
                ));
            }
        }

        public static void FilledArc (PrimitiveBatch<VertexPositionColor> batch, Vector2 center, float innerRadius, float outerRadius, float startAngle, float endAngle, Color startColor, Color endColor) {
            FilledArc(batch, center, new Vector2(innerRadius, innerRadius), new Vector2(outerRadius, outerRadius), startAngle, endAngle, startColor, endColor);
        }

        public static void FilledArc (PrimitiveBatch<VertexPositionColor> batch, Vector2 center, Vector2 innerRadius, Vector2 outerRadius, float startAngle, float endAngle, Color startColor, Color endColor) {
            if (endAngle <= startAngle)
                return;

            int numPoints = (int)Math.Ceiling(Math.Abs(endAngle - startAngle) * 6) * 2 + 4;
            float a = startAngle, c = 0.0f;
            float astep = (float)((endAngle - startAngle) / (numPoints - 2) * 2), cstep = 1.0f / (numPoints - 2) * 2;
            float cos, sin;
            var vertex = new VertexPositionColor();

            using (var buffer = batch.CreateBuffer(numPoints + 2)) {
                var points = buffer.Buffer;

                for (int i = 0; i < numPoints; i++) {
                    cos = (float)Math.Cos(a);
                    sin = (float)Math.Sin(a);

                    vertex.Color = Color.Lerp(startColor, endColor, c);
                    vertex.Position.X = center.X + (float)(cos * innerRadius.X);
                    vertex.Position.Y = center.Y + (float)(sin * innerRadius.Y);
                    points[i] = vertex;

                    i++;

                    vertex.Position.X = center.X + (float)(cos * outerRadius.X);
                    vertex.Position.Y = center.Y + (float)(sin * outerRadius.Y);
                    points[i] = vertex;

                    a += astep;
                    if (a > endAngle)
                        a = endAngle;
                    c += cstep;
                }

                batch.Add(PrimitiveDrawCall.New(
                    PrimitiveType.TriangleStrip,
                    points, 0, numPoints - 2
                ));
            }
        }

        public static void Line (PrimitiveBatch<VertexPositionColor> batch, Vector2 a, Vector2 b, Color color) {
            using (var buffer = batch.CreateBuffer(2)) {
                var writer = buffer.GetWriter(2);
                writer.Write(new VertexPositionColor(new Vector3(a, 0), color));
                writer.Write(new VertexPositionColor(new Vector3(b, 0), color));

                batch.Add(writer.GetDrawCall(PrimitiveType.LineList));
            }
        }
    }
}
