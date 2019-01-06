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
            // var result = FastMath.CompareF(x.SortKey.Order, y.SortKey.Order);
            var delta = x.SortKey.Order - y.SortKey.Order;
            if (delta == 0)
                return (x.Textures.HashCode - y.Textures.HashCode);

            return (delta < 0) ? -1 : 1;
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

    public class BitmapBatch : ListBatch<BitmapDrawCall>, IBitmapBatch {
        public static readonly SamplerState DefaultSamplerState = SamplerState.LinearClamp;

        public struct Reservation {
            public readonly BitmapBatch Batch;
            public readonly int ID;

            public readonly BitmapDrawCall[] Array;
            public readonly int Offset;
            public int Count;
            public readonly StackTrace Stack;

            internal Reservation (BitmapBatch batch, BitmapDrawCall[] array, int offset, int count) {
                Batch = batch;
                ID = ++batch.LastReservationID;
                Array = array;
                Offset = offset;
                Count = count;
                if (CaptureStackTraces)
                    Stack = new StackTrace(2, true);
                else
                    Stack = null;
            }

            public void Shrink (int newCount) {
                if (ID != Batch.LastReservationID)
                    throw new InvalidOperationException("You can't shrink a reservation after another one has been created");
                if (newCount > Count)
                    throw new ArgumentException("Can't grow using shrink, silly", "newCount");
                if (newCount == Count)
                    return;

                Batch.RemoveRange(Offset + newCount, Count - newCount);
                Count = newCount;
            }
        }

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
                var bl = (BitmapBatch)lhs;
                var br = (BitmapBatch)rhs;

                using (var b = br._DrawCalls.GetBuffer(false)) {
                    var drawCallsRhsBuffer = b.Data;

                    for (int i = 0, l = b.Count; i < l; i++) {
                        if (!drawCallsRhsBuffer[i].IsValid)
                            throw new Exception("Invalid draw call in batch");

                        bl._DrawCalls.Add(ref drawCallsRhsBuffer[i]);
                    }
                }

                br._DrawCalls.Clear();
                rhs.State.IsCombined = true;
                if (CaptureStackTraces) {
                    if (lhs.BatchesCombinedIntoThisOne == null)
                        lhs.BatchesCombinedIntoThisOne = new UnorderedList<Batch>();

                    lhs.BatchesCombinedIntoThisOne.Add(rhs);
                }

                return lhs;
            }
        }

        struct NativeBatch {
            public readonly ISoftwareBuffer SoftwareBuffer;
            public readonly TextureSet TextureSet;

            public readonly Vector2 Texture1Size, Texture1HalfTexel;
            public readonly Vector2 Texture2Size, Texture2HalfTexel;

            public readonly int LocalVertexOffset;
            public readonly int VertexCount;

            public NativeBatch (
                ISoftwareBuffer softwareBuffer, TextureSet textureSet, 
                int localVertexOffset, int vertexCount
            ) {
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

        /// <summary>
        /// Specifies a declarative sorter that overrides the default sorting order for draw calls.
        /// Note that draw calls are still sorted by texture in the event that you provide no ordering
        ///  for a given pair of calls. 
        /// </summary>
        public Sorter<BitmapDrawCall> Sorter {
            get; set;
        }

        public bool DisableSorting = false;

        public SamplerState SamplerState;
        public SamplerState SamplerState2;

        /// <summary>
        /// If set and no declarative sorter is provided, draw calls will only be sorted by texture,
        ///  and the z-buffer will be relied on to provide sorting of individual draw calls.
        /// </summary>
        public bool UseZBuffer = false;

        internal static BitmapDrawCallOrderAndTextureComparer DrawCallComparer = new BitmapDrawCallOrderAndTextureComparer();
        internal static BitmapDrawCallTextureComparer DrawCallTextureComparer = new BitmapDrawCallTextureComparer();

        internal static ThreadLocal<BitmapDrawCallSorterComparer> DrawCallSorterComparer = new ThreadLocal<BitmapDrawCallSorterComparer>(
            () => new BitmapDrawCallSorterComparer()
        );

        public const int NativeBatchSize = 1024;
        private const int NativeBatchCapacityLimit = 1024;

        private int LastReservationID = 0;

        private static ListPool<NativeBatch> _NativePool = new ListPool<NativeBatch>(
            320, 4, 64, 256, 1024
        );
        private DenseList<NativeBatch> _NativeBatches;

        private enum PrepareState : int {
            Invalid,
            NotPrepared,
            Preparing,
            Prepared,
            Issuing,
            Issued
        }

        private volatile int _State = (int)PrepareState.Invalid;

        private static ThreadLocal<VertexBufferBinding[]> _ScratchBindingArray = 
            new ThreadLocal<VertexBufferBinding[]>(() => new VertexBufferBinding[2]);
        private static ThreadLocal<int[]> _SortIndexArray = new ThreadLocal<int[]>();
  
        private BufferGenerator<BitmapVertex> _BufferGenerator = null;
        private BufferGenerator<CornerVertex>.SoftwareBuffer _CornerBuffer = null;

        private UnorderedList<Reservation> RangeReservations = null;

        static BitmapBatch () {
            BatchCombiner.Combiners.Add(new BitmapBatchCombiner());
        }

        new public static void AdjustPoolCapacities (
            int? smallItemSizeLimit, int? largeItemSizeLimit,
            int? smallPoolCapacity, int? largePoolCapacity
        ) {
            ListBatch<BitmapDrawCall>.AdjustPoolCapacities(smallItemSizeLimit, largeItemSizeLimit, smallPoolCapacity, largePoolCapacity);

            _NativePool.SmallPoolMaxItemSize = smallItemSizeLimit.GetValueOrDefault(_NativePool.SmallPoolMaxItemSize);
            _NativePool.LargePoolMaxItemSize = largeItemSizeLimit.GetValueOrDefault(_NativePool.LargePoolMaxItemSize);
            _NativePool.SmallPoolCapacity = smallPoolCapacity.GetValueOrDefault(_NativePool.SmallPoolCapacity);
            _NativePool.LargePoolCapacity = largePoolCapacity.GetValueOrDefault(_NativePool.LargePoolCapacity);
        }

        public static BitmapBatch New (IBatchContainer container, int layer, Material material, SamplerState samplerState = null, SamplerState samplerState2 = null, bool useZBuffer = false) {
            if (container == null)
                throw new ArgumentNullException("container");
            if (material == null)
                throw new ArgumentNullException("material");
            if (material.Effect == null)
                throw new ArgumentNullException("material.Effect");

            var result = container.RenderManager.AllocateBatch<BitmapBatch>();
            result.Initialize(container, layer, material, samplerState, samplerState2 ?? samplerState, useZBuffer);
            result.CaptureStack(0);
            return result;
        }

        public void Initialize (
            IBatchContainer container, int layer, 
            Material material, SamplerState samplerState = null, SamplerState samplerState2 = null, 
            bool useZBuffer = false, int? capacity = null
        ) {
            base.Initialize(container, layer, material, true, capacity);

            if (RangeReservations != null)
                RangeReservations.Clear();

            SamplerState = samplerState ?? BitmapBatch.DefaultSamplerState;
            SamplerState2 = samplerState2 ?? samplerState ?? BitmapBatch.DefaultSamplerState;

            UseZBuffer = useZBuffer;

            var prior = (PrepareState)Interlocked.Exchange(ref _State, (int)PrepareState.NotPrepared);
            if ((prior == PrepareState.Issuing) || (prior == PrepareState.Preparing))
                throw new ThreadStateException("This batch is currently in use");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void StateTransition (PrepareState from, PrepareState to) {
            var prior = (PrepareState)Interlocked.Exchange(ref _State, (int)to);
            if (prior != from)
                throw new ThreadStateException(string.Format(
                    "Expected to transition this batch from {0} to {1}, but state was {2}",
                    from, to, prior
                ));
        }

        public Reservation ReserveSpace (int count) {
            var range = _DrawCalls.ReserveSpace(count);
            var reservation = new Reservation(
                this, range.Array, range.Offset, range.Count
            );

            if (CaptureStackTraces) {
                if (RangeReservations == null)
                    RangeReservations = new UnorderedList<Reservation>();

                RangeReservations.Add(reservation);
            }

            return reservation;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add (BitmapDrawCall item) {
            if (!item.IsValid)
                throw new InvalidOperationException("Invalid draw call");

            _DrawCalls.Add(ref item);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        new public void Add (ref BitmapDrawCall item) {
            if (!item.IsValid)
                throw new InvalidOperationException("Invalid draw call");

            _DrawCalls.Add(ref item);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        new public void Add (ref BitmapDrawCall item, Material material) {
            if (!item.IsValid)
                throw new InvalidOperationException("Invalid draw call");
            if (material != null)
                throw new ArgumentException("Must be null because this is not a MultimaterialBitmapBatch", nameof(material));

            _DrawCalls.Add(ref item);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddRange (ArraySegment<BitmapDrawCall> items) {
            _DrawCalls.AddRange(items);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddRange (BitmapDrawCall[] items, int firstIndex, int count) {
            _DrawCalls.AddRange(items, firstIndex, count);
        }

        public void AddRange (
            BitmapDrawCall[] items, int firstIndex, int count, 
            Vector2? offset = null, Color? multiplyColor = null, Color? addColor = null, 
            DrawCallSortKey? sortKey = null, Vector2? scale = null, Material material = null
        ) {
            if (material != null)
                throw new ArgumentException("Must be null because this is not a MultimaterialBitmapBatch", nameof(material));

            if (
                (offset == null) && (multiplyColor == null) && (addColor == null) &&
                (sortKey == null) && (scale == null)
            ) {
                AddRange(items, firstIndex, count);
                return;
            }

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
                    item.Scale *= scale.Value;

                _DrawCalls.Add(ref item);
            }
        }

        private unsafe void FillOneSoftwareBuffer (
            int[] indices, ref int drawCallsPrepared, int count
        ) {
            int totalVertCount = 0;
            int vertCount = 0, vertOffset = 0;
            int nativeBatchSizeLimit = NativeBatchSize;
            int vertexWritePosition = 0;

            TextureSet currentTextures = new TextureSet();

            var remainingDrawCalls = (count - drawCallsPrepared);
            var remainingVertices = remainingDrawCalls;

            int nativeBatchSize = Math.Min(nativeBatchSizeLimit, remainingVertices);
            var softwareBuffer = _BufferGenerator.Allocate(nativeBatchSize, 1);

            float zBufferFactor = UseZBuffer ? 1.0f : 0.0f;

            fixed (BitmapVertex* pVertices = &softwareBuffer.Vertices.Array[softwareBuffer.Vertices.Offset]) {
                for (int i = drawCallsPrepared; i < count; i++) {
                    if (totalVertCount >= nativeBatchSizeLimit)
                        break;

                    BitmapDrawCall call;
                    if (indices != null) {
                        var callIndex = indices[i];
                        if (!_DrawCalls.TryGetItem(callIndex, out call))
                            break;
                    } else {
                        if (!_DrawCalls.TryGetItem(i, out call))
                            break;
                    }

                    bool texturesEqual = call.Textures.Equals(ref currentTextures);

                    if (!texturesEqual) {
                        if (vertCount > 0) {
                            if ((currentTextures.Texture1 == null) || currentTextures.Texture1.IsDisposed)
                                throw new InvalidDataException("Invalid draw call(s)");

                            _NativeBatches.Add(new NativeBatch(
                                softwareBuffer, currentTextures,
                                vertOffset,
                                vertCount
                            ));

                            vertOffset += vertCount;
                            vertCount = 0;
                        }

                        currentTextures = call.Textures;
                    }

                    pVertices[vertexWritePosition] = new BitmapVertex {
                        Position = {
                            X = call.Position.X,
                            Y = call.Position.Y,
                            Z = call.SortKey.Order * zBufferFactor
                        },
                        Texture1Region = call.TextureRegion.ToVector4(),
                        Texture2Region = call.TextureRegion2.GetValueOrDefault(call.TextureRegion).ToVector4(),
                        MultiplyColor = call.MultiplyColor,
                        AddColor = call.AddColor,
                        Scale = call.Scale,
                        Origin = call.Origin,
                        Rotation = call.Rotation
                    };

                    vertexWritePosition += 1;
                    totalVertCount += 1;
                    vertCount += 1;

                    drawCallsPrepared += 1;
                }
            }

            if (vertexWritePosition > softwareBuffer.Vertices.Count)
                throw new InvalidOperationException("Wrote too many vertices");

            if (vertCount > 0) {
                if ((currentTextures.Texture1 == null) || currentTextures.Texture1.IsDisposed)
                    throw new InvalidDataException("Invalid draw call(s)");

                _NativeBatches.Add(new NativeBatch(
                    softwareBuffer, currentTextures,
                    vertOffset,
                    vertCount
                ));
            }
        }

        private int[] GetIndexArray (int minimumSize) {
            const int rounding = 4096;
            var size = ((minimumSize + (rounding - 1)) / rounding) * rounding + 16;
            var array = _SortIndexArray.Value;
            if ((array == null) || (array.Length < size))
                _SortIndexArray.Value = array = new int[size];

            return array;
        }
        
        protected override void Prepare (PrepareManager manager) {
            var prior = (PrepareState)Interlocked.Exchange(ref _State, (int)PrepareState.Preparing);
            if ((prior == PrepareState.Issuing) || (prior == PrepareState.Preparing))
                throw new ThreadStateException("This batch is currently in use");
            else if (prior == PrepareState.Invalid)
                throw new ThreadStateException("This batch is not valid");

            if (_DrawCalls.Count == 0)
                return;

            Squared.Render.NativeBatch.RecordPrimitives(_DrawCalls.Count * 2);

            // If the batch contains a lot of draw calls, try to make sure we allocate our native batch from the large pool.
            int? nativeBatchCapacity = null;
            if (_DrawCalls.Count >= BatchCapacityLimit)
                nativeBatchCapacity = Math.Min(NativeBatchCapacityLimit + 2, _DrawCalls.Count / 8);

            _NativeBatches.Clear();
            _NativeBatches.ListPool = _NativePool;
            _NativeBatches.ListCapacity = nativeBatchCapacity;

            var count = _DrawCalls.Count;
            int[] indexArray = null;

#if USE_INDEXED_SORT
            if (!DisableSorting) {
                indexArray = GetIndexArray(count);
                for (int i = 0; i < count; i++)
                    indexArray[i] = i;
            }
#endif

            if (DisableSorting) {
            } else if (Sorter != null) {
                var comparer = DrawCallSorterComparer.Value;
                comparer.Comparer = Sorter.GetComparer(true);
                _DrawCalls.Sort(comparer, indexArray);
            } else if (UseZBuffer) {
                _DrawCalls.Sort(DrawCallTextureComparer, indexArray);
            } else {
                _DrawCalls.Sort(DrawCallComparer, indexArray);
            }

            _BufferGenerator = Container.RenderManager.GetBufferGenerator<BufferGenerator<BitmapVertex>>();

            _CornerBuffer = QuadUtils.CreateCornerBuffer(Container);

            int drawCallsPrepared = 0;
            while (drawCallsPrepared < count)
                FillOneSoftwareBuffer(indexArray, ref drawCallsPrepared, count);

            StateTransition(PrepareState.Preparing, PrepareState.Prepared);
        }
            
        public override void Issue (DeviceManager manager) {
            if (_DrawCalls.Count == 0)
                return;

            StateTransition(PrepareState.Prepared, PrepareState.Issuing);

            if (State.IsCombined)
                throw new InvalidOperationException("Batch was combined into another batch");

            if (_BufferGenerator == null)
                throw new InvalidOperationException("Already issued");

            var device = manager.Device;

            IHardwareBuffer previousHardwareBuffer = null;

            // if (RenderTrace.EnableTracing)
            //    RenderTrace.ImmediateMarker("BitmapBatch.Issue(layer={0}, count={1})", Layer, _DrawCalls.Count);

            VertexBuffer vb, cornerVb;
            DynamicIndexBuffer ib, cornerIb;

            var cornerHwb = _CornerBuffer.HardwareBuffer;
            try {
                cornerHwb.SetActive();
                cornerHwb.GetBuffers(out cornerVb, out cornerIb);
                if (device.Indices != cornerIb)
                    device.Indices = cornerIb;

                var scratchBindings = _ScratchBindingArray.Value;

                var previousSS1 = device.SamplerStates[0];
                var previousSS2 = device.SamplerStates[1];

                manager.ApplyMaterial(Material);
                {
                    TextureSet currentTexture = new TextureSet();
                    var paramSize = manager.CurrentParameters.BitmapTextureSize;
                    var paramHalfTexel = manager.CurrentParameters.HalfTexel;
                    var paramSize2 = manager.CurrentParameters.BitmapTextureSize2;
                    var paramHalfTexel2 = manager.CurrentParameters.HalfTexel2;

                    var m = manager.CurrentMaterial;
                    var paramTexture1 = m.Effect.Parameters["BitmapTexture"];
                    var paramTexture2 = m.Effect.Parameters["SecondTexture"];

                    paramTexture1.SetValue((Texture2D)null);
                    paramTexture2.SetValue((Texture2D)null);

                    for (int nc = _NativeBatches.Count, n = 0; n < nc; n++) {
                        NativeBatch nb;
                        if (!_NativeBatches.TryGetItem(n, out nb))
                            break;

                        if (nb.TextureSet != currentTexture) {
                            currentTexture = nb.TextureSet;
                            var tex1 = currentTexture.Texture1;

                            // FIXME: What is going wrong with XNA here?
                            paramTexture1.SetValue((Texture2D)null);
                            paramTexture1.SetValue(tex1);
                            if (paramTexture2 != null) {
                                paramTexture2.SetValue((Texture2D)null);
                                paramTexture2.SetValue(currentTexture.Texture2);
                            }

                            paramSize.SetValue(nb.Texture1Size);
                            paramHalfTexel.SetValue(nb.Texture1HalfTexel);

                            if ((paramTexture2 != null) && (currentTexture.Texture2 != null)) {
                                paramSize2.SetValue(nb.Texture2Size);
                                paramHalfTexel2.SetValue(nb.Texture2HalfTexel);
                            }

                            manager.CurrentMaterial.Flush();

                            device.SamplerStates[0] = SamplerState;
                            device.SamplerStates[1] = SamplerState2;
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
                                previousHardwareBuffer.SetInactive();

                            hwb.SetActive();
                            previousHardwareBuffer = hwb;
                        }

                        hwb.GetBuffers(out vb, out ib);

                        scratchBindings[0] = cornerVb;
                        scratchBindings[1] = new VertexBufferBinding(vb, swb.HardwareVertexOffset + nb.LocalVertexOffset, 1);

                        device.SetVertexBuffers(scratchBindings);
                        device.DrawInstancedPrimitives(
                            PrimitiveType.TriangleList, 
                            0, _CornerBuffer.HardwareVertexOffset, 4, 
                            _CornerBuffer.HardwareIndexOffset, 2, 
                            nb.VertexCount
                        );
                    }

                    if (previousHardwareBuffer != null)
                        previousHardwareBuffer.SetInactive();

                    paramTexture1.SetValue((Texture2D)null);
                    paramTexture2.SetValue((Texture2D)null);
                }

                device.SamplerStates[0] = previousSS1;
                device.SamplerStates[1] = previousSS2;
            } finally {
                cornerHwb.TrySetInactive();
                if (previousHardwareBuffer != null)
                    previousHardwareBuffer.TrySetInactive();
            }

            _BufferGenerator = null;
            _CornerBuffer = null;

            base.Issue(manager);

            StateTransition(PrepareState.Issuing, PrepareState.Issued);
        }

        protected override void OnReleaseResources () {
            _State = (int)PrepareState.Invalid;
            _BufferGenerator = null;
            _CornerBuffer = null;

            _NativeBatches.Dispose();

            base.OnReleaseResources();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveRange (int index, int count) {
            _DrawCalls.RemoveRange(index, count);
        }
    }

    public struct MaterialBitmapDrawCall {
        public BitmapDrawCall DrawCall;
        public Material Material;
        public SamplerState SamplerState1, SamplerState2;

        public MaterialBitmapDrawCall (
            ref BitmapDrawCall drawCall, Material material, 
            SamplerState samplerState1, SamplerState samplerState2
        ) {
            DrawCall = drawCall;
            Material = material;
            SamplerState1 = samplerState1;
            SamplerState2 = samplerState2;
        }
    }

    public class MultimaterialBitmapBatch : ListBatch<MaterialBitmapDrawCall>, IBitmapBatch {
        internal class MultimaterialComparer : IRefComparer<MaterialBitmapDrawCall>, IComparer<MaterialBitmapDrawCall> {
            public IRefComparer<BitmapDrawCall> DrawCallComparer;
            public static readonly ReferenceComparer<Material> MaterialComparer = new ReferenceComparer<Material>();

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public int Compare (MaterialBitmapDrawCall lhs, MaterialBitmapDrawCall rhs) {
                return Compare(ref lhs, ref rhs);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public int Compare (ref MaterialBitmapDrawCall lhs, ref MaterialBitmapDrawCall rhs) {
                var result = DrawCallComparer.Compare(ref lhs.DrawCall, ref rhs.DrawCall);

                if (result == 0)
                    result = lhs.Material.MaterialID.CompareTo(rhs.Material.MaterialID);

                if (result == 0)
                    result = lhs.DrawCall.Textures.HashCode.CompareTo(rhs.DrawCall.Textures.HashCode);

                return result;
            }
        }

        internal static ThreadLocal<MultimaterialComparer> Comparer = new ThreadLocal<MultimaterialComparer>(
            () => new MultimaterialComparer()
        );

        /// <summary>
        /// Specifies a declarative sorter that overrides the default sorting order for draw calls.
        /// Note that draw calls are still sorted by texture in the event that you provide no ordering
        ///  for a given pair of calls.
        /// </summary>
        public Sorter<BitmapDrawCall> Sorter {
            get; set;
        }

        /// <summary>
        /// If set and no declarative sorter is provided, draw calls will only be sorted by texture,
        ///  and the z-buffer will be relied on to provide sorting of individual draw calls.
        /// </summary>
        public bool UseZBuffer = false;

        private BatchGroup _Group;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add (BitmapDrawCall item) {
            Add(item, Material, null, null);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        new public void Add (ref BitmapDrawCall item) {
            Add(ref item, Material, null, null);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        new public void AddRange (ArraySegment<BitmapDrawCall> items) {
            for (int i = 0; i < items.Count; i++)
                Add(ref items.Array[i + items.Offset]);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add (BitmapDrawCall item, Material material, SamplerState samplerState1 = null, SamplerState samplerState2 = null) {
            if (!item.IsValid)
                throw new InvalidOperationException("Invalid draw call");

            var dcm = material ?? Material;
            if ((dcm == null) || (dcm.Effect == null))
                throw new InvalidOperationException("Draw call has no material and this batch has no material");

            var dc = new MaterialBitmapDrawCall(ref item, dcm, samplerState1, samplerState2);
            _DrawCalls.Add(ref dc);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add (ref BitmapDrawCall item, Material material, SamplerState samplerState1 = null, SamplerState samplerState2 = null) {
            if (!item.IsValid)
                throw new InvalidOperationException("Invalid draw call");

            var dcm = material ?? Material;
            if ((dcm == null) || (dcm.Effect == null))
                throw new InvalidOperationException("Draw call has no material and this batch has no material");

            var dc = new MaterialBitmapDrawCall(ref item, dcm, samplerState1, samplerState2);
            _DrawCalls.Add(ref dc);
        }

        public void AddRange (
            BitmapDrawCall[] items, int firstIndex, int count, 
            Vector2? offset = null, Color? multiplyColor = null, Color? addColor = null, 
            DrawCallSortKey? sortKey = null, Vector2? scale = null, Material customMaterial = null,
            SamplerState samplerState1 = null, SamplerState samplerState2 = null
        ) {
            MaterialBitmapDrawCall dc;
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
                    item.Scale *= scale.Value;

                var dcm = customMaterial ?? Material;
                if ((dcm == null) || (dcm.Effect == null))
                    throw new InvalidOperationException("Draw call has no material and this batch has no material");

                dc.DrawCall = item;
                dc.Material = dcm;
                dc.SamplerState1 = samplerState1;
                dc.SamplerState2 = samplerState2;

                _DrawCalls.Add(ref dc);
            }
        }

        public static MultimaterialBitmapBatch New (
            IBatchContainer container, int layer, Material material, 
            bool useZBuffer = false, Sorter<BitmapDrawCall> sorter = null
        ) {
            if (container == null)
                throw new ArgumentNullException("container");

            var result = container.RenderManager.AllocateBatch<MultimaterialBitmapBatch>();
            result.Initialize(container, layer, material, sorter, useZBuffer);
            result.CaptureStack(0);            
            return result;
        }

        new public void Initialize (
            IBatchContainer container, int layer,             
            Material material, Sorter<BitmapDrawCall> sorter,
            bool useZBuffer, int? capacity = null
        ) {
            base.Initialize(container, layer, material, true, capacity);

            Sorter = sorter;
            UseZBuffer = useZBuffer;

            _DrawCalls.Clear();

            _Group = container.RenderManager.AllocateBatch<BatchGroup>();
            _Group.Initialize(container, layer, null, null, null, false);
            _Group.CaptureStack(0);
        }

        public override void Prepare (PrepareContext context) {
            using (var b = _DrawCalls.GetBuffer(true)) {
                var drawCalls = b.Data;
                var count = b.Count;

                var comparer = Comparer.Value;
                if (Sorter != null)
                    comparer.DrawCallComparer = Sorter.GetComparer(true);
                else
                    comparer.DrawCallComparer = BitmapBatch.DrawCallComparer;

                Sort.FastCLRSortRef(
                    drawCalls, comparer, 0, count
                );

                BitmapBatch currentBatch = null;
                int layerIndex = 0;

                for (var i = 0; i < count; i++) {
                    var dc = drawCalls[i];
                    var material = dc.Material;
                    if (material == null)
                        throw new Exception("Missing material for draw call");

                    var ss1 = dc.SamplerState1 ?? BitmapBatch.DefaultSamplerState;
                    var ss2 = dc.SamplerState2 ?? BitmapBatch.DefaultSamplerState;

                    if (
                        (currentBatch == null) ||
                        (currentBatch.Material != material) ||
                        (currentBatch.SamplerState != ss1) ||
                        (currentBatch.SamplerState2 != ss2)
                    ) {
                        if (currentBatch != null)
                            currentBatch.Dispose();

                        currentBatch = BitmapBatch.New(
                            _Group, layerIndex++, material, dc.SamplerState1, dc.SamplerState2, UseZBuffer
                        );

                        // We've already sorted the draw calls.
                        currentBatch.DisableSorting = true;
                    }

                    currentBatch.Add(dc.DrawCall);
                }

                if (currentBatch != null)
                    currentBatch.Dispose();

                _Group.Dispose();
                context.Prepare(_Group);

                OnPrepareDone();
            }
        }

        public override void Issue (DeviceManager manager) {
            manager.ApplyMaterial(Material);
            _Group.Issue(manager);

            base.Issue(manager);
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