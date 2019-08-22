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
    public class BitmapBatch : BitmapBatchBase<BitmapDrawCall>, IBitmapBatch {
        public static readonly SamplerState DefaultSamplerState = SamplerState.LinearClamp;

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

                if (bblhs.ZBufferOnlySorting != bbrhs.ZBufferOnlySorting)
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

        /// <summary>
        /// Specifies a declarative sorter that overrides the default sorting order for draw calls.
        /// Note that draw calls are still sorted by texture in the event that you provide no ordering
        ///  for a given pair of calls. 
        /// </summary>
        public Sorter<BitmapDrawCall> Sorter {
            get; set;
        }

        /// <summary>
        /// Performs no sorting on the draw calls. This usually will be slower but hey, you're the boss.
        /// </summary>
        public bool DisableSorting = false;
        /// <summary>
        /// A lighter-touch version of DisableSorting: Ignores the SortKey field for better performance. Texture sorting still happens.
        /// </summary>
        public bool DisableSortKeys = false;

        public SamplerState SamplerState;
        public SamplerState SamplerState2;

        internal static BitmapDrawCallOrderAndTextureComparer DrawCallComparer = new BitmapDrawCallOrderAndTextureComparer();
        internal static BitmapDrawCallTextureComparer DrawCallTextureComparer = new BitmapDrawCallTextureComparer();

        internal static ThreadLocal<BitmapDrawCallSorterComparer> DrawCallSorterComparer = new ThreadLocal<BitmapDrawCallSorterComparer>(
            () => new BitmapDrawCallSorterComparer()
        );

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

        public static BitmapBatch New (IBatchContainer container, int layer, Material material, SamplerState samplerState = null, SamplerState samplerState2 = null, bool useZBuffer = false, int? capacity = null) {
            if (container == null)
                throw new ArgumentNullException("container");
            if (material == null)
                throw new ArgumentNullException("material");
            if (material.Effect == null)
                throw new ArgumentNullException("material.Effect");

            var result = container.RenderManager.AllocateBatch<BitmapBatch>();
            result.Initialize(container, layer, material, samplerState, samplerState2 ?? samplerState, useZBuffer, capacity: capacity);
            result.CaptureStack(0);
            return result;
        }

        public void Initialize (
            IBatchContainer container, int layer, 
            Material material, SamplerState samplerState = null, SamplerState samplerState2 = null, 
            bool useZBuffer = false, bool zBufferOnlySorting = false, int? capacity = null
        ) {
            base.Initialize(container, layer, material, true, capacity);

            if (RangeReservations != null)
                RangeReservations.Clear();

            SamplerState = samplerState ?? BitmapBatch.DefaultSamplerState;
            SamplerState2 = samplerState2 ?? samplerState ?? BitmapBatch.DefaultSamplerState;

            UseZBuffer = useZBuffer;
            ZBufferOnlySorting = zBufferOnlySorting;

            var rm = container.RenderManager;
            _DrawCalls.ListPool.ThreadGroup = rm.ThreadGroup;
            rm.AddDrainRequiredListPool(_DrawCalls.ListPool);

            var prior = (BitmapBatchPrepareState)Interlocked.Exchange(ref _State, (int)BitmapBatchPrepareState.NotPrepared);
            if ((prior == BitmapBatchPrepareState.Issuing) || (prior == BitmapBatchPrepareState.Preparing))
                throw new ThreadStateException("This batch is currently in use");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        new public void Add (BitmapDrawCall item) {
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
        public void Add (ref BitmapDrawCall item, Material material) {
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
            DrawCallSortKey? sortKey = null, Vector2? scale = null, Material material = null,
            Vector4? userData = null
        ) {
            if (material != null)
                throw new ArgumentException("Must be null because this is not a MultimaterialBitmapBatch", nameof(material));

            if (
                (offset == null) && (multiplyColor == null) && (addColor == null) &&
                (userData == null) && (sortKey == null) && (scale == null)
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
                if (userData.HasValue)
                    item.UserData = userData.Value;
                if (sortKey.HasValue)
                    item.SortKey = sortKey.Value;
                if (scale.HasValue)
                    item.Scale *= scale.Value;

                _DrawCalls.Add(ref item);
            }
        }
        
        protected override void PrepareDrawCalls (PrepareManager manager) {
            Squared.Render.NativeBatch.RecordPrimitives(_DrawCalls.Count * 2);

            AllocateNativeBatches();

            var count = _DrawCalls.Count;
            int[] indexArray = null;

            if (!DisableSorting) {
                indexArray = GetIndexArray(count);
                for (int i = 0; i < count; i++)
                    indexArray[i] = i;
            }

            if (DisableSorting) {
            } else if (Sorter != null) {
                var comparer = DrawCallSorterComparer.Value;
                comparer.Comparer = Sorter.GetComparer(true);
                _DrawCalls.Sort(comparer, indexArray);
            } else if ((UseZBuffer && ZBufferOnlySorting) || DisableSortKeys) {
                _DrawCalls.Sort(DrawCallTextureComparer, indexArray);
            } else {
                _DrawCalls.Sort(DrawCallComparer, indexArray);
            }

            _BufferGenerator = Container.RenderManager.GetBufferGenerator<BufferGenerator<BitmapVertex>>();
            _CornerBuffer = QuadUtils.CreateCornerBuffer(Container);

            using (var callBuffer = _DrawCalls.GetBuffer(false)) {
                var callSegment = new ArraySegment<BitmapDrawCall>(callBuffer.Data, 0, callBuffer.Count);
                int drawCallsPrepared = 0;
                while (drawCallsPrepared < count)
                    FillOneSoftwareBuffer(
                        indexArray, callSegment, ref drawCallsPrepared, count,
                        Material, SamplerState, SamplerState2
                    );
            }
        }

        protected override void OnReleaseResources () {
            _State = (int)BitmapBatchPrepareState.Invalid;
            _BufferGenerator = null;
            _CornerBuffer = null;

            _NativeBatches.Dispose();

            base.OnReleaseResources();
        }
    }
}