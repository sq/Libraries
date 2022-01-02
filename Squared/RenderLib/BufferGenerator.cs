using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.Xna.Framework.Graphics;
using Squared.Util;

using TIndex = System.UInt16;

namespace Squared.Render.Internal {
    public interface IBufferGenerator : IDisposable {
        void Reset(int frameIndex);
        void Flush(int frameIndex);

        int ManagedVertexBytes { get; }
        int ManagedIndexBytes { get; }
    }

    public interface IHardwareBuffer : IDisposable {
        void SetInactive ();
        bool TrySetInactive ();
        IHardwareBuffer SetActive ();
        void SetInactiveAndUnapply (GraphicsDevice device);
        IHardwareBuffer SetActiveAndApply (GraphicsDevice device);
        void GetBuffers (out VertexBuffer vb, out DynamicIndexBuffer ib);
        /*
        void Invalidate (int frameIndex);
        void Validate (int frameIndex);
        int VertexCount { get; }
        int IndexCount { get; }
        int Id { get; }
        int Age { get; set; }
        */
    }

    public interface ISoftwareBuffer {
        IHardwareBuffer HardwareBuffer { get; }
        int HardwareVertexOffset { get; }
        int HardwareIndexOffset { get; }
        int Id { get; }
    }

    public class BufferGenerator<TVertex> : IBufferGenerator
        where TVertex : struct
    {
        protected class SoftwareBufferPool : BaseObjectPool<SoftwareBuffer> {
            public readonly BufferGenerator<TVertex> BufferGenerator;

            public SoftwareBufferPool (BufferGenerator<TVertex> bufferGenerator) {
                BufferGenerator = bufferGenerator;
            }

            protected override SoftwareBuffer AllocateNew () {
                return new SoftwareBuffer(BufferGenerator);
            }
        }

        protected class HardwareBufferEntry {
            public int VertexOffset { get; private set; }
            public int IndexOffset { get; private set; }
            public int SourceVertexCount;
            public int SourceIndexCount;

            public int SoftwareBufferCount;
            public int VerticesUsed;
            public int IndicesUsed;

            public XNABufferPair<TVertex> Buffer;

            internal bool AddedToList = false;

            public HardwareBufferEntry () {
            }

            public void Initialize (
                XNABufferPair<TVertex> buffer,
                int vertexOffset, int indexOffset,
                int sourceVertexCount, int sourceIndexCount
            ) {
                Buffer = buffer;
                VertexOffset = vertexOffset;
                IndexOffset = indexOffset;
                SourceVertexCount = sourceVertexCount;
                SourceIndexCount = sourceIndexCount;
                SoftwareBufferCount = 0;
                VerticesUsed = IndicesUsed = 0;
            }

            public float UtilizationPercentage {
                get {
                    var usedVerts = VerticesUsed / (float)Buffer.VertexCount;
                    var usedIndices = IndicesUsed / (float)Buffer.IndexCount;
                    return ((usedVerts + usedIndices) / 2) * 100;
                }
            }
        }

        public class SoftwareBuffer : ISoftwareBuffer {
            private static volatile int NextId;

            public readonly BufferGenerator<TVertex> BufferGenerator;
            internal bool IsInitialized = false;

            public int Id {
                get;
                private set;
            }

            public XNABufferPair<TVertex> HardwareBuffer {
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

            internal SoftwareBuffer (BufferGenerator<TVertex> bufferGenerator) {
                Id = Interlocked.Increment(ref NextId);
                BufferGenerator = bufferGenerator;
            }

            public void Uninitialize () {
                IsInitialized = false;
            }

            IHardwareBuffer ISoftwareBuffer.HardwareBuffer {
                get {
                    return this.HardwareBuffer;
                }
            }

            public void Initialize (
                ArraySegment<TVertex> vertices, ArraySegment<TIndex> indices,
                XNABufferPair<TVertex> hardwareBuffer, int hardwareVertexOffset, int hardwareIndexOffset
            ) {
                if (IsInitialized)
                    throw new ThreadStateException("Software buffer already initialized.");

                Vertices = vertices;
                Indices = indices;
                HardwareBuffer = hardwareBuffer;
                HardwareVertexOffset = hardwareVertexOffset;
                HardwareIndexOffset = hardwareIndexOffset;
                IsInitialized = true;
            }

            internal void ArraysChanged (TVertex[] newVertexArray, TIndex[] newIndexArray) {
                Vertices = new ArraySegment<TVertex>(
                    newVertexArray, Vertices.Offset, Vertices.Count
                );
                Indices = new ArraySegment<TIndex>(
                    newIndexArray, Indices.Offset, Indices.Count
                );
            }

            public override string ToString() {
                return String.Format("<Software Buffer #{0} ({1} - {2})>", Id, IsInitialized ? "initialized" : "invalid", HardwareBuffer);
            }
        }

        protected static object _StaticStateLock = new object();
        protected static int    _InstanceCount = 0;
        protected static int    _LastFrameStaticReset = -1;
        protected static readonly UnorderedList<XNABufferPair<TVertex>> _UnusedHardwareBuffers 
            = new UnorderedList<XNABufferPair<TVertex>>();

        protected readonly UnorderedList<HardwareBufferEntry> _UsedHardwareBufferEntries = new UnorderedList<HardwareBufferEntry>(),
            _PreviouslyUsedHardwareBufferEntries = new UnorderedList<HardwareBufferEntry>(),
            _ReusableHardwareBufferEntries = new UnorderedList<HardwareBufferEntry>();

        protected object _StateLock = new object();
        protected int _VertexCount = 0, _IndexCount = 0;
        protected TVertex[] _VertexArray;
        protected TIndex[] _IndexArray;
        protected int _FlushedToBuffers = 0;
        private int _LastFrameReset, _LastFrameFlushed;

        protected readonly SoftwareBufferPool _SoftwareBufferPool;
        protected readonly UnorderedList<SoftwareBuffer> _SoftwareBuffers = new UnorderedList<SoftwareBuffer>();

        protected readonly Dictionary<string, SoftwareBuffer> _BufferCache = new Dictionary<string, SoftwareBuffer>();

        protected HardwareBufferEntry _FillingHardwareBufferEntry = null;

        const int MaxBufferAge = 5;
        const int MaxUnusedBuffers = 5;

        // Initial size of the single vertex and index buffers
        const int InitialArraySize = 8192;
        // Once the buffer passes this capacity it's considered 'large' and grows slower
        // Large buffers are also not retained between frames
        const int LargeSizeThreshold = 102400;
        // Once the buffer is considered large it grows at this rate
        const int LargeChunkSize = 65536;

        const int MinVerticesPerHardwareBuffer = 10240,
            // Dedicated buffers tend to be very small, so we want to make sure we allocate them
            //  with some extra padding space to increase the odds of being reusable on future frames
            MinVerticesPerDedicatedHardwareBuffer = 256,
            MinIndicesPerHardwareBuffer = 15360,
            MinIndicesPerDedicatedHardwareBuffer = 512;
        
        protected int MaxVerticesPerHardwareBuffer = 204800;
        protected int MaxSoftwareBuffersPerHardwareBuffer = 4096;

        public readonly RenderManager RenderManager;
        public readonly object CreateResourceLock;

        public int DeviceId { get; private set; }
        private static volatile int _NextBufferId = 0;

        public int ManagedVertexBytes {
            get {
                return System.Runtime.InteropServices.Marshal.SizeOf(typeof(TVertex)) * _VertexArray.Length;
            }
        }

        public int ManagedIndexBytes {
            get {
                return 2 * _IndexArray.Length;
            }
        }

        public BufferGenerator (RenderManager renderManager) {
            if (renderManager == null)
                throw new ArgumentNullException("renderManager");

            _SoftwareBufferPool = new SoftwareBufferPool(this);
            RenderManager = renderManager;
            DeviceId = renderManager.DeviceManager.DeviceId;
            CreateResourceLock = renderManager.CreateResourceLock;
            _VertexArray = new TVertex[InitialArraySize];
            _IndexArray = new TIndex[InitialArraySize];

            lock (_StaticStateLock)
                _InstanceCount++;
        }

        public virtual void Dispose () {
            lock (_StateLock) {
                _VertexArray = null;
                _IndexArray = null;

                _VertexCount = _IndexCount = 0;

                _FlushedToBuffers = 0;

                lock (_StaticStateLock) {
                    _InstanceCount -= 1;

                    if (_InstanceCount <= 0) {
                        foreach (var buffer in _UnusedHardwareBuffers)
                            buffer.Dispose();

                        _UnusedHardwareBuffers.Clear();
                    }
                }
            }
        }

        protected virtual int PickNewArraySize (int previousSize, int requestedSize) {
            if (previousSize >= LargeSizeThreshold) {
                var newSize = ((requestedSize + LargeChunkSize - 1) / LargeChunkSize) * LargeChunkSize;

                return newSize;
            } else {
                var newSize = 1 << (int)Math.Ceiling(Math.Log(requestedSize, 2));

                return newSize;
            }
        }

        private static void StaticReset (int frameIndex, RenderManager renderManager) {
            if (_LastFrameStaticReset >= frameIndex)
                return;

            // Any buffers that remain unused (either from being too small, or being unnecessary now)
            //  should be disposed.
            XNABufferPair<TVertex> hb;
            using (var e = _UnusedHardwareBuffers.GetEnumerator())
            while (e.GetNext(out hb)) {
                hb.Age += 1;

                bool shouldKill = (hb.Age >= MaxBufferAge) ||
                    ((_UnusedHardwareBuffers.Count > MaxUnusedBuffers) && (hb.Age > 1));

                if (shouldKill) {
                    e.RemoveCurrent();
                    hb.Invalidate(frameIndex);

                    renderManager.DisposeResource(hb);
                }
            }

            _LastFrameStaticReset = frameIndex;
        }

        public void Reset (int frameIndex) {
            var id = RenderManager.DeviceManager.DeviceId;

            lock (_StateLock) {
                _FillingHardwareBufferEntry = null;
                _FlushedToBuffers = 0;
                _VertexCount = _IndexCount = 0;

                lock (_StaticStateLock)
                    StaticReset(frameIndex, RenderManager);

                foreach (var _hb in _PreviouslyUsedHardwareBufferEntries)
                    _ReusableHardwareBufferEntries.Add(_hb);
                _PreviouslyUsedHardwareBufferEntries.Clear();

                // Return any buffers that were used this frame to the unused state.
                foreach (var _hb in _UsedHardwareBufferEntries) {
                    _hb.AddedToList = false;
                    _PreviouslyUsedHardwareBufferEntries.Add(_hb);

                    // HACK
                    var hwb = _hb.Buffer;
                    if (hwb.DeviceId < id)
                        continue;
                    hwb.Invalidate(frameIndex);

                    lock (_StaticStateLock)
                        _UnusedHardwareBuffers.Add(hwb);
                }

                _UsedHardwareBufferEntries.Clear();

                foreach (var kvp in _BufferCache) {
                    var swb = kvp.Value;
                    if (!swb.IsInitialized)
                        continue;

                    // Console.WriteLine($"Uninit corner buffer {swb}");
                    swb.Uninitialize();
                    _SoftwareBufferPool.Release(swb);
                }
                _BufferCache.Clear();

                foreach (var swb in _SoftwareBuffers) {
                    if (!swb.IsInitialized)
                        continue;

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

        private bool EnsureBufferCapacity<T> (
            ref T[] array, ref int usedElementCount,
            int elementsToAdd, out int oldElementCount
        ) {
            oldElementCount = usedElementCount;
            int newElementCount = (usedElementCount += elementsToAdd);

            var oldArray = array;
            int oldArraySize = 0;
            if (array != null)
                oldArraySize = array.Length;
            if (oldArraySize >= newElementCount)
                return false;

            var newSize = PickNewArraySize(oldArraySize, newElementCount);
            Array.Resize(ref array, newSize);
            return true;
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
                var newBuffer = AllocateSuitablySizedHardwareBuffer(additionalVertexCount, additionalIndexCount, forceExclusiveBuffer);
                HardwareBufferEntry entry;

                if (!_ReusableHardwareBufferEntries.TryPopFront(out entry)) {
                    // FIXME: This frequently happens and it seems like the reason is that switching buffer generators to thread local
                    //  means that parallel prepare ends up creating dozens of generators that all fail to pool enough instances
                    // Console.WriteLine("New entry");
                    entry = new HardwareBufferEntry();
                } else {
                    // Console.WriteLine("Reused entry");
                }

                entry.Initialize(
                    newBuffer, vertexOffset, indexOffset, additionalVertexCount, additionalIndexCount
                );
                entry.AddedToList = true;
                _UsedHardwareBufferEntries.Add(entry);

                _FillingHardwareBufferEntry = entry;

                return entry;
            } else {
                if (!currentBufferEntry.AddedToList) {
                    currentBufferEntry.AddedToList = true;
                    _UsedHardwareBufferEntries.Add(currentBufferEntry);
                }
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

        public void SetCachedBuffer (string key, SoftwareBuffer buffer) {
            lock (_BufferCache)
                // FIXME: We could leak an old one here
                _BufferCache[key] = buffer;
        }

        public bool TryGetCachedBuffer (string key, int vertexCount, int indexCount, out SoftwareBuffer result) {
            lock (_BufferCache)
            if (!_BufferCache.TryGetValue(key, out result))
                return false;

            if ((result.Vertices.Count < vertexCount) || (result.Indices.Count < indexCount)) {
                result = null;
                return false;
            }

            return true;
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

            var requestedVertexCount = vertexCount;
            var requestedIndexCount = indexCount;

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
                var didArraysChange = EnsureBufferCapacity(
                    ref _VertexArray, ref _VertexCount, vertexCount, out oldVertexCount
                );
                if (EnsureBufferCapacity(
                    ref _IndexArray, ref _IndexCount, indexCount, out oldIndexCount
                ))
                    didArraysChange = true;

                if (didArraysChange) {
                    foreach (var _swb in _SoftwareBuffers)
                        _swb.ArraysChanged(_VertexArray, _IndexArray);
                }

                HardwareBufferEntry hardwareBufferEntry;

                hardwareBufferEntry = _FillingHardwareBufferEntry;

                hardwareBufferEntry = PrepareToFillBuffer(
                    hardwareBufferEntry,
                    oldVertexCount, oldIndexCount,
                    vertexCount, indexCount,
                    forceExclusiveBuffer
                );

                int oldHwbVerticesUsed, oldHwbIndicesUsed;
                oldHwbVerticesUsed = hardwareBufferEntry.VerticesUsed;
                oldHwbIndicesUsed = hardwareBufferEntry.IndicesUsed;

                hardwareBufferEntry.VerticesUsed += vertexCount;
                hardwareBufferEntry.IndicesUsed += indexCount;

                hardwareBufferEntry.SoftwareBufferCount += 1;

                swb = _SoftwareBufferPool.Allocate();
                if (swb.IsInitialized)
                    throw new ThreadStateException();
                swb.Initialize(
                    new ArraySegment<TVertex>(_VertexArray, hardwareBufferEntry.VertexOffset + oldHwbVerticesUsed, requestedVertexCount),
                    new ArraySegment<TIndex>(_IndexArray, hardwareBufferEntry.IndexOffset + oldHwbIndicesUsed, requestedIndexCount),
                    hardwareBufferEntry.Buffer,
                    oldHwbVerticesUsed,
                    oldHwbIndicesUsed
                );

                _SoftwareBuffers.Add(swb);

                return swb;
            }
        }

        private XNABufferPair<TVertex> AllocateSuitablySizedHardwareBuffer (int vertexCount, int indexCount, bool dedicated) {
            XNABufferPair<TVertex> buffer;

            lock (_StaticStateLock) {
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
            }

            if (!dedicated) {
                if (vertexCount < MinVerticesPerHardwareBuffer)
                    vertexCount = MinVerticesPerHardwareBuffer;
                if (indexCount < MinIndicesPerHardwareBuffer)
                    indexCount = MinIndicesPerHardwareBuffer;
            } else {
                if (vertexCount < MinVerticesPerDedicatedHardwareBuffer)
                    vertexCount = MinVerticesPerDedicatedHardwareBuffer;
                if (indexCount < MinIndicesPerDedicatedHardwareBuffer)
                    indexCount = MinIndicesPerDedicatedHardwareBuffer;
            }

            // We didn't find a suitably large buffer.
            buffer = AllocateHardwareBuffer(vertexCount, indexCount);

            return buffer;
        }

        private void FlushToBuffers (int frameIndex) {
            var va = _VertexArray;
            var ia = _IndexArray;

            foreach (var hwbe in _UsedHardwareBufferEntries) {
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

        public void Flush (int frameIndex) {
            lock (_StateLock) {
                if (_FlushedToBuffers == 0) {
                    FlushToBuffers(frameIndex);
                } else {
                    throw new InvalidOperationException("Buffer generator flushed twice in a row");
                }
            }
        }

        protected XNABufferPair<TVertex> AllocateHardwareBuffer (int vertexCount, int indexCount) {
            int id = Interlocked.Increment(ref _NextBufferId);
            return new XNABufferPair<TVertex>(RenderManager, id, vertexCount, indexCount);
        }

        protected void FillHardwareBuffer (
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

    public class XNABufferPair<TVertex> : IHardwareBuffer
        where TVertex : struct 
    {
        internal volatile int _IsValid = 0, _IsActive = 0;
        private int _LastFrameValidated, _LastFrameInvalidated;

        public readonly object InUseLock = new object();

        public readonly RenderManager RenderManager;
        public DynamicVertexBuffer Vertices;
        public DynamicIndexBuffer Indices;

        public int DeviceId { get; private set; }

        public XNABufferPair (
            RenderManager renderManager, int id,
            int vertexCount, int indexCount
        ) {
            if (renderManager == null)
                throw new ArgumentNullException("renderManager");
            if (vertexCount >= UInt16.MaxValue)
                throw new ArgumentOutOfRangeException("vertexCount", vertexCount, "Vertex count must be less than UInt16.MaxValue");

            Id = id;
            RenderManager = renderManager;            
            VertexCount = vertexCount;
            IndexCount = indexCount;
        }

        public void SetInactiveAndUnapply (GraphicsDevice device) {
            var wasActive = Interlocked.Exchange(ref _IsActive, 0);
            if (wasActive != 1)
                throw new InvalidOperationException("Buffer not active");
            if (_IsValid != 1)
                throw new ThreadStateException("Buffer not valid");

            device.SetVertexBuffer(null);
            device.Indices = null;
            Monitor.Exit(InUseLock);
        }

        public IHardwareBuffer SetActiveAndApply (GraphicsDevice device) {
            var wasActive = Interlocked.Exchange(ref _IsActive, 1);
            if (wasActive != 0)
                throw new InvalidOperationException("Buffer already active");

            // ???????
            if (_IsValid != 1)
                throw new ThreadStateException("Buffer not valid");

            Monitor.Enter(InUseLock);
            device.SetVertexBuffer(Vertices);
            device.Indices = Indices;

            return this;
        }

        public void SetInactive () {
            var wasActive = Interlocked.Exchange(ref _IsActive, 0);
            if (wasActive != 1)
                throw new InvalidOperationException("Buffer not active");
            if (_IsValid != 1)
                throw new ThreadStateException("Buffer not valid");

            Monitor.Exit(InUseLock);
        }

        public bool TrySetInactive () {
            var wasActive = Interlocked.Exchange(ref _IsActive, 0);
            if (_IsValid != 1)
                throw new ThreadStateException("Buffer not valid");

            if (wasActive != 0) {
                Monitor.Exit(InUseLock);
                return true;
            } else {
                return false;
            }
        }

        public IHardwareBuffer SetActive () {
            var wasActive = Interlocked.Exchange(ref _IsActive, 1);
            if (wasActive != 0)
                throw new InvalidOperationException("Buffer already active");

            // ???????
            if (_IsValid != 1)
                throw new ThreadStateException("Buffer not valid");

            Monitor.Enter(InUseLock);

            return this;
        }

        public void GetBuffers (out VertexBuffer vb, out DynamicIndexBuffer ib) {
            if (_IsActive != 1)
                throw new InvalidOperationException("Buffer not active");

            // ???????
            if (_IsValid != 1)
                throw new ThreadStateException("Buffer not valid");

            vb = Vertices;
            ib = Indices;
        }

        public void Allocate () {
            DeviceId = RenderManager.DeviceManager.DeviceId;
            Vertices = new DynamicVertexBuffer(RenderManager.DeviceManager.Device, typeof(TVertex), VertexCount, BufferUsage.WriteOnly);
            Interlocked.Add(ref RenderManager.TotalVertexBytes, System.Runtime.InteropServices.Marshal.SizeOf(typeof(TVertex)) * VertexCount);
            Indices = new DynamicIndexBuffer(RenderManager.DeviceManager.Device, IndexElementSize.SixteenBits, IndexCount, BufferUsage.WriteOnly);
            Interlocked.Add(ref RenderManager.TotalIndexBytes, 2 * IndexCount);
            IsAllocated = true;
        }

        public void Dispose () {
            if (_IsActive != 0)
                throw new InvalidOperationException("Disposed buffer while in use");

            Interlocked.Add(ref RenderManager.TotalVertexBytes, System.Runtime.InteropServices.Marshal.SizeOf(typeof(TVertex)) * -VertexCount);
            Interlocked.Add(ref RenderManager.TotalIndexBytes, 2 * -IndexCount);

            RenderManager.DisposeResource(Vertices);
            RenderManager.DisposeResource(Indices);
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
            return $"XNABufferPair<{typeof(TVertex).Name}> #{Id} [{VertexCount}v {IndexCount}i] {((_IsValid != 0) ? "valid" : "invalid")}";
        }

        public void Validate (int frameIndex) {
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

        public void Invalidate (int frameIndex) {
            var wasActive = Interlocked.Exchange(ref _IsActive, 0);
            var wasValid = Interlocked.Exchange(ref _IsValid, 0);

            if (wasActive != 0)
                throw new InvalidOperationException("Buffer in use");

            _LastFrameInvalidated = frameIndex;
        }
    }
}
