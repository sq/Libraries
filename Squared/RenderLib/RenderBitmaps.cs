using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework;
using Squared.Game;
using Squared.Util;
using System.Reflection;

namespace Squared.Render {
    [Serializable, StructLayout(LayoutKind.Explicit)]
    public struct BitmapVertex {
        [FieldOffset(0)]
        public Vector2 Position;
        [FieldOffset(8)]
        public Vector2 TextureTopLeft;
        [FieldOffset(16)]
        public Vector2 TextureBottomRight;
        [FieldOffset(24)]
        public Vector2 Scale;
        [FieldOffset(32)]
        public Vector2 Origin;
        [FieldOffset(40)]
        public float Rotation;
        [FieldOffset(44)]
        public Color MultiplyColor;
        [FieldOffset(48)]
        public Color AddColor;
        [FieldOffset(52)]
        public short Corner;
        [FieldOffset(54)]
        public short Unused;

        public static readonly VertexElement[] Elements;

        public unsafe static int SizeInBytes {
            get { return sizeof(BitmapVertex); }
        }

        unsafe static BitmapVertex () {
            Elements = new VertexElement[] {
            new VertexElement( 0, 0, 
                VertexElementFormat.Vector2, VertexElementMethod.Default, VertexElementUsage.Position, 0 ),
            new VertexElement( 0, (short)(sizeof(Vector2)), 
                VertexElementFormat.Vector4, VertexElementMethod.Default, VertexElementUsage.Position, 1 ),
            new VertexElement( 0, (short)(sizeof(Vector2) + sizeof(Vector4)), 
                VertexElementFormat.Vector4, VertexElementMethod.Default, VertexElementUsage.Position, 2 ),
            new VertexElement( 0, (short)(sizeof(Vector2) + sizeof(Vector4) * 2), 
                VertexElementFormat.Single, VertexElementMethod.Default, VertexElementUsage.Position, 3 ),
            new VertexElement( 0, (short)(sizeof(Vector2) + sizeof(Vector4) * 2 + sizeof(float)), 
                VertexElementFormat.Color, VertexElementMethod.Default, VertexElementUsage.Color, 0 ),
            new VertexElement( 0, (short)(sizeof(Vector2) + sizeof(Vector4) * 2 + sizeof(float) + sizeof(Color)), 
                VertexElementFormat.Color, VertexElementMethod.Default, VertexElementUsage.Color, 1 ),
            new VertexElement( 0, (short)(sizeof(Vector2) + sizeof(Vector4) * 2 + sizeof(float) + sizeof(Color) * 2), 
                VertexElementFormat.Short2, VertexElementMethod.Default, VertexElementUsage.BlendIndices, 0 )
          };
        }
    }

    public sealed class BitmapDrawCallComparer : IComparer<BitmapDrawCall> {
        public int Compare (BitmapDrawCall x, BitmapDrawCall y) {
            var result = (int)(x.SortKey - y.SortKey);
            if (result == 0)
                result = (int)(x.TextureID - y.TextureID);
            return result;
        }
    }

    public class BitmapBatch : ListBatch<BitmapDrawCall> {
        struct NativeBatch {
            public TextureSet TextureSet;
            public int VertexOffset;
            public int VertexCount;
        }

        public static BitmapDrawCallComparer DrawCallComparer = new BitmapDrawCallComparer();

        public const int BitmapBatchSize = 256;

        private static short[] _IndexBatch;

        private ArrayPoolAllocator<BitmapVertex> _Allocator;
        private static ListPool<NativeBatch> _NativePool = new ListPool<NativeBatch>(
            256, 16, 128
        );
        private BitmapVertex[] _NativeBuffer = null;
        private List<NativeBatch> _NativeBatches = null;
        private volatile bool _Prepared = false;

        static BitmapBatch () {
            _IndexBatch = GenerateIndices(BitmapBatchSize * 6);
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

        public static BitmapBatch New (Frame frame, int layer, Material material) {
            if (frame == null)
                throw new ArgumentNullException("frame");
            if (material == null)
                throw new ArgumentNullException("material");

            var result = frame.RenderManager.AllocateBatch<BitmapBatch>();
            result.Initialize(frame, layer, material);
            return result;
        }

        public override void Initialize (Frame frame, int layer, Material material) {
            base.Initialize(frame, layer, material);

            _Allocator = frame.RenderManager.GetArrayAllocator<BitmapVertex>();
            _NativeBatches = _NativePool.Allocate();
        }

        public override void Add (ref BitmapDrawCall item) {
            item.TextureID = item.Textures.GetHashCode();

            base.Add(ref item);
        }

        public unsafe override void Prepare () {
            if (_DrawCalls.Count == 0)
                return;

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

                vertex.Position = call.Position;
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
                throw new InvalidOperationException();

            var device = manager.Device;

            using (manager.ApplyMaterial(Material)) {
                TextureSet currentTexture = new TextureSet();
                var paramTexture = manager.CurrentParameters["BitmapTexture"];
                var paramTexture2 = manager.CurrentParameters["SecondTexture"];
                var paramSize = manager.CurrentParameters["BitmapTextureSize"];
                var paramTexel = manager.CurrentParameters["Texel"];

                foreach (var nb in _NativeBatches) {
                    if (nb.TextureSet != currentTexture) {
                        currentTexture = nb.TextureSet;
                        var tex1 = currentTexture.Texture1.Texture;
                        paramTexture.SetValue(tex1);
                        paramTexture2.SetValue(currentTexture.Texture2.Texture);
                        var vSize = new Vector2(tex1.Width, tex1.Height);
                        paramSize.SetValue(vSize);
                        paramTexel.SetValue(new Vector2(1.0f / vSize.X, 1.0f / vSize.Y));
                        manager.CommitChanges();
                    }

                    device.DrawUserIndexedPrimitives(
                        PrimitiveType.TriangleList, _NativeBuffer, 
                        nb.VertexOffset, nb.VertexCount, 
                        _IndexBatch, 0, 
                        nb.VertexCount / 2
                    );
                }
            }
        }

        public override void ReleaseResources () {
            _Prepared = false;

            _NativeBuffer = null;
            _NativePool.Release(ref _NativeBatches);

            base.ReleaseResources();
        }
    }

    // This allows us to automatically defer a RenderTarget2D.GetTexture() 
    //  invocation until preceding draw calls have completed
    public struct TextureRef {
        private readonly Texture2D _Texture;
        private readonly RenderTarget2D _RenderTarget;
        private bool _NeedsMipmapsGenerated;

        public TextureRef (Texture2D texture) {
            _Texture = texture;
            _RenderTarget = null;
            _NeedsMipmapsGenerated = false;
        }

        public TextureRef (RenderTarget2D renderTarget) {
            _Texture = null;
            _RenderTarget = renderTarget;
            _NeedsMipmapsGenerated = false;
        }

        public TextureRef (RenderTarget2D renderTarget, bool needsMipmapsGenerated) {
            _Texture = null;
            _RenderTarget = renderTarget;
            _NeedsMipmapsGenerated = needsMipmapsGenerated;
        }

        public static implicit operator TextureRef (Texture2D texture) {
            return new TextureRef(texture);
        }

        public static implicit operator TextureRef (RenderTarget2D renderTarget) {
            return new TextureRef(renderTarget);
        }

        public override bool Equals (object obj) {
            if (obj is TextureRef) {
                var rhs = (TextureRef)obj;
                return this == rhs;
            } else {
                return base.Equals(obj);
            }
        }

        public override int GetHashCode () {
            if (_Texture != null)
                return _Texture.GetHashCode();
            else if (_RenderTarget != null)
                return _RenderTarget.GetHashCode();
            else
                return 0;
        }

        public static bool operator == (TextureRef lhs, TextureRef rhs) {
            return (lhs._Texture == rhs._Texture) && (lhs._RenderTarget == rhs._RenderTarget);
        }

        public static bool operator != (TextureRef lhs, TextureRef rhs) {
            return (lhs._Texture != rhs._Texture) || (lhs._RenderTarget != rhs._RenderTarget);
        }

        public bool IsDisposed {
            get {
                if (_Texture != null)
                    return _Texture.IsDisposed;
                else if (_RenderTarget != null)
                    return _RenderTarget.IsDisposed;
                else
                    return false;
            }
        }

        public int Width {
            get {
                if (_Texture != null)
                    return _Texture.Width;
                else if (_RenderTarget != null)
                    return _RenderTarget.Width;
                else
                    return 0;
            }
        }

        public int Height {
            get {
                if (_Texture != null)
                    return _Texture.Height;
                else if (_RenderTarget != null)
                    return _RenderTarget.Height;
                else
                    return 0;
            }
        }

        public Texture2D Texture {
            get {
                if (_Texture != null)
                    return _Texture;
                else if (_RenderTarget != null) {
                    var result = _RenderTarget.GetTexture();
                    if (_NeedsMipmapsGenerated) {
                        result.GenerateMipMaps(TextureFilter.Linear);
                        _NeedsMipmapsGenerated = false;
                    }
                    return result;
                } else
                    return null;
            }
        }
    }

    public struct TextureSet {
        public TextureRef Texture1, Texture2;

        public TextureSet (TextureRef texture1) {
            Texture1 = texture1;
            Texture2 = new TextureRef();
        }

        public TextureSet (TextureRef texture1, TextureRef texture2) {
            Texture1 = texture1;
            Texture2 = texture2;
        }

        public TextureRef this[int index] {
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
            return new TextureSet(new TextureRef(texture1));
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
            return Texture1.GetHashCode() ^ Texture2.GetHashCode();
        }
    }

    public class ImageReference {
        public readonly TextureRef Texture;
        public readonly Bounds TextureRegion;

        public ImageReference (TextureRef texture, Bounds region) {
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
        public int SortKey;

        internal int TextureID;

        public BitmapDrawCall (TextureRef texture, Vector2 position) 
            : this (texture, position, new Bounds(Vector2.Zero, Vector2.One)) {
        }

        public BitmapDrawCall (TextureRef texture, Vector2 position, Color color)
            : this(texture, position, new Bounds(Vector2.Zero, Vector2.One), color) {
        }

        public BitmapDrawCall (TextureRef texture, Vector2 position, Bounds textureRegion)
            : this(texture, position, textureRegion, Color.White) {
        }

        public BitmapDrawCall (TextureRef texture, Vector2 position, Bounds textureRegion, Color color)
            : this(texture, position, textureRegion, color, Vector2.One) {
        }

        public BitmapDrawCall (TextureRef texture, Vector2 position, float scale)
            : this(texture, position, new Bounds(Vector2.Zero, Vector2.One), Color.White, new Vector2(scale, scale)) {
        }

        public BitmapDrawCall (TextureRef texture, Vector2 position, Bounds textureRegion, Color color, float scale)
            : this(texture, position, textureRegion, color, new Vector2(scale, scale)) {
        }

        public BitmapDrawCall (TextureRef texture, Vector2 position, Bounds textureRegion, Color color, Vector2 scale)
            : this(texture, position, textureRegion, color, scale, Vector2.Zero) {
        }

        public BitmapDrawCall (TextureRef texture, Vector2 position, Bounds textureRegion, Color color, Vector2 scale, Vector2 origin)
            : this(texture, position, textureRegion, color, scale, origin, 0.0f) {
        }

        public BitmapDrawCall (TextureRef texture, Vector2 position, Bounds textureRegion, Color color, Vector2 scale, Vector2 origin, float rotation) {
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
    }
}