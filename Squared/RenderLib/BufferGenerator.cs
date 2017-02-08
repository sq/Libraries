using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.Xna.Framework.Graphics;
using Squared.Util;

namespace Squared.Render.Internal {
    public interface IBufferGenerator : IDisposable {
        void Reset(int frameIndex);
        void Flush(int frameIndex);
    }

    public interface IHardwareBuffer : IDisposable {
        void SetInactive (GraphicsDevice device);
        void SetActive (GraphicsDevice device);
        void Invalidate (int frameIndex);
        void Validate (int frameIndex);
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

        protected object _StateLock = new object();
        protected int _VertexCount = 0, _IndexCount = 0;
        protected TVertex[] _VertexArray;
        protected TIndex[] _IndexArray;
        protected int _FlushedToBuffers = 0;
        private int _LastFrameReset, _LastFrameFlushed;

        protected readonly SoftwareBufferPool _SoftwareBufferPool;
        protected readonly UnorderedList<SoftwareBuffer> _SoftwareBuffers = new UnorderedList<SoftwareBuffer>();
        protected readonly UnorderedList<THardwareBuffer> _UnusedHardwareBuffers = new UnorderedList<THardwareBuffer>();
        protected readonly UnorderedList<HardwareBufferEntry> _UsedHardwareBuffers = new UnorderedList<HardwareBufferEntry>();
        protected readonly UnorderedList<PendingCopy> _PendingCopies = new UnorderedList<PendingCopy>();

        protected HardwareBufferEntry _FillingHardwareBufferEntry = null;

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
        public readonly Action<IDisposable> DisposeResource;

        public BufferGenerator (
            GraphicsDevice graphicsDevice, 
            object createResourceLock, Action<IDisposable> disposeResource
        ) {
            if (graphicsDevice == null)
                throw new ArgumentNullException("graphicsDevice");
            if (createResourceLock == null)
                throw new ArgumentNullException("createResourceLock");
            if (disposeResource == null)
                throw new ArgumentNullException("disposeResource");

            _SoftwareBufferPool = new SoftwareBufferPool(this);
            GraphicsDevice = graphicsDevice;
            CreateResourceLock = createResourceLock;
            DisposeResource = disposeResource;
            _VertexArray = new TVertex[InitialArraySize];
            _IndexArray = new TIndex[InitialArraySize];
        }

        protected abstract THardwareBuffer AllocateHardwareBuffer (int vertexCount, int indexCount);
        protected abstract void FillHardwareBuffer (
            THardwareBuffer buffer, 
            ArraySegment<TVertex> vertices, 
            ArraySegment<TIndex> indices
        );

        public virtual void Dispose () {
            lock (_StateLock) {
                foreach (var buffer in _UnusedHardwareBuffers)
                    buffer.Dispose();

                _UnusedHardwareBuffers.Clear();

                _PendingCopies.Clear();

                _VertexArray = null;
                _IndexArray = null;

                _VertexCount = _IndexCount = 0;

                _FlushedToBuffers = 0;
            }
        }

        protected virtual int PickNewArraySize (int previousSize, int requestedSize) {
            var newSize = 1 << (int)Math.Ceiling(Math.Log(requestedSize, 2));

            return newSize;
        }

        void IBufferGenerator.Reset (int frameIndex) {
            lock (_StateLock) {
                _FillingHardwareBufferEntry = null;
                _FlushedToBuffers = 0;
                _VertexCount = _IndexCount = 0;

                // Any buffers that remain unused (either from being too small, or being unnecessary now)
                //  should be disposed.
                THardwareBuffer hb;
                using (var e = _UnusedHardwareBuffers.GetEnumerator())
                while (e.GetNext(out hb)) {
                    hb.Age += 1;

                    bool shouldKill = (hb.Age >= MaxBufferAge) ||
                        ((_UnusedHardwareBuffers.Count > MaxUnusedBuffers) && (hb.Age > 1));

                    if (shouldKill) {
                        e.RemoveCurrent();
                        hb.Invalidate(frameIndex);

                        DisposeResource(hb);
                    }
                }

                // Return any buffers that were used this frame to the unused state.
                foreach (var _hb in _UsedHardwareBuffers) {
                    // HACK
                    var hwb = _hb.Buffer;
                    hwb.Invalidate(frameIndex);

                    _UnusedHardwareBuffers.Add(hwb);
                }

                _UsedHardwareBuffers.Clear();

                foreach (var swb in _SoftwareBuffers) {
                    swb.Uninitialize();
                    _SoftwareBufferPool.Release(swb);
                }

                _SoftwareBuffers.Clear();

                _LastFrameReset = frameIndex;

                /*
                Array.Clear(_VertexArray, 0, _VertexArray.Length);
                Array.Clear(_IndexArray, 0, _IndexArray.Length);
                 */
            }
        }

        private T[] EnsureBufferCapacity<T> (
            ref T[] array, ref int usedElementCount,
            int elementsToAdd, out int oldElementCount
        ) {
            oldElementCount = usedElementCount;
            int newElementCount = (usedElementCount += elementsToAdd);

            var oldArray = array;
            var oldArraySize = array.Length;
            if (oldArraySize >= newElementCount)
                return array;

            var newSize = PickNewArraySize(oldArraySize, newElementCount);
            var newArray = new T[newSize];

            _PendingCopies.Add(new PendingCopy {
                Source = oldArray,
                SourceIndex = 0,
                Destination = newArray,
                DestinationIndex = 0,
                Count = oldElementCount
            });

            return array = newArray;
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
            var currentUsed = hbe.VerticesUsed;
            var newUsed = hbe.VerticesUsed + vertexCount;

            if (newUsed >= hbe.Buffer.VertexCount)
                return true;

            if (hbe.SoftwareBufferCount >= MaxSoftwareBuffersPerHardwareBuffer)
                return true;

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

            lock (_StateLock) {
                if (_FlushedToBuffers != 0)
                    throw new InvalidOperationException("Already flushed");

                // When we resize our internal array, we have to queue up copies from the old small array to the new large array.
                // This is because while Allocate is thread-safe, consumers are allowed to write to their allocations without synchronization,
                //  so we can't be sure that the old array has been filled yet.
                // Flush() is responsible for doing all these copies to ensure that all vertex data eventually makes it into the large array
                //  from which actual hardware buffer initialization occurs.

                SoftwareBuffer swb;

                int oldVertexCount, oldIndexCount;
                var vertexArray = EnsureBufferCapacity(
                    ref _VertexArray, ref _VertexCount, vertexCount, out oldVertexCount
                );
                var indexArray = EnsureBufferCapacity(
                    ref _IndexArray, ref _IndexCount, indexCount, out oldIndexCount
                );

                HardwareBufferEntry hardwareBufferEntry;

                hardwareBufferEntry = _FillingHardwareBufferEntry;

                hardwareBufferEntry = PrepareToFillBuffer(
                    hardwareBufferEntry,
                    oldVertexCount, oldIndexCount,
                    vertexCount, indexCount,
                    forceExclusiveBuffer
                );

                int oldHwbVerticesUsed, oldHwbIndicesUsed;

                // Guess the interlocked isn't really needed...
                oldHwbVerticesUsed = hardwareBufferEntry.VerticesUsed;
                hardwareBufferEntry.VerticesUsed += vertexCount;
                oldHwbIndicesUsed = hardwareBufferEntry.IndicesUsed;
                hardwareBufferEntry.IndicesUsed += indexCount;

                hardwareBufferEntry.SoftwareBufferCount += 1;

                swb = _SoftwareBufferPool.Allocate();
                swb.Initialize(
                    new ArraySegment<TVertex>(vertexArray, hardwareBufferEntry.VertexOffset + oldHwbVerticesUsed, vertexCount),
                    new ArraySegment<TIndex>(indexArray, hardwareBufferEntry.IndexOffset + oldHwbIndicesUsed, indexCount),
                    hardwareBufferEntry.Buffer,
                    oldHwbVerticesUsed,
                    oldHwbIndicesUsed
                );

                _SoftwareBuffers.Add(swb);

                return swb;
            }
        }

        private THardwareBuffer AllocateSuitablySizedHardwareBuffer (int vertexCount, int indexCount) {
            THardwareBuffer buffer;

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
            buffer = AllocateHardwareBuffer(vertexCount, indexCount);

            return buffer;
        }

        private void FlushToBuffers (int frameIndex) {
            var va = _VertexArray;
            var ia = _IndexArray;

            foreach (var hwbe in _UsedHardwareBuffers) {
                ArraySegment<TVertex> vertexSegment;
                ArraySegment<TIndex> indexSegment;

                vertexSegment = new ArraySegment<TVertex>(va, hwbe.VertexOffset, hwbe.SourceVertexCount);
                indexSegment = new ArraySegment<TIndex>(ia, hwbe.IndexOffset, hwbe.SourceIndexCount);

                FillHardwareBuffer(
                    hwbe.Buffer, vertexSegment, indexSegment
                );

                hwbe.Buffer.Validate(frameIndex);
            }

            _LastFrameFlushed = frameIndex;
        }

        void IBufferGenerator.Flush (int frameIndex) {
            lock (_StateLock) {
                if (_FlushedToBuffers == 0) {
                    if (_PendingCopies.Count > 0) {
                        PendingCopy pc;

                        using (var e = _PendingCopies.GetEnumerator())
                            while (e.GetNext(out pc))
                                pc.Execute();

                        _PendingCopies.Clear();
                    }

                    FlushToBuffers(frameIndex);
                } else {
                    throw new InvalidOperationException("Buffer generator flushed twice in a row");
                }
            }
        }
    }

    public class XNABufferPair<TVertex> : IHardwareBuffer
        where TVertex : struct 
    {
        internal volatile int _IsValid = 0, _IsActive = 0;
        private int _LastFrameValidated, _LastFrameInvalidated;

        public readonly object InUseLock = new object();

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
            Monitor.Exit(InUseLock);
        }

        public void SetActive (GraphicsDevice device) {
            var wasActive = Interlocked.Exchange(ref _IsActive, 1);
            if (wasActive != 0)
                throw new InvalidOperationException("Buffer already active");

            // ???????
            if (_IsValid != 1)
                throw new ThreadStateException("Buffer not valid");

            Monitor.Enter(InUseLock);
            device.SetVertexBuffer(Vertices);
            device.Indices = Indices;
        }

        public void Allocate () {
            Vertices = new DynamicVertexBuffer(GraphicsDevice, typeof(TVertex), VertexCount, BufferUsage.WriteOnly);
            Indices = new DynamicIndexBuffer(GraphicsDevice, IndexElementSize.SixteenBits, IndexCount, BufferUsage.WriteOnly);
            IsAllocated = true;
        }

        public void Dispose () {
            if (_IsActive != 0)
                throw new InvalidOperationException("Disposed buffer while in use");

            DisposeResource(Vertices);
            DisposeResource(Indices);
            Vertices = null;
            Indices = null;
            IsAllocated = false;
            _IsValid = 0;
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

        void IHardwareBuffer.Validate (int frameIndex) {
            var wasActive = Interlocked.Exchange(ref _IsActive, 0);
            var wasValid = Interlocked.Exchange(ref _IsValid, 1);

            if (wasActive != 0)
                throw new InvalidOperationException("Buffer in use");
            else if (wasValid != 0)
                // FIXME: Happens sometimes on device reset?
                throw new InvalidOperationException("Buffer validated twice");
                ;

            _LastFrameValidated = frameIndex;
        }

        void IHardwareBuffer.Invalidate (int frameIndex) {
            var wasActive = Interlocked.Exchange(ref _IsActive, 0);
            var wasValid = Interlocked.Exchange(ref _IsValid, 0);

            if (wasActive != 0)
                throw new InvalidOperationException("Buffer in use");

            _LastFrameInvalidated = frameIndex;
        }
    }

    public class XNABufferGenerator<TVertex> : BufferGenerator<XNABufferPair<TVertex>, TVertex, ushort> 
        where TVertex : struct {

        private static volatile int _NextBufferId = 0;

        public XNABufferGenerator (
            GraphicsDevice graphicsDevice, object createResourceLock, Action<IDisposable> disposeResource
        )
            : base(graphicsDevice, createResourceLock, disposeResource) {

            MaxSoftwareBuffersPerHardwareBuffer = 512;
        }

        protected override XNABufferPair<TVertex> AllocateHardwareBuffer (int vertexCount, int indexCount) {
            int id = Interlocked.Increment(ref _NextBufferId);
            return new XNABufferPair<TVertex>(GraphicsDevice, id, vertexCount, indexCount, DisposeResource);
        }

        protected override void FillHardwareBuffer (
            XNABufferPair<TVertex> hardwareBuffer, 
            ArraySegment<TVertex> vertices,
            ArraySegment<ushort> indices
        ) {
            if (!hardwareBuffer.IsAllocated) {
                lock (CreateResourceLock)
                    hardwareBuffer.Allocate();
            }

            lock (hardwareBuffer.InUseLock) {
                hardwareBuffer.Vertices.SetData(vertices.Array, vertices.Offset, vertices.Count, SetDataOptions.Discard);
                hardwareBuffer.Indices.SetData(indices.Array, indices.Offset, indices.Count, SetDataOptions.Discard);
            }
        }
    }
}
