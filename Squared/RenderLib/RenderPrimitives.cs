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
using System.Diagnostics;

namespace Squared.Render.Internal {
    public struct VertexBuffer<T> : IDisposable
        where T : struct {

        public readonly ArraySegment<T> Storage;
        public int Count;

        public VertexBuffer(ArraySegment<T> storage) {
            Storage = storage;
            Count = 0;
        }

        public VertexWriter<T> GetWriter(int capacity) {
            var offset = Count;
            var newCount = Count + capacity;

            if (newCount > Storage.Count)
                throw new InvalidOperationException();

            // FIXME: This shouldn't be needed!
            var newStorage = new ArraySegment<T>(Storage.Array, Storage.Offset + offset, capacity);
            Array.Clear(newStorage.Array, newStorage.Offset, newStorage.Count);

            Count = newCount;
            return new VertexWriter<T>(newStorage, offset);
        }

        public void Dispose() {
            Count = 0;
        }
    }

    public struct VertexWriter<T>
        where T : struct {

        public readonly ArraySegment<T> Storage;
        public int IndexOffset;
        public int Count;

        public VertexWriter(ArraySegment<T> storage, int indexOffset) {
            Storage = storage;
            IndexOffset = indexOffset;
            Count = 0;
        }

        public void Write (T newVertex) {
            if (Count >= Storage.Count)
                throw new InvalidOperationException();

            Storage.Array[Storage.Offset + Count] = newVertex;
            Count += 1;
        }

        public void Write (ref T newVertex) {
            if (Count >= Storage.Count)
                throw new InvalidOperationException();

            Storage.Array[Storage.Offset + Count] = newVertex;
            Count += 1;
        }

        public void Write (int index, T newVertex) {
            if (index >= Storage.Count)
                throw new InvalidOperationException();

            Count = Math.Max(Count, index + 1);
            index += Storage.Offset;

            Storage.Array[index] = newVertex;
        }

        public void Write (int index, ref T newVertex) {
            if (index >= Storage.Count)
                throw new InvalidOperationException();

            Count = Math.Max(Count, index + 1);
            index += Storage.Offset;

            Storage.Array[index] = newVertex;
        }
    }

    public struct IndexBuffer : IDisposable {
        public readonly ArraySegment<ushort> Storage;
        public int Count;

        public IndexBuffer(ArraySegment<ushort> storage) {
            Storage = storage;
            Count = 0;
        }

        public IndexWriter GetWriter<T>(int capacity, ref VertexWriter<T> vertexWriter) 
            where T : struct {
            var offset = Count;
            var newCount = Count + capacity;

            if (newCount > Storage.Count)
                throw new InvalidOperationException();

            var newSegment = new ArraySegment<ushort>(Storage.Array, Storage.Offset + offset, capacity);
            // FIXME: This shouldn't be needed!
            Array.Clear(newSegment.Array, newSegment.Offset, newSegment.Count);

            Count = newCount;
            return new IndexWriter(newSegment, vertexWriter.IndexOffset);
        }

        public void Dispose() {
            Count = 0;
        }
    }

    public struct IndexWriter {
        public readonly ArraySegment<ushort> Storage;
        public readonly int IndexOffset;
        public int Count;

        public IndexWriter (ArraySegment<ushort> storage, int indexOffset) {
            Storage = storage;
            IndexOffset = indexOffset;
            Count = 0;
        }

        public void Write (ushort newIndex) {
            if (Count >= Storage.Count)
                throw new InvalidOperationException();

            Storage.Array[Storage.Offset + Count] = (ushort)(newIndex + IndexOffset);
            Count += 1;
        }

        public void Write (ushort[] newIndices) {
            int l = newIndices.Length;
            if (Count + l - 1 >= Storage.Count)
                throw new InvalidOperationException();

            for (int i = 0; i < l; i++)
                Storage.Array[Storage.Offset + Count + i] = (ushort)(newIndices[i] + IndexOffset);

            Count += l;
        }

        public void Write (int index, ushort newIndex) {
            if (index >= Storage.Count)
                throw new InvalidOperationException();

            Count = Math.Max(Count, index + 1);
            index += Storage.Offset;

            Storage.Array[index] = (ushort)(newIndex + IndexOffset);
        }
    }
}

namespace Squared.Render {
    public class PrimitiveBatch<T> : ListBatch<PrimitiveDrawCall<T>>
        where T : struct, IVertexType {

        private Action<DeviceManager, object> _BatchSetup;
        private object _UserData;

        public void Initialize (IBatchContainer container, int layer, Material material, Action<DeviceManager, object> batchSetup, object userData) {
            base.Initialize(container, layer, material);

            _BatchSetup = batchSetup;
            _UserData = userData;
        }

        public void Add (PrimitiveDrawCall<T> item) {
            Add(ref item);
        }

        new public void Add (ref PrimitiveDrawCall<T> item) {
            if (item.Vertices == null)
                return;

#if VALIDATE
            var indexCount = item.PrimitiveType.ComputeVertexCount(item.PrimitiveCount);
            Debug.Assert(
                item.Indices.Length >= item.IndexOffset + indexCount
            );

            for (int i = 0; i < indexCount; i++) {
                Debug.Assert(item.Indices[i + item.IndexOffset] >= 0);
                Debug.Assert(item.Indices[i + item.IndexOffset] < item.VertexCount);
            }
#endif

            var _drawCalls = _DrawCalls.GetBuffer();
            int count = _DrawCalls.Count;
            while (count > 0) {
                PrimitiveDrawCall<T> lastCall = _drawCalls[count - 1];

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

                _drawCalls[count - 1] = new PrimitiveDrawCall<T>(
                    lastCall.PrimitiveType, lastCall.Vertices, 
                    lastCall.VertexOffset, lastCall.VertexCount + item.VertexCount, 
                    null, 0, 
                    lastCall.PrimitiveCount + item.PrimitiveCount
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

            if (_BatchSetup != null)
                _BatchSetup(manager, _UserData);

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

            base.Issue(manager);
        }

        public static PrimitiveBatch<T> New (IBatchContainer container, int layer, Material material, Action<DeviceManager, object> batchSetup = null, object userData = null) {
            if (container == null)
                throw new ArgumentNullException("container");
            if (material == null)
                throw new ArgumentNullException("material");

            var result = container.RenderManager.AllocateBatch<PrimitiveBatch<T>>();
            result.Initialize(container, layer, material, batchSetup, userData);
            result.CaptureStack(0);
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

    public class NativeBatch : ListBatch<NativeDrawCall> {
        private Action<DeviceManager, object> _BatchSetup;
        private object _UserData;

        public void Initialize (IBatchContainer container, int layer, Material material, Action<DeviceManager, object> batchSetup, object userData) {
            base.Initialize(container, layer, material);

            _BatchSetup = batchSetup;
            _UserData = userData;
        }

        public void Add (NativeDrawCall item) {
            Add(ref item);
        }

        new public void Add (ref NativeDrawCall item) {
#if PSM
            if (item.VertexOffset != 0)
                throw new InvalidOperationException("VertexOffset not supported on PlayStation Mobile");
#endif
            base.Add(ref item);
        }

        public override void Prepare () {
        }

        public override void Issue (DeviceManager manager) {
            if (_DrawCalls.Count == 0)
                return;

            if (_BatchSetup != null)
                _BatchSetup(manager, _UserData);

            using (manager.ApplyMaterial(Material)) {
                var device = manager.Device;

                foreach (var call in _DrawCalls) {
#if PSM
                    device.SetVertexBuffer(call.VertexBuffer);
#elif SDL2
                    // TODO: Check if we _really_ have to implement this for MG-SDL2 -flibit
                    if (call.VertexOffset != 0) System.Console.WriteLine("OH GOD WHY");
                    device.SetVertexBuffer(call.VertexBuffer);
#else
                    device.SetVertexBuffer(call.VertexBuffer, call.VertexOffset);
#endif
                    device.Indices = call.IndexBuffer;

                    if (call.IndexBuffer != null)
                        device.DrawIndexedPrimitives(call.PrimitiveType, call.BaseVertex, call.MinVertexIndex, call.NumVertices, call.StartIndex, call.PrimitiveCount);
                    else
                        device.DrawPrimitives(call.PrimitiveType, call.StartVertex, call.PrimitiveCount);

                }

                device.SetVertexBuffer(null);
                device.Indices = null;
            }

            base.Issue(manager);
        }

        public static NativeBatch New (IBatchContainer container, int layer, Material material, Action<DeviceManager, object> batchSetup = null, object userData = null) {
            if (container == null)
                throw new ArgumentNullException("container");
            if (material == null)
                throw new ArgumentNullException("material");

            var result = container.RenderManager.AllocateBatch<NativeBatch>();
            result.Initialize(container, layer, material, batchSetup, userData);
            result.CaptureStack(0);
            return result;
        }
    }

    public struct NativeDrawCall {
        public readonly PrimitiveType PrimitiveType;
        public readonly VertexBuffer VertexBuffer;
        public readonly IndexBuffer IndexBuffer;

        public readonly int VertexOffset;
        public readonly int BaseVertex;
        public readonly int MinVertexIndex;
        public readonly int NumVertices;
        public readonly int StartIndex;
        public readonly int StartVertex;
        public readonly int PrimitiveCount;

        public NativeDrawCall (PrimitiveType primitiveType, VertexBuffer vertexBuffer, int vertexOffset, IndexBuffer indexBuffer, int baseVertex, int minVertexIndex, int numVertices, int startIndex, int primitiveCount) {
            if (vertexBuffer == null)
                throw new ArgumentNullException("vertexBuffer");

            PrimitiveType = primitiveType;
            VertexBuffer = vertexBuffer;
            VertexOffset = vertexOffset;
            IndexBuffer = indexBuffer;
            BaseVertex = baseVertex;
            MinVertexIndex = minVertexIndex;
            NumVertices = numVertices;
            StartIndex = startIndex;
            PrimitiveCount = primitiveCount;

            StartVertex = 0;
        }

        public NativeDrawCall (PrimitiveType primitiveType, VertexBuffer vertexBuffer, int vertexOffset, int startVertex, int primitiveCount) {
            if (vertexBuffer == null)
                throw new ArgumentNullException("vertexBuffer");

            PrimitiveType = primitiveType;
            VertexBuffer = vertexBuffer;
            VertexOffset = vertexOffset;
            StartVertex = startVertex;
            PrimitiveCount = primitiveCount;

            IndexBuffer = null;
            BaseVertex = MinVertexIndex = NumVertices = StartIndex = 0;
        }
    }
}
