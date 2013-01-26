using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework;
using Squared.Game;
using Squared.Util;
using System.Reflection;

namespace Squared.Render {
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct BitmapVertex : IVertexType {
        public Vector3 Position;
        public Vector2 TextureTopLeft;
        public Vector2 TextureBottomRight;
        public Vector2 Scale;
        public Vector2 Origin;
        public float Rotation;
        public Color MultiplyColor;
        public Color AddColor;
        public short Corner;
        public short Unused;

        public static readonly VertexElement[] Elements;
        static readonly VertexDeclaration _VertexDeclaration;

        public unsafe static int SizeInBytes {
            get { return sizeof(BitmapVertex); }
        }

        unsafe static BitmapVertex () {
            Elements = new VertexElement[] {
                new VertexElement( 0, 
                    VertexElementFormat.Vector3, VertexElementUsage.Position, 0 ),
                new VertexElement( (short)(sizeof(Vector3)), 
                    VertexElementFormat.Vector4, VertexElementUsage.Position, 1 ),
                new VertexElement( (short)(sizeof(Vector3) + sizeof(Vector4)), 
                    VertexElementFormat.Vector4, VertexElementUsage.Position, 2 ),
                new VertexElement( (short)(sizeof(Vector3) + sizeof(Vector4) * 2), 
                    VertexElementFormat.Single, VertexElementUsage.Position, 3 ),
                new VertexElement( (short)(sizeof(Vector3) + sizeof(Vector4) * 2 + sizeof(float)), 
                    VertexElementFormat.Color, VertexElementUsage.Color, 0 ),
                new VertexElement( (short)(sizeof(Vector3) + sizeof(Vector4) * 2 + sizeof(float) + sizeof(Color)), 
                    VertexElementFormat.Color, VertexElementUsage.Color, 1 ),
                new VertexElement( (short)(sizeof(Vector3) + sizeof(Vector4) * 2 + sizeof(float) + sizeof(Color) * 2), 
                    VertexElementFormat.Short2, VertexElementUsage.BlendIndices, 0 )
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

    public interface IBitmapBatch {
        void Add (BitmapDrawCall item);
        void Add (ref BitmapDrawCall item);
        void AddRange (BitmapDrawCall[] items);
        void Issue ();
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

                bblhs._DrawCalls.AddRange(bbrhs._DrawCalls);
                bbrhs._DrawCalls.Clear();

                return lhs;
            }
        }

        struct NativeBatch {
            public TextureSet TextureSet;
            public int VertexOffset;
            public int VertexCount;
        }

        public SamplerState SamplerState;
        public bool UseZBuffer = false;

        public static BitmapDrawCallComparer DrawCallComparer = new BitmapDrawCallComparer();
        public static BitmapDrawCallTextureComparer DrawCallTextureComparer = new BitmapDrawCallTextureComparer();

        public const int BitmapBatchSize = 256;

        private static short[] _IndexBatch;

        private ArrayPoolAllocator<BitmapVertex> _Allocator;
        private static ListPool<NativeBatch> _NativePool = new ListPool<NativeBatch>(
            2048, 16, 128
        );
        private BitmapVertex[] _NativeBuffer = null;
        private List<NativeBatch> _NativeBatches = null;
        private volatile bool _Prepared = false;

        static BitmapBatch () {
            _IndexBatch = GenerateIndices(BitmapBatchSize * 6);

            BatchCombiner.Combiners.Add(new BitmapBatchCombiner());
        }

        protected static unsafe short[] GenerateIndices (int numIndices) {
            int numQuads = numIndices / 6;
            int numVertices = numQuads * 4;
            short[] result = new short[numIndices];

            fixed (short* p = &result[0])
                for (short i = 0, j = 0; i < numVertices; i += 4, j += 6) {
                    p[j] = i;
                    p[j + 1] = (short)(i + 1);
                    p[j + 2] = (short)(i + 3);
                    p[j + 3] = (short)(i + 1);
                    p[j + 4] = (short)(i + 2);
                    p[j + 5] = (short)(i + 3);
                }

            return result;
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
            Add(ref item);
        }

        new public void Add (ref BitmapDrawCall item) {
            item.TextureID = item.Textures.GetHashCode();

            base.Add(ref item);
        }

        public void AddRange (BitmapDrawCall[] items) {
            for (int i = 0, l = items.Length; i < l; i++) {
                var item = items[i];
                if (!item.IsValid)
                    continue;

                item.TextureID = item.Textures.GetHashCode();
                base.Add(ref item);
            }
        }

        public void Issue () {
            Dispose();
        }

        public unsafe override void Prepare () {
            if (_DrawCalls.Count == 0)
                return;

            if (_NativeBatches == null)
                _NativeBatches = _NativePool.Allocate();

            if (UseZBuffer)
                _DrawCalls.Sort(DrawCallTextureComparer);
            else
                _DrawCalls.Sort(DrawCallComparer);

            var count = _DrawCalls.Count;
            int vertCount = 0, vertOffset = 0, bufferSize = count * 4;
            int blockSizeLimit = BitmapBatchSize * 4;
            var buffer = _NativeBuffer = _Allocator.Allocate(bufferSize).Buffer;
            int v = 0;

            TextureSet currentTextures = new TextureSet();
            BitmapVertex vertex = new BitmapVertex();

            fixed (BitmapVertex* d = &buffer[0])
            for (int i = 0; i < count; i++) {
                var call = _DrawCalls[i];

                bool flush = (call.Textures != currentTextures) || (vertCount >= blockSizeLimit);

                if (flush && (vertCount > 0)) {
                    _NativeBatches.Add(new NativeBatch { 
                        TextureSet = currentTextures, 
                        VertexCount = vertCount,
                        VertexOffset = vertOffset,
                    });

                    vertOffset += vertCount;
                    vertCount = 0;
                }

                if (call.Textures != currentTextures)
                    currentTextures = call.Textures;

                vertex.Position = new Vector3(call.Position, UseZBuffer ? call.SortKey : 0);
                vertex.TextureTopLeft = call.TextureRegion.TopLeft;
                vertex.TextureBottomRight = call.TextureRegion.BottomRight;
                vertex.MultiplyColor = call.MultiplyColor;
                vertex.AddColor = call.AddColor;
                vertex.Scale = call.Scale;
                vertex.Origin = call.Origin;
                vertex.Rotation = call.Rotation;

                for (short j = 0; j < 4; j++, v++) {
                    vertex.Unused = vertex.Corner = j;
                    d[v] = vertex;
                }

                vertCount += 4;
            }

            if (vertCount > 0)
                _NativeBatches.Add(new NativeBatch {
                    TextureSet = currentTextures,
                    VertexCount = vertCount,
                    VertexOffset = vertOffset,
                });

            _Prepared = true;
        }

        public override void Issue (DeviceManager manager) {
            if (_DrawCalls.Count == 0)
                return;

            if (_Prepared == false)
                throw new InvalidOperationException("Not prepared");

            if (_NativeBuffer == null)
                throw new InvalidOperationException("Already issued");

            var device = manager.Device;

            using (manager.ApplyMaterial(Material)) {
                TextureSet currentTexture = new TextureSet();
                var paramSize = manager.CurrentParameters["BitmapTextureSize"];
                var paramTexel = manager.CurrentParameters["Texel"];

                foreach (var nb in _NativeBatches) {
                    if (nb.TextureSet != currentTexture) {
                        currentTexture = nb.TextureSet;
                        var tex1 = currentTexture.Texture1;

                        device.Textures[0] = tex1;
                        device.Textures[1] = currentTexture.Texture2;
                        device.SamplerStates[0] = device.SamplerStates[1] = SamplerState;

                        var vSize = new Vector2(tex1.Width, tex1.Height);
                        paramSize.SetValue(vSize);
                        paramTexel.SetValue(new Vector2(1.0f / vSize.X, 1.0f / vSize.Y));
                        manager.CurrentEffect.CurrentTechnique.Passes[0].Apply();
                    }

                    if (UseZBuffer) {
                        var dss = device.DepthStencilState;
                        if (dss.DepthBufferEnable == false)
                            throw new InvalidOperationException("UseZBuffer set to true but depth buffer is disabled");
                    }

                    device.DrawUserIndexedPrimitives(
                        PrimitiveType.TriangleList, _NativeBuffer, 
                        nb.VertexOffset, nb.VertexCount, 
                        _IndexBatch, 0, 
                        nb.VertexCount / 2
                    );
                }
            }

            _NativeBuffer = null;
        }

        protected override void OnReleaseResources () {
            _Prepared = false;
            _NativeBuffer = null;

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

        public override bool Equals (object obj) {
            if (obj is TextureSet) {
                var rhs = (TextureSet)obj;
                return this == rhs;
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