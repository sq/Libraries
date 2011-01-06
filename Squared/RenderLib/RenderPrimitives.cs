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

        public PrimitiveDrawCall<T> GetDrawCall (PrimitiveType type, short[] indices, int indexOffset, int indexCount) {
            int primCount = type.ComputePrimitiveCount(indexCount);
            return new PrimitiveDrawCall<T>(
                type, Buffer, Offset, Count, indices, indexOffset, primCount
            );
        }

        public PrimitiveDrawCall<T> GetDrawCall (PrimitiveType type, ref IndexWriter indices) {
            int primCount = type.ComputePrimitiveCount(indices.Count);
            return new PrimitiveDrawCall<T>(
                type, Buffer, Offset, Count, indices.Buffer, indices.Offset, primCount
            );
        }

        public PrimitiveDrawCall<T> GetDrawCallTriangleFan (Batch batch) {
            int primCount = Count - 2;
            var ibuf = batch.Frame.RenderManager.GetArrayAllocator<short>().Allocate(primCount * 3);
            var indices = ibuf.Buffer;

            for (int i = 2, j = 0; i < Count; i++, j += 3) {
                indices[j] = 0;
                indices[j + 1] = (short)(i - 1);
                indices[j + 2] = (short)i;
            }

            return new PrimitiveDrawCall<T>(
                PrimitiveType.TriangleList, Buffer, Offset, Count, indices, 0, primCount
            );
        }
    }

    public struct IndexBuffer : IDisposable {
        public readonly ArrayPoolAllocator<short>.Allocation Allocation;
        public int Count;

        public IndexBuffer(ArrayPoolAllocator<short> allocator, int capacity) {
            Allocation = allocator.Allocate(capacity);
            Count = 0;
        }

        public short[] Buffer {
            get {
                return Allocation.Buffer;
            }
        }

        public IndexWriter GetWriter(int capacity, short indexOffset) {
            var offset = Count;
            var newCount = Count + capacity;

            if (newCount >= Allocation.Buffer.Length)
                throw new InvalidOperationException();

            Count = newCount;
            return new IndexWriter(this.Allocation.Buffer, offset, Count, indexOffset);
        }

        public void Dispose() {
            Count = 0;
        }
    }

    public struct IndexWriter {
        public readonly short[] Buffer;
        public readonly int Offset;
        public readonly int Size;
        public readonly short IndexOffset;
        public int Count;

        public IndexWriter (short[] buffer, int offset, int size, short indexOffset) {
            Buffer = buffer;
            Offset = offset;
            IndexOffset = indexOffset;
            Size = size;
            Count = 0;
        }

        public void Write (short newIndex) {
            if (Count >= Size)
                throw new InvalidOperationException();

            Buffer[Offset + Count] = (short)(newIndex + IndexOffset);
            Count += 1;
        }

        public void Write (short[] newIndices) {
            int l = newIndices.Length;
            if (Count + l - 1 >= Size)
                throw new InvalidOperationException();

            for (int i = 0; i < l; i++)
                Buffer[Offset + Count + i] = (short)(newIndices[i] + IndexOffset);

            Count += l;
        }

        public void Write (int index, short newIndex) {
            if (index >= Size)
                throw new InvalidOperationException();

            Count = Math.Max(Count, index + 1);
            index += Offset;

            Buffer[index] = (short)(newIndex + IndexOffset);
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

        public void Add (PrimitiveDrawCall<T> item) {
            Add(ref item);
        }

        new public void Add (ref PrimitiveDrawCall<T> item) {
            if (item.Vertices == null)
                return;

            int count = _DrawCalls.Count;
            while (count > 0) {
                PrimitiveDrawCall<T> lastCall = _DrawCalls[count - 1];

                // Attempt to combine
                if (lastCall.PrimitiveType != item.PrimitiveType)
                    break;

                if ((item.PrimitiveType == PrimitiveType.TriangleStrip) || (item.PrimitiveType == PrimitiveType.LineStrip))
                    break;

                if (lastCall.Vertices != item.Vertices)
                    break;
                if (item.VertexOffset != lastCall.VertexOffset + lastCall.VertexCount)
                    break;

                if ((lastCall.Indices ?? item.Indices) != null)
                    break;

                _DrawCalls[count - 1] = new PrimitiveDrawCall<T>(
                    lastCall.PrimitiveType, lastCall.Vertices, lastCall.VertexOffset, lastCall.VertexCount, lastCall.Indices, lastCall.IndexOffset, lastCall.PrimitiveCount
                );
                return;
            }

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
    }
}
