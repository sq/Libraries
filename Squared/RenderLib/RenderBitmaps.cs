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

#if PSM
using VertexFormat = Sce.PlayStation.Core.Graphics.VertexFormat;
#endif

namespace Squared.Render {    
#if PSM
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
#else
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
#endif
    public struct BitmapVertex : IVertexType {
        public Vector3 Position;
        public Vector2 TextureTopLeft;
        public Vector2 TextureBottomRight;
        public Vector2 Scale;
        public Vector2 Origin;
        public float Rotation;
        public Color MultiplyColor;
        public Color AddColor;
        
#if PSM
        public float Corner;
#else
        public short Corner;
        public short Unused;
#endif

        public static readonly VertexElement[] Elements;
        static readonly VertexDeclaration _VertexDeclaration;

        static BitmapVertex () {
#if PSM
            // fuck sony
            short sizeF = 4;
            short sizeColor = 4;
            short sizeV3 = (short)(sizeF * 3);
            short sizeV4 = (short)(sizeF * 4);
            
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
                    VertexElementFormat.Color, VertexElementUsage.Color, 1 ),
                new VertexElement( sizeV3 + sizeV4 * 2 + sizeF + sizeColor * 2, 
                    VertexElementFormat.Single, VertexElementUsage.BlendIndices, 0 )
            };
#else
            var tThis = typeof(BitmapVertex);

            Elements = new VertexElement[] {
                new VertexElement( Marshal.OffsetOf(tThis, "Position").ToInt32(), 
                    VertexElementFormat.Vector3, VertexElementUsage.Position, 0 ),
                // TextureRegion
                new VertexElement( Marshal.OffsetOf(tThis, "TextureTopLeft").ToInt32(), 
                    VertexElementFormat.Vector4, VertexElementUsage.Position, 1 ),
                // ScaleOrigin
                new VertexElement( Marshal.OffsetOf(tThis, "Scale").ToInt32(), 
                    VertexElementFormat.Vector4, VertexElementUsage.Position, 2 ),
                new VertexElement( Marshal.OffsetOf(tThis, "Rotation").ToInt32(), 
                    VertexElementFormat.Single, VertexElementUsage.Position, 3 ),
                new VertexElement( Marshal.OffsetOf(tThis, "MultiplyColor").ToInt32(), 
                    VertexElementFormat.Color, VertexElementUsage.Color, 0 ),
                new VertexElement( Marshal.OffsetOf(tThis, "AddColor").ToInt32(), 
                    VertexElementFormat.Color, VertexElementUsage.Color, 1 ),
                new VertexElement( Marshal.OffsetOf(tThis, "Corner").ToInt32(), 
                    VertexElementFormat.Short2, VertexElementUsage.BlendIndices, 0 )
            };
#endif

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
                result = (x.Textures.HashCode > y.Textures.HashCode)
                ? 1
                : (
                    (x.Textures.HashCode < y.Textures.HashCode)
                    ? -1
                    : 0
                );
            return result;
        }
    }

    public sealed class BitmapDrawCallTextureComparer : IComparer<BitmapDrawCall> {
        public int Compare (BitmapDrawCall x, BitmapDrawCall y) {
            return (x.Textures.HashCode > y.Textures.HashCode)
                ? 1
                : (
                    (x.Textures.HashCode < y.Textures.HashCode)
                    ? -1
                    : 0
                );
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

                if (bblhs.IsReusable || bbrhs.IsReusable)
                    return false;

                if (bblhs.Material.MaterialID != bbrhs.Material.MaterialID)
                    return false;

                if (bblhs.Layer != bbrhs.Layer)
                    return false;

                if (bblhs.UseZBuffer != bbrhs.UseZBuffer)
                    return false;

                if (bblhs.SamplerState != bbrhs.SamplerState)
                    return false;

                if (bblhs.SamplerState2 != bbrhs.SamplerState2)
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
                rhs.IsCombined = true;

                return lhs;
            }
        }

        struct NativeBatch {
            public readonly ISoftwareBuffer SoftwareBuffer;
            public readonly TextureSet TextureSet;

            public readonly int LocalIndexOffset;
            public readonly int LocalVertexOffset;
            public readonly int VertexCount;

            public NativeBatch (ISoftwareBuffer softwareBuffer, TextureSet textureSet, int localIndexOffset, int localVertexOffset, int vertexCount) {
                SoftwareBuffer = softwareBuffer;
                TextureSet = textureSet;

                LocalIndexOffset = localIndexOffset;
                LocalVertexOffset = localVertexOffset;
                VertexCount = vertexCount;
            }
        }

        public SamplerState SamplerState;
        public SamplerState SamplerState2;
        public bool UseZBuffer = false;

        public static IComparer<BitmapDrawCall> DrawCallComparer = new BitmapDrawCallComparer();
        public static IComparer<BitmapDrawCall> DrawCallTextureComparer = new BitmapDrawCallTextureComparer();

        public const int NativeBatchSize = 1024;
        private const int NativeBatchCapacityLimit = 1024;

        private ArrayPoolAllocator<BitmapVertex> _Allocator;
        private static ListPool<NativeBatch> _NativePool = new ListPool<NativeBatch>(
            256, 16, 64, NativeBatchCapacityLimit
        );
        private UnorderedList<NativeBatch> _NativeBatches = null;
        private volatile bool _Prepared = false;

        private static readonly ushort[] QuadIndices = new ushort[] {
            0, 1, 2,
            0, 2, 3
        };
  
#if PSM
        private static readonly float[] FloatCorners = new [] { 0f, 1f, 2f, 3f };
        private PSMBufferGenerator<BitmapVertex> _BufferGenerator = null;
#else
        private XNABufferGenerator<BitmapVertex> _BufferGenerator = null;
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

        public static BitmapBatch New (IBatchContainer container, int layer, Material material, SamplerState samplerState = null, SamplerState samplerState2 = null, bool useZBuffer = false) {
            if (container == null)
                throw new ArgumentNullException("container");
            if (material == null)
                throw new ArgumentNullException("material");

            var result = container.RenderManager.AllocateBatch<BitmapBatch>();
            result.Initialize(container, layer, material, samplerState, samplerState2, useZBuffer);
            result.CaptureStack(0);
            return result;
        }

        public void Initialize (
            IBatchContainer container, int layer, 
            Material material, SamplerState samplerState = null, SamplerState samplerState2 = null, 
            bool useZBuffer = false, int? capacity = null
        ) {
            base.Initialize(container, layer, material, capacity);

            SamplerState = samplerState ?? SamplerState.LinearClamp;
            SamplerState2 = samplerState2 ?? SamplerState.LinearClamp;

            _Allocator = container.RenderManager.GetArrayAllocator<BitmapVertex>();

            UseZBuffer = useZBuffer;
        }

        public ArraySegment<BitmapDrawCall> ReserveSpace (int count) {
            return _DrawCalls.ReserveSpace(count);
        }

        public void Add (BitmapDrawCall item) {
            if (!item.IsValid)
                throw new InvalidOperationException("Invalid draw call");

            _DrawCalls.Add(ref item);
        }

        new public void Add (ref BitmapDrawCall item) {
            if (!item.IsValid)
                throw new InvalidOperationException("Invalid draw call");

            _DrawCalls.Add(ref item);
        }

        public void AddRange (BitmapDrawCall[] items) {
            AddRange(items, 0, items.Length, null);
        }

        public void AddRange (
            BitmapDrawCall[] items, int firstIndex, int count, 
            Vector2? offset = null, Color? multiplyColor = null, Color? addColor = null, float? sortKey = null, Vector2? scale = null
        ) {
            for (int i = 0; i < count; i++) {
                var item = items[i + firstIndex];
                if (!item.IsValid)
                    continue;

                if (scale.HasValue)
                    item.Position *= scale.Value;
                if (offset.HasValue)
                    item.Position += offset.Value;
                if (multiplyColor.HasValue)
                    item.MultiplyColor = multiplyColor.Value;
                if (addColor.HasValue)
                    item.AddColor = addColor.Value;
                if (sortKey.HasValue)
                    item.SortKey = sortKey.Value;
                if (scale.HasValue)
                    item.Scale = scale.Value;

                _DrawCalls.Add(ref item);
            }
        }

#if PSM
        private void FillOneSoftwareBuffer (BitmapDrawCall[] drawCalls, ref int drawCallsPrepared, int count) {
#else
        private unsafe void FillOneSoftwareBuffer (BitmapDrawCall[] drawCalls, ref int drawCallsPrepared, int count) {
#endif
            int totalVertCount = 0;
            int vertCount = 0, vertOffset = 0;
            int indexCount = 0, indexOffset = 0;
            int nativeBatchSizeLimit = NativeBatchSize * 4;
            int vertexWritePosition = 0, indexWritePosition = 0;

            TextureSet currentTextures = new TextureSet();
            BitmapVertex vertex = new BitmapVertex();

            var remainingDrawCalls = (count - drawCallsPrepared);
            var remainingVertices = remainingDrawCalls * 4;

            int nativeBatchSize = Math.Min(nativeBatchSizeLimit, remainingVertices);
            var softwareBuffer = _BufferGenerator.Allocate(nativeBatchSize, (nativeBatchSize / 4) * 6);

            ushort indexBase = (ushort)softwareBuffer.HardwareVertexOffset;

            float zBufferFactor = UseZBuffer ? 1.0f : 0.0f;

#if !PSM
            fixed (BitmapVertex* pVertices = &softwareBuffer.Vertices.Array[softwareBuffer.Vertices.Offset])
            fixed (ushort* pIndices = &softwareBuffer.Indices.Array[softwareBuffer.Indices.Offset])
#else
            var indexArray = softwareBuffer.Indices.Array;
            var indexArrayOffset = softwareBuffer.Indices.Offset;
#endif
                for (int i = drawCallsPrepared; i < count; i++) {
                    if (totalVertCount >= nativeBatchSizeLimit)
                        break;

                    var call = drawCalls[i];

#if PSM
                // HACK: PSM render targets have an inverted Y axis, so if the bitmap being drawn is a render target,
                //   flip it vertically.
                if (call.Textures.Texture1 is RenderTarget2D)
                    call.Mirror(false, true);
#endif

                    bool texturesEqual = call.Textures.Equals(ref currentTextures);

                    if (!texturesEqual) {
                        if (vertCount > 0) {
                            _NativeBatches.Add(new NativeBatch(
                                softwareBuffer, currentTextures,
                                indexOffset,
                                vertOffset,
                                vertCount
                            ));

                            indexOffset += indexCount;
                            vertOffset += vertCount;
                            indexCount = 0;
                            vertCount = 0;
                        }

                        currentTextures = call.Textures;
                    }

                    vertex.Position.X = call.Position.X;
                    vertex.Position.Y = call.Position.Y;
                    vertex.Position.Z = call.SortKey * zBufferFactor;
                    var tr = call.TextureRegion;
                    vertex.TextureTopLeft = tr.TopLeft;
                    vertex.TextureBottomRight = tr.BottomRight;
                    vertex.MultiplyColor = call.MultiplyColor;
                    vertex.AddColor = call.AddColor;
                    vertex.Scale = call.Scale;
                    vertex.Origin = call.Origin;
                    vertex.Rotation = call.Rotation;

#if !PSM
                    for (var j = 0; j < 6; j++)
                        pIndices[indexWritePosition + j] = (ushort)(indexBase + QuadIndices[j]);
#else
                    for (var j = 0; j < 6; j++)
                        indexArray[indexArrayOffset + indexWritePosition + j] = (ushort)(indexBase + QuadIndices[j]);
#endif

                    indexWritePosition += 6;

                    for (short j = 0; j < 4; j++) {
#if !PSM
                        vertex.Unused = vertex.Corner = j;
                        pVertices[vertexWritePosition + j] = vertex;
#else
                        vertex.Corner = FloatCorners[j];
                        buffers.Vertices.Array[buffers.Vertices.Offset + vertexWritePosition + j] = vertex;
#endif
                    }

                    vertexWritePosition += 4;
                    indexBase += 4;

                    totalVertCount += 4;
                    vertCount += 4;
                    indexCount += 6;

                    drawCallsPrepared += 1;
                }

            if (indexWritePosition > softwareBuffer.Indices.Count)
                throw new InvalidOperationException("Wrote too many indices");
            else if (vertexWritePosition > softwareBuffer.Vertices.Count)
                throw new InvalidOperationException("Wrote too many vertices");

            if (vertCount > 0)
                _NativeBatches.Add(new NativeBatch(
                    softwareBuffer, currentTextures,
                    indexOffset,
                    vertOffset,
                    vertCount
                ));
        }
        
        public override void Prepare () {
            if (_DrawCalls.Count == 0)
                return;

            if (_NativeBatches == null) {
                // If the batch contains a lot of draw calls, try to make sure we allocate our native batch from the large pool.
                int? nativeBatchCapacity = null;
                if (_DrawCalls.Count >= BatchCapacityLimit)
                    nativeBatchCapacity = Math.Min(NativeBatchCapacityLimit + 2, _DrawCalls.Count / 8);

                _NativeBatches = _NativePool.Allocate(nativeBatchCapacity);
            }

            var sorter =
                UseZBuffer ? DrawCallTextureComparer : DrawCallComparer;

            // FIXME: This sort takes a *TON* of time on large element sets with many identical elements.
            // Use a merge sort, perhaps? Good worst-case time.
#if PSM
            _DrawCalls.Timsort(sorter);
#else
            _DrawCalls.Sort(sorter);
#endif

            var count = _DrawCalls.Count;

#if PSM                
            _BufferGenerator = Container.RenderManager.GetBufferGenerator<PSMBufferGenerator<BitmapVertex>>();
#else
            _BufferGenerator = Container.RenderManager.GetBufferGenerator<XNABufferGenerator<BitmapVertex>>();
#endif

            var _drawCalls = _DrawCalls.GetBuffer();
            int drawCallsPrepared = 0;

            while (drawCallsPrepared < count)
                FillOneSoftwareBuffer(_drawCalls, ref drawCallsPrepared, count);

            _Prepared = true;
        }
            
        public override void Issue (DeviceManager manager) {
            if (IsCombined)
                throw new InvalidOperationException("Batch was combined into another batch");

            if (_DrawCalls.Count == 0)
                return;

            if (_Prepared == false)
                throw new InvalidOperationException("Not prepared");

            if (_BufferGenerator == null)
                throw new InvalidOperationException("Already issued");

            var device = manager.Device;

            IHardwareBuffer previousHardwareBuffer = null;

            // if (RenderTrace.EnableTracing)
            //    RenderTrace.ImmediateMarker("BitmapBatch.Issue(layer={0}, count={1})", Layer, _DrawCalls.Count);

            using (manager.ApplyMaterial(Material)) {
                TextureSet currentTexture = new TextureSet();
                var paramSize = manager.CurrentParameters.BitmapTextureSize;
                var paramHalfTexel = manager.CurrentParameters.HalfTexel;

                var em = (IEffectMaterial)manager.CurrentMaterial;
                var paramTexture1 = em.Effect.Parameters["BitmapTexture"];
                var paramTexture2 = em.Effect.Parameters["SecondTexture"];

                foreach (var nb in _NativeBatches) {
                    if (nb.TextureSet != currentTexture) {
                        currentTexture = nb.TextureSet;
                        var tex1 = currentTexture.Texture1;

                        device.SamplerStates[0] = SamplerState;
                        device.SamplerStates[1] = SamplerState2;

                        paramTexture1.SetValue(tex1);
                        if (paramTexture2 != null)
                            paramTexture2.SetValue(currentTexture.Texture2);

                        var vSize = new Vector2(tex1.Width, tex1.Height);
                        paramSize.SetValue(vSize);
                        paramHalfTexel.SetValue(new Vector2(1.0f / vSize.X, 1.0f / vSize.Y) * 0.5f);

                        manager.CurrentMaterial.Flush();
                    }

                    if (UseZBuffer) {
                        var dss = device.DepthStencilState;
                        if (dss.DepthBufferEnable == false)
                            throw new InvalidOperationException("UseZBuffer set to true but depth buffer is disabled");
                    }

                    var swb = nb.SoftwareBuffer;
                    var hwb = swb.HardwareBuffer;
                    if (previousHardwareBuffer != hwb) {
                        if (previousHardwareBuffer != null)
                            previousHardwareBuffer.SetInactive(device);

                        hwb.SetActive(device);
                        previousHardwareBuffer = hwb;
                    }
      
#if PSM
                    // MonoGame and PSM are both retarded.
                    device.ApplyState(false);
                    device.Textures.SetTextures(device);
                    device.SamplerStates.SetSamplers(device);
                        
                    device._graphics.DrawArrays(
                        Sce.PlayStation.Core.Graphics.DrawMode.Triangles,
                        nb.IndexOffset, (nb.VertexCount / 4) * 6
                    );
#else
                    device.DrawIndexedPrimitives(
                        PrimitiveType.TriangleList, 0, 
                        swb.HardwareVertexOffset + nb.LocalVertexOffset, 
                        nb.VertexCount, 
                        swb.HardwareIndexOffset + nb.LocalIndexOffset,
                        nb.VertexCount / 2
                    );
#endif
                }

                if (previousHardwareBuffer != null)
                    previousHardwareBuffer.SetInactive(device);
            }

            _BufferGenerator = null;

            base.Issue(manager);
        }

        protected override void OnReleaseResources () {
            _Prepared = false;
            _BufferGenerator = null;

            _NativePool.Release(ref _NativeBatches);

            base.OnReleaseResources();
        }

        public void RemoveRange (int index, int count) {
            _DrawCalls.RemoveRange(index, count);
        }
    }

    public struct TextureSet {
        public readonly Texture2D Texture1, Texture2;
        public readonly int HashCode;

        public TextureSet (Texture2D texture1) {
            Texture1 = texture1;
            Texture2 = null;
            HashCode = texture1.GetHashCode();
        }

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

        public static implicit operator TextureSet (Texture2D texture1) {
            return new TextureSet(texture1);
        }

        public bool Equals (ref TextureSet rhs) {
            return (HashCode == rhs.HashCode) && (Texture1 == rhs.Texture1) && (Texture2 == rhs.Texture2);
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
            return (lhs.HashCode == rhs.HashCode) && (lhs.Texture1 == rhs.Texture1) && (lhs.Texture2 == rhs.Texture2);
        }

        public static bool operator != (TextureSet lhs, TextureSet rhs) {
            return (lhs.Texture1 != rhs.Texture1) || (lhs.Texture2 != rhs.Texture2);
        }

        public override int GetHashCode () {
            return HashCode;
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

        public BitmapDrawCall (Texture2D texture, Vector2 position)
            : this(texture, position, texture.Bounds()) {
        }

        public BitmapDrawCall (Texture2D texture, Vector2 position, Color color)
            : this(texture, position, texture.Bounds(), color) {
        }

        public BitmapDrawCall (Texture2D texture, Vector2 position, Bounds textureRegion)
            : this(texture, position, textureRegion, Color.White) {
        }

        public BitmapDrawCall (Texture2D texture, Vector2 position, Bounds textureRegion, Color color)
            : this(texture, position, textureRegion, color, Vector2.One) {
        }

        public BitmapDrawCall (Texture2D texture, Vector2 position, float scale)
            : this(texture, position, texture.Bounds(), Color.White, new Vector2(scale, scale)) {
        }

        public BitmapDrawCall (Texture2D texture, Vector2 position, Vector2 scale)
            : this(texture, position, texture.Bounds(), Color.White, scale) {
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
            if (texture == null)
                throw new ArgumentNullException("texture");
            else if (texture.IsDisposed)
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

        public void AdjustOrigin (Vector2 newOrigin) {
            var newPosition = Position;

            var textureSize = new Vector2(Texture.Width, Texture.Height) * TextureRegion.Size;
            newPosition += ((newOrigin - Origin) * textureSize * Scale);

            Position = newPosition;
            Origin = newOrigin;
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
                return ((Textures.Texture1 != null) && !Textures.Texture1.IsDisposed);
            }
        }
    }
}