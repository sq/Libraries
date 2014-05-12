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
        void Invalidate ();
        void Validate ();
        int VertexCount { get; }
        int IndexCount { get; }
        int Id { get; }
        int Age { get; set; }
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
            public readonly int VertexOffset;
            public readonly int IndexOffset;
            public int SourceVertexCount;
            public int SourceIndexCount;

            public int SoftwareBufferCount;
            public int VerticesUsed;
            public int IndicesUsed;

            public THardwareBuffer Buffer;

            public HardwareBufferEntry (
                THardwareBuffer buffer,
                int vertexOffset, int indexOffset,
                int sourceVertexCount, int sourceIndexCount
            ) {
                Buffer = buffer;
                VertexOffset = vertexOffset;
                IndexOffset = indexOffset;
                SourceVertexCount = sourceVertexCount;
                SourceIndexCount = sourceIndexCount;
                VerticesUsed = IndicesUsed = 0;
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
                IsValid = false;
            }

            public void Initialize (
                ArraySegment<TVertex> vertices, ArraySegment<TIndex> indices,
                THardwareBuffer hardwareBuffer, int hardwareVertexOffset, int hardwareIndexOffset
            ) {
                if (IsValid)
                    throw new ThreadStateException("Software buffer already initialized.");

                Vertices = vertices;
                Indices = indices;
                HardwareBuffer = hardwareBuffer;
                HardwareVertexOffset = hardwareVertexOffset;
                HardwareIndexOffset = hardwareIndexOffset;
                IsValid = true;
            }

            public override string ToString() {
                return String.Format("<Software Buffer #{0} ({1} - {2})>", Id, IsValid ? "valid" : "invalid", HardwareBuffer);
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
        protected readonly UnorderedList<HardwareBufferEntry> _UsedHardwareBuffers = new UnorderedList<HardwareBufferEntry>();
        protected readonly UnorderedList<PendingCopy> _PendingCopies = new UnorderedList<PendingCopy>();

        protected object _FillingHardwareBufferLock = new object();
        protected HardwareBufferEntry _FillingHardwareBufferEntry = null;

        protected volatile int _VertexCount = 0, _IndexCount = 0;
        protected TVertex[] _VertexArray;
        protected TIndex[] _IndexArray;
        protected volatile int _FlushedToBuffers = 0;

        const int MaxBufferAge = 16;
        const int MaxUnusedBuffers = 16;
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

        void IBufferGenerator.Reset () {
            _FillingHardwareBufferEntry = null;
            _FlushedToBuffers = 0;
            _VertexCount = _IndexCount = 0;

            // Any buffers that remain unused (either from being too small, or being unnecessary now)
            //  should be disposed.
            lock (_UnusedHardwareBuffers) {
                THardwareBuffer hb;
                using (var e = _UnusedHardwareBuffers.GetEnumerator())
                while (e.GetNext(out hb)) {
                    hb.Age += 1;

                    bool shouldKill = (hb.Age >= MaxBufferAge) ||
                        ((_UnusedHardwareBuffers.Count > MaxUnusedBuffers) && (hb.Age > 1));

                    if (shouldKill) {
                        e.RemoveCurrent();
                        hb.Invalidate();

                        // HACK
                        if (hb != null)
                            DisposeResource(hb);
                    }
                }
            }

            // Return any buffers that were used this frame to the unused state.
            lock (_UsedHardwareBuffers) {                
                foreach (var hb in _UsedHardwareBuffers) {
                    // HACK
                    var hwb = hb.Buffer;
                    hwb.Invalidate();

                    lock (_UnusedHardwareBuffers)
                        _UnusedHardwareBuffers.Add(hwb);
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

        private HardwareBufferEntry PrepareToFillBuffer (
            HardwareBufferEntry currentBufferEntry, 
            int vertexOffset, int indexOffset, 
            int additionalVertexCount, int additionalIndexCount,
            bool forceExclusiveBuffer
        ) {
            var allocateNew = (currentBufferEntry == null) || 
                forceExclusiveBuffer ||
                CannotFitInBuffer(currentBufferEntry, additionalVertexCount, additionalIndexCount);

            if (allocateNew) {
                var newBuffer = AllocateSuitablySizedHardwareBuffer(additionalVertexCount, additionalIndexCount);
                var entry = new HardwareBufferEntry(newBuffer, vertexOffset, indexOffset, additionalVertexCount, additionalIndexCount);

                _UsedHardwareBuffers.Add(entry);
                _FillingHardwareBufferEntry = entry;

                return entry;
            } else {
                currentBufferEntry.SourceVertexCount += additionalVertexCount;
                currentBufferEntry.SourceIndexCount += additionalIndexCount;

                return currentBufferEntry;
            }
        }

        private bool CannotFitInBuffer (HardwareBufferEntry hbe, int vertexCount, int indexCount) {
            lock (hbe) {
                var currentUsed = hbe.VerticesUsed;
                var newUsed = hbe.VerticesUsed + vertexCount;

                if (newUsed >= hbe.Buffer.VertexCount)
                    return true;

                if (hbe.SoftwareBufferCount >= MaxSoftwareBuffersPerHardwareBuffer)
                    return true;
            }

            return false;
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

                lock (_UsedHardwareBuffers)
                    hardwareBufferEntry = PrepareToFillBuffer(
                        hardwareBufferEntry,
                        oldVertexCount, oldIndexCount, 
                        vertexCount, indexCount, 
                        forceExclusiveBuffer
                    );
            }

            int oldHwbVerticesUsed, oldHwbIndicesUsed;

            lock (hardwareBufferEntry) {
                // Guess the interlocked isn't really needed...
                oldHwbVerticesUsed = Interlocked.Add(ref hardwareBufferEntry.VerticesUsed, vertexCount) - vertexCount;
                oldHwbIndicesUsed = Interlocked.Add(ref hardwareBufferEntry.IndicesUsed, indexCount) - indexCount;
                Interlocked.Add(ref hardwareBufferEntry.SoftwareBufferCount, 1);
            }

            swb = _SoftwareBufferPool.Allocate();
            swb.Initialize(
                new ArraySegment<TVertex>(_VertexArray, hardwareBufferEntry.VertexOffset + oldHwbVerticesUsed, vertexCount),
                new ArraySegment<TIndex>(_IndexArray, hardwareBufferEntry.IndexOffset + oldHwbIndicesUsed, indexCount),
                hardwareBufferEntry.Buffer,
                oldHwbVerticesUsed,
                oldHwbIndicesUsed
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

                    buffer.Age = Arithmetic.Clamp(buffer.Age - 2, 0, MaxBufferAge);
                    return buffer;
                }
            }

            if (MaxSoftwareBuffersPerHardwareBuffer > 1) {
                if (vertexCount < MinVerticesPerBuffer)
                    vertexCount = MinVerticesPerBuffer;
                if (indexCount < MinIndicesPerBuffer)
                    indexCount = MinIndicesPerBuffer;
            }

            // We didn't find a suitably large buffer.
            lock (CreateResourceLock)
                buffer = AllocateHardwareBuffer(vertexCount, indexCount);

            return buffer;
        }

        protected void FlushToBuffers () {
            var va = _VertexArray;
            var ia = _IndexArray;

            lock (_UsedHardwareBuffers)
            foreach (var hwbe in _UsedHardwareBuffers) {
                ArraySegment<TVertex> vertexSegment;
                ArraySegment<TIndex> indexSegment;

                lock (va)
                    vertexSegment = new ArraySegment<TVertex>(va, hwbe.VertexOffset, hwbe.SourceVertexCount);
                lock (ia)
                    indexSegment = new ArraySegment<TIndex>(ia, hwbe.IndexOffset, hwbe.SourceIndexCount);

                FillHardwareBuffer(
                    hwbe.Buffer, UseResourceLock,
                    va, vertexSegment, 
                    ia, indexSegment
                );

                hwbe.Buffer.Validate();
            }
        }

        void IBufferGenerator.Flush () {
            if (Interlocked.Exchange(ref _FlushedToBuffers, 1) == 0) {
                lock (_PendingCopies)
                if (_PendingCopies.Count > 0) {
                    PendingCopy pc;

                    using (var e = _PendingCopies.GetEnumerator())
                    while (e.GetNext(out pc))
                        pc.Execute();

                    _PendingCopies.Clear();
                }

                FlushToBuffers();
            } else {
                throw new InvalidOperationException("Buffer generator flushed twice in a row");
            }
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
        internal volatile int _IsValid, _IsActive;

        public readonly GraphicsDevice GraphicsDevice;
        public DynamicVertexBuffer Vertices;
        public DynamicIndexBuffer Indices;
        public readonly Action<IDisposable> DisposeResource;

        public XNABufferPair (
            GraphicsDevice graphicsDevice, int id,
            int vertexCount, int indexCount, 
            Action<IDisposable> disposeResource
        ) {
            if (vertexCount >= UInt16.MaxValue)
                throw new ArgumentOutOfRangeException("vertexCount", vertexCount, "Vertex count must be less than UInt16.MaxValue");

            Id = id;
            GraphicsDevice = graphicsDevice;
            VertexCount = vertexCount;
            IndexCount = indexCount;
            DisposeResource = disposeResource;
        }

        public void SetInactive (GraphicsDevice device) {
            var wasActive = Interlocked.Exchange(ref _IsActive, 0);
            if (wasActive != 1)
                throw new InvalidOperationException("Buffer not active");
            if (_IsValid != 1)
                throw new ThreadStateException("Buffer not valid");

            device.SetVertexBuffer(null);
            device.Indices = null;
        }

        public void SetActive (GraphicsDevice device) {
            var wasActive = Interlocked.Exchange(ref _IsActive, 1);
            if (wasActive != 0)
                throw new InvalidOperationException("Buffer already active");

            if (_IsValid != 1)
                throw new ThreadStateException("Buffer not valid");

            device.SetVertexBuffer(Vertices);
            device.Indices = Indices;
        }

        public void Allocate () {
            Vertices = new DynamicVertexBuffer(GraphicsDevice, typeof(TVertex), VertexCount, BufferUsage.WriteOnly);
            Indices = new DynamicIndexBuffer(GraphicsDevice, IndexElementSize.SixteenBits, IndexCount, BufferUsage.WriteOnly);
            IsAllocated = true;
        }

        public void Dispose () {
            DisposeResource(Vertices);
            DisposeResource(Indices);
            Vertices = null;
            Indices = null;
            IsAllocated = false;
        }

        public int Age {
            get;
            set;
        }

        public bool IsAllocated {
            get;
            private set;
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

        void IHardwareBuffer.Validate () {
            var wasActive = Interlocked.Exchange(ref _IsActive, 0);
            var wasValid = Interlocked.Exchange(ref _IsValid, 1);

            if (wasActive != 0)
                throw new InvalidOperationException("Buffer in use");
        }

        void IHardwareBuffer.Invalidate () {
            var wasActive = Interlocked.Exchange(ref _IsActive, 0);
            var wasValid = Interlocked.Exchange(ref _IsValid, 0);

            if (wasActive != 0)
                throw new InvalidOperationException("Buffer in use");
        }
    }

    public class XNABufferGenerator<TVertex> : BufferGenerator<XNABufferPair<TVertex>, TVertex, ushort> 
        where TVertex : struct {

        private static volatile int _NextBufferId = 0;

        public XNABufferGenerator (
            GraphicsDevice graphicsDevice, 
            object createResourceLock, object useResourceLock, 
            Action<IDisposable> disposeResource
        )
            : base(graphicsDevice, createResourceLock, useResourceLock, disposeResource) {

            MaxSoftwareBuffersPerHardwareBuffer = 512;
        }

        protected override XNABufferPair<TVertex> AllocateHardwareBuffer (int vertexCount, int indexCount) {
            int id = Interlocked.Increment(ref _NextBufferId);
            return new XNABufferPair<TVertex>(GraphicsDevice, id, vertexCount, indexCount, DisposeResource);
        }

        protected override void FillHardwareBuffer (
            XNABufferPair<TVertex> hardwareBuffer, object useResourceLock,
            object vertexLock,
            ArraySegment<TVertex> vertices,
            object indexLock,
            ArraySegment<ushort> indices
        ) {
            if (!hardwareBuffer.IsAllocated) {
                lock (CreateResourceLock)
                    hardwareBuffer.Allocate();
            }

            lock (useResourceLock)
            lock (vertexLock)
                hardwareBuffer.Vertices.SetData(vertices.Array, vertices.Offset, vertices.Count);

            lock (useResourceLock)
            lock (indexLock)
                hardwareBuffer.Indices.SetData(indices.Array, indices.Offset, indices.Count);

            var wasValid = Interlocked.Exchange(ref hardwareBuffer._IsValid, 1);
            if (wasValid != 0)
                throw new ThreadStateException("Buffer uploaded twice without being invalidated");
        }
    }
#endif
}
