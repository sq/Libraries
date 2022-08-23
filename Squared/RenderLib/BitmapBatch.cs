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
using System.Runtime;

namespace Squared.Render {
    public sealed class BitmapBatch : BitmapBatchBase<BitmapDrawCall>, IBitmapBatch {
        private static bool WarnedAboutNullMaterial, WarnedAboutFillError;

        public static readonly SamplerState DefaultSamplerState = SamplerState.LinearClamp;

        static BitmapBatch () {
            BatchCombiner.Combiners.Add(new BitmapBatchCombiner());
            // We can safely enable fast clear because BitmapDrawCall does not contain any object references,
            //  so leaving old garbage data around will not cause any problems
            ConfigureClearBehavior(true);
        }

        sealed class BitmapBatchCombiner : IBatchCombiner {
            public bool CanCombine (Batch lhs, Batch rhs) {
                // Combining large batches could be counter-productive
                const int combineThreshold = 2048;

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

                if (bblhs.TwoPassDraw != bbrhs.TwoPassDraw)
                    return false;

                if (bblhs.DepthPrePassOnly != bbrhs.DepthPrePassOnly)
                    return false;

                if (bblhs.SamplerState != bbrhs.SamplerState)
                    return false;

                if (bblhs.SamplerState2 != bbrhs.SamplerState2)
                    return false;

                if (!bblhs.ReleaseAfterDraw)
                    return false;

                if (!bbrhs.ReleaseAfterDraw)
                    return false;

                if ((bblhs.Count > combineThreshold) || (bbrhs.Count > combineThreshold))
                    return false;

                if (!bblhs.MaterialParameters.Equals(ref bbrhs.MaterialParameters))
                    return false;

                return true;
            }

            public Batch Combine (Batch lhs, Batch rhs) {
                var bl = (BitmapBatch)lhs;
                var br = (BitmapBatch)rhs;

                for (int i = 0, l = br._DrawCalls.Count; i < l; i++) {
                    ref var rb = ref br._DrawCalls.Item(i);
                    if (!BitmapDrawCall.CheckValid(ref rb))
                        // FIXME
                        // throw new Exception("Invalid draw call in batch");
                        continue;

                    bl._DrawCalls.Add(ref rb);
                }

                br._DrawCalls.Clear();
                rhs.SetCombined(true);
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

        internal static ThreadLocal<BitmapDrawCallOrderAndTextureComparer> DrawCallComparer = new ThreadLocal<BitmapDrawCallOrderAndTextureComparer>(
            () => new BitmapDrawCallOrderAndTextureComparer()
        );
        internal static BitmapDrawCallTextureComparer DrawCallTextureComparer = new BitmapDrawCallTextureComparer();
        internal static ThreadLocal<BitmapDrawCallTextureAndReverseOrderComparer> DrawCallTextureAndReverseOrderComparer = new ThreadLocal<BitmapDrawCallTextureAndReverseOrderComparer>(
            () => new BitmapDrawCallTextureAndReverseOrderComparer()
        );

        internal static ThreadLocal<BitmapDrawCallSorterComparer> DrawCallSorterComparer = new ThreadLocal<BitmapDrawCallSorterComparer>(
            () => new BitmapDrawCallSorterComparer()
        );

        public static void SetAllocators (UnorderedList<BitmapDrawCall>.Allocator drawCallAllocator, UnorderedList<NativeBatch>.Allocator nativeBatchAllocator) {
            ListBatch<BitmapDrawCall>.SetAllocator(drawCallAllocator);
            _NativePool.Allocator = nativeBatchAllocator;
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

        public static BitmapBatch New (
            IBatchContainer container, int layer, Material material, 
            SamplerState samplerState = null, SamplerState samplerState2 = null, 
            bool useZBuffer = false, bool zBufferOnlySorting = false, 
            bool depthPrePass = false, bool worldSpace = false,
            int? capacity = null
        ) {
            if (container == null)
                throw new ArgumentNullException("container");
            if (material == null)
                throw new ArgumentNullException("material");
            if (material.Effect == null)
                throw new ArgumentNullException("material.Effect");

            var result = container.RenderManager.AllocateBatch<BitmapBatch>();
            result.Initialize(
                container, layer, material, 
                samplerState, samplerState2 ?? samplerState, 
                useZBuffer: useZBuffer, zBufferOnlySorting: zBufferOnlySorting, 
                depthPrePass: depthPrePass, worldSpace: worldSpace,
                capacity: capacity
            );
            result.CaptureStack(0);
            return result;
        }

        public void Initialize (
            IBatchContainer container, int layer, 
            Material material, SamplerState samplerState = null, SamplerState samplerState2 = null, 
            bool useZBuffer = false, bool zBufferOnlySorting = false, 
            bool depthPrePass = false, bool worldSpace = false,
            int? capacity = null
        ) {
            base.Initialize(container, layer, material, true, capacity);

            if (RangeReservations != null)
                RangeReservations.Clear();

            SamplerState = samplerState ?? BitmapBatch.DefaultSamplerState;
            SamplerState2 = samplerState2 ?? samplerState ?? BitmapBatch.DefaultSamplerState;

            UseZBuffer = useZBuffer;
            ZBufferOnlySorting = zBufferOnlySorting;
            DepthPrePassOnly = depthPrePass;
            WorldSpace = worldSpace;

            if (capacity.HasValue)
                _DrawCalls.EnsureCapacity(capacity.Value);

            var rm = container.RenderManager;
            var lp = (ListPool<BitmapDrawCall>)_DrawCalls.ListPool;
            lp.ThreadGroup = rm.ThreadGroup;
            rm.AddDrainRequiredListPool(lp);

            var prior = (BitmapBatchPrepareState)Interlocked.Exchange(ref _State, (int)BitmapBatchPrepareState.NotPrepared);
            if ((prior == BitmapBatchPrepareState.Issuing) || (prior == BitmapBatchPrepareState.Preparing))
                throw new ThreadStateException("This batch is currently in use");
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        new public void Add (BitmapDrawCall item) {
            if (!BitmapDrawCall.CheckValid(ref item))
                throw new InvalidOperationException("Invalid draw call");

            _DrawCalls.Add(ref item);
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        new public void Add (ref BitmapDrawCall item) {
            if (!BitmapDrawCall.CheckValid(ref item))
                throw new InvalidOperationException("Invalid draw call");

            _DrawCalls.Add(ref item);
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add (ref BitmapDrawCall item, Material material) {
            if (!BitmapDrawCall.CheckValid(ref item))
                throw new InvalidOperationException("Invalid draw call");
            if (material != null)
                throw new ArgumentException("Must be null because this is not a MultimaterialBitmapBatch", nameof(material));

            _DrawCalls.Add(ref item);
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddRange (ArraySegment<BitmapDrawCall> items) {
            _DrawCalls.AddRange(items);
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddRange (BitmapDrawCall[] items, int firstIndex, int count) {
            _DrawCalls.AddRange(items, firstIndex, count);
        }

        public void AddRange (
            BitmapDrawCall[] items, int firstIndex, int count, 
            Vector2? offset = null, Color? multiplyColor = null, Color? addColor = null, 
            DrawCallSortKey? sortKey = null, Vector2? scale = null, Material material = null,
            Vector4? userData = null, float? multiplyOpacity = null
        ) {
            if (material != null)
                throw new ArgumentException("Must be null because this is not a MultimaterialBitmapBatch", nameof(material));

            bool hasScale = (scale ?? Vector2.One) != Vector2.One,
                hasOffset = offset.HasValue,
                hasMultiplyColor = multiplyColor.HasValue,
                hasAddColor = addColor.HasValue,
                hasSortKey = sortKey.HasValue,
                hasUserData = userData.HasValue,
                hasOpacity = multiplyOpacity.HasValue;

            if (
                !hasOffset && !hasMultiplyColor && !hasAddColor &&
                !hasUserData && !hasSortKey && !hasScale && !hasOpacity
            ) {
                AddRange(items, firstIndex, count);
                return;
            }

            Vector2 _scale = scale ?? default(Vector2),
                _offset = offset ?? default(Vector2);
            Color _multiplyColor = multiplyColor ?? default(Color),
                _addColor = addColor ?? default(Color);
            var _sortKey = sortKey ?? default(DrawCallSortKey);
            var _userData = userData ?? default(Vector4);
            var _opacity = multiplyOpacity ?? 1.0f;

            var newCount = _DrawCalls.Count + count;
            _DrawCalls.EnsureCapacity(newCount);
            for (int i = 0; i < count; i++) {
                ref var item = ref DenseList<BitmapDrawCall>.UnsafeCreateSlotWithKnownCapacity(ref _DrawCalls);
                item = items[i + firstIndex];
                if (!BitmapDrawCall.CheckValid(ref item))
                    continue;

                if (hasScale) {
                    item.Position.X *= _scale.X;
                    item.Position.Y *= _scale.Y;
                    item.Scale.X *= _scale.X;
                    item.Scale.Y *= _scale.Y;
                }
                if (hasOffset) {
                    item.Position.X += _offset.X;
                    item.Position.Y += _offset.Y;
                }
                if (hasMultiplyColor) {
                    if (hasOpacity)
                        item.MultiplyColor = _multiplyColor * _opacity;
                    else
                        item.MultiplyColor = _multiplyColor;
                } else if (hasOpacity) {
                    item.MultiplyColor *= _opacity;
                }

                if (hasAddColor)
                    item.AddColor = _addColor;
                if (hasUserData)
                    item.UserData = _userData;
                if (hasSortKey) {
                    item.SortOrder = _sortKey.Order;
                    item.SortTags = _sortKey.Tags;
                }
            }
        }
        
        protected override bool PrepareDrawCalls (PrepareManager manager) {
            Squared.Render.NativeBatch.RecordPrimitives(_DrawCalls.Count * 2);

            AllocateNativeBatches();

            var textureCache = manager.TextureCache;

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
            } else if (
                (UseZBuffer && ZBufferOnlySorting) || 
                DisableSortKeys ||
                (UseZBuffer && DepthPrePassOnly)
            ) {
                // If sort keys are enabled, we want to try to draw back-to-front to maximize the effectiveness of the z-buffer
                if (DisableSortKeys) {
                    _DrawCalls.Sort(DrawCallTextureComparer, indexArray);
                } else {
                    _DrawCalls.Sort(DrawCallTextureAndReverseOrderComparer.Value, indexArray);
                }
            } else {
                _DrawCalls.Sort(DrawCallComparer.Value, indexArray);
            }

            _BufferGenerator = Container.RenderManager.GetBufferGenerator<BufferGenerator<BitmapVertex>>();
            _CornerBuffer = Container.Frame.PrepareData.GetCornerBuffer(Container);

            if (Material == null) {
                if (!WarnedAboutNullMaterial) {
                    WarnedAboutNullMaterial = true;
                    Console.Error.WriteLine("WARNING: BitmapBatch prepared with null material");
                }
                return false;
            }

            using (var callBuffer = _DrawCalls.GetBuffer(false, _TinyScratchBuffer)) {
                var callSegment = new ArraySegment<BitmapDrawCall>(callBuffer.Data, callBuffer.Offset, _DrawCalls.Count);
                int drawCallsPrepared = 0;
                var parameters = new BatchBuilderParameters {
                    material = Material,
                    samplerState1 = SamplerState,
                    samplerState2 = SamplerState2,
                    textureCache = textureCache
                };
                while (drawCallsPrepared < count) {
                    FillOneSoftwareBuffer(
                        indexArray, callSegment, ref drawCallsPrepared, count,
                        ref parameters, out bool failed
                    );
                    if (failed) {
                        if (!WarnedAboutFillError) {
                            WarnedAboutFillError = true;
                            Console.Error.WriteLine("WARNING: BitmapBatch prepare failed");
                        }
                        return false;
                    }
                }
            }

            return true;
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