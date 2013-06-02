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
        public class SoftwareBuffer : ISoftwareBuffer {
            public readonly BufferGenerator<THardwareBuffer, TVertex, TIndex> BufferGenerator;

            public readonly ArraySegment<TVertex> Vertices;
            public readonly ArraySegment<TIndex> Indices;

            public readonly int HardwareBufferIndex;
            public readonly int HardwareVertexOffset, HardwareIndexOffset;

            IHardwareBuffer ISoftwareBuffer.HardwareBuffer {
                get {
                    return BufferGenerator.GetHardwareBufferByIndex(HardwareBufferIndex);
                }
            }

            int ISoftwareBuffer.HardwareVertexOffset {
                get {
                    return this.HardwareVertexOffset;
                }
            }

            int ISoftwareBuffer.HardwareIndexOffset {
                get {
                    return this.HardwareIndexOffset;
                }
            }

            internal SoftwareBuffer (
                BufferGenerator<THardwareBuffer, TVertex, TIndex> bufferGenerator,
                ArraySegment<TVertex> vertices, ArraySegment<TIndex> indices,
                int hardwareBufferIndex, int vertexOffset, int indexOffset
            ) {
                BufferGenerator = bufferGenerator;
                Vertices = vertices;
                Indices = indices;
                HardwareBufferIndex = hardwareBufferIndex;
                HardwareVertexOffset = vertexOffset;
                HardwareIndexOffset = indexOffset;
            }
        }

        protected struct PendingCopy {
            public Array Source, Destination;
            public int SourceIndex, DestinationIndex, Count;

            public void Execute () {
                Array.Copy(Source, SourceIndex, Destination, DestinationIndex, Count);
            }
        }

        protected readonly UnorderedList<SoftwareBuffer> _SoftwareBuffers = new UnorderedList<SoftwareBuffer>();
        protected readonly UnorderedList<THardwareBuffer> _UnusedHardwareBuffers = new UnorderedList<THardwareBuffer>();
        protected readonly List<THardwareBuffer> _UsedHardwareBuffers = new List<THardwareBuffer>();
        protected readonly UnorderedList<PendingCopy> _PendingCopies = new UnorderedList<PendingCopy>();

        protected int _FillingHardwareBufferIndex = 0;
        protected readonly UnorderedList<SoftwareBuffer> _FillingHardwareBufferSoftwareBuffers = new UnorderedList<SoftwareBuffer>();
        protected int _FillingHardwareBufferVertexCount = 0;
        protected int _FillingHardwareBufferIndexCount = 0;

        protected int _VertexCount = 0, _IndexCount = 0;
        protected TVertex[] _VertexArray;
        protected TIndex[] _IndexArray;
        protected bool _FlushedToBuffers = false;

        const int InitialArraySize = 4096;
        
        // Modify these per-platform as necessary
        protected int MaxVerticesPerHardwareBuffer = UInt16.MaxValue;
        protected int MaxSoftwareBuffersPerHardwareBuffer = 1;

        public readonly GraphicsDevice GraphicsDevice;

        public BufferGenerator (GraphicsDevice graphicsDevice) {
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
                _FillingHardwareBufferIndex = 0;
                ResetFillingHardwareBuffer();

                // Any buffers that remain unused (either from being too small, or being unnecessary now)
                //  should be disposed.
                foreach (var hb in _UnusedHardwareBuffers)
                    hb.Dispose();
                _UnusedHardwareBuffers.Clear();

                // Return any buffers that were used this frame to the unused state.
                foreach (var hb in _UsedHardwareBuffers)
                    _UnusedHardwareBuffers.Add(hb);
                _UsedHardwareBuffers.Clear();

                _SoftwareBuffers.Clear();

                _FlushedToBuffers = false;
                _VertexCount = _IndexCount = 0;
            }
        }

        public SoftwareBuffer Allocate (int vertexCount, int indexCount) {
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
                    ((_FillingHardwareBufferVertexCount + vertexCount) > MaxVerticesPerHardwareBuffer) ||
                    (_FillingHardwareBufferSoftwareBuffers.Count >= MaxSoftwareBuffersPerHardwareBuffer)
                ) {
                    _FillingHardwareBufferIndex += 1;
                    ResetFillingHardwareBuffer();
                }

                var swb = new SoftwareBuffer(
                    this,
                    new ArraySegment<TVertex>(_VertexArray, vertexOffset, vertexCount),
                    new ArraySegment<TIndex>(_IndexArray, indexOffset, indexCount),
                    _FillingHardwareBufferIndex, _FillingHardwareBufferVertexCount, _FillingHardwareBufferIndexCount
                );

                _FillingHardwareBufferVertexCount += vertexCount;
                _FillingHardwareBufferIndexCount += indexCount;

                _SoftwareBuffers.Add(ref swb);
                return swb;
            }
        }

        protected void ResetFillingHardwareBuffer () {
            _FillingHardwareBufferSoftwareBuffers.Clear();
            _FillingHardwareBufferVertexCount = 0;
            _FillingHardwareBufferIndexCount = 0;
        }

        protected THardwareBuffer AllocateSuitablySizedHardwareBuffer (int vertexCount, int indexCount) {
            THardwareBuffer buffer;

            using (var e = _UnusedHardwareBuffers.GetEnumerator())
            while (e.GetNext(out buffer)) {
                if (
                    (buffer.VertexCount >= vertexCount) ||
                    (buffer.IndexCount >= indexCount)
                ) {
                    // This buffer is large enough, so return it.
                    e.RemoveCurrent();

                    _UsedHardwareBuffers.Add(buffer);
                    return buffer;
                }
            }

            // We didn't find a suitably large buffer.
            buffer = AllocateHardwareBuffer(vertexCount, indexCount);
            _UsedHardwareBuffers.Add(buffer);
            return buffer;
        }

        protected void FlushToBuffers () {
            // FIXME: This will break if software buffers are not allocated contiguously...
            int currentHardwareBufferIndex = 0;
            int minVertexOffset = int.MaxValue;
            int minIndexOffset = int.MaxValue;
            int vertexCount = 0;
            int indexCount = 0;

            THardwareBuffer hwb;

            foreach (var swb in _SoftwareBuffers) {
                if (swb.HardwareBufferIndex != currentHardwareBufferIndex) {
                    if (vertexCount > 0) {
                        hwb = AllocateSuitablySizedHardwareBuffer(vertexCount, indexCount);
                        FillHardwareBuffer(
                            hwb,
                            new ArraySegment<TVertex>(_VertexArray, minVertexOffset, vertexCount),
                            new ArraySegment<TIndex>(_IndexArray, minIndexOffset, indexCount)
                        );
                    }

                    currentHardwareBufferIndex = swb.HardwareBufferIndex;
                    minVertexOffset = minIndexOffset = int.MaxValue;
                    vertexCount = indexCount = 0;
                }

                minVertexOffset = Math.Min(minVertexOffset, swb.Vertices.Offset);
                minIndexOffset = Math.Min(minIndexOffset, swb.Indices.Offset);
                vertexCount += swb.Vertices.Count;
                indexCount += swb.Indices.Count;
            }

            if (vertexCount > 0) {
                hwb = AllocateSuitablySizedHardwareBuffer(vertexCount, indexCount);
                FillHardwareBuffer(
                    hwb,
                    new ArraySegment<TVertex>(_VertexArray, minVertexOffset, vertexCount),
                    new ArraySegment<TIndex>(_IndexArray, minIndexOffset, indexCount)
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
            return _UsedHardwareBuffers[index];
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
