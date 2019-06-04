#define USE_INDEXED_SORT

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework;
using Squared.Game;
using Squared.Render.Internal;
using Squared.Render.Tracing;
using Squared.Util;
using System.Reflection;
using Squared.Util.DeclarativeSort;
using System.Threading;
using System.Runtime.CompilerServices;

namespace Squared.Render {
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct CornerVertex : IVertexType {
        public short Corner;
        public short Unused;

        public static readonly VertexElement[] Elements;
        static readonly VertexDeclaration _VertexDeclaration;

        static CornerVertex () {
            var tThis = typeof(CornerVertex);

            Elements = new VertexElement[] {
                new VertexElement( Marshal.OffsetOf(tThis, "Corner").ToInt32(), 
                    VertexElementFormat.Short2, VertexElementUsage.BlendIndices, 0 )
            };

            _VertexDeclaration = new VertexDeclaration(Elements);
        }

        public VertexDeclaration VertexDeclaration {
            get { return _VertexDeclaration; }
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct BitmapVertex : IVertexType {
        public Vector3 Position;
        public Vector4 Texture1Region;
        public Vector4 Texture2Region;
        public Vector2 Scale;
        public Vector2 Origin;
        public float Rotation;
        public Color MultiplyColor;
        public Color AddColor;

        public static readonly VertexElement[] Elements;
        static readonly VertexDeclaration _VertexDeclaration;

        static BitmapVertex () {
            var tThis = typeof(BitmapVertex);

            Elements = new VertexElement[] {
                new VertexElement( Marshal.OffsetOf(tThis, "Position").ToInt32(), 
                    VertexElementFormat.Vector3, VertexElementUsage.Position, 0 ),
                new VertexElement( Marshal.OffsetOf(tThis, "Texture1Region").ToInt32(), 
                    VertexElementFormat.Vector4, VertexElementUsage.Position, 1 ),
                new VertexElement( Marshal.OffsetOf(tThis, "Texture2Region").ToInt32(), 
                    VertexElementFormat.Vector4, VertexElementUsage.Position, 2 ),
                // ScaleOrigin
                new VertexElement( Marshal.OffsetOf(tThis, "Scale").ToInt32(), 
                    VertexElementFormat.Vector4, VertexElementUsage.Position, 3 ),
                new VertexElement( Marshal.OffsetOf(tThis, "Rotation").ToInt32(), 
                    VertexElementFormat.Single, VertexElementUsage.Position, 4 ),
                new VertexElement( Marshal.OffsetOf(tThis, "MultiplyColor").ToInt32(), 
                    VertexElementFormat.Color, VertexElementUsage.Color, 0 ),
                new VertexElement( Marshal.OffsetOf(tThis, "AddColor").ToInt32(), 
                    VertexElementFormat.Color, VertexElementUsage.Color, 1 ),
            };

            _VertexDeclaration = new VertexDeclaration(Elements);
        }

        public VertexDeclaration VertexDeclaration {
            get { return _VertexDeclaration; }
        }
    }

    public sealed class BitmapDrawCallSorterComparer : IRefComparer<BitmapDrawCall>, IComparer<BitmapDrawCall> {
        public Sorter<BitmapDrawCall>.SorterComparer Comparer;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Compare (ref BitmapDrawCall x, ref BitmapDrawCall y) {
            var result = Comparer.Compare(x, y);

            if (result == 0) {
                result = (x.Textures.HashCode > y.Textures.HashCode)
                ? 1
                : (
                    (x.Textures.HashCode < y.Textures.HashCode)
                    ? -1
                    : 0
                );
            }

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Compare (BitmapDrawCall x, BitmapDrawCall y) {
            return Compare(ref x, ref y);
        }
    }

    public sealed class BitmapDrawCallOrderAndTextureComparer : IRefComparer<BitmapDrawCall>, IComparer<BitmapDrawCall> {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe int Compare (ref BitmapDrawCall x, ref BitmapDrawCall y) {
            var result = FastMath.CompareF(x.SortKey.Order, y.SortKey.Order);
            if (result == 0)
                result = (x.Textures.HashCode - y.Textures.HashCode);
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Compare (BitmapDrawCall x, BitmapDrawCall y) {
            return Compare(ref x, ref y);
        }
    }

    public sealed class BitmapDrawCallTextureComparer : IRefComparer<BitmapDrawCall>, IComparer<BitmapDrawCall> {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Compare (ref BitmapDrawCall x, ref BitmapDrawCall y) {
            return (x.Textures.HashCode > y.Textures.HashCode)
                ? 1
                : (
                    (x.Textures.HashCode < y.Textures.HashCode)
                    ? -1
                    : 0
                );
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Compare (BitmapDrawCall x, BitmapDrawCall y) {
            return Compare(ref x, ref y);
        }
    }

    public interface IBitmapBatch : IBatch {
        Sorter<BitmapDrawCall> Sorter {
            get; set;
        }

        void Add (BitmapDrawCall item);
        void Add (ref BitmapDrawCall item);
        void AddRange (ArraySegment<BitmapDrawCall> items);
    }

    public static class QuadUtils {
        private static readonly ushort[] QuadIndices = new ushort[] {
            0, 1, 2,
            0, 2, 3
        };

        public static BufferGenerator<CornerVertex>.SoftwareBuffer CreateCornerBuffer (IBatchContainer container) {
            BufferGenerator<CornerVertex>.SoftwareBuffer result;
            var cornerGenerator = container.RenderManager.GetBufferGenerator<BufferGenerator<CornerVertex>>();
            // TODO: Is it OK to share the buffer?
            if (!cornerGenerator.TryGetCachedBuffer("QuadCorners", 4, 6, out result)) {
                result = cornerGenerator.Allocate(4, 6, true);
                cornerGenerator.SetCachedBuffer("QuadCorners", result);
                // TODO: Can we just skip filling the buffer here?
            }

            var verts = result.Vertices;
            var indices = result.Indices;

            var v = new CornerVertex();
            for (var i = 0; i < 4; i++) {
                v.Corner = v.Unused = (short)i;
                verts.Array[verts.Offset + i] = v;
            }

            for (var i = 0; i < QuadIndices.Length; i++)
                indices.Array[indices.Offset + i] = QuadIndices[i];

            return result;
        }
    }

    public class BitmapBatchBase<TDrawCall> : ListBatch<TDrawCall> {
        internal struct NativeBatch {
            public readonly Material Material;

            public SamplerState SamplerState;
            public SamplerState SamplerState2;

            public readonly ISoftwareBuffer SoftwareBuffer;
            public readonly TextureSet TextureSet;

            public readonly Vector2 Texture1Size, Texture1HalfTexel;
            public readonly Vector2 Texture2Size, Texture2HalfTexel;

            public readonly int LocalVertexOffset;
            public readonly int VertexCount;

            public NativeBatch (
                ISoftwareBuffer softwareBuffer, TextureSet textureSet, 
                int localVertexOffset, int vertexCount, Material material,
                SamplerState samplerState, SamplerState samplerState2
            ) {
                Material = material;
                SamplerState = samplerState;
                SamplerState2 = samplerState2;

                SoftwareBuffer = softwareBuffer;
                TextureSet = textureSet;

                LocalVertexOffset = localVertexOffset;
                VertexCount = vertexCount;

                Texture1Size = new Vector2(textureSet.Texture1.Width, textureSet.Texture1.Height);
                Texture1HalfTexel = new Vector2(1.0f / Texture1Size.X, 1.0f / Texture1Size.Y);

                if (textureSet.Texture2 != null) {
                    Texture2Size = new Vector2(textureSet.Texture2.Width, textureSet.Texture2.Height);
                    Texture2HalfTexel = new Vector2(1.0f / Texture2Size.X, 1.0f / Texture2Size.Y);
                } else {
                    Texture2HalfTexel = Texture2Size = Vector2.Zero;
                }
            }
        }
    }

    public struct TextureSet {
        public readonly Texture2D Texture1, Texture2;
        public readonly int HashCode;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TextureSet (Texture2D texture1) {
            Texture1 = texture1;
            Texture2 = null;
            HashCode = texture1.GetHashCode();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TextureSet (Texture2D texture1, Texture2D texture2) {
            Texture1 = texture1;
            Texture2 = texture2;
            HashCode = texture1.GetHashCode() ^ texture2.GetHashCode();
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
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator TextureSet (Texture2D texture1) {
            return new TextureSet(texture1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals (ref TextureSet rhs) {
            return (HashCode == rhs.HashCode) && (Texture1 == rhs.Texture1) && (Texture2 == rhs.Texture2);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals (object obj) {
            if (obj is TextureSet) {
                var rhs = (TextureSet)obj;
                return this.Equals(ref rhs);
            } else {
                return base.Equals(obj);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator == (TextureSet lhs, TextureSet rhs) {
            return (lhs.HashCode == rhs.HashCode) && (lhs.Texture1 == rhs.Texture1) && (lhs.Texture2 == rhs.Texture2);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator != (TextureSet lhs, TextureSet rhs) {
            return (lhs.Texture1 != rhs.Texture1) || (lhs.Texture2 != rhs.Texture2);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode () {
            return HashCode;
        }
    }

    public class ImageReference {
        public readonly Texture2D Texture;
        public readonly Bounds TextureRegion;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ImageReference (Texture2D texture, Bounds region) {
            Texture = texture;
            TextureRegion = region;
        }
    }

    public struct DrawCallSortKey {
        public Tags  Tags;
        public float Order;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public DrawCallSortKey (Tags tags = default(Tags), float order = 0) {
            Tags = tags;
            Order = order;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator DrawCallSortKey (Tags tags) {
            return new DrawCallSortKey(tags: tags);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator DrawCallSortKey (float order) {
            return new DrawCallSortKey(order: order);
        }
    }

    public struct BitmapDrawCall {
        public TextureSet Textures;
        public Vector2    Position;
        public Vector2    Scale;
        public Vector2    Origin;
        public Bounds     TextureRegion;
        public Bounds?    TextureRegion2;
        public float      Rotation;
        public Color      MultiplyColor, AddColor;
        public DrawCallSortKey SortKey;

#if DEBUG
        public static bool ValidateFields = false;
#else
        public static bool ValidateFields = false;
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public BitmapDrawCall (Texture2D texture, Vector2 position)
            : this(texture, position, texture.Bounds()) {
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public BitmapDrawCall (Texture2D texture, Vector2 position, Color color)
            : this(texture, position, texture.Bounds(), color) {
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public BitmapDrawCall (Texture2D texture, Vector2 position, Bounds textureRegion)
            : this(texture, position, textureRegion, Color.White) {
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public BitmapDrawCall (Texture2D texture, Vector2 position, Bounds textureRegion, Color color)
            : this(texture, position, textureRegion, color, Vector2.One) {
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public BitmapDrawCall (Texture2D texture, Vector2 position, float scale)
            : this(texture, position, texture.Bounds(), Color.White, new Vector2(scale, scale)) {
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public BitmapDrawCall (Texture2D texture, Vector2 position, Vector2 scale)
            : this(texture, position, texture.Bounds(), Color.White, scale) {
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public BitmapDrawCall (Texture2D texture, Vector2 position, Bounds textureRegion, Color color, float scale)
            : this(texture, position, textureRegion, color, new Vector2(scale, scale)) {
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public BitmapDrawCall (Texture2D texture, Vector2 position, Bounds textureRegion, Color color, Vector2 scale)
            : this(texture, position, textureRegion, color, scale, Vector2.Zero) {
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public BitmapDrawCall (Texture2D texture, Vector2 position, Bounds textureRegion, Color color, Vector2 scale, Vector2 origin)
            : this(texture, position, textureRegion, color, scale, origin, 0.0f) {
        }

        public BitmapDrawCall (Texture2D texture, Vector2 position, Bounds textureRegion, Color color, Vector2 scale, Vector2 origin, float rotation) {
            if (texture == null)
                throw new ArgumentNullException("texture");
            else if (texture.IsDisposed)
                throw new ObjectDisposedException("texture");

            Textures = new TextureSet(texture);
            Position = position;
            TextureRegion = textureRegion;
            TextureRegion2 = null;
            MultiplyColor = color;
            AddColor = new Color(0, 0, 0, 0);
            Scale = scale;
            Origin = origin;
            Rotation = rotation;

            SortKey = default(DrawCallSortKey);
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
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set {
                if (value == null)
                    throw new ArgumentNullException("texture");

                Textures = new TextureSet(value);
            }
        }

        public float ScaleF {
            get {
                return (Scale.X + Scale.Y) / 2.0f;
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set {
                Scale = new Vector2(value, value);
            }
        }

        public Tags SortTags {
            get {
                return SortKey.Tags;
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set {
                SortKey.Tags = value;
            }
        }

        public float SortOrder {
            get {
                return SortKey.Order;
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set {
                SortKey.Order = value;
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
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set {
                TextureRegion = Texture.BoundsFromRectangle(ref value);
            }
        }

        public Color Color {
            get {
                return MultiplyColor;
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set {
                MultiplyColor = value;
            }
        }

        public void AdjustOrigin (Vector2 newOrigin) {
            var newPosition = Position;

            var textureSize = new Vector2(Texture.Width, Texture.Height) * TextureRegion.Size;
            newPosition += ((newOrigin - Origin) * textureSize * Scale);

            Position = newPosition;
            Origin = newOrigin;
        }

        public Bounds EstimateDrawBounds () {
            var texSize = new Vector2(Textures.Texture1.Width, Textures.Texture1.Height);
            var texRgn = (TextureRegion.BottomRight - TextureRegion.TopLeft) * texSize * Scale;
            var offset = Origin * texRgn;

            return new Bounds(
                Position - offset,
                Position + texRgn - offset
            );
        }

        // Returns true if the draw call was modified at all
        public bool Crop (Bounds cropBounds) {
            // HACK
            if (Math.Abs(Rotation) >= 0.01)
                return false;

            AdjustOrigin(Vector2.Zero);

            var texSize = new Vector2(Textures.Texture1.Width, Textures.Texture1.Height);
            var texRgnPx = TextureRegion.Scale(texSize);
            var drawBounds = EstimateDrawBounds();

            var newBounds_ = Bounds.FromIntersection(drawBounds, cropBounds);
            if (!newBounds_.HasValue) {
                TextureRegion = new Bounds(Vector2.Zero, Vector2.Zero);
                return true;
            }

            var newBounds = newBounds_.Value;
            var scaledSize = texSize * Scale;

            if (newBounds.TopLeft.X > drawBounds.TopLeft.X) {
                Position.X += (newBounds.TopLeft.X - drawBounds.TopLeft.X);
                TextureRegion.TopLeft.X += (newBounds.TopLeft.X - drawBounds.TopLeft.X) / scaledSize.X;
            }
            if (newBounds.TopLeft.Y > drawBounds.TopLeft.Y) {
                Position.Y += (newBounds.TopLeft.Y - drawBounds.TopLeft.Y);
                TextureRegion.TopLeft.Y += (newBounds.TopLeft.Y - drawBounds.TopLeft.Y) / scaledSize.Y;
            }

            if (newBounds.BottomRight.X < drawBounds.BottomRight.X)
                TextureRegion.BottomRight.X += (newBounds.BottomRight.X - drawBounds.BottomRight.X) / scaledSize.X;
            if (newBounds.BottomRight.Y < drawBounds.BottomRight.Y)
                TextureRegion.BottomRight.Y += (newBounds.BottomRight.Y - drawBounds.BottomRight.Y) / scaledSize.Y;

            return true;
        }

        public ImageReference ImageRef {
            get {
                return new ImageReference(Textures.Texture1, TextureRegion);
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set {
                if (value == null || value.Texture == null)
                    throw new ArgumentNullException("texture");
                Textures = new TextureSet(value.Texture);
                TextureRegion = value.TextureRegion;
            }
        }

        public bool IsValid {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                if (ValidateFields) {
                    if (!Position.IsFinite())
                        return false;
                    if (!TextureRegion.TopLeft.IsFinite())
                        return false;
                    if (!TextureRegion.BottomRight.IsFinite())
                        return false;
                    if (TextureRegion2.HasValue) {
                        if (!TextureRegion2.Value.TopLeft.IsFinite())
                            return false;
                        if (!TextureRegion2.Value.BottomRight.IsFinite())
                            return false;
                    }
                    if (!Arithmetic.IsFinite(Rotation))
                        return false;
                    if (!Scale.IsFinite())
                        return false;
                }

                return ((Textures.Texture1 != null) && !Textures.Texture1.IsDisposed);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static BitmapDrawCall operator * (BitmapDrawCall dc, float opacity) {
            dc.MultiplyColor *= opacity;
            dc.AddColor *= opacity;
            return dc;
        }

        public override string ToString () {
            string name = null;
            if (Texture == null)
                name = "null";
            else if (!ObjectNames.TryGetName(Texture, out name))
                name = string.Format("{0}x{1}", Texture.Width, Texture.Height);

            return string.Format("tex {0} pos {1}", name, Position);
        }
    }
}