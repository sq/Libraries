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
using System.Threading;
using System.Runtime.CompilerServices;

namespace Squared.Render.Internal {
    public struct VertexBuffer<T> : IDisposable
        where T : struct {

        public readonly ArraySegment<T> Storage;
        public int Count;

        public VertexBuffer(ArraySegment<T> storage) {
            Storage = storage;
            Count = 0;
        }

        public VertexWriter<T> GetWriter (int capacity, bool clear = true) {
            var offset = Count;
            var newCount = Count + capacity;

            if (newCount > Storage.Count)
                throw new InvalidOperationException();

            // FIXME: This shouldn't be needed!
            var newStorage = new ArraySegment<T>(Storage.Array, Storage.Offset + offset, capacity);
            if (clear)
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
    public sealed class PrimitiveBatch<T> : ListBatch<PrimitiveDrawCall<T>>
        where T : struct, IVertexType {

        private readonly PrimitiveDrawCallComparer<T> _Comparer = new PrimitiveDrawCallComparer<T>();
        private Action<DeviceManager, object> _BatchSetup;
        private object _UserData;

        public void Initialize (IBatchContainer container, int layer, Material material, Action<DeviceManager, object> batchSetup, object userData) {
            base.Initialize(container, layer, material, true);

            _BatchSetup = batchSetup;
            _UserData = userData;
        }

        new public void Add (PrimitiveDrawCall<T> item) {
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

            var _drawCalls = _DrawCalls.GetBuffer(true);
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

        protected override void Prepare (PrepareManager manager) {
            _DrawCalls.Sort(_Comparer);

            int primCount = 0;

            for (int i = 0, c = _DrawCalls.Count; i < c; i++)
                primCount += _DrawCalls[i].PrimitiveCount;

            NativeBatch.RecordPrimitives(primCount);
        }

        public override void Issue (DeviceManager manager) {
            base.Issue(manager);

            if (_DrawCalls.Count == 0)
                return;

            if (_BatchSetup != null)
                _BatchSetup(manager, _UserData);

            manager.ApplyMaterial(Material, ref MaterialParameters);
            {
                var device = manager.Device;

                for (int i = 0, c = _DrawCalls.Count; i < c; i++) {
                    ref var call = ref _DrawCalls.Item(i);

                    var beforeDraw = call.BeforeDraw;
                    if (beforeDraw != null)
                        beforeDraw(manager, ref call, i);

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

    public sealed class PrimitiveDrawCallComparer<T> : IRefComparer<PrimitiveDrawCall<T>>, IComparer<PrimitiveDrawCall<T>>
        where T : struct
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Compare (ref PrimitiveDrawCall<T> x, ref PrimitiveDrawCall<T> y) {
            // FIXME: Tags?
            var result = (x.SortKey.Order > y.SortKey.Order)
                ? 1
                : (
                    (x.SortKey.Order < y.SortKey.Order)
                    ? -1
                    : 0
                );
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Compare (PrimitiveDrawCall<T> x, PrimitiveDrawCall<T> y) {
            return Compare(ref x, ref y);
        }
    }

    public delegate void PrimitiveBeforeDraw<T> (DeviceManager dm, ref PrimitiveDrawCall<T> drawCall, int index)
        where T : struct;

    public struct PrimitiveDrawCall<T> 
        where T : struct {

        public readonly PrimitiveType PrimitiveType;
        public readonly short[] Indices;
        public readonly int IndexOffset;
        public readonly T[] Vertices;
        public readonly int VertexOffset;
        public readonly int VertexCount;
        public readonly int PrimitiveCount;

        public readonly DrawCallSortKey SortKey;

        public readonly PrimitiveBeforeDraw<T> BeforeDraw;
        public readonly object UserData;

        public static PrimitiveDrawCall<T> Null = new PrimitiveDrawCall<T>();

        public PrimitiveDrawCall (
            PrimitiveType primitiveType, T[] vertices, int vertexOffset, int primitiveCount
        )
            : this (primitiveType, vertices, vertexOffset, vertices.Length, null, 0, primitiveCount) {
        }

        public PrimitiveDrawCall (
            PrimitiveType primitiveType, T[] vertices, int vertexOffset, int vertexCount, 
            short[] indices, int indexOffset, int primitiveCount,
            DrawCallSortKey sortKey = default(DrawCallSortKey),
            PrimitiveBeforeDraw<T> beforeDraw = null,
            object userData = null
        ) {
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
            BeforeDraw = beforeDraw;
            UserData = userData;
            SortKey = sortKey;
        }
    }

    public sealed class NativeBatch : ListBatch<NativeDrawCall> {
        private Action<DeviceManager, object> _BatchSetup;
        private Action<DeviceManager, object> _BatchTeardown;
        private object _UserData;

        private VertexBufferBinding[] TwoBindings = new VertexBufferBinding[2], 
            ThreeBindings = new VertexBufferBinding[3];

        private static long _PrimitiveCount, _CommandCount = 0;

        public void Initialize (
            IBatchContainer container, int layer, Material material, 
            Action<DeviceManager, object> batchSetup, 
            Action<DeviceManager, object> batchTeardown,
            object userData
        ) {
            base.Initialize(container, layer, material, true);

            _BatchSetup = batchSetup;
            _BatchTeardown = batchTeardown;
            _UserData = userData;
        }

        new public void Add (NativeDrawCall item) {
            Add(ref item);
        }

        new public void Add (ref NativeDrawCall item) {
            if (item.PrimitiveCount < 1)
                throw new ArgumentException("At least one primitive must be drawn", "item.PrimitiveCount");
            base.Add(ref item);
        }

        protected override void Prepare (PrepareManager manager) {
            // FIXME: Why the hell do we have to record these in Prepare and not Issue? >:|
            long primCount = 0;

            for (int i = 0, c = _DrawCalls.Count; i < c; i++) {
                ref var call = ref _DrawCalls.Item(i);
                if (call.InstanceCount.HasValue)
                    primCount += call.PrimitiveCount * call.InstanceCount.Value;
                else
                    primCount += call.PrimitiveCount;
            }

            RecordPrimitives(primCount);
        }

        public static void IssueDrawCall (
            GraphicsDevice device, ref NativeDrawCall call, 
            VertexBufferBinding[] twoBindings, VertexBufferBinding[] threeBindings
        ) {
            if (twoBindings?.Length != 2)
                throw new ArgumentException("twoBindings");
            if (threeBindings?.Length != 3)
                throw new ArgumentException("threeBindings");

            if (call.InstanceCount.HasValue) {
                if (call.VertexBuffer3 != null) {
                    threeBindings[0] = new VertexBufferBinding(call.VertexBuffer, 0, 0);
                    threeBindings[1] = new VertexBufferBinding(call.VertexBuffer2, call.VertexOffset2, 1);
                    threeBindings[2] = new VertexBufferBinding(call.VertexBuffer3, call.VertexOffset3, 1);
                    device.SetVertexBuffers(threeBindings);
                } else if (call.VertexBuffer2 != null) {
                    twoBindings[0] = new VertexBufferBinding(call.VertexBuffer, 0, 0);
                    twoBindings[1] = new VertexBufferBinding(call.VertexBuffer2, call.VertexOffset2, 1);
                    device.SetVertexBuffers(twoBindings);
                } else {
                    // FIXME: Throw?
                    device.SetVertexBuffers(call.VertexBuffer);
                }

                device.Indices = call.IndexBuffer;
                device.DrawInstancedPrimitives(
                    call.PrimitiveType, call.BaseVertex, call.MinVertexIndex, 
                    call.NumVertices, call.StartIndex, call.PrimitiveCount, 
                    call.InstanceCount.Value
                );
                return;
            }

            device.SetVertexBuffer(call.VertexBuffer, call.VertexOffset);
            device.Indices = call.IndexBuffer;

            if (call.IndexBuffer != null)
                device.DrawIndexedPrimitives(call.PrimitiveType, call.BaseVertex, call.MinVertexIndex, call.NumVertices, call.StartIndex, call.PrimitiveCount);
            else
                device.DrawPrimitives(call.PrimitiveType, call.StartVertex, call.PrimitiveCount);
        }

        public override void Issue (DeviceManager manager) {
            base.Issue(manager);

            if (_BatchSetup != null)
                _BatchSetup(manager, _UserData);

            if (_DrawCalls.Count > 0) {
                manager.ApplyMaterial(Material, ref MaterialParameters);
                {
                    var device = manager.Device;

                    for (int i = 0, c = _DrawCalls.Count; i < c; i++) {
                        ref var call = ref _DrawCalls.Item(i);
                        IssueDrawCall(device, ref call, TwoBindings, ThreeBindings);
                    }

                    device.SetVertexBuffer(null);
                    device.Indices = null;
                }
            }

            if (_BatchTeardown != null)
                _BatchTeardown(manager, _UserData);
        }

        public static NativeBatch New (
            IBatchContainer container, int layer, Material material, 
            Action<DeviceManager, object> batchSetup = null, 
            Action<DeviceManager, object> batchTeardown = null, 
            object userData = null
        ) {
            if (container == null)
                throw new ArgumentNullException("container");
            if (material == null)
                throw new ArgumentNullException("material");

            var result = container.RenderManager.AllocateBatch<NativeBatch>();
            result.Initialize(container, layer, material, batchSetup, batchTeardown, userData);
            result.CaptureStack(0);
            return result;
        }

        public static void RecordCommands (long count) {
            Interlocked.Add(ref _CommandCount, count);
        }

        public static void RecordPrimitives (long count) {
            Interlocked.Add(ref _PrimitiveCount, count);
        }

        public static long LifetimeCommandCount {
            get {
                // HACK
                return Interlocked.Add(ref _CommandCount, 0);
            }
        }

        public static long LifetimePrimitiveCount {
            get {
                // HACK
                return Interlocked.Add(ref _PrimitiveCount, 0);
            }
        }
    }

    public struct NativeDrawCall {
        public readonly PrimitiveType PrimitiveType;
        public readonly VertexBuffer VertexBuffer, VertexBuffer2, VertexBuffer3;
        public readonly IndexBuffer IndexBuffer;

        public readonly int VertexOffset, VertexOffset2, VertexOffset3;
        public readonly int BaseVertex;
        public readonly int MinVertexIndex;
        public readonly int NumVertices;
        public readonly int StartIndex;
        public readonly int StartVertex;
        public readonly int PrimitiveCount;

        public readonly int? InstanceCount;

        /// <summary>
        /// This maps to a call to DrawIndexedPrimitives.
        /// </summary>
        /// <param name="primitiveType">Describes the type of primitive to render. PrimitiveType.PointList is not supported.</param>
        /// <param name="vertexOffset">The offset (in vertices) from the beginning of the vertex buffer.</param>
        /// <param name="baseVertex">Offset to add to each vertex index in the index buffer.</param>
        /// <param name="minVertexIndex">Minimum vertex index for vertices used during the call. The minVertexIndex parameter and all of the indices in the index stream are relative to the baseVertex parameter.</param>
        /// <param name="numVertices">Number of vertices used during the call. The first vertex is located at index: baseVertex + minVertexIndex.</param>
        /// <param name="startIndex">Location in the index buffer at which to start reading vertices.</param>
        /// <param name="primitiveCount">Number of primitives to render. The number of vertices used is a function of primitiveCount and primitiveType.</param>
        public NativeDrawCall (
            PrimitiveType primitiveType, 
            VertexBuffer vertexBuffer, int vertexOffset, 
            IndexBuffer indexBuffer, 
            int baseVertex, int minVertexIndex, int numVertices, 
            int startIndex, int primitiveCount
        ) {
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
            VertexOffset2 = VertexOffset3 = 0;
            VertexBuffer2 = VertexBuffer3 = null;
            InstanceCount = null;
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
            VertexOffset2 = VertexOffset3 = 0;
            VertexBuffer2 = VertexBuffer3 = null;
            InstanceCount = null;
        }

        public NativeDrawCall (
            PrimitiveType primitiveType, 
            VertexBuffer vertexBuffer1, int vertexOffset1,
            VertexBuffer vertexBuffer2, int vertexOffset2,
            VertexBuffer vertexBuffer3, int vertexOffset3,
            IndexBuffer indexBuffer, 
            int baseVertex, int minVertexIndex, int numVertices, 
            int startIndex, int primitiveCount,
            int instanceCount
        ) {
            if (vertexBuffer1 == null)
                throw new ArgumentNullException("vertexBuffer1");
            if (vertexBuffer2 == null)
                throw new ArgumentNullException("vertexBuffer2");

            PrimitiveType = primitiveType;
            VertexBuffer = vertexBuffer1;
            VertexOffset = vertexOffset1;
            VertexBuffer2 = vertexBuffer2;
            VertexOffset2 = vertexOffset2;
            VertexBuffer3 = vertexBuffer3;
            VertexOffset3 = vertexOffset3;
            IndexBuffer = indexBuffer;
            BaseVertex = baseVertex;
            MinVertexIndex = minVertexIndex;
            NumVertices = numVertices;
            StartIndex = startIndex;
            PrimitiveCount = primitiveCount;

            StartVertex = 0;
            InstanceCount = instanceCount;
        }
    }
}
