using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.Xna.Framework.Graphics;
using Squared.Util;

#if PSM
using Sce.PlayStation.Core.Graphics;
#endif

namespace Squared.Render.Internal {
    public interface IBufferGenerator : IDisposable {
        void Reset();
        void Flush();
    }

    public interface IHardwareBuffer : IDisposable {
        void SetInactive (GraphicsDevice device);
        void SetActive (GraphicsDevice device);
        int VertexCount { get; }
        int IndexCount { get; }
        int Id { get; }
    }

    public interface ISoftwareBuffer {
        IHardwareBuffer HardwareBuffer { get; }
        int HardwareVertexOffset { get; }
        int HardwareIndexOffset { get; }
        int Id { get; }
    }

    public abstract class BufferGenerator<THardwareBuffer, TVertex, TIndex> : IBufferGenerator
        where THardwareBuffer : class, IHardwareBuffer
        where TVertex : struct
        where TIndex : struct
    {
        protected class SoftwareBufferPool : BaseObjectPool<SoftwareBuffer> {
            public readonly BufferGenerator<THardwareBuffer, TVertex, TIndex> BufferGenerator;

            public SoftwareBufferPool (BufferGenerator<THardwareBuffer, TVertex, TIndex> bufferGenerator) {
                BufferGenerator = bufferGenerator;
            }

            protected override SoftwareBuffer AllocateNew () {
                return new SoftwareBuffer(BufferGenerator);
            }
        }

        protected class HardwareBufferEntry {
            public readonly int Index;
            public readonly int VertexOffset;
            public readonly int IndexOffset;

            public int SoftwareBufferCount;
            public int VertexCount;
            public int IndexCount;

            public THardwareBuffer HardwareBuffer;

            public HardwareBufferEntry (int index, int vertexOffset, int indexOffset) {
                Index = index;
                VertexOffset = vertexOffset;
                IndexOffset = indexOffset;
            }
        }

        public class SoftwareBuffer : ISoftwareBuffer {
            private static volatile int NextId;

            public readonly BufferGenerator<THardwareBuffer, TVertex, TIndex> BufferGenerator;
            internal bool IsValid = false;

            public int Id {
                get;
                private set;
            }

            public IHardwareBuffer HardwareBuffer {
                get {
                    return BufferGenerator.GetHardwareBufferByIndex(HardwareBufferIndex);
                }
            }

            public int HardwareBufferIndex {
                get;
                private set;
            }

            public int HardwareVertexOffset {
                get;
                private set;
            }

            public int HardwareIndexOffset {
                get;
                private set;
            }

            public ArraySegment<TVertex> Vertices {
                get;
                private set;
            }

            public ArraySegment<TIndex> Indices {
                get;
                private set;
            }

            internal SoftwareBuffer (BufferGenerator<THardwareBuffer, TVertex, TIndex> bufferGenerator) {
                Id = Interlocked.Increment(ref NextId);
                BufferGenerator = bufferGenerator;
            }

            public void Uninitialize () {
                HardwareBufferIndex = -1;
                IsValid = false;
            }

            public void Initialize (
                ArraySegment<TVertex> vertices, ArraySegment<TIndex> indices,
                int hardwareBufferIndex, int vertexOffset, int indexOffset
            ) {
                if (IsValid)
                    throw new ThreadStateException("Software buffer already initialized.");

                Vertices = vertices;
                Indices = indices;
                HardwareBufferIndex = hardwareBufferIndex;
                HardwareVertexOffset = vertexOffset;
                HardwareIndexOffset = indexOffset;
                IsValid = true;
            }

            public override string ToString() {
                return String.Format("<Software Buffer #{0} ({1})>", Id, IsValid ? "valid" : "invalid");
            }
        }

        protected struct PendingCopy {
            public Array Source, Destination;
            public int SourceIndex, DestinationIndex, Count;

            public void Execute () {
                // FIXME: Acquire lock(s) here?
                Array.Copy(Source, SourceIndex, Destination, DestinationIndex, Count);
            }
        }

        protected readonly SoftwareBufferPool _SoftwareBufferPool;
        protected readonly UnorderedList<SoftwareBuffer> _SoftwareBuffers = new UnorderedList<SoftwareBuffer>();
        protected readonly UnorderedList<THardwareBuffer> _UnusedHardwareBuffers = new UnorderedList<THardwareBuffer>();
        protected readonly Dictionary<int, HardwareBufferEntry> _UsedHardwareBuffers = new Dictionary<int, HardwareBufferEntry>();
        protected readonly UnorderedList<PendingCopy> _PendingCopies = new UnorderedList<PendingCopy>();

        protected object _FillingHardwareBufferLock = new object();
        protected HardwareBufferEntry _FillingHardwareBufferEntry = null;

        protected volatile int _VertexCount = 0, _IndexCount = 0;
        protected TVertex[] _VertexArray;
        protected TIndex[] _IndexArray;
        protected volatile int _FlushedToBuffers = 0;

        const int InitialArraySize = 4096;

        const int MinVerticesPerBuffer = 1024;
        const int MinIndicesPerBuffer = 1536;
        
        // Modify these per-platform as necessary
        protected int MaxVerticesPerHardwareBuffer = UInt16.MaxValue;
        protected int MaxSoftwareBuffersPerHardwareBuffer = 1;

        public readonly GraphicsDevice GraphicsDevice;
        public readonly object CreateResourceLock;
        public readonly object UseResourceLock;
        public readonly Action<IDisposable> DisposeResource;

        public BufferGenerator (
            GraphicsDevice graphicsDevice, 
            object createResourceLock, object useResourceLock, 
            Action<IDisposable> disposeResource
        ) {
            if (graphicsDevice == null)
                throw new ArgumentNullException("graphicsDevice");
            if (createResourceLock == null)
                throw new ArgumentNullException("createResourceLock");
            if (useResourceLock == null)
                throw new ArgumentNullException("useResourceLock");
            if (disposeResource == null)
                throw new ArgumentNullException("disposeResource");

            _SoftwareBufferPool = new SoftwareBufferPool(this);
            GraphicsDevice = graphicsDevice;
            CreateResourceLock = createResourceLock;
            UseResourceLock = useResourceLock;
            DisposeResource = disposeResource;
            _VertexArray = new TVertex[InitialArraySize];
            _IndexArray = new TIndex[InitialArraySize];
        }

        protected abstract THardwareBuffer AllocateHardwareBuffer (int vertexCount, int indexCount);
        protected abstract void FillHardwareBuffer (
            THardwareBuffer buffer, 
            object useResourceLock,
            object vertexLock,
            ArraySegment<TVertex> vertices, 
            object indexLock,
            ArraySegment<TIndex> indices
        );

        public virtual void Dispose () {
            lock (_UnusedHardwareBuffers) {
                foreach (var buffer in _UnusedHardwareBuffers)
                    buffer.Dispose();

                _UnusedHardwareBuffers.Clear();
            }

            lock (_PendingCopies)
                _PendingCopies.Clear();

            lock (_VertexArray)
                _VertexArray = null;
            lock (_IndexArray)
                _IndexArray = null;

            _VertexCount = _IndexCount = 0;

            _FlushedToBuffers = 0;
        }

        protected virtual int PickNewArraySize (int previousSize, int requestedSize) {
            return 1 << (int)Math.Ceiling(Math.Log(requestedSize, 2));
        }

        public void Reset () {
            lock (_FillingHardwareBufferLock)
                _FillingHardwareBufferEntry = null;

            // Any buffers that remain unused (either from being too small, or being unnecessary now)
            //  should be disposed.
            lock (_UnusedHardwareBuffers) {
                foreach (var hb in _UnusedHardwareBuffers) {
                    // HACK
                    if (hb != null)
                        DisposeResource(hb);
                }
                _UnusedHardwareBuffers.Clear();
            }

            // Return any buffers that were used this frame to the unused state.
            lock (_UsedHardwareBuffers) {
                foreach (var hb in _UsedHardwareBuffers.Values) {
                    // HACK
                    if (hb.HardwareBuffer != null) {
                        lock (_UnusedHardwareBuffers)
                            _UnusedHardwareBuffers.Add(hb.HardwareBuffer);
                    }
                }

                _UsedHardwareBuffers.Clear();
            }

            lock (_SoftwareBuffers) {
                foreach (var swb in _SoftwareBuffers) {
                    swb.Uninitialize();
                    _SoftwareBufferPool.Release(swb);
                }

                _SoftwareBuffers.Clear();
            }

            _FlushedToBuffers = 0;
            _VertexCount = _IndexCount = 0;
        }

        private void EnsureBufferCapacity<T> (ref T[] array, ref int count, int oldCount, int newCount) {
            var oldArray = array;

            lock (oldArray) {
                if (newCount <= oldArray.Length)
                    return;

                var newSize = PickNewArraySize(oldArray.Length, newCount);
                var newArray = new T[newSize];

                lock (_PendingCopies)
                    _PendingCopies.Add(new PendingCopy {
                        Source = oldArray,
                        SourceIndex = 0,
                        Destination = newArray,
                        DestinationIndex = 0,
                        Count = oldCount
                    });

                array = newArray;
            }
        }

        /// <summary>
        /// Allocates a software vertex/index buffer pair that you can write vertices and indices into. 
        /// Once this generator is flushed, it will have an associated hardware buffer containing your vertex/index data.
        /// </summary>
        /// <param name="vertexCount">The number of vertices.</param>
        /// <param name="indexCount">The number of indices.</param>
        /// <param name="forceExclusiveBuffer">Forces a unique hardware vertex/index buffer pair to be created for this allocation. This allows you to ignore the hardware vertex/index offsets.</param>
        /// <returns>A software buffer.</returns>
        public SoftwareBuffer Allocate (int vertexCount, int indexCount, bool forceExclusiveBuffer = false) {
            if (vertexCount > MaxVerticesPerHardwareBuffer)
                throw new ArgumentOutOfRangeException("vertexCount", vertexCount, "Maximum vertex count on this platform is " + MaxVerticesPerHardwareBuffer);

            if (_FlushedToBuffers != 0)
                throw new InvalidOperationException("Already flushed");

            var newVertexCount = Interlocked.Add(ref _VertexCount, vertexCount);
            int oldVertexCount = newVertexCount - vertexCount;
            var newIndexCount = Interlocked.Add(ref _IndexCount, indexCount);
            int oldIndexCount = newIndexCount - indexCount;
                
            // When we resize our internal array, we have to queue up copies from the old small array to the new large array.
            // This is because while Allocate is thread-safe, consumers are allowed to write to their allocations without synchronization,
            //  so we can't be sure that the old array has been filled yet.
            // Flush() is responsible for doing all these copies to ensure that all vertex data eventually makes it into the large array
            //  from which actual hardware buffer initialization occurs.

            SoftwareBuffer swb;

            EnsureBufferCapacity(ref _VertexArray, ref _VertexCount, oldVertexCount, newVertexCount);
            EnsureBufferCapacity(ref _IndexArray, ref _IndexCount, oldIndexCount, newIndexCount);

            HardwareBufferEntry hardwareBufferEntry;

            lock (_FillingHardwareBufferLock) {
                hardwareBufferEntry = _FillingHardwareBufferEntry;
                if (
                    (hardwareBufferEntry == null) ||
                    ((hardwareBufferEntry.VertexCount + vertexCount) > MaxVerticesPerHardwareBuffer) ||
                    (hardwareBufferEntry.SoftwareBufferCount >= MaxSoftwareBuffersPerHardwareBuffer) ||
                    forceExclusiveBuffer
                ) {
                    lock (_UsedHardwareBuffers) {
                        var index = _UsedHardwareBuffers.Count;

                        _FillingHardwareBufferEntry = hardwareBufferEntry =
                                new HardwareBufferEntry(index, oldVertexCount, oldIndexCount);
                        _UsedHardwareBuffers.Add(index, hardwareBufferEntry);
                    }
                }
            }

            var oldHwbVertexCount = Interlocked.Add(ref hardwareBufferEntry.VertexCount, vertexCount) - vertexCount;
            var oldHwbIndexCount = Interlocked.Add(ref hardwareBufferEntry.IndexCount, indexCount) - indexCount;
            Interlocked.Add(ref hardwareBufferEntry.SoftwareBufferCount, 1);

            swb = _SoftwareBufferPool.Allocate();
            swb.Initialize(
                new ArraySegment<TVertex>(_VertexArray, hardwareBufferEntry.VertexOffset + oldHwbVertexCount, vertexCount),
                new ArraySegment<TIndex>(_IndexArray, hardwareBufferEntry.IndexOffset + oldHwbIndexCount, indexCount),
                hardwareBufferEntry.Index,
                oldHwbVertexCount,
                oldHwbIndexCount
            );

            lock (_SoftwareBuffers)
                _SoftwareBuffers.Add(swb);

            return swb;
        }

        protected THardwareBuffer AllocateSuitablySizedHardwareBuffer (int vertexCount, int indexCount) {
            THardwareBuffer buffer;

            lock (_UnusedHardwareBuffers)
            using (var e = _UnusedHardwareBuffers.GetEnumerator())
            while (e.GetNext(out buffer)) {
                if (
                    (buffer.VertexCount >= vertexCount) &&
                    (buffer.IndexCount >= indexCount)
                ) {
                    // This buffer is large enough, so return it.
                    e.RemoveCurrent();

                    return buffer;
                }
            }

            if (vertexCount < MinVerticesPerBuffer)
                vertexCount = MinVerticesPerBuffer;
            if (indexCount < MinIndicesPerBuffer)
                indexCount = MinIndicesPerBuffer;

            // We didn't find a suitably large buffer.
            lock (CreateResourceLock)
                buffer = AllocateHardwareBuffer(vertexCount, indexCount);
            return buffer;
        }

        protected void FlushToBuffers () {
            var va = _VertexArray;
            var ia = _IndexArray;

            lock (_UsedHardwareBuffers)
            foreach (var kvp in _UsedHardwareBuffers) {
                var index = kvp.Key;
                var hwbe = kvp.Value;

                if (hwbe.VertexCount <= 0)
                    continue;
                var hwb = AllocateSuitablySizedHardwareBuffer(hwbe.VertexCount, hwbe.IndexCount);
                hwbe.HardwareBuffer = hwb;

                ArraySegment<TVertex> vertexSegment;
                ArraySegment<TIndex> indexSegment;

                lock (va)
                    vertexSegment = new ArraySegment<TVertex>(va, hwbe.VertexOffset, hwbe.VertexCount);
                lock (ia)
                    indexSegment = new ArraySegment<TIndex>(ia, hwbe.IndexOffset, hwbe.IndexCount);

                FillHardwareBuffer(
                    hwb, UseResourceLock,
                    va, vertexSegment, 
                    ia, indexSegment
                );
            }
        }

        public void Flush () {
            lock (_PendingCopies)
            if (_PendingCopies.Count > 0) {
                PendingCopy pc;

                using (var e = _PendingCopies.GetEnumerator())
                while (e.GetNext(out pc))
                    pc.Execute();

                _PendingCopies.Clear();
            }

            if (Interlocked.Exchange(ref _FlushedToBuffers, 1) == 0)
                FlushToBuffers();
        }

        internal THardwareBuffer GetHardwareBufferByIndex (int index) {
            HardwareBufferEntry entry;
            bool gotOne;

            lock (_UsedHardwareBuffers)
                gotOne = _UsedHardwareBuffers.TryGetValue(index, out entry);

            if (gotOne)
                return entry.HardwareBuffer;
            else
                return null;
        }
    }

#if PSM
    public class PSMHardwareBuffer : IHardwareBuffer {
    }

    public class PSMBufferGenerator<TVertex> : BufferGenerator<Sce.PlayStation.Core.Graphics.VertexBuffer, TVertex, ushort> 
        where TVertex : struct {
        
        public static VertexFormat[] VertexFormat = null;

        public XNABufferGenerator (GraphicsDevice graphicsDevice, object useResourceLock, Action<IDisposable> disposeResource)
            : base(graphicsDevice, useResourceLock, disposeResource) {
            
            if (VertexFormat == null)
                throw new InvalidOperationException("Please set PSMBufferGenerator<TVertex>.VertexFormat first.");
        }

        protected override void FlushToBuffer () {
            if (
                (_Buffer != null) &&
                (
                    (_Buffer.VertexCount < _VertexArray.Length) ||
                    (_Buffer.IndexCount < _IndexArray.Length)
                )
            ) {
                DisposeResource(_Buffer);
                _Buffer = null;
            }

            if (_VertexArray.Length >= UInt16.MaxValue)
                throw new InvalidOperationException("Too many vertices");

            if (_Buffer == null)
                _Buffer = new Sce.PlayStation.Core.Graphics.VertexBuffer(_VertexArray.Length, _IndexArray.Length, VertexFormat);

            _Buffer.SetVertices(_VertexArray, 0, 0, _VertexCount);
            _Buffer.SetIndices(_IndexArray, 0, 0, _IndexCount);
        }
    }
#else
    public class XNABufferPair<TVertex> : IHardwareBuffer
        where TVertex : struct 
    {
        private static volatile int NextId;
        
        public readonly DynamicVertexBuffer Vertices;
        public readonly DynamicIndexBuffer Indices;

        public XNABufferPair (GraphicsDevice graphicsDevice, int vertexCount, int indexCount) {
            if (vertexCount >= UInt16.MaxValue)
                throw new ArgumentOutOfRangeException("vertexCount", vertexCount, "Vertex count must be less than UInt16.MaxValue");

            Id = Interlocked.Increment(ref NextId);
            Vertices = new DynamicVertexBuffer(graphicsDevice, typeof(TVertex), vertexCount, BufferUsage.WriteOnly);
            Indices = new DynamicIndexBuffer(graphicsDevice, IndexElementSize.SixteenBits, indexCount, BufferUsage.WriteOnly);

            VertexCount = vertexCount;
            IndexCount = indexCount;
        }

        public void SetInactive (GraphicsDevice device) {
            device.SetVertexBuffer(null);
            device.Indices = null;
        }

        public void SetActive (GraphicsDevice device) {
            device.SetVertexBuffer(Vertices);
            device.Indices = Indices;
        }

        public void Dispose () {
            Vertices.Dispose();
            Indices.Dispose();
        }

        public int Id {
            get;
            private set;
        }

        public int VertexCount {
            get;
            private set;
        }

        public int IndexCount {
            get;
            private set;
        }

        public override string ToString() {
            return String.Format("XNABufferPair<{0}> #{1}", typeof(TVertex).Name, Id);
        }
    }

    public class XNABufferGenerator<TVertex> : BufferGenerator<XNABufferPair<TVertex>, TVertex, ushort> 
        where TVertex : struct {

        public XNABufferGenerator (
            GraphicsDevice graphicsDevice, 
            object createResourceLock, object useResourceLock, 
            Action<IDisposable> disposeResource
        )
            : base(graphicsDevice, createResourceLock, useResourceLock, disposeResource) {

            MaxSoftwareBuffersPerHardwareBuffer = 1;
        }

        protected override XNABufferPair<TVertex> AllocateHardwareBuffer (int vertexCount, int indexCount) {
            return new XNABufferPair<TVertex>(GraphicsDevice, vertexCount, indexCount);
        }

        protected override void FillHardwareBuffer (
            XNABufferPair<TVertex> hardwareBuffer, object useResourceLock,
            object vertexLock,
            ArraySegment<TVertex> vertices,
            object indexLock,
            ArraySegment<ushort> indices
        ) {
            lock (useResourceLock)
            lock (vertexLock)
                hardwareBuffer.Vertices.SetData(vertices.Array, vertices.Offset, vertices.Count);

            lock (useResourceLock)
            lock (indexLock)
                hardwareBuffer.Indices.SetData(indices.Array, indices.Offset, indices.Count);
        }
    }
#endif
}
