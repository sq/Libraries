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
        void Reset ();
    }

    public abstract class BufferGenerator<TBuffer, TVertex, TIndex> : IBufferGenerator
        where TBuffer : class, IDisposable
        where TVertex : struct
        where TIndex : struct
    {
        public struct TemporaryBuffer {
            public readonly ArraySegment<TVertex> Vertices;
            public readonly ArraySegment<TIndex> Indices;

            public TemporaryBuffer (ArraySegment<TVertex> vertices, ArraySegment<TIndex> indices) {
                Vertices = vertices;
                Indices = indices;
            }
        }

        protected struct PendingCopy {
            public Array Source, Destination;
            public int SourceIndex, DestinationIndex, Count;

            public void Execute () {
                Array.Copy(Source, SourceIndex, Destination, DestinationIndex, Count);
            }
        }

        protected TBuffer _Buffer;

        protected int _VertexCount = 0, _IndexCount = 0;
        protected TVertex[] _VertexArray;
        protected TIndex[] _IndexArray;
        protected bool _FlushedToBuffer = false;

        protected UnorderedList<PendingCopy> _PendingCopies = new UnorderedList<PendingCopy>();

        const int InitialArraySize = 4096;

        public readonly GraphicsDevice GraphicsDevice;

        public BufferGenerator (GraphicsDevice graphicsDevice) {
            GraphicsDevice = graphicsDevice;
            _VertexArray = new TVertex[InitialArraySize];
            _IndexArray = new TIndex[InitialArraySize];
        }

        public virtual void Dispose () {
            if (_Buffer != null)
                _Buffer.Dispose();
        }

        protected virtual int PickNewArraySize (int previousSize, int requestedSize) {
            return 1 << (int)Math.Ceiling(Math.Log(requestedSize, 2));
        }

        protected abstract void FlushToBuffer ();

        public void Reset () {
            lock (this) {
                _FlushedToBuffer = false;
                _VertexCount = _IndexCount = 0;
            }
        }

        public TemporaryBuffer Allocate (int vertexCount, int indexCount) {
            lock (this) {
                int vertexOffset = _VertexCount;
                int indexOffset = _IndexCount;

                _VertexCount += vertexCount;
                _IndexCount += indexCount;

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

                return new TemporaryBuffer(
                    new ArraySegment<TVertex>(_VertexArray, vertexOffset, vertexCount),
                    new ArraySegment<TIndex>(_IndexArray, indexOffset, indexCount)
                );
            }
        }

        public TBuffer GetBuffer () {
            lock (this) {
                if (_PendingCopies.Count > 0) {
                    foreach (var pc in _PendingCopies)
                        pc.Execute();

                    _PendingCopies.Clear();
                }

                if (!_FlushedToBuffer) {
                    FlushToBuffer();
                    _FlushedToBuffer = true;
                }

                return _Buffer;
            }
        }
    }

#if PSM
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
    public class XNABufferPair<TVertex> : IDisposable
        where TVertex : struct 
    {
        public readonly DynamicVertexBuffer Vertices;
        public readonly DynamicIndexBuffer Indices;

        public XNABufferPair (GraphicsDevice graphicsDevice, int vertexCount, int indexCount) {
            if (vertexCount >= UInt16.MaxValue)
                throw new InvalidOperationException("Too many vertices");

            Vertices = new DynamicVertexBuffer(graphicsDevice, typeof(TVertex), vertexCount, BufferUsage.WriteOnly);
            Indices = new DynamicIndexBuffer(graphicsDevice, IndexElementSize.SixteenBits, indexCount, BufferUsage.WriteOnly);
        }

        public void Dispose () {
            Vertices.Dispose();
            Indices.Dispose();
        }
    }

    public class XNABufferGenerator<TVertex> : BufferGenerator<XNABufferPair<TVertex>, TVertex, ushort> 
        where TVertex : struct {

        public XNABufferGenerator (GraphicsDevice graphicsDevice)
            : base(graphicsDevice) {
        }

        protected override void FlushToBuffer () {
            if (
                (_Buffer != null) &&
                (
                    (_Buffer.Vertices.VertexCount < _VertexArray.Length) ||
                    (_Buffer.Indices.IndexCount < _IndexArray.Length)
                )
            ) {
                _Buffer.Dispose();
                _Buffer = null;
            }

            if (_Buffer == null)
                _Buffer = new XNABufferPair<TVertex>(GraphicsDevice, _VertexArray.Length, _IndexArray.Length);

            _Buffer.Vertices.SetData(_VertexArray, 0, _VertexCount);
            _Buffer.Indices.SetData(_IndexArray, 0, _IndexCount);
        }
    }
#endif
}
