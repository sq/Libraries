using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework;
using Squared.Game;
using Squared.Render.Internal;
using Squared.Util;
using System.Reflection;

#if PSM
using VertexFormat = Sce.PlayStation.Core.Graphics.VertexFormat;
#endif

namespace Squared.Render {    
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct BitmapVertex : IVertexType {
        public Vector3 Position;
        public Vector2 TextureTopLeft;
        public Vector2 TextureBottomRight;
        public Vector2 Scale;
        public Vector2 Origin;
        public float Rotation;
        public Color MultiplyColor;
        public Color AddColor;

        public static readonly VertexElement[] Elements;
        static readonly VertexDeclaration _VertexDeclaration;

        static BitmapVertex () {
#if PSM
            // fuck sony
            short sizeF = 4;
            short sizeColor = 4;
            short sizeV3 = (short)(sizeF * 3);
            short sizeV4 = (short)(sizeF * 4);
#else
            short sizeF = (short)Marshal.SizeOf(typeof(float));
            short sizeColor = (short)Marshal.SizeOf(typeof(Color));
            short sizeV3 = (short)Marshal.SizeOf(typeof(Vector3));
            short sizeV4 = (short)Marshal.SizeOf(typeof(Vector4));
#endif
            
            Elements = new VertexElement[] {
                new VertexElement( 0, 
                    VertexElementFormat.Vector3, VertexElementUsage.Position, 0 ),
                new VertexElement( sizeV3, 
                    VertexElementFormat.Vector4, VertexElementUsage.Position, 1 ),
                new VertexElement( sizeV3 + sizeV4, 
                    VertexElementFormat.Vector4, VertexElementUsage.Position, 2 ),
                new VertexElement( sizeV3 + sizeV4 * 2, 
                    VertexElementFormat.Single, VertexElementUsage.Position, 3 ),
                new VertexElement( sizeV3 + sizeV4 * 2 + sizeF, 
                    VertexElementFormat.Color, VertexElementUsage.Color, 0 ),
                new VertexElement( sizeV3 + sizeV4 * 2 + sizeF + sizeColor, 
                    VertexElementFormat.Color, VertexElementUsage.Color, 1 )
            };
            _VertexDeclaration = new VertexDeclaration(Elements);
        }

        public VertexDeclaration VertexDeclaration {
            get { return _VertexDeclaration; }
        }
    }

    public struct BitmapCornerVertex : IVertexType {
        public float Corner;

        public static readonly VertexElement[] Elements;
        static readonly VertexDeclaration _VertexDeclaration;

        public BitmapCornerVertex (float corner) {
            Corner = corner;
        }

        static BitmapCornerVertex () {
            Elements = new VertexElement[] {
                new VertexElement( 0, 
                    VertexElementFormat.Single, VertexElementUsage.BlendIndices, 0 
                ),
            };
            _VertexDeclaration = new VertexDeclaration(Elements);
        }

        public VertexDeclaration VertexDeclaration {
            get { return _VertexDeclaration; }
        }    
    }

    public sealed class BitmapDrawCallComparer : IComparer<BitmapDrawCall> {
        public int Compare (BitmapDrawCall x, BitmapDrawCall y) {
            var result = (x.SortKey > y.SortKey)
                ? 1
                : (
                    (x.SortKey < y.SortKey)
                    ? -1
                    : 0
                );
            if (result == 0)
                result = (int)(x.TextureID - y.TextureID);
            return result;
        }
    }

    public sealed class BitmapDrawCallTextureComparer : IComparer<BitmapDrawCall> {
        public int Compare (BitmapDrawCall x, BitmapDrawCall y) {
            return (int)(x.TextureID - y.TextureID);
        }
    }

    public interface IBitmapBatch : IDisposable {
        void Add (BitmapDrawCall item);
        void Add (ref BitmapDrawCall item);
        void AddRange (BitmapDrawCall[] items);
    }

    public class BitmapBatch : ListBatch<BitmapDrawCall>, IBitmapBatch {
        class BitmapBatchCombiner : IBatchCombiner {
            public bool CanCombine (Batch lhs, Batch rhs) {
                if ((lhs == null) || (rhs == null))
                    return false;

                BitmapBatch bblhs = lhs as BitmapBatch, bbrhs = rhs as BitmapBatch;

                if ((bblhs == null) || (bbrhs == null))
                    return false;

                if (bblhs.Material.MaterialID != bbrhs.Material.MaterialID)
                    return false;

                if (bblhs.Layer != bbrhs.Layer)
                    return false;

                if (bblhs.UseZBuffer != bbrhs.UseZBuffer)
                    return false;

                if (bblhs.SamplerState != bbrhs.SamplerState)
                    return false;

                if (!bblhs.ReleaseAfterDraw)
                    return false;

                if (!bbrhs.ReleaseAfterDraw)
                    return false;

                return true;
            }

            public Batch Combine (Batch lhs, Batch rhs) {
                var bblhs = (BitmapBatch)lhs;
                var bbrhs = (BitmapBatch)rhs;

                var drawCallsLhs = bblhs._DrawCalls;
                var drawCallsRhs = bbrhs._DrawCalls;
                var drawCallsRhsBuffer = drawCallsRhs.GetBuffer();

                for (int i = 0, l = drawCallsRhs.Count; i < l; i++)
                    drawCallsLhs.Add(drawCallsRhsBuffer[i]);

                drawCallsRhs.Clear();

                return lhs;
            }
        }

        struct NativeBatch {
            public TextureSet TextureSet;
            public int IndexOffset;
            public int VertexOffset;
            public int VertexCount;
            public int PrimitiveCount;
            public int InstanceCount;
        }

        public SamplerState SamplerState;
        public bool UseZBuffer = false;

        public static Comparison<BitmapDrawCall> DrawCallComparer = new BitmapDrawCallComparer().Compare;
        public static Comparison<BitmapDrawCall> DrawCallTextureComparer = new BitmapDrawCallTextureComparer().Compare;

        public const int BitmapBatchSize = 1024;

        private ArrayPoolAllocator<BitmapVertex> _Allocator;
        private static ListPool<NativeBatch> _NativePool = new ListPool<NativeBatch>(
            2048, 16, 128
        );
        private UnorderedList<NativeBatch> _NativeBatches = null;
        private volatile bool _Prepared = false;

        private static readonly float[] FloatCorners = new[] { 0f, 1f, 2f, 3f };

#if PSM
        private PSMBufferGenerator<BitmapVertex> _BufferGenerator = null;
        private PSMBufferGenerator<BitmapCornerVertex> _CornerBufferGenerator = null;
#else
        private XNABufferGenerator<BitmapVertex> _BufferGenerator = null;
        private XNABufferGenerator<BitmapCornerVertex> _CornerBufferGenerator = null;
#endif

        static BitmapBatch () {
            BatchCombiner.Combiners.Add(new BitmapBatchCombiner());
            
#if PSM
            PSMBufferGenerator<BitmapVertex>.VertexFormat = new [] {
              VertexFormat.Float3, VertexFormat.Float4, VertexFormat.Float4,
              VertexFormat.Float, VertexFormat.UByte4N, VertexFormat.UByte4N, VertexFormat.Float
            };
#endif
        }

        public static BitmapBatch New (IBatchContainer container, int layer, Material material, SamplerState samplerState = null, bool useZBuffer = false) {
            if (container == null)
                throw new ArgumentNullException("container");
            if (material == null)
                throw new ArgumentNullException("material");

            var result = container.RenderManager.AllocateBatch<BitmapBatch>();
            result.Initialize(container, layer, material, samplerState, useZBuffer);
            result.CaptureStack(0);
            return result;
        }

        public void Initialize (IBatchContainer container, int layer, Material material, SamplerState samplerState = null, bool useZBuffer = false) {
            base.Initialize(container, layer, material);

            SamplerState = samplerState ?? SamplerState.LinearClamp;

            _Allocator = container.RenderManager.GetArrayAllocator<BitmapVertex>();

            UseZBuffer = useZBuffer;
        }

        public void Add (BitmapDrawCall item) {
            item.TextureID = item.Textures.GetHashCode();

            _DrawCalls.Add(item);
        }

        new public void Add (ref BitmapDrawCall item) {
            item.TextureID = item.Textures.GetHashCode();

            _DrawCalls.Add(ref item);
        }

        public void AddRange (BitmapDrawCall[] items) {
            AddRange(items, 0, items.Length, null);
        }

        public void AddRange (
            BitmapDrawCall[] items, int firstIndex, int count, 
            Vector2? offset = null, Color? multiplyColor = null, Color? addColor = null, float? sortKey = null
        ) {
            for (int i = 0; i < count; i++) {
                var item = items[i + firstIndex];
                if (!item.IsValid)
                    continue;

                item.TextureID = item.Textures.GetHashCode();
                if (offset.HasValue)
                    item.Position += offset.Value;
                if (multiplyColor.HasValue)
                    item.MultiplyColor = multiplyColor.Value;
                if (addColor.HasValue)
                    item.AddColor = addColor.Value;
                if (sortKey.HasValue)
                    item.SortKey = sortKey.Value;

                _DrawCalls.Add(ref item);
            }
        }
        
#if PSM
        public override void Prepare () {
#else
        public unsafe override void Prepare () {
#endif
            if (_DrawCalls.Count == 0)
                return;

            if (_NativeBatches == null)
                _NativeBatches = _NativePool.Allocate();

            if (UseZBuffer)
                _DrawCalls.Timsort(DrawCallTextureComparer);
            else
                _DrawCalls.Timsort(DrawCallComparer);

            var count = _DrawCalls.Count;
            int vertCount = 0, vertOffset = 0, bufferSize = count;
            int blockSizeLimit = BitmapBatchSize;
            int vertexWritePosition = 0;
                
            TextureSet currentTextures = new TextureSet();
            BitmapVertex vertex = new BitmapVertex();

#if PSM                
            _BufferGenerator = Container.RenderManager.GetBufferGenerator<PSMBufferGenerator<BitmapVertex>>();
            _CornerBufferGenerator = Container.RenderManager.GetBufferGenerator<PSMBufferGenerator<BitmapCornerVertex>>();
#else
            _BufferGenerator = Container.RenderManager.GetBufferGenerator<XNABufferGenerator<BitmapVertex>>();
            _CornerBufferGenerator = Container.RenderManager.GetBufferGenerator<XNABufferGenerator<BitmapCornerVertex>>();
#endif
            var buffers = _BufferGenerator.Allocate(bufferSize, 0);
            var _drawCalls = _DrawCalls.GetBuffer();

            var cornerBuffers = _CornerBufferGenerator.Allocate(4, 6);

            // Generate the corner vertices/indices.
            for (var i = 0; i < 4; i++)
                cornerBuffers.Vertices.Array[cornerBuffers.Vertices.Offset + i] = new BitmapCornerVertex(FloatCorners[i]);

            cornerBuffers.Indices.Array[cornerBuffers.Indices.Offset + 0] = 0;
            cornerBuffers.Indices.Array[cornerBuffers.Indices.Offset + 1] = 1;
            cornerBuffers.Indices.Array[cornerBuffers.Indices.Offset + 2] = 2;
            cornerBuffers.Indices.Array[cornerBuffers.Indices.Offset + 3] = 0;
            cornerBuffers.Indices.Array[cornerBuffers.Indices.Offset + 4] = 2;
            cornerBuffers.Indices.Array[cornerBuffers.Indices.Offset + 5] = 3;

#if !PSM
            fixed (BitmapVertex* pVertices = &buffers.Vertices.Array[buffers.Vertices.Offset])
#endif
            for (int i = 0; i < count; i++) {
                var call = _drawCalls[i];
                    
#if PSM
                // HACK: PSM render targets have an inverted Y axis, so if the bitmap being drawn is a render target,
                //   flip it vertically.
                if (call.Textures.Texture1 is RenderTarget2D)
                    call.Mirror(false, true);
#endif

                bool texturesEqual = call.Textures.Equals(ref currentTextures);
                bool flush = !texturesEqual || (vertCount >= blockSizeLimit);

                if (flush && (vertCount > 0)) {
                    _NativeBatches.Add(new NativeBatch { 
                        TextureSet = currentTextures, 
                        IndexOffset = cornerBuffers.Indices.Offset,
                        VertexCount = vertCount,
                        VertexOffset = vertOffset + buffers.Vertices.Offset
                    });

                    vertOffset += vertCount;
                    vertCount = 0;
                }

                currentTextures = call.Textures;

                vertex.Position = new Vector3(call.Position, UseZBuffer ? call.SortKey : 0);
                vertex.TextureTopLeft = call.TextureRegion.TopLeft;
                vertex.TextureBottomRight = call.TextureRegion.BottomRight;
                vertex.MultiplyColor = call.MultiplyColor;
                vertex.AddColor = call.AddColor;
                vertex.Scale = call.Scale;
                vertex.Origin = call.Origin;
                vertex.Rotation = call.Rotation;

#if !PSM
                pVertices[vertexWritePosition] = vertex;
#else
                buffers.Vertices.Array[buffers.Vertices.Offset + vertexWritePosition] = vertex;
#endif

                vertexWritePosition += 1;
                vertCount += 1;
            }

            if (vertCount > 0)
                _NativeBatches.Add(new NativeBatch {
                    TextureSet = currentTextures,
                    IndexOffset = cornerBuffers.Indices.Offset,
                    VertexCount = vertCount,
                    VertexOffset = vertOffset + buffers.Vertices.Offset,
                });

            _Prepared = true;
        }
            
        public override void Issue (DeviceManager manager) {
            if (_DrawCalls.Count == 0)
                return;

            if (_Prepared == false)
                throw new InvalidOperationException("Not prepared");

            if (_BufferGenerator == null)
                throw new InvalidOperationException("Already issued");

            var device = manager.Device;
            var buffers = _BufferGenerator.GetBuffer();
            var cornerBuffers = _CornerBufferGenerator.GetBuffer();

            var bindings = new VertexBufferBinding[] { 
                new VertexBufferBinding(cornerBuffers.Vertices, 0),
                // MSDN says vertexOffset is in bytes but it is in vertices. :|
                new VertexBufferBinding(buffers.Vertices, 0, 1)
            };

            using (manager.ApplyMaterial(Material)) {
#if PSM
                // FIXME
                device._graphics.SetVertexBuffer(0, cornerBuffers);
                device._graphics.SetVertexBuffer(1, buffers);
#else
                device.Indices = cornerBuffers.Indices;
#endif

                TextureSet currentTexture = new TextureSet();
                var paramSize = manager.CurrentParameters["BitmapTextureSize"];
                var paramHalfTexel = manager.CurrentParameters["HalfTexel"];

                foreach (var nb in _NativeBatches) {
                    if (nb.TextureSet != currentTexture) {
                        currentTexture = nb.TextureSet;
                        var tex1 = currentTexture.Texture1;

                        device.Textures[0] = tex1;
                        device.Textures[1] = currentTexture.Texture2;
                        device.SamplerStates[0] = device.SamplerStates[1] = SamplerState;

                        var vSize = new Vector2(tex1.Width, tex1.Height);
                        paramSize.SetValue(vSize);
                        paramHalfTexel.SetValue(new Vector2(1.0f / vSize.X, 1.0f / vSize.Y) * 0.5f);
                            
                        manager.CurrentEffect.CurrentTechnique.Passes[0].Apply();
                    }

                    if (UseZBuffer) {
                        var dss = device.DepthStencilState;
                        if (dss.DepthBufferEnable == false)
                            throw new InvalidOperationException("UseZBuffer set to true but depth buffer is disabled");
                    }
      
#if PSM
                    // MonoGame and PSM are both retarded.
                    device.ApplyState(false);
                    device.Textures.SetTextures(device);
                    device.SamplerStates.SetSamplers(device);
                        
                    device._graphics.DrawArrays(
                        Sce.PlayStation.Core.Graphics.DrawMode.Triangles,
                        nb.IndexOffset, nb.VertexCount * 6
                    );
#else
                    bindings[1] = new VertexBufferBinding(buffers.Vertices, nb.VertexOffset, 1);
                    device.SetVertexBuffers(bindings);
                    
                    device.DrawInstancedPrimitives(
                        PrimitiveType.TriangleList, 0, 0, 4, 0,
                        2, nb.VertexCount
                    );
#endif
                }

#if PSM
                device._graphics.SetVertexBuffer(0, null);
#else
                device.Indices = null;
                device.SetVertexBuffer(null);
#endif
            }

            _BufferGenerator = null;
            _CornerBufferGenerator = null;

            base.Issue(manager);
        }

        protected override void OnReleaseResources () {
            _Prepared = false;
            _BufferGenerator = null;
            _CornerBufferGenerator = null;

            _NativePool.Release(ref _NativeBatches);

            base.OnReleaseResources();
        }
    }

    public struct TextureSet {

        public Texture2D Texture1, Texture2;

        public TextureSet (Texture2D texture1) {
            Texture1 = texture1;
            Texture2 = null;
        }

        public TextureSet (Texture2D texture1, Texture2D texture2) {
            Texture1 = texture1;
            Texture2 = texture2;
        }

        public Texture2D this[int index] {
            get {
                if (index == 0)
                    return Texture1;
                else if (index == 1)
                    return Texture2;
                else
                    throw new InvalidOperationException();
            }
            set {
                if (index == 0)
                    Texture1 = value;
                else if (index == 1)
                    Texture2 = value;
                else
                    throw new InvalidOperationException();
            }
        }

        public static implicit operator TextureSet (Texture2D texture1) {
            return new TextureSet(texture1);
        }

        public bool Equals (ref TextureSet rhs) {
            return (Texture1 == rhs.Texture1) && (Texture2 == rhs.Texture2);
        }

        public override bool Equals (object obj) {
            if (obj is TextureSet) {
                var rhs = (TextureSet)obj;
                return this.Equals(ref rhs);
            } else {
                return base.Equals(obj);
            }
        }

        public static bool operator == (TextureSet lhs, TextureSet rhs) {
            return (lhs.Texture1 == rhs.Texture1) && (lhs.Texture2 == rhs.Texture2);
        }

        public static bool operator != (TextureSet lhs, TextureSet rhs) {
            return (lhs.Texture1 != rhs.Texture1) || (lhs.Texture2 != rhs.Texture2);
        }

        public override int GetHashCode () {
            if (Texture2 != null)
                return Texture1.GetHashCode() ^ Texture2.GetHashCode();
            else
                return Texture1.GetHashCode();
        }
    }

    public class ImageReference {
        public readonly Texture2D Texture;
        public readonly Bounds TextureRegion;

        public ImageReference (Texture2D texture, Bounds region) {
            Texture = texture;
            TextureRegion = region;
        }
    }

    public struct BitmapDrawCall {
        public TextureSet Textures;
        public Vector2 Position;
        public Bounds TextureRegion;
        public Vector2 Scale;
        public Vector2 Origin;
        public float Rotation;
        public Color MultiplyColor, AddColor;
        public float SortKey;

        internal int TextureID;

        public BitmapDrawCall (Texture2D texture, Vector2 position) 
            : this (texture, position, new Bounds(Vector2.Zero, Vector2.One)) {
        }

        public BitmapDrawCall (Texture2D texture, Vector2 position, Color color)
            : this(texture, position, new Bounds(Vector2.Zero, Vector2.One), color) {
        }

        public BitmapDrawCall (Texture2D texture, Vector2 position, Bounds textureRegion)
            : this(texture, position, textureRegion, Color.White) {
        }

        public BitmapDrawCall (Texture2D texture, Vector2 position, Bounds textureRegion, Color color)
            : this(texture, position, textureRegion, color, Vector2.One) {
        }

        public BitmapDrawCall (Texture2D texture, Vector2 position, float scale)
            : this(texture, position, new Bounds(Vector2.Zero, Vector2.One), Color.White, new Vector2(scale, scale)) {
        }

        public BitmapDrawCall (Texture2D texture, Vector2 position, Vector2 scale)
            : this(texture, position, new Bounds(Vector2.Zero, Vector2.One), Color.White, scale) {
        }

        public BitmapDrawCall (Texture2D texture, Vector2 position, Bounds textureRegion, Color color, float scale)
            : this(texture, position, textureRegion, color, new Vector2(scale, scale)) {
        }

        public BitmapDrawCall (Texture2D texture, Vector2 position, Bounds textureRegion, Color color, Vector2 scale)
            : this(texture, position, textureRegion, color, scale, Vector2.Zero) {
        }

        public BitmapDrawCall (Texture2D texture, Vector2 position, Bounds textureRegion, Color color, Vector2 scale, Vector2 origin)
            : this(texture, position, textureRegion, color, scale, origin, 0.0f) {
        }

        public BitmapDrawCall (Texture2D texture, Vector2 position, Bounds textureRegion, Color color, Vector2 scale, Vector2 origin, float rotation) {
            if (texture.IsDisposed)
                throw new ObjectDisposedException("texture");

            Textures = new TextureSet(texture);
            Position = position;
            TextureRegion = textureRegion;
            MultiplyColor = color;
            AddColor = new Color(0, 0, 0, 0);
            Scale = scale;
            Origin = origin;
            Rotation = rotation;

            SortKey = 0;
            TextureID = 0;
        }

        public void Mirror (bool x, bool y) {
            var newBounds = TextureRegion;

            if (x) {
                newBounds.TopLeft.X = TextureRegion.BottomRight.X;
                newBounds.BottomRight.X = TextureRegion.TopLeft.X;
            }

            if (y) {
                newBounds.TopLeft.Y = TextureRegion.BottomRight.Y;
                newBounds.BottomRight.Y = TextureRegion.TopLeft.Y;
            }

            TextureRegion = newBounds;
        }

        public Texture2D Texture {
            get {
                if (Textures.Texture2 == null)
                    return Textures.Texture1;
                else
                    throw new InvalidOperationException("DrawCall has multiple textures");
            }
            set {
                Textures = new TextureSet(value);
            }
        }

        public float ScaleF {
            get {
                return (Scale.X + Scale.Y) / 2.0f;
            }
            set {
                Scale = new Vector2(value, value);
            }
        }

        public Rectangle TextureRectangle {
            get {
                // WARNING: Loss of precision!
                return new Rectangle(
                    (int)Math.Floor(TextureRegion.TopLeft.X * Texture.Width),
                    (int)Math.Floor(TextureRegion.TopLeft.Y * Texture.Height),
                    (int)Math.Ceiling(TextureRegion.Size.X * Texture.Width),
                    (int)Math.Ceiling(TextureRegion.Size.Y * Texture.Height)
                );
            }
            set {
                TextureRegion = Texture.BoundsFromRectangle(ref value);
            }
        }

        public Color Color {
            get {
                return MultiplyColor;
            }
            set {
                MultiplyColor = value;
            }
        }

        public bool Crop (Bounds cropBounds) {
            var texSize = new Vector2(Textures.Texture1.Width, Textures.Texture1.Height);
            var texRgn = (TextureRegion.BottomRight - TextureRegion.TopLeft) * texSize * Scale;
            var drawBounds = new Bounds(
                Position,
                Position + texRgn
            );

            var newBounds_ = Bounds.FromIntersection(drawBounds, cropBounds);
            if (!newBounds_.HasValue)
                return false;
            var newBounds = newBounds_.Value;

            if (newBounds.TopLeft.X > drawBounds.TopLeft.X) {
                Position.X += newBounds.TopLeft.X - drawBounds.TopLeft.X;
                TextureRegion.TopLeft.X += (newBounds.TopLeft.X - drawBounds.TopLeft.X) / texSize.X / Scale.X;
            }
            if (newBounds.TopLeft.Y > drawBounds.TopLeft.Y) {
                Position.Y += newBounds.TopLeft.Y - drawBounds.TopLeft.Y;
                TextureRegion.TopLeft.Y += (newBounds.TopLeft.Y - drawBounds.TopLeft.Y) / texSize.Y / Scale.Y;
            }

            if (newBounds.BottomRight.X < drawBounds.BottomRight.X)
                TextureRegion.BottomRight.X += (newBounds.BottomRight.X - drawBounds.BottomRight.X) / texSize.X / Scale.X;
            if (newBounds.BottomRight.Y < drawBounds.BottomRight.Y)
                TextureRegion.BottomRight.Y += (newBounds.BottomRight.Y - drawBounds.BottomRight.Y) / texSize.Y / Scale.Y;

            return true;
        }

        public ImageReference ImageRef {
            get {
                return new ImageReference(Textures.Texture1, TextureRegion);
            }
            set {
                Textures = new TextureSet(value.Texture);
                TextureRegion = value.TextureRegion;
            }
        }

        public bool IsValid {
            get {
                return (Textures.Texture1 != null);
            }
        }
    }
}