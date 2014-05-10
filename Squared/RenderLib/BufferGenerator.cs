using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
    }

    public interface ISoftwareBuffer {
        IHardwareBuffer HardwareBuffer { get; }
        int HardwareVertexOffset { get; }
        int HardwareIndexOffset { get; }
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
            public readonly BufferGenerator<THardwareBuffer, TVertex, TIndex> BufferGenerator;
            internal bool IsValid = false;

            IHardwareBuffer ISoftwareBuffer.HardwareBuffer {
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
                BufferGenerator = bufferGenerator;
            }

            public void Initialize (
                ArraySegment<TVertex> vertices, ArraySegment<TIndex> indices,
                int hardwareBufferIndex, int vertexOffset, int indexOffset
            ) {
                Vertices = vertices;
                Indices = indices;
                HardwareBufferIndex = hardwareBufferIndex;
                HardwareVertexOffset = vertexOffset;
                HardwareIndexOffset = indexOffset;
                IsValid = true;
            }
        }

        protected struct PendingCopy {
            public Array Source, Destination;
            public int SourceIndex, DestinationIndex, Count;

            public void Execute () {
                Array.Copy(Source, SourceIndex, Destination, DestinationIndex, Count);
            }
        }

        protected readonly SoftwareBufferPool _SoftwareBufferPool;
        protected readonly UnorderedList<SoftwareBuffer> _SoftwareBuffers = new UnorderedList<SoftwareBuffer>();
        protected readonly UnorderedList<THardwareBuffer> _UnusedHardwareBuffers = new UnorderedList<THardwareBuffer>();
        protected readonly Dictionary<int, HardwareBufferEntry> _UsedHardwareBuffers = new Dictionary<int, HardwareBufferEntry>();
        protected readonly UnorderedList<PendingCopy> _PendingCopies = new UnorderedList<PendingCopy>();

        protected HardwareBufferEntry _FillingHardwareBufferEntry = null;

        protected int _VertexCount = 0, _IndexCount = 0;
        protected TVertex[] _VertexArray;
        protected TIndex[] _IndexArray;
        protected bool _FlushedToBuffers = false;

        const int InitialArraySize = 4096;

        const int MinVerticesPerBuffer = 1024;
        const int MinIndicesPerBuffer = 1536;
        
        // Modify these per-platform as necessary
        protected int MaxVerticesPerHardwareBuffer = UInt16.MaxValue;
        protected int MaxSoftwareBuffersPerHardwareBuffer = 1;

        public readonly GraphicsDevice GraphicsDevice;

        public BufferGenerator (GraphicsDevice graphicsDevice) {
            _SoftwareBufferPool = new SoftwareBufferPool(this);
            GraphicsDevice = graphicsDevice;
            _VertexArray = new TVertex[InitialArraySize];
            _IndexArray = new TIndex[InitialArraySize];
        }

        protected abstract THardwareBuffer AllocateHardwareBuffer (int vertexCount, int indexCount);
        protected abstract void FillHardwareBuffer (THardwareBuffer buffer, ArraySegment<TVertex> vertices, ArraySegment<TIndex> indices);

        public virtual void Dispose () {
            foreach (var buffer in _UnusedHardwareBuffers)
                buffer.Dispose();

            _UnusedHardwareBuffers.Clear();
            _PendingCopies.Clear();

            _VertexArray = null;
            _IndexArray = null;

            _VertexCount = _IndexCount = 0;

            _FlushedToBuffers = false;
        }

        protected virtual int PickNewArraySize (int previousSize, int requestedSize) {
            return 1 << (int)Math.Ceiling(Math.Log(requestedSize, 2));
        }

        public void Reset () {
            lock (this) {
                _FillingHardwareBufferEntry = null;

                // Any buffers that remain unused (either from being too small, or being unnecessary now)
                //  should be disposed.
                foreach (var hb in _UnusedHardwareBuffers) {
                    // HACK
                    if (hb != null)
                        hb.Dispose();
                }
                _UnusedHardwareBuffers.Clear();

                // Return any buffers that were used this frame to the unused state.
                foreach (var hb in _UsedHardwareBuffers.Values) {
                    // HACK
                    if (hb.HardwareBuffer != null)
                        _UnusedHardwareBuffers.Add(hb.HardwareBuffer);
                }

                _UsedHardwareBuffers.Clear();

                foreach (var swb in _SoftwareBuffers) {
                    swb.IsValid = false;
                    _SoftwareBufferPool.Release(swb);
                }

                _SoftwareBuffers.Clear();

                _FlushedToBuffers = false;
                _VertexCount = _IndexCount = 0;
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

            if (_FlushedToBuffers)
                throw new InvalidOperationException("Already flushed");

            lock (this) {
                int vertexOffset = _VertexCount;
                int indexOffset = _IndexCount;

                _VertexCount += vertexCount;
                _IndexCount += indexCount;
                
                // When we resize our internal array, we have to queue up copies from the old small array to the new large array.
                // This is because while Allocate is thread-safe, consumers are allowed to write to their allocations without synchronization,
                //  so we can't be sure that the old array has been filled yet.
                // Flush() is responsible for doing all these copies to ensure that all vertex data eventually makes it into the large array
                //  from which actual hardware buffer initialization occurs.

                if (_VertexCount > _VertexArray.Length) {
                    var oldArray = _VertexArray;
                    _VertexArray = new TVertex[PickNewArraySize(_VertexArray.Length, _VertexCount)];

                    // Thread safety :/
                    _PendingCopies.Add(new PendingCopy {
                        Source = oldArray,
                        SourceIndex = 0,
                        Destination = _VertexArray,
                        DestinationIndex = 0,
                        Count = vertexOffset
                    });
                }
                if (_IndexCount > _IndexArray.Length) {
                    var oldArray = _IndexArray;
                    _IndexArray = new TIndex[PickNewArraySize(_IndexArray.Length, _IndexCount)];

                    // Thread safety :/
                    _PendingCopies.Add(new PendingCopy {
                        Source = oldArray,
                        SourceIndex = 0,
                        Destination = _IndexArray,
                        DestinationIndex = 0,
                        Count = indexOffset
                    });
                }

                if (
                    (_FillingHardwareBufferEntry == null) ||
                    ((_FillingHardwareBufferEntry.VertexCount + vertexCount) > MaxVerticesPerHardwareBuffer) ||
                    (_FillingHardwareBufferEntry.SoftwareBufferCount >= MaxSoftwareBuffersPerHardwareBuffer) ||
                    forceExclusiveBuffer
                ) {
                    var index = _UsedHardwareBuffers.Count;

                    _UsedHardwareBuffers.Add(
                        index,
                        _FillingHardwareBufferEntry = new HardwareBufferEntry(index, vertexOffset, indexOffset)
                    );
                }

                var swb = _SoftwareBufferPool.Allocate();
                swb.Initialize(                    
                    new ArraySegment<TVertex>(_VertexArray, vertexOffset, vertexCount),
                    new ArraySegment<TIndex>(_IndexArray, indexOffset, indexCount),
                    _FillingHardwareBufferEntry.Index, 
                    _FillingHardwareBufferEntry.VertexCount, 
                    _FillingHardwareBufferEntry.IndexCount
                );

                _SoftwareBuffers.Add(swb);

                _FillingHardwareBufferEntry.VertexCount += vertexCount;
                _FillingHardwareBufferEntry.IndexCount += indexCount;
                _FillingHardwareBufferEntry.SoftwareBufferCount += 1;

                return swb;
            }
        }

        protected THardwareBuffer AllocateSuitablySizedHardwareBuffer (int vertexCount, int indexCount) {
            THardwareBuffer buffer;

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
            buffer = AllocateHardwareBuffer(vertexCount, indexCount);
            return buffer;
        }

        protected void FlushToBuffers () {
            foreach (var kvp in _UsedHardwareBuffers) {
                var index = kvp.Key;
                var hwbe = kvp.Value;

                if (hwbe.VertexCount <= 0)
                    continue;
                var hwb = AllocateSuitablySizedHardwareBuffer(hwbe.VertexCount, hwbe.IndexCount);
                hwbe.HardwareBuffer = hwb;

                FillHardwareBuffer(
                    hwb,
                    new ArraySegment<TVertex>(_VertexArray, hwbe.VertexOffset, hwbe.VertexCount),
                    new ArraySegment<TIndex>(_IndexArray, hwbe.IndexOffset, hwbe.IndexCount)
                );
            }
        }

        public void Flush () {
            lock (this) {
                if (_PendingCopies.Count > 0) {
                    foreach (var pc in _PendingCopies)
                        pc.Execute();

                    _PendingCopies.Clear();
                }

                if (!_FlushedToBuffers) {
                    FlushToBuffers();
                    _FlushedToBuffers = true;
                }
            }
        }

        internal THardwareBuffer GetHardwareBufferByIndex (int index) {
            return _UsedHardwareBuffers[index].HardwareBuffer;
        }
    }

#if PSM
    public class PSMHardwareBuffer : IHardwareBuffer {
    }

    public class PSMBufferGenerator<TVertex> : BufferGenerator<Sce.PlayStation.Core.Graphics.VertexBuffer, TVertex, ushort> 
        where TVertex : struct {
        
        public static VertexFormat[] VertexFormat = null;

        public PSMBufferGenerator (GraphicsDevice graphicsDevice)
            : base(graphicsDevice) {
            
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
                _Buffer.Dispose();
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
        public readonly DynamicVertexBuffer Vertices;
        public readonly DynamicIndexBuffer Indices;

        public XNABufferPair (GraphicsDevice graphicsDevice, int vertexCount, int indexCount) {
            if (vertexCount >= UInt16.MaxValue)
                throw new ArgumentOutOfRangeException("vertexCount", vertexCount, "Vertex count must be less than UInt16.MaxValue");

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

        public int VertexCount {
            get;
            private set;
        }

        public int IndexCount {
            get;
            private set;
        }
    }

    public class XNABufferGenerator<TVertex> : BufferGenerator<XNABufferPair<TVertex>, TVertex, ushort> 
        where TVertex : struct {

        public XNABufferGenerator (GraphicsDevice graphicsDevice)
            : base(graphicsDevice) {

            MaxSoftwareBuffersPerHardwareBuffer = 256;
        }

        protected override XNABufferPair<TVertex> AllocateHardwareBuffer (int vertexCount, int indexCount) {
            return new XNABufferPair<TVertex>(GraphicsDevice, vertexCount, indexCount);
        }

        protected override void FillHardwareBuffer (XNABufferPair<TVertex> hardwareBuffer, ArraySegment<TVertex> vertices, ArraySegment<ushort> indices) {
            hardwareBuffer.Vertices.SetData(vertices.Array, vertices.Offset, vertices.Count);
            hardwareBuffer.Indices.SetData(indices.Array, indices.Offset, indices.Count);
        }
    }
#endif
}
