﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Game;
using Squared.Render.RasterShape;
using Squared.Render.RasterStroke;
using Squared.Render.Text;
using Squared.Util;
using Squared.Util.DeclarativeSort;
using Squared.Util.Text;

namespace Squared.Render.Convenience {
    public struct ImperativeRenderer {
        [Flags]
        private enum CachedBatchFlags : byte {
            WorldSpace = 0b1,
            UseZBuffer = 0b10,
            ZBufferOnlySorting = 0b100,
            DepthPrePass = 0b1000,
        }

        private struct CachedBatch {
            public Batch Batch;

            public CachedBatchType BatchType;
            public IBatchContainer Container;
            public int Layer;
            public BlendState BlendState;
            public SamplerState SamplerState1, SamplerState2;
            public RasterizerState RasterizerState;
            public DepthStencilState DepthStencilState;
            public object ExtraData;
            public CachedBatchFlags Flags;
            
            public static bool KeysEqual (ref CachedBatch lhs, ref CachedBatch rhs) {
                var result = (
                    (lhs.BatchType == rhs.BatchType) &&
                    (lhs.Container == rhs.Container) &&
                    (lhs.Layer == rhs.Layer) &&
                    (lhs.Flags == rhs.Flags) &&
                    (lhs.BlendState == rhs.BlendState) &&
                    (lhs.RasterizerState == rhs.RasterizerState) &&
                    (lhs.DepthStencilState == rhs.DepthStencilState) &&
                    object.ReferenceEquals(lhs.ExtraData, rhs.ExtraData) &&
                    object.ReferenceEquals(lhs.SamplerState1, rhs.SamplerState1) &&
                    object.ReferenceEquals(lhs.SamplerState2, rhs.SamplerState2)
                );

                return result;
            }

            public override bool Equals (object obj) {
                throw new InvalidOperationException("Don't box this");
            }

            public override int GetHashCode () {
                return 0;
            }

            public override string ToString () {
                return string.Format("{0} (layer={1} extra={2})", Batch, Layer, ExtraData);
            }
        }

        private enum CachedBatchType : byte {
            Bitmap,
            MultimaterialBitmap,
            Geometry,
            RasterShape,
            RasterStroke
        }

        private struct CachedBatches {
#if MEASURE_CACHE_HIT_COUNTS
            public static volatile int HitCount, MissCount;

            public static double HitRate => (HitCount * 100.0) / (HitCount + MissCount);
#endif

            public const int Capacity = 4;

            public int Count;
            public CachedBatch Batch0, Batch1, Batch2, Batch3;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Clear () {
                Count = 0;
                Batch0 = Batch1 = Batch2 = Batch3 = default(CachedBatch);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void InsertAtFront (ref CachedBatch item, int previousIndex) {
                // No-op
                if (previousIndex == 0)
                    return;
                else if (Count == 0) {
                    Batch0 = item;
                    Count = 1;
                    return;
                }

                InsertAtFront_Slow(in item, previousIndex);
            }

            internal static ref CachedBatch ItemAtIndex (ref CachedBatches @this, int index) {
                switch (index) {
                    default:
                        return ref @this.Batch0;
                    case 1:
                        return ref @this.Batch1;
                    case 2:
                        return ref @this.Batch2;
                    case 3:
                        return ref @this.Batch3;
                }
            }

            private void InsertAtFront_Slow (in CachedBatch item, int previousIndex) {
                // Move items back to create space for the item at the front
                int writePosition;
                if (Count == Capacity) {
                    writePosition = Capacity - 1;
                } else {
                    writePosition = Count;
                }

                for (int i = writePosition - 1; i >= 0; i--) {
                    // If the item is being moved from its position to the front, make sure we don't also move it back
                    if (i == previousIndex)
                        continue;

                    ref var source = ref ItemAtIndex(ref this, i);
                    ref var destination = ref ItemAtIndex(ref this, writePosition);
                    destination = source;
                    writePosition -= 1;
                }

                Batch0 = item;

                if (previousIndex < 0) {
                    if (Count < Capacity)
                        Count += 1;
                }
            }
        }

        // Most of the IR's data grouped in one blob to make cloning easier/faster
        private struct Configuration {
            public IBatchContainer Container;
            public DefaultMaterialSet Materials;
            public DepthStencilState DepthStencilState;
            public RasterizerState RasterizerState;
            public SamplerState SamplerState, SamplerState2;
            public Material DefaultBitmapMaterial;
            public Sorter<BitmapDrawCall> DeclarativeSorter;
            public int Layer, FrameIndex;
            public float RasterGammaMinusOne;
            public object BlendStateOrSelector;
            public List<RasterShape.RasterShapeComposite> RasterComposites;
            public Vector2 BitmapMarginSize;
        }

        private CachedBatches Cache;
        private Configuration Config;
        private DrawCallSortKey NextSortKey;
        public ImperativeRendererFlags Flags;

        /// <summary>
        /// All batches created by this renderer will have these material parameters applied
        /// </summary>
        public MaterialParameterValues Parameters;

        public Vector2 BitmapMarginSize {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Config.BitmapMarginSize;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => Config.BitmapMarginSize = value;
        }

        public IBatchContainer Container {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Config.Container;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private set => Config.Container = value;
        }
        public DefaultMaterialSet Materials {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Config.Materials;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private set => Config.Materials = value;
        }

        public DepthStencilState DepthStencilState {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Config.DepthStencilState;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => Config.DepthStencilState = value;
        }
        public RasterizerState RasterizerState {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Config.RasterizerState;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => Config.RasterizerState = value;
        }
        public SamplerState SamplerState {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Config.SamplerState;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => Config.SamplerState = value;
        }
        public SamplerState SamplerState2 {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Config.SamplerState2;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => Config.SamplerState2 = value;
        }

        /// <summary>
        /// Overrides the default material used to draw bitmaps if no custom material has been specified.
        /// </summary>
        public Material DefaultBitmapMaterial {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Config.DefaultBitmapMaterial;
            set => Config.DefaultBitmapMaterial = value;
        }

        /// <summary>
        /// Specifies a custom set of declarative sorting rules used to order draw calls.
        /// </summary>
        public Sorter<BitmapDrawCall> DeclarativeSorter {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Config.DeclarativeSorter;
            set => Config.DeclarativeSorter = value;
        }

        public int Layer {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Config.Layer;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => Config.Layer = value;
        }

        public BlendState BlendState {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Config.BlendStateOrSelector as BlendState;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => Config.BlendStateOrSelector = value;
        }
        public Func<AbstractTextureReference, BlendState> BlendStateSelector {
            get => Config.BlendStateOrSelector as Func<AbstractTextureReference, BlendState>;
            set => Config.BlendStateOrSelector = value;
        }

        /// <summary>
        /// Uses world-space coordinates.
        /// </summary>
        public bool WorldSpace {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => GetFlag(ImperativeRendererFlags.WorldSpace);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => SetFlag(ImperativeRendererFlags.WorldSpace, value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetFlag (ImperativeRendererFlags flag, bool value) {
            if (value)
                Flags |= flag;
            else
                Flags &= ~flag;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool GetFlag (ImperativeRendererFlags flag) {
            return (Flags & flag) == flag;
        }

        /// <summary>
        /// Generates z coordinates so that the z buffer can be used to order draw calls.
        /// </summary>
        public bool UseZBuffer {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => GetFlag(ImperativeRendererFlags.UseZBuffer);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => SetFlag(ImperativeRendererFlags.UseZBuffer, value);
        }

        /// <summary>
        /// Disables draw call sorting and relies on the z buffer to maintain ordering.
        /// </summary>
        public bool ZBufferOnlySorting {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => GetFlag(ImperativeRendererFlags.ZBufferOnlySorting);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => SetFlag(ImperativeRendererFlags.ZBufferOnlySorting, value);
        }

        /// <summary>
        /// If z-buffering is enabled, only a depth buffer generating pass will happen, not color rendering.
        /// </summary>
        public bool DepthPrePass {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => GetFlag(ImperativeRendererFlags.DepthPrePass);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => SetFlag(ImperativeRendererFlags.DepthPrePass, value);
        }

        /// <summary>
        /// Increments the Layer after each drawing operation.
        /// </summary>
        public bool AutoIncrementLayer {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => GetFlag(ImperativeRendererFlags.AutoIncrementLayer);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => SetFlag(ImperativeRendererFlags.AutoIncrementLayer, value);
        }

        /// <summary>
        /// Increments the sorting key's order value after each drawing operation.
        /// </summary>
        public bool AutoIncrementSortKey {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => GetFlag(ImperativeRendererFlags.AutoIncrementSortKey);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => SetFlag(ImperativeRendererFlags.AutoIncrementSortKey, value);
        }

        /// <summary>
        /// If true, materials are last in the sort order instead of first.
        /// This allows precise ordering of bitmaps by sort key, regardless of material.
        /// </summary>
        public bool LowPriorityMaterialOrdering {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => GetFlag(ImperativeRendererFlags.LowPriorityMaterialOrdering);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => SetFlag(ImperativeRendererFlags.LowPriorityMaterialOrdering, value);
        }

        /// <summary>
        /// Bitmaps will use a shader with discard by default. Discard ensures transparent pixels are not drawn.
        /// </summary>
        public bool UseDiscard {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => GetFlag(ImperativeRendererFlags.UseDiscard);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => SetFlag(ImperativeRendererFlags.UseDiscard, value);
        }

        /// <summary>
        /// If set, outlines on raster shapes will be soft instead of hard.
        /// For shapes with an outline thickness of 0 the fill will always be hard unless gamma is negative.
        /// </summary>
        public bool RasterSoftOutlines {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => GetFlag(ImperativeRendererFlags.RasterSoftOutlines);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => SetFlag(ImperativeRendererFlags.RasterSoftOutlines, value);
        }

        /// <summary>
        /// If set, raster shapes will be drawn using the generic ubershader in a single large pass.
        /// This is slower for large shapes but produces fewer draw calls.
        /// </summary>
        public bool RasterUseUbershader {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => GetFlag(ImperativeRendererFlags.RasterUseUbershader);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => SetFlag(ImperativeRendererFlags.RasterUseUbershader, value);
        }

        /// <summary>
        /// If true, raster shape colors will be converted from sRGB to linear space before
        ///  blending and then converted back to sRGB for rendering.
        /// If false, colors will be directly blended. This might look bad.
        /// </summary>
        public bool RasterBlendInLinearSpace {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => GetFlag(ImperativeRendererFlags.RasterBlendInLinearSpace);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set {
                SetFlag(ImperativeRendererFlags.RasterBlendInLinearSpace, value);
                SetFlag(ImperativeRendererFlags.RasterBlendInOkLabSpace, false);
            }
        }

        /// <summary>
        /// If true, raster shape colors will be converted from sRGB to OkLab space before
        ///  blending and then converted back to sRGB for rendering. This is a superset of linear space.
        /// </summary>
        public bool RasterBlendInOkLabSpace {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => GetFlag(ImperativeRendererFlags.RasterBlendInOkLabSpace);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set {
                SetFlag(ImperativeRendererFlags.RasterBlendInOkLabSpace, value);
                if (value)
                    SetFlag(ImperativeRendererFlags.RasterBlendInLinearSpace, true);
            }
        }

        /// <summary>
        /// If true, default dithering behavior will be suppressed for raster shaders.
        /// </summary>
        public bool DisableDithering {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => GetFlag(ImperativeRendererFlags.DisableDithering);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set {
                SetFlag(ImperativeRendererFlags.DisableDithering, value);
                if (value)
                    SetFlag(ImperativeRendererFlags.DisableDithering, true);
            }
        }

        public void ClearRasterComposites () {
            Config.RasterComposites?.Clear();
        }

        public void AddRasterComposite (RasterShapeComposite value) {
            Config.RasterComposites ??= new ();
            Config.RasterComposites.Add(value);
        }

        private bool TryGetCachedBatch<T> (
            out CachedBatch result,
            CachedBatchType cbt,
            int layer, 
            bool worldSpace, 
            RasterizerState rasterizerState, 
            DepthStencilState depthStencilState, 
            BlendState blendState, 
            SamplerState samplerState1, 
            SamplerState samplerState2,
            object extraData
        ) {
            var flags = default(CachedBatchFlags);
            if (worldSpace)
                flags |= CachedBatchFlags.WorldSpace;
            if (UseZBuffer)
                flags |= CachedBatchFlags.UseZBuffer;
            if (ZBufferOnlySorting)
                flags |= CachedBatchFlags.ZBufferOnlySorting;
            if (DepthPrePass)
                flags |= CachedBatchFlags.DepthPrePass;

            result = new CachedBatch {
                BatchType = cbt,
                Container = Config.Container,
                Layer = layer,
                Flags = flags,
                // FIXME: Mask if multimaterial?
            };

            if (cbt != CachedBatchType.MultimaterialBitmap) {
                result.RasterizerState = rasterizerState;
                result.DepthStencilState = depthStencilState;
                result.BlendState = blendState;
                result.SamplerState1 = samplerState1;
                result.SamplerState2 = samplerState2;
                result.ExtraData = extraData;
            }

            int i;
            if (CompareCachedBatch(ref Cache.Batch0, ref result, ref Parameters))
                i = 0;
            else if (CompareCachedBatch(ref Cache.Batch1, ref result, ref Parameters))
                i = 1;
            else if (CompareCachedBatch(ref Cache.Batch2, ref result, ref Parameters))
                i = 2;
            else if (CompareCachedBatch(ref Cache.Batch3, ref result, ref Parameters))
                i = 3;
            else {
#if MEASURE_CACHE_HIT_COUNTS
                CachedBatches.MissCount++;
#endif
                return false;
            }

            Cache.InsertAtFront(ref result, i);
            return (result.Batch != null);
        }

        private bool CompareCachedBatch (ref CachedBatch cached, ref CachedBatch result, ref MaterialParameterValues parameters) {
            if (cached.Batch == null)
                return false;
            else if (!CachedBatch.KeysEqual(ref cached, ref result))
                return false;
            else if (!cached.Batch.AreParametersEqual(ref parameters))
                return false;

            result = cached;
#if MEASURE_CACHE_HIT_COUNTS
            CachedBatches.HitCount++;
#endif
            return true;
        }

        /// <summary>
        /// If you already have a default-initialized ImperativeRenderer, you can call this to make it usable
        /// </summary>
        public void FastInitialize (
            IBatchContainer container,
            DefaultMaterialSet materials
        ) {
            if (container == null)
                throw new ArgumentNullException("container");
            if (materials == null)
                throw new ArgumentNullException("materials");

            Config.Container = container;
            Config.Materials = materials;
            Config.FrameIndex = container.FrameIndex;
            Flags = ImperativeRendererFlags.RasterBlendInLinearSpace | ImperativeRendererFlags.WorldSpace;
        }

        public ImperativeRenderer (
            IBatchContainer container,
            DefaultMaterialSet materials,
            int layer = 0,
            RasterizerState rasterizerState = null,
            DepthStencilState depthStencilState = null,
            BlendState blendState = null,
            SamplerState samplerState = null,
            SamplerState samplerState2 = null,
            bool worldSpace = true,
            bool autoIncrementSortKey = false,
            bool autoIncrementLayer = false,
            ImperativeRendererFlags flags = default,
            Sorter<BitmapDrawCall> declarativeSorter = null,
            Tags tags = default(Tags)
        ) : this() {
            if (container == null)
                throw new ArgumentNullException("container");
            if (materials == null)
                throw new ArgumentNullException("materials");

            Config.Container = container;
            Config.Materials = materials;
            Config.Layer = layer;
            Config.RasterizerState = rasterizerState;
            Config.DepthStencilState = depthStencilState;
            Config.BlendStateOrSelector = blendState;
            Config.SamplerState = samplerState;
            Config.SamplerState2 = samplerState2;
            Config.DeclarativeSorter = declarativeSorter;
            Config.RasterGammaMinusOne = 0;
            Config.DefaultBitmapMaterial = null;
            Config.FrameIndex = container.FrameIndex;
            if (worldSpace)
                flags |= ImperativeRendererFlags.WorldSpace;
            if (autoIncrementSortKey)
                flags |= ImperativeRendererFlags.AutoIncrementSortKey;
            if (autoIncrementLayer)
                flags |= ImperativeRendererFlags.AutoIncrementLayer;
            flags |= ImperativeRendererFlags.RasterBlendInLinearSpace;
            Flags = flags;
        }

        /// <summary>
        /// Applies gamma correction to outlines to make them look sharper or softer.
        /// More effective when soft outlines are enabled.
        /// </summary>
        public float RasterOutlineGamma {
            get {
                return Config.RasterGammaMinusOne + 1;
            }
            set {
                Config.RasterGammaMinusOne = value - 1;
            }
        }

        /// <summary>
        /// Applies gamma to the interior of a shape in addition to its outline.
        /// </summary>
        public float RasterGamma {
            get {
                if (Config.RasterGammaMinusOne < -1)
                    return -(Config.RasterGammaMinusOne + 1);
                else
                    return 0;
            }
            set {
                if (value <= 0) {
                    if (Config.RasterGammaMinusOne > -1)
                        return;
                    else
                        Config.RasterGammaMinusOne = 0;
                } else {
                    Config.RasterGammaMinusOne = -value - 1;
                }
            }
        }

        public Tags DefaultTags {
            get {
                return NextSortKey.Tags;
            }
            set {
                NextSortKey.Tags = value;
            }
        }

        public ImperativeRenderer MakeSubgroup (
            bool nextLayer = true, Action<DeviceManager, object> before = null, Action<DeviceManager, object> after = null, object userData = null,
            string name = null, int? layer = null, ViewTransformModifier viewTransformModifier = null, bool preserveParameters = false
        ) {
            ImperativeRenderer result;
            MakeSubgroup(out result, nextLayer, before, after, userData, name, layer, viewTransformModifier, preserveParameters);
            return result;
        }

        public ImperativeRenderer MakeSubgroup (
            in ViewTransform viewTransform, bool nextLayer = true, 
            Action<DeviceManager, object> before = null, Action<DeviceManager, object> after = null, object userData = null,
            string name = null, int? layer = null, bool preserveParameters = false
        ) {
            ImperativeRenderer result;
            MakeSubgroup(out result, in viewTransform, nextLayer, before, after, userData, name, layer, preserveParameters);
            return result;
        }

        public ImperativeRenderer MakeSubgroup (
            Matrix matrix, bool replace = false, bool nextLayer = true, 
            Action<DeviceManager, object> before = null, Action<DeviceManager, object> after = null,
            string name = null, int? layer = null, bool preserveParameters = false
        ) {
            ImperativeRenderer result;
            MatrixBox mb;
            lock (ImperativeRendererUtil.MatrixBoxes) {
                if (!ImperativeRendererUtil.MatrixBoxes.TryPopBack(out mb))
                    mb = new MatrixBox();
            }
            mb.Matrix = matrix;
            mb.Replace = replace;
            MakeSubgroup(out result, nextLayer, before, after, mb, name, layer, viewTransformModifier: ImperativeRendererUtil.ChangeMatrixModifier, preserveParameters);
            return result;
        }

        public void MakeSubgroup (
            out ImperativeRenderer result, bool nextLayer = true, Action<DeviceManager, object> before = null, Action<DeviceManager, object> after = null, object userData = null,
            string name = null, int? layer = null, ViewTransformModifier viewTransformModifier = null, bool preserveParameters = false
        ) {
            CloneInto(out result, true, false, preserveParameters);

            var group = BatchGroup.New(
                Config.Container, layer ?? Config.Layer, before: before, after: after, userData: userData,
                materialSet: Config.Materials, name: name
            );
            if (viewTransformModifier != null)
                group.SetViewTransform(viewTransformModifier);
            group.Dispose();
            result.Config.Container = group;
            result.Config.Layer = 0;

            if (nextLayer)
                Config.Layer += 1;
        }

        public void MakeSubgroup (
            out ImperativeRenderer result, in ViewTransform viewTransform, 
            bool nextLayer = true, Action<DeviceManager, object> before = null, 
            Action<DeviceManager, object> after = null, object userData = null,
            string name = null, int? layer = null, bool preserveParameters = false
        ) {
            MakeSubgroup(out result, nextLayer, before, after, userData, name, layer, preserveParameters: preserveParameters);
            ((BatchGroup)result.Config.Container).SetViewTransform(in viewTransform);
        }
        
        public void CloneInto (out ImperativeRenderer result, bool nextLayer = true, bool preserveCache = true, bool preserveParameters = true) {
#if !NOSPAN
            // NOTE: Make sure the library builds even if you comment out the SkipInit! If it doesn't,
            //  you missed a field!
            Unsafe.SkipInit(out result);
            if (preserveCache)
                result.Cache = Cache;
            else
                result.Cache = default;

            if (!Parameters.IsEmpty && preserveParameters) {
                // We need to ensure that both we and our copy will allocate new storage on write
                //  so that changes made by one don't accidentally trample on the other
                Parameters.AllocateNewStorageOnWrite = true;
                result.Parameters = Parameters;
            } else
                result.Parameters = default;
#else
            result = default;
            if (preserveCache)
                result.Cache = Cache;
            if (!Parameters.IsEmpty && preserveParameters) {
                Parameters.AllocateNewStorageOnWrite = true;
                result.Parameters = Parameters;
            }
#endif

            result.Config = Config;
            result.Flags = Flags;
            result.NextSortKey = NextSortKey;

            if ((result.Config.RasterComposites?.Count ?? 0) > 0)
                result.Config.RasterComposites = new List<RasterShapeComposite>(result.Config.RasterComposites);
            
            if (nextLayer)
                Config.Layer += 1;
        }

        public ImperativeRenderer Clone (bool nextLayer = true, bool preserveParameters = true) {
            CloneInto(out var result, nextLayer, true, preserveParameters);
            return result;
        }

        public ImperativeRenderer ForRenderTarget (
            AutoRenderTarget renderTarget, Action<DeviceManager, object> before = null, Action<DeviceManager, object> after = null, 
            object userData = null, string name = null, int? layer = null, IBatchContainer newContainer = null, 
            in ViewTransform? viewTransform = null, bool preserveParameters = false
        ) {
            CloneInto(out var result, true, false, preserveParameters);

            var group = BatchGroup.ForRenderTarget(
                newContainer ?? Config.Container, layer ?? Config.Layer, renderTarget, before, after, userData, name: name, 
                materialSet: Config.Materials, viewTransform: viewTransform
            );
            group.Dispose();
            result.Config.Container = group;
            // FIXME: is this ever correct?
            result.Config.Layer = 0;

            Config.Layer += 1;

            return result;
        }

        public ImperativeRenderer ForRenderTarget (
            RenderTarget2D renderTarget, Action<DeviceManager, object> before = null, Action<DeviceManager, object> after = null, 
            object userData = null, string name = null, int? layer = null, IBatchContainer newContainer = null, 
            in ViewTransform? viewTransform = null, bool preserveParameters = false
        ) {
            CloneInto(out var result, true, false, preserveParameters);

            var group = BatchGroup.ForRenderTarget(
                newContainer ?? Config.Container, layer ?? Config.Layer, renderTarget, before, after, userData, name: name, 
                materialSet: Config.Materials, viewTransform: viewTransform
            );
            group.Dispose();
            result.Config.Container = group;
            // FIXME: is this ever correct?
            result.Config.Layer = 0;

            Config.Layer += 1;

            return result;
        }

        /// <summary>
        /// Adds a clear batch. Note that this will *always* advance the layer unless you specify a layer index explicitly.
        /// </summary>
        public void Clear (
            int? layer = null,
            Color? color = null,
            float? z = null,
            int? stencil = null,
            Vector4? value = null,
            string name = null
        ) {
            int _layer = layer.GetValueOrDefault(Config.Layer);

            ClearBatch.AddNew(Config.Container, _layer, Config.Materials.Clear, color, z, stencil, value, name);

            if (!layer.HasValue)
                Config.Layer += 1;
        }

        public IBitmapBatch Draw (
            BitmapDrawCall drawCall, 
            int? layer = null, bool? worldSpace = null,
            BlendState blendState = null, SamplerState samplerState = null, SamplerState samplerState2 = null,
            DepthStencilState depthStencilState = null, RasterizerState rasterizerState = null,
            Material material = null
        ) {
            return Draw(
                ref drawCall, layer, worldSpace, 
                blendState, samplerState, samplerState2,
                depthStencilState, rasterizerState, material
            );
        }

        public void ChangeModelViewMatrix (Matrix m, bool replace = false, int? layer = null) => ChangeModelViewMatrix(ref m, replace, layer);

        public void ChangeModelViewMatrix (ref Matrix m, bool replace = false, int? layer = null) {
            MatrixBox mb;
            lock (ImperativeRendererUtil.MatrixBoxes)
                if (!ImperativeRendererUtil.MatrixBoxes.TryPopBack(out mb))
                    mb = new MatrixBox();
            mb.Matrix = m;
            mb.Replace = replace;
            ModifyViewTransformBatch.AddNew(Config.Container, layer ?? Config.Layer, Config.Materials, ImperativeRendererUtil.ChangeMatrixModifier, mb);
            if (!layer.HasValue)
                Config.Layer++;
        }

        private BlendState PickBlendStateForTextures (ref TextureSet textures) {
            if (Config.BlendStateOrSelector is BlendState bs)
                return bs;
            else if (Config.BlendStateOrSelector is Func<AbstractTextureReference, BlendState> selector)
                return selector(textures.Texture1) ?? selector(textures.Texture2);
            else
                return null;
        }

        public IBitmapBatch Draw (
            ref BitmapDrawCall drawCall, 
            int? layer = null, bool? worldSpace = null,
            BlendState blendState = null, SamplerState samplerState = null, SamplerState samplerState2 = null,
            DepthStencilState depthStencilState = null, RasterizerState rasterizerState = null,
            Material material = null
        ) {
            if (Config.Container == null)
                throw new InvalidOperationException("You cannot use the argumentless ImperativeRenderer constructor.");
            else if (Config.Container.IsReleased)
                throw new ObjectDisposedException("The container this ImperativeRenderer is drawing into has been disposed.");

            var batch = GetBitmapBatch(
                layer, worldSpace,
                blendState ?? PickBlendStateForTextures(ref drawCall.Textures), samplerState, depthStencilState ?? DepthStencilState,
                rasterizerState ?? RasterizerState, material ?? DefaultBitmapMaterial,
                samplerState2: samplerState2 ?? SamplerState2
            );
            {
                if (LowPriorityMaterialOrdering) {
                    if (material != null)
                        material = Config.Materials.Get(
                            material, rasterizerState ?? RasterizerState, depthStencilState ?? DepthStencilState, blendState ?? BlendState
                        );
                    else
                        material = Config.Materials.GetBitmapMaterial(
                            worldSpace ?? WorldSpace, rasterizerState ?? RasterizerState, depthStencilState ?? DepthStencilState, blendState ?? BlendState, UseDiscard
                        );

                    var mmbb = (MultimaterialBitmapBatch)batch;
                    if (worldSpace.HasValue && (drawCall.WorldSpace != worldSpace)) {
                        var temp = drawCall;
                        temp.WorldSpace = worldSpace;
                        mmbb.Add(ref temp, material, samplerState, samplerState2);
                    } else
                        mmbb.Add(ref drawCall, material, samplerState, samplerState2);
                } else {
                    if (worldSpace.HasValue && (drawCall.WorldSpace != worldSpace)) {
                        var temp = drawCall;
                        temp.WorldSpace = worldSpace;
                        batch.Add(ref temp);
                    } else
                        batch.Add(ref drawCall);
                }
            }
            return batch;
        }

        public IBitmapBatch Draw (
            IDynamicTexture texture, Vector2 position,
            Rectangle? sourceRectangle = null, Color? multiplyColor = null, Color addColor = default(Color),
            float rotation = 0, Vector2? scale = null, Vector2 origin = default(Vector2),
            bool mirrorX = false, bool mirrorY = false, DrawCallSortKey? sortKey = null,
            int? layer = null, bool? worldSpace = null, 
            BlendState blendState = null, SamplerState samplerState = null, SamplerState samplerState2 = null,
            DepthStencilState depthStencilState = null, RasterizerState rasterizerState = null,
            Material material = null
        ) {
            var textureSet = new TextureSet(new AbstractTextureReference(texture));
            var textureRegion = Bounds.Unit;
            if (sourceRectangle.HasValue) {
                var sourceRectangleValue = sourceRectangle.Value;
                textureRegion = GameExtensionMethods.BoundsFromRectangle(texture.Width, texture.Height, in sourceRectangleValue);
            }

            var drawCall = new BitmapDrawCall(
                textureSet, position, textureRegion,
                multiplyColor.GetValueOrDefault(Color.White),
                scale.GetValueOrDefault(Vector2.One), origin, rotation
            ) {
                AddColor = addColor,
                SortKey = sortKey.GetValueOrDefault(NextSortKey)
            };

            if (mirrorX || mirrorY)
                drawCall.Mirror(mirrorX, mirrorY);

            if (AutoIncrementSortKey)
                NextSortKey.Order += 1;

            return Draw(
                ref drawCall, layer: layer, worldSpace: worldSpace, blendState: blendState, 
                samplerState: samplerState, samplerState2: SamplerState2, 
                depthStencilState: depthStencilState, rasterizerState: rasterizerState, material: material
            );
        }

        public IBitmapBatch Draw (
            Texture2D texture, Vector2 position,
            Rectangle? sourceRectangle = null, Color? multiplyColor = null, Color addColor = default(Color),
            float rotation = 0, Vector2? scale = null, Vector2 origin = default(Vector2),
            bool mirrorX = false, bool mirrorY = false, DrawCallSortKey? sortKey = null,
            int? layer = null, bool? worldSpace = null, 
            BlendState blendState = null, SamplerState samplerState = null, SamplerState samplerState2 = null,
            DepthStencilState depthStencilState = null, RasterizerState rasterizerState = null,
            Material material = null, Vector4 userData = default(Vector4)
        ) {
            var drawCall = new BitmapDrawCall(texture, position);
            if (sourceRectangle.HasValue)
                drawCall.TextureRegion = texture.BoundsFromRectangle(sourceRectangle.Value);
            drawCall.MultiplyColor = multiplyColor.GetValueOrDefault(Color.White);
            drawCall.AddColor = addColor;
            drawCall.Rotation = rotation;
            drawCall.Scale = scale.GetValueOrDefault(Vector2.One);
            drawCall.Origin = origin;
            drawCall.UserData = userData;
            if (mirrorX || mirrorY)
                drawCall.Mirror(mirrorX, mirrorY);

            drawCall.SortKey = sortKey.GetValueOrDefault(NextSortKey);
            if (AutoIncrementSortKey)
                NextSortKey.Order += 1;

            return Draw(
                ref drawCall, layer: layer, worldSpace: worldSpace, blendState: blendState, 
                samplerState: samplerState, samplerState2: samplerState2,
                depthStencilState: depthStencilState, rasterizerState: rasterizerState, material: material
            );
        }

        public IBitmapBatch Draw (
            Texture2D texture, float x, float y,
            Rectangle? sourceRectangle = null, Color? multiplyColor = null, Color addColor = default(Color),
            float rotation = 0, float scaleX = 1, float scaleY = 1, float originX = 0, float originY = 0,
            bool mirrorX = false, bool mirrorY = false, DrawCallSortKey? sortKey = null,
            int? layer = null, bool? worldSpace = null,
            BlendState blendState = null, SamplerState samplerState = null, SamplerState samplerState2 = null,
            DepthStencilState depthStencilState = null, RasterizerState rasterizerState = null,
            Material material = null, Vector4 userData = default(Vector4)
        ) {
            var drawCall = new BitmapDrawCall(texture, new Vector2(x, y));
            if (sourceRectangle.HasValue)
                drawCall.TextureRegion = texture.BoundsFromRectangle(sourceRectangle.Value);
            drawCall.MultiplyColor = multiplyColor.GetValueOrDefault(Color.White);
            drawCall.AddColor = addColor;
            drawCall.Rotation = rotation;
            drawCall.UserData = userData;
            drawCall.Scale = new Vector2(scaleX, scaleY);
            drawCall.Origin = new Vector2(originX, originY);
            if (mirrorX || mirrorY)
                drawCall.Mirror(mirrorX, mirrorY);

            drawCall.SortKey = sortKey.GetValueOrDefault(NextSortKey);
            if (AutoIncrementSortKey)
                NextSortKey.Order += 1;

            return Draw(
                ref drawCall, layer: layer, worldSpace: worldSpace, blendState: blendState, 
                samplerState: samplerState, samplerState2: samplerState2,
                depthStencilState: depthStencilState, rasterizerState: rasterizerState,
                material: material
            );
        }

        public IBitmapBatch Draw (
            TextureSet textures, Vector2 position,
            Rectangle? sourceRectangle1 = null, Rectangle? sourceRectangle2 = null,
            Color? multiplyColor = null, Color addColor = default(Color),
            float rotation = 0, Vector2? scale = null, Vector2? origin = null,
            bool mirrorX = false, bool mirrorY = false, DrawCallSortKey? sortKey = null,
            int? layer = null, bool? worldSpace = null,
            BlendState blendState = null, SamplerState samplerState = null, SamplerState samplerState2 = null,
            DepthStencilState depthStencilState = null, RasterizerState rasterizerState = null,
            Material material = null, Vector4 userData = default(Vector4)
        ) {
            var drawCall = new BitmapDrawCall(textures, position);
            if (sourceRectangle1.HasValue)
                drawCall.TextureRegion = textures.Texture1.Instance.BoundsFromRectangle(sourceRectangle1.Value);
            if (sourceRectangle2.HasValue)
                drawCall.TextureRegion2 = textures.Texture2.Instance.BoundsFromRectangle(sourceRectangle1.Value);
            else
                drawCall.TextureRegion2 = Bounds.Unit;

            drawCall.MultiplyColor = multiplyColor.GetValueOrDefault(Color.White);
            drawCall.AddColor = addColor;
            drawCall.Rotation = rotation;
            drawCall.UserData = userData;
            drawCall.Scale = scale ?? Vector2.One;
            drawCall.Origin = origin ?? Vector2.Zero;
            if (mirrorX || mirrorY)
                drawCall.Mirror(mirrorX, mirrorY);

            drawCall.SortKey = sortKey.GetValueOrDefault(NextSortKey);
            if (AutoIncrementSortKey)
                NextSortKey.Order += 1;

            return Draw(
                ref drawCall, layer: layer, worldSpace: worldSpace, blendState: blendState, 
                samplerState: samplerState, samplerState2: samplerState2,
                depthStencilState: depthStencilState, rasterizerState: rasterizerState,
                material: material
            );
        }

        public IBitmapBatch Draw (
            Texture2D texture, Rectangle destRectangle,
            Rectangle? sourceRectangle = null, Color? multiplyColor = null, Color addColor = default(Color),
            float rotation = 0, float originX = 0, float originY = 0,
            bool mirrorX = false, bool mirrorY = false, DrawCallSortKey? sortKey = null,
            int? layer = null, bool? worldSpace = null,
            BlendState blendState = null, SamplerState samplerState = null, SamplerState samplerState2 = null,
            DepthStencilState depthStencilState = null, RasterizerState rasterizerState = null,
            Material material = null, Vector4 userData = default(Vector4)
        ) {
            var drawCall = new BitmapDrawCall(texture, new Vector2(destRectangle.X, destRectangle.Y));
            if (sourceRectangle.HasValue) {
                var sr = sourceRectangle.Value;
                drawCall.TextureRegion = texture.BoundsFromRectangle(in sr);
                drawCall.Scale = new Vector2(destRectangle.Width / (float)sr.Width, destRectangle.Height / (float)sr.Height);
            } else {
                drawCall.Scale = new Vector2(destRectangle.Width / (float)texture.Width, destRectangle.Height / (float)texture.Height);
            }
            drawCall.MultiplyColor = multiplyColor.GetValueOrDefault(Color.White);
            drawCall.AddColor = addColor;
            drawCall.Rotation = rotation;
            drawCall.Origin = new Vector2(originX, originY);
            drawCall.UserData = userData;
            if (mirrorX || mirrorY)
                drawCall.Mirror(mirrorX, mirrorY);

            drawCall.SortKey = sortKey.GetValueOrDefault(NextSortKey);
            if (AutoIncrementSortKey)
                NextSortKey.Order += 1;

            return Draw(
                ref drawCall, layer: layer, worldSpace: worldSpace, blendState: blendState, 
                samplerState: samplerState, samplerState2: samplerState2,
                depthStencilState: depthStencilState, rasterizerState: rasterizerState, material: material
            );
        }

        public IBitmapBatch DrawMultiple (
            ArraySegment<BitmapDrawCall> drawCalls,
            Vector2? offset = null, Color? multiplyColor = null, Color? addColor = null, DrawCallSortKey? sortKey = null,
            int? layer = null, bool? worldSpace = null,
            BlendState blendState = null, SamplerState samplerState = null, SamplerState samplerState2 = null,
            DepthStencilState depthStencilState = null, RasterizerState rasterizerState = null,
            Material material = null, Vector2? scale = null, Vector4? userData = null,
            float? multiplyOpacity = null
        ) {
            var batch = GetBitmapBatch(
                layer, worldSpace, blendState, samplerState,
                depthStencilState ?? DepthStencilState, rasterizerState ?? RasterizerState,
                material ?? DefaultBitmapMaterial, samplerState2: samplerState2,
                capacity: drawCalls.Count
            );
            {
                if (LowPriorityMaterialOrdering) {
                    if (material != null)
                        material = Config.Materials.Get(
                            material, rasterizerState ?? RasterizerState, depthStencilState ?? DepthStencilState, blendState ?? BlendState
                        );
                    else
                        material = Config.Materials.GetBitmapMaterial(
                            worldSpace ?? WorldSpace, rasterizerState ?? RasterizerState, depthStencilState ?? DepthStencilState, blendState ?? BlendState, UseDiscard
                        );

                    var mmbb = (MultimaterialBitmapBatch)batch;
                    mmbb.AddRange(
                        drawCalls.Array, drawCalls.Offset, drawCalls.Count,
                        offset: offset, multiplyColor: multiplyColor, addColor: addColor, userData: userData, sortKey: sortKey,
                        scale: scale, customMaterial: material, 
                        samplerState1: samplerState, samplerState2: samplerState
                    );
                } else {
                    var bb = (BitmapBatch)batch;
                    bb.AddRange(
                        drawCalls.Array, drawCalls.Offset, drawCalls.Count,
                        offset: offset, multiplyColor: multiplyColor, addColor: addColor, userData: userData, sortKey: sortKey,
                        scale: scale, multiplyOpacity: multiplyOpacity
                    );
                }
            }
            return batch;
        }

        public void DrawString (
            SpriteFont font, AbstractString text,
            Vector2 position, Color? color = null, float scale = 1, DrawCallSortKey? sortKey = null,
            int characterSkipCount = 0, int characterLimit = int.MaxValue,
            int? layer = null, bool? worldSpace = null, bool alignToPixels = true,
            BlendState blendState = null, SamplerState samplerState = null, Material material = null,
            Color? addColor = null
        ) {
            using (var buffer = BufferPool<BitmapDrawCall>.Allocate(text.Length)) {
                var layout = font.LayoutString(
                    text, new ArraySegment<BitmapDrawCall>(buffer.Data),
                    position, color, scale, sortKey.GetValueOrDefault(NextSortKey),
                    characterSkipCount, characterLimit, alignToPixels: alignToPixels,
                    addColor: addColor
                );

                DrawMultiple(
                    layout,
                    layer: layer, worldSpace: worldSpace, blendState: blendState, samplerState: samplerState,
                    material: material
                );

                buffer.Clear();
            }
        }

        public void DrawString (
            IGlyphSource glyphSource, AbstractString text,
            Vector2 position, Color? color = null, float scale = 1, DrawCallSortKey? sortKey = null,
            int characterSkipCount = 0, int characterLimit = int.MaxValue,
            int? layer = null, bool? worldSpace = null, bool alignToPixels = true,
            BlendState blendState = null, SamplerState samplerState = null,
            Material material = null, Color? addColor = null
        ) {
            using (var buffer = BufferPool<BitmapDrawCall>.Allocate(text.Length)) {
                var layout = glyphSource.LayoutString(
                    text, new ArraySegment<BitmapDrawCall>(buffer.Data),
                    position, color, scale, sortKey.GetValueOrDefault(NextSortKey),
                    characterSkipCount, characterLimit, alignToPixels: alignToPixels,
                    addColor: addColor
                );

                DrawMultiple(
                    layout,
                    layer: layer, worldSpace: worldSpace, blendState: blendState, samplerState: samplerState,
                    material: material
                );

                buffer.Clear();
            }
        }

        /// <summary>
        /// Sets the current scissor rectangle.
        /// </summary>
        /// <param name="rectangle">The new scissor rectangle (will automatically be clipped to fit into the viewport), or null to reset to default.</param>
        /// <param name="intersect">If true, the new scissor rectangle will be intersected with the current scissor rectangle instead of replacing it.</param>
        public void SetScissor (Rectangle? rectangle, int? layer = null, bool intersect = false) {
            SetScissorBatch.AddNew(Config.Container, layer.GetValueOrDefault(Config.Layer), Config.Materials.SetScissor, rectangle, intersect);

            if (AutoIncrementLayer && !layer.HasValue)
                Config.Layer += 1;
        }

        public void SetViewport (Rectangle? rectangle, bool updateViewTransform, int? layer = null) {
            SetViewportBatch.AddNew(Config.Container, layer.GetValueOrDefault(Config.Layer), Config.Materials.SetViewport, rectangle, updateViewTransform, Config.Materials);

            if (AutoIncrementLayer && !layer.HasValue)
                Config.Layer += 1;
        }

        public void FillRectangle (
            Rectangle rectangle, Color fillColor,
            int? layer = null, bool? worldSpace = null,
            BlendState blendState = null, Material customMaterial = null
        ) {
            FillRectangle(new Bounds(rectangle), fillColor, layer, worldSpace, blendState, customMaterial);
        }

        public void FillRectangle (
            Bounds bounds, Color fillColor,
            int? layer = null, bool? worldSpace = null,
            BlendState blendState = null, Material customMaterial = null
        ) {
            using (var gb = GetGeometryBatch(layer, worldSpace, blendState, customMaterial))
                gb.AddFilledQuad(bounds, fillColor);
        }

        public void GradientFillRectangle (
            Bounds bounds, Color topLeft, Color topRight, Color bottomLeft, Color bottomRight,
            int? layer = null, bool? worldSpace = null,
            BlendState blendState = null, Material customMaterial = null
        ) {
            using (var gb = GetGeometryBatch(layer, worldSpace, blendState, customMaterial))
                gb.AddGradientFilledQuad(bounds, topLeft, topRight, bottomLeft, bottomRight);
        }

        public void OutlineRectangle (
            Rectangle rectangle, Color outlineColor,
            int? layer = null, bool? worldSpace = null,
            BlendState blendState = null
        ) {
            OutlineRectangle(new Bounds(rectangle), outlineColor, layer, worldSpace, blendState);
        }

        public void OutlineRectangle (
            Bounds bounds, Color outlineColor,
            int? layer = null, bool? worldSpace = null,
            BlendState blendState = null
        ) {
            using (var gb = GetGeometryBatch(layer, worldSpace, blendState))
                gb.AddOutlinedQuad(bounds, outlineColor);
        }

        public void DrawLine (
            Vector2 start, Vector2 end, Color lineColor,
            int? layer = null, bool? worldSpace = null,
            BlendState blendState = null
        ) {
            DrawLine(start, end, lineColor, lineColor, layer, worldSpace, blendState);
        }

        public void DrawLine (
            Vector2 start, Vector2 end, Color firstColor, Color secondColor,
            int? layer = null, bool? worldSpace = null,
            BlendState blendState = null
        ) {
            using (var gb = GetGeometryBatch(
                layer, worldSpace, blendState
            ))
                gb.AddLine(start, end, firstColor, secondColor);
        }

        public void DrawPoint (
            Vector2 position, Color color,
            int? layer = null, bool? worldSpace = null,
            BlendState blendState = null
        ) {
            using (var gb = GetGeometryBatch(
                layer, worldSpace, blendState
            ))
                gb.AddLine(position, position + Vector2.One, color, color);
        }

        public void FillCircle (
            Vector2 center, float innerRadius, float outerRadius, 
            Color innerColor, Color outerColor,
            int? layer = null, bool? worldSpace = null,
            BlendState blendState = null
        ) {
            using (var gb = GetGeometryBatch(
                layer, worldSpace, blendState
            ))
                gb.AddFilledRing(center, innerRadius, outerRadius, innerColor, outerColor);
        }

        public void FillRing (
            Vector2 center, Vector2 innerRadius, Vector2 outerRadius, 
            Color innerColorStart, Color outerColorStart, 
            Color? innerColorEnd = null, Color? outerColorEnd = null, 
            float startAngle = 0, float endAngle = (float)(Math.PI * 2),
            float quality = 0,
            int? layer = null, bool? worldSpace = null,
            BlendState blendState = null
        ) {
            using (var gb = GetGeometryBatch(
                layer, worldSpace, blendState
            ))
                gb.AddFilledRing(center, innerRadius, outerRadius, innerColorStart, outerColorStart, innerColorEnd, outerColorEnd, startAngle, endAngle, quality);
        }

        internal static float ConvertFillMode (RasterFillMode fillMode, float fillAngle) {
            float fillModeF = (int)fillMode;
            if (fillMode >= RasterFillMode.Angular) {
                fillAngle = Arithmetic.WrapExclusive(fillAngle, 0, 360);
                fillModeF += fillAngle;
            }
            return fillModeF;
        }

        private RasterShapeColorSpace RasterColorSpace {

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                if (GetFlag(ImperativeRendererFlags.RasterBlendInOkLabSpace))
                    return RasterShapeColorSpace.OkLab;
                else if (GetFlag(ImperativeRendererFlags.RasterBlendInLinearSpace))
                    return RasterShapeColorSpace.LinearRGB;
                else
                    return RasterShapeColorSpace.sRGB;
            }
        }

        public RasterShapeBatch RasterizeEllipse (
            Vector2 center, Vector2 radius, pSRGBColor innerColor, pSRGBColor? outerColor = null,
            RasterFillSettings fill = default, float? annularRadius = null,
            RasterShadowSettings? shadow = null,
            int? layer = null, bool? worldSpace = null, RasterShapeColorSpace? colorSpace = null,
            BlendState blendState = null, Texture2D texture = null,
            Bounds? textureRegion = null, SamplerState samplerState = null,
            RasterTextureSettings? textureSettings = null, Texture2D rampTexture = null,
            Vector2? rampUVOffset = null, int sortKey = 0, Vector2? gradientCenter = null,
            Quaternion? orientation = null
        ) {
            var rsb = GetRasterShapeBatch(
                layer, worldSpace, blendState, texture, samplerState, rampTexture, rampUVOffset
            );
            rsb.Add(new RasterShapeDrawCall {
                Type = RasterShapeType.Ellipse,
                SortKey = sortKey,
                WorldSpace = worldSpace ?? WorldSpace,
                A = center,
                B = radius,
                C = (gradientCenter ?? new Vector2(0.5f, 0.5f)) - new Vector2(0.5f, 0.5f),
                Radius = radius,
                OutlineSize = 0,
                InnerColor = innerColor,
                OuterColor = outerColor.GetValueOrDefault(innerColor),
                OutlineColor = outerColor.GetValueOrDefault(innerColor),
                GammaMinusOne = Config.RasterGammaMinusOne,
                BlendIn = colorSpace ?? RasterColorSpace,
                Fill = fill,
                AnnularRadius = annularRadius ?? 0,
                Shadow = shadow ?? default,
                TextureBounds = textureRegion ?? Bounds.Unit,
                TextureSettings = textureSettings ?? default(RasterTextureSettings),
                SoftOutline = RasterSoftOutlines,
                Orientation = orientation ?? default
            });
            return rsb;
        }

        public RasterShapeBatch RasterizeEllipse (
            Vector2 center, Vector2 radius, float outlineRadius,
            pSRGBColor innerColor, pSRGBColor outerColor, pSRGBColor outlineColor,
            RasterFillSettings fill = default, float? annularRadius = null,
            RasterShadowSettings? shadow = null,
            int? layer = null, bool? worldSpace = null, RasterShapeColorSpace? colorSpace = null,
            BlendState blendState = null, Texture2D texture = null,
            Bounds? textureRegion = null, SamplerState samplerState = null,
            RasterTextureSettings? textureSettings = null, Texture2D rampTexture = null,
            Vector2? rampUVOffset = null, int sortKey = 0, Vector2? gradientCenter = null,
            Quaternion? orientation = null
        ) {
            var rsb = GetRasterShapeBatch(
                layer, worldSpace, blendState, texture, samplerState, rampTexture, rampUVOffset
            );
            rsb.Add(new RasterShapeDrawCall {
                Type = RasterShapeType.Ellipse,
                SortKey = sortKey,
                WorldSpace = worldSpace ?? WorldSpace,
                A = center,
                B = radius,
                C = (gradientCenter ?? new Vector2(0.5f, 0.5f)) - new Vector2(0.5f, 0.5f),
                Radius = radius,
                OutlineSize = outlineRadius,
                InnerColor = innerColor,
                OuterColor = outerColor,
                OutlineColor = outlineColor,
                GammaMinusOne = Config.RasterGammaMinusOne,
                BlendIn = colorSpace ?? RasterColorSpace,
                Fill = fill,
                AnnularRadius = annularRadius ?? 0,
                Shadow = shadow ?? default,
                TextureBounds = textureRegion ?? Bounds.Unit,
                TextureSettings = textureSettings ?? default(RasterTextureSettings),
                SoftOutline = RasterSoftOutlines,
                Orientation = orientation ?? default
            });
            return rsb;
        }

        public RasterShapeBatch RasterizeLineSegment (
            Vector2 a, Vector2 b, float radius, pSRGBColor innerColor, pSRGBColor? outerColor = null,
            RasterFillSettings fill = default, float? annularRadius = null,
            RasterShadowSettings? shadow = null,
            int? layer = null, bool? worldSpace = null, RasterShapeColorSpace? colorSpace = null,
            BlendState blendState = null, Texture2D texture = null,
            Bounds? textureRegion = null, SamplerState samplerState = null,
            RasterTextureSettings? textureSettings = null, Texture2D rampTexture = null,
            Vector2? rampUVOffset = null, int sortKey = 0
        ) {
            var rsb = GetRasterShapeBatch(
                layer, worldSpace, blendState, texture, samplerState, rampTexture, rampUVOffset
            );
            rsb.Add(new RasterShapeDrawCall {
                Type = RasterShapeType.LineSegment,
                SortKey = sortKey,
                WorldSpace = worldSpace ?? WorldSpace,
                A = a, B = b,
                C = Vector2.Zero,
                Radius = new Vector2(radius, 0),
                OutlineSize = 0,
                InnerColor = innerColor,
                OuterColor = outerColor.GetValueOrDefault(innerColor),
                OutlineColor = outerColor.GetValueOrDefault(innerColor),
                GammaMinusOne = Config.RasterGammaMinusOne,
                BlendIn = colorSpace ?? RasterColorSpace,
                Fill = fill,
                AnnularRadius = annularRadius ?? 0,
                Shadow = shadow ?? default,
                TextureBounds = textureRegion ?? Bounds.Unit,
                TextureSettings = textureSettings ?? default(RasterTextureSettings),
                SoftOutline = RasterSoftOutlines
            });
            return rsb;
        }

        public RasterShapeBatch RasterizeLineSegment (
            Vector2 a, Vector2 b, float startRadius, float? endRadius, float outlineRadius,
            pSRGBColor innerColor, pSRGBColor outerColor, pSRGBColor outlineColor,
            RasterFillSettings fill = default, float? annularRadius = null,
            RasterShadowSettings? shadow = null,
            int? layer = null, bool? worldSpace = null, RasterShapeColorSpace? colorSpace = null,
            BlendState blendState = null, Texture2D texture = null,
            Bounds? textureRegion = null, SamplerState samplerState = null,
            RasterTextureSettings? textureSettings = null, Texture2D rampTexture = null,
            Vector2? rampUVOffset = null, int sortKey = 0
        ) {
            float _endRadius = endRadius.GetValueOrDefault(startRadius);
            float maxRadius = Math.Max(startRadius, _endRadius);

            var rsb = GetRasterShapeBatch(
                layer, worldSpace, blendState, texture, samplerState, rampTexture, rampUVOffset
            );
            rsb.Add(new RasterShapeDrawCall {
                Type = RasterShapeType.LineSegment,
                SortKey = sortKey,
                WorldSpace = worldSpace ?? WorldSpace,
                A = a, B = b,
                C = new Vector2(0, startRadius - maxRadius),
                Radius = new Vector2(maxRadius, _endRadius - maxRadius),
                OutlineSize = outlineRadius,
                InnerColor = innerColor,
                OuterColor = outerColor,
                OutlineColor = outlineColor,
                GammaMinusOne = Config.RasterGammaMinusOne,
                BlendIn = colorSpace ?? RasterColorSpace,
                Fill = fill,
                AnnularRadius = annularRadius ?? 0,
                Shadow = shadow.GetValueOrDefault(),
                TextureBounds = textureRegion ?? Bounds.Unit,
                TextureSettings = textureSettings ?? default(RasterTextureSettings),
                SoftOutline = RasterSoftOutlines
            });
            return rsb;
        }

        public RasterShapeBatch RasterizeRectangle (
            Vector2 tl, Vector2 br, float radius,
            pSRGBColor innerColor, pSRGBColor? outerColor = null,
            RasterFillSettings fill = default, float? annularRadius = null,
            RasterShadowSettings? shadow = null,
            int? layer = null, bool? worldSpace = null, RasterShapeColorSpace? colorSpace = null,
            BlendState blendState = null, Texture2D texture = null,
            Bounds? textureRegion = null, SamplerState samplerState = null,
            RasterTextureSettings? textureSettings = null, Texture2D rampTexture = null,
            Vector2? rampUVOffset = null, int sortKey = 0,
            Quaternion? orientation = null
        ) {
            var rsb = GetRasterShapeBatch(
                layer, worldSpace, blendState, texture, samplerState, rampTexture, rampUVOffset
            );
            rsb.Add(new RasterShapeDrawCall {
                Type = RasterShapeType.Rectangle,
                SortKey = sortKey,
                WorldSpace = worldSpace ?? WorldSpace,
                A = tl, B = br,
                C = new Vector2(radius),
                Radius = new Vector2(radius),
                OutlineSize = 0,
                InnerColor = innerColor,
                OuterColor = outerColor.GetValueOrDefault(innerColor),
                OutlineColor = outerColor.GetValueOrDefault(innerColor),
                GammaMinusOne = Config.RasterGammaMinusOne,
                BlendIn = colorSpace ?? RasterColorSpace,
                Fill = fill,
                AnnularRadius = annularRadius ?? 0,
                Shadow = shadow.GetValueOrDefault(),
                TextureBounds = textureRegion ?? Bounds.Unit,
                TextureSettings = textureSettings ?? default(RasterTextureSettings),
                SoftOutline = RasterSoftOutlines,
                Orientation = orientation ?? default
            });
            return rsb;
        }

        public RasterShapeBatch RasterizeRectangle (
            Vector2 tl, Vector2 br, float radius, float outlineRadius,
            pSRGBColor innerColor, pSRGBColor outerColor, pSRGBColor outlineColor,
            RasterFillSettings fill = default, float? annularRadius = null,
            RasterShadowSettings? shadow = null,
            int? layer = null, bool? worldSpace = null, RasterShapeColorSpace? colorSpace = null,
            BlendState blendState = null, Texture2D texture = null,
            Bounds? textureRegion = null, SamplerState samplerState = null,
            RasterTextureSettings? textureSettings = null, Texture2D rampTexture = null,
            Vector2? rampUVOffset = null, int sortKey = 0,
            Quaternion? orientation = null
        ) {
            var rsb = GetRasterShapeBatch(
                layer, worldSpace, blendState, texture, samplerState, rampTexture, rampUVOffset
            );
            rsb.Add(new RasterShapeDrawCall {
                Type = RasterShapeType.Rectangle,
                SortKey = sortKey,
                WorldSpace = worldSpace ?? WorldSpace,
                A = tl, B = br,
                C = new Vector2(radius),
                Radius = new Vector2(radius),
                OutlineSize = outlineRadius,
                InnerColor = innerColor,
                OuterColor = outerColor,
                OutlineColor = outlineColor,
                GammaMinusOne = Config.RasterGammaMinusOne,
                BlendIn = colorSpace ?? RasterColorSpace,
                Fill = fill,
                AnnularRadius = annularRadius ?? 0,
                Shadow = shadow.GetValueOrDefault(),
                TextureBounds = textureRegion ?? Bounds.Unit,
                TextureSettings = textureSettings ?? default(RasterTextureSettings),
                SoftOutline = RasterSoftOutlines,
                Orientation = orientation ?? default
            });
            return rsb;
        }

        public RasterShapeBatch RasterizeRectangle (
            Vector2 tl, Vector2 br, Vector4 radiusCW, float outlineRadius,
            pSRGBColor innerColor, pSRGBColor outerColor, pSRGBColor outlineColor,
            RasterFillSettings fill = default, float? annularRadius = null,
            RasterShadowSettings? shadow = null,
            int? layer = null, bool? worldSpace = null, RasterShapeColorSpace? colorSpace = null,
            BlendState blendState = null, Texture2D texture = null,
            Bounds? textureRegion = null, SamplerState samplerState = null,
            RasterTextureSettings? textureSettings = null, Texture2D rampTexture = null,
            Vector2? rampUVOffset = null, int sortKey = 0,
            Quaternion? orientation = null
        ) {
            var rsb = GetRasterShapeBatch(
                layer, worldSpace, blendState, texture, samplerState, rampTexture, rampUVOffset
            );
            rsb.Add(new RasterShapeDrawCall {
                Type = RasterShapeType.Rectangle,
                SortKey = sortKey,
                WorldSpace = worldSpace ?? WorldSpace,
                A = tl, B = br,
                C = new Vector2(radiusCW.X, radiusCW.Y),
                Radius = new Vector2(radiusCW.Z, radiusCW.W),
                OutlineSize = outlineRadius,
                InnerColor = innerColor,
                OuterColor = outerColor,
                OutlineColor = outlineColor,
                GammaMinusOne = Config.RasterGammaMinusOne,
                BlendIn = colorSpace ?? RasterColorSpace,
                Fill = fill,
                AnnularRadius = annularRadius ?? 0,
                Shadow = shadow.GetValueOrDefault(),
                TextureBounds = textureRegion ?? Bounds.Unit,
                TextureSettings = textureSettings ?? default(RasterTextureSettings),
                SoftOutline = RasterSoftOutlines,
                Orientation = orientation ?? default
            });
            return rsb;
        }

        public RasterShapeBatch RasterizeTriangle (
            Vector2 a, Vector2 b, Vector2 c, float radius, 
            pSRGBColor innerColor, pSRGBColor? outerColor = null,
            RasterFillSettings fill = default, float? annularRadius = null,
            RasterShadowSettings? shadow = null,
            int? layer = null, bool? worldSpace = null, RasterShapeColorSpace? colorSpace = null,
            BlendState blendState = null, Texture2D texture = null,
            Bounds? textureRegion = null, SamplerState samplerState = null,
            RasterTextureSettings? textureSettings = null, Texture2D rampTexture = null,
            Vector2? rampUVOffset = null, int sortKey = 0,
            Quaternion? orientation = null
        ) {
            var rsb = GetRasterShapeBatch(
                layer, worldSpace, blendState, texture, samplerState, rampTexture, rampUVOffset
            );
            rsb.Add(new RasterShapeDrawCall {
                Type = RasterShapeType.Triangle,
                SortKey = sortKey,
                WorldSpace = worldSpace ?? WorldSpace,
                A = a, B = b, C = c,
                Radius = new Vector2(radius, fill.Offset),
                OutlineSize = 0,
                InnerColor = innerColor,
                OuterColor = outerColor.GetValueOrDefault(innerColor),
                OutlineColor = outerColor.GetValueOrDefault(innerColor),
                GammaMinusOne = Config.RasterGammaMinusOne,
                BlendIn = colorSpace ?? RasterColorSpace,
                Fill = fill,
                AnnularRadius = annularRadius ?? 0,
                Shadow = shadow.GetValueOrDefault(),
                TextureBounds = textureRegion ?? Bounds.Unit,
                TextureSettings = textureSettings ?? default(RasterTextureSettings),
                SoftOutline = RasterSoftOutlines,
                Orientation = orientation ?? default
            });
            return rsb;
        }

        public RasterShapeBatch RasterizeTriangle (
            Vector2 a, Vector2 b, Vector2 c, float radius, float outlineRadius,
            pSRGBColor innerColor, pSRGBColor outerColor, pSRGBColor outlineColor,
            RasterFillSettings fill = default, float? annularRadius = null,
            RasterShadowSettings? shadow = null,
            int? layer = null, bool? worldSpace = null, RasterShapeColorSpace? colorSpace = null,
            BlendState blendState = null, Texture2D texture = null,
            Bounds? textureRegion = null, SamplerState samplerState = null,
            RasterTextureSettings? textureSettings = null, Texture2D rampTexture = null,
            Vector2? rampUVOffset = null, int sortKey = 0,
            Quaternion? orientation = null
        ) {
            var rsb = GetRasterShapeBatch(
                layer, worldSpace, blendState, texture, samplerState, rampTexture, rampUVOffset
            );
            rsb.Add(new RasterShapeDrawCall {
                Type = RasterShapeType.Triangle,
                SortKey = sortKey,
                WorldSpace = worldSpace ?? WorldSpace,
                A = a, B = b, C = c,
                Radius = new Vector2(radius, fill.Offset),
                OutlineSize = outlineRadius,
                InnerColor = innerColor,
                OuterColor = outerColor,
                OutlineColor = outlineColor,
                GammaMinusOne = Config.RasterGammaMinusOne,
                BlendIn = colorSpace ?? RasterColorSpace,
                Fill = fill,
                AnnularRadius = annularRadius ?? 0,
                Shadow = shadow.GetValueOrDefault(),
                TextureBounds = textureRegion ?? Bounds.Unit,
                TextureSettings = textureSettings ?? default(RasterTextureSettings),
                SoftOutline = RasterSoftOutlines,
                Orientation = orientation ?? default
            });
            return rsb;
        }

        public RasterShapeBatch RasterizeQuadraticBezier (
            Vector2 a, Vector2 b, Vector2 c, float radius, pSRGBColor color,
            RasterFillSettings fill = default, float? annularRadius = null,
            RasterShadowSettings? shadow = null,
            int? layer = null, bool? worldSpace = null, RasterShapeColorSpace? colorSpace = null,
            BlendState blendState = null, Texture2D texture = null,
            Bounds? textureRegion = null, SamplerState samplerState = null,
            RasterTextureSettings? textureSettings = null, Texture2D rampTexture = null,
            Vector2? rampUVOffset = null, int sortKey = 0
        ) {
            var rsb = GetRasterShapeBatch(
                layer, worldSpace, blendState, texture, samplerState, rampTexture, rampUVOffset
            );
            rsb.Add(new RasterShapeDrawCall {
                Type = RasterShapeType.QuadraticBezier,
                SortKey = sortKey,
                WorldSpace = worldSpace ?? WorldSpace,
                A = a, B = b, C = c,
                Radius = new Vector2(radius),
                OutlineSize = 0,
                InnerColor = color,
                OuterColor = color,
                OutlineColor = color,
                GammaMinusOne = Config.RasterGammaMinusOne,
                BlendIn = colorSpace ?? RasterColorSpace,
                Fill = fill,
                AnnularRadius = annularRadius ?? 0,
                Shadow = shadow.GetValueOrDefault(),
                TextureBounds = textureRegion ?? Bounds.Unit,
                TextureSettings = textureSettings ?? default(RasterTextureSettings),
                SoftOutline = RasterSoftOutlines
            });
            return rsb;
        }

        public RasterShapeBatch RasterizePolygon (
            ArraySegment<RasterPolygonVertex> vertices, bool closed,
            float radius, float outlineRadius,
            pSRGBColor innerColor, pSRGBColor outerColor, pSRGBColor outlineColor,
            Vector2 offset = default, RasterFillSettings fill = default, float? annularRadius = null,
            RasterShadowSettings? shadow = null,
            int? layer = null, bool? worldSpace = null, RasterShapeColorSpace? colorSpace = null,
            BlendState blendState = null, Texture2D texture = null,
            Bounds? textureRegion = null, SamplerState samplerState = null,
            RasterTextureSettings? textureSettings = null, Texture2D rampTexture = null,
            Vector2? rampUVOffset = null, int sortKey = 0,
            Matrix? vertexTransform = null, 
            Func<RasterPolygonVertex, RasterPolygonVertex> vertexModifier = null,
            Quaternion? orientation = null
        ) {
            if (vertices.Count < 2)
                return null;
            if (vertices.Count > 255)
                throw new ArgumentOutOfRangeException("vertices.Count", "Vertex count may not exceed 255");

            var rsb = GetRasterShapeBatch(
                layer, worldSpace, blendState, texture, samplerState, rampTexture, rampUVOffset
            );
            rsb.AddPolygonVertices(vertices, out int indexOffset, out int count, closed, vertexTransform, vertexModifier);
            rsb.Add(new RasterShapeDrawCall {
                Type = RasterShapeType.Polygon,
                SortKey = sortKey,
                WorldSpace = worldSpace ?? WorldSpace,
                PolygonIndexOffset = indexOffset,
                PolygonVertexCount = count,
                B = new Vector2(closed ? 1f : 0f, 0f),
                C = offset,
                Radius = new Vector2(radius, 0),
                OutlineSize = outlineRadius,
                InnerColor = innerColor,
                OuterColor = outerColor,
                OutlineColor = outlineColor,
                GammaMinusOne = Config.RasterGammaMinusOne,
                BlendIn = colorSpace ?? RasterColorSpace,
                Fill = fill,
                AnnularRadius = annularRadius ?? 0,
                Shadow = shadow.GetValueOrDefault(),
                TextureBounds = textureRegion ?? Bounds.Unit,
                TextureSettings = textureSettings ?? default(RasterTextureSettings),
                SoftOutline = RasterSoftOutlines,
                Orientation = orientation ?? default
            });
            return rsb;
        }

        public RasterShapeBatch RasterizeQuadraticBezier (
            Vector2 a, Vector2 b, Vector2 c, float radius, float outlineRadius,
            pSRGBColor innerColor, pSRGBColor outerColor, pSRGBColor outlineColor,
            RasterFillSettings fill = default, float? annularRadius = null,
            RasterShadowSettings? shadow = null,
            int? layer = null, bool? worldSpace = null, RasterShapeColorSpace? colorSpace = null,
            BlendState blendState = null, Texture2D texture = null,
            Bounds? textureRegion = null, SamplerState samplerState = null,
            RasterTextureSettings? textureSettings = null, Texture2D rampTexture = null,
            Vector2? rampUVOffset = null, int sortKey = 0
        ) {
            var rsb = GetRasterShapeBatch(
                layer, worldSpace, blendState, texture, samplerState, rampTexture, rampUVOffset
            );
            rsb.Add(new RasterShapeDrawCall {
                Type = RasterShapeType.QuadraticBezier,
                SortKey = sortKey,
                WorldSpace = worldSpace ?? WorldSpace,
                A = a, B = b, C = c,
                Radius = new Vector2(radius),
                OutlineSize = outlineRadius,
                InnerColor = innerColor,
                OuterColor = outerColor,
                OutlineColor = outlineColor,
                GammaMinusOne = Config.RasterGammaMinusOne,
                BlendIn = colorSpace ?? RasterColorSpace,
                Fill = fill,
                AnnularRadius = annularRadius ?? 0,
                Shadow = shadow.GetValueOrDefault(),
                TextureBounds = textureRegion ?? Bounds.Unit,
                TextureSettings = textureSettings ?? default(RasterTextureSettings),
                SoftOutline = RasterSoftOutlines
            });
            return rsb;
        }

        /// <param name="center">The center point of the shape.</param>
        /// <param name="startAngleDegrees">The start angle of the arc (in degrees)</param>
        /// <param name="sizeDegrees">The length of the arc (in degrees), relative to the start angle. A start of 80 + a length of 20 = an arc from 80 to 100.</param>
        /// <param name="ringRadius">The distance from the center point at which the ring will be centered.</param>
        /// <param name="fillRadius">The radius of the body of the ring (it will be centered around the circle located at ringRadius).</param>
        /// <param name="outlineRadius">The radius of the outline at the outside of the body of the ring.</param>
        public RasterShapeBatch RasterizeArc (
            Vector2 center, float startAngleDegrees, float sizeDegrees, 
            float ringRadius, float fillRadius, float outlineRadius,
            pSRGBColor innerColor, pSRGBColor? outerColor = null, pSRGBColor? outlineColor = null, 
            RasterFillSettings fill = default,
            float? annularRadius = null, RasterShadowSettings? shadow = null,
            int? layer = null, bool? worldSpace = null, RasterShapeColorSpace? colorSpace = null,
            BlendState blendState = null, Texture2D texture = null,
            Bounds? textureRegion = null, SamplerState samplerState = null,
            RasterTextureSettings? textureSettings = null, Texture2D rampTexture = null,
            Vector2? rampUVOffset = null, float endRounding = 1f, int sortKey = 0
        ) {
            var centerAngleDegrees = (startAngleDegrees + (sizeDegrees / 2)) % 360;
            var offsetAngleDegrees = (startAngleDegrees + 90) % 360;
            startAngleDegrees = startAngleDegrees % 360;
            var centerAngleRadians = MathHelper.ToRadians(centerAngleDegrees);
            var sizeRadians = MathHelper.ToRadians(sizeDegrees);
            Vector2 b = new Vector2(centerAngleRadians, sizeRadians / 2f), c = new Vector2(0, 1.0f - endRounding);
            if (fill.Mode == RasterFillMode.Along) {
                // HACK: Bump the start and end angles out to account for the radius of the arc itself,
                //  otherwise we get gross hard cut-offs at the start and end
                var p1 = new Vector2(0, ringRadius);
                // FIXME: Do a smooth transition here as the rounding decreases? Probably not necessary
                var totalRadius = (endRounding > 0)
                    ? (outlineRadius + fillRadius + (annularRadius ?? 0)) * 1.1f
                    : 0f;
                var p2 = p1 + new Vector2(totalRadius, 0);
                var roundingOffsetRadians = (float)Math.Abs(Math.Atan2(p2.Y, p2.X) - Math.Atan2(p1.Y, p1.X));
                var fillSizeBias = (float)((sizeRadians + (roundingOffsetRadians * 2)) / (Math.PI * 2));
                if (fillSizeBias > 1)
                    fillSizeBias = 1;
                fill.Size *= fillSizeBias;
                c.X = MathHelper.ToRadians(offsetAngleDegrees) - roundingOffsetRadians;
            }

            if (b.Y >= MathHelper.ToRadians(179.9f))
                c.Y = 0;

            var rsb = GetRasterShapeBatch(
                layer, worldSpace, blendState, texture, samplerState, rampTexture, rampUVOffset
            );
            rsb.Add(new RasterShapeDrawCall {
                Type = RasterShapeType.Arc,
                SortKey = sortKey,
                WorldSpace = worldSpace ?? WorldSpace,
                A = center, B = b, C = c,
                // HACK: Inverse order because the shader uses radius.x for bounding box math
                Radius = new Vector2(ringRadius, fillRadius),
                OutlineSize = outlineRadius,
                InnerColor = innerColor,
                OuterColor = outerColor.GetValueOrDefault(innerColor),
                OutlineColor = outlineColor.GetValueOrDefault(Color.Transparent),
                GammaMinusOne = Config.RasterGammaMinusOne,
                BlendIn = colorSpace ?? RasterColorSpace,
                Fill = fill,
                AnnularRadius = annularRadius ?? 0,
                Shadow = shadow.GetValueOrDefault(),
                TextureBounds = textureRegion ?? Bounds.Unit,
                TextureSettings = textureSettings ?? default(RasterTextureSettings),
                SoftOutline = RasterSoftOutlines
            });
            return rsb;
        }
        
        public RasterShapeBatch RasterizeStar (
            Vector2 center, float radius, int count, float m, float outlineRadius,
            pSRGBColor innerColor, pSRGBColor? outerColor = null, pSRGBColor? outlineColor = null, 
            RasterFillSettings fill = default, float? annularRadius = null, RasterShadowSettings? shadow = null,
            int? layer = null, bool? worldSpace = null, RasterShapeColorSpace? colorSpace = null,
            BlendState blendState = null, Texture2D texture = null,
            Bounds? textureRegion = null, SamplerState samplerState = null,
            RasterTextureSettings? textureSettings = null, Texture2D rampTexture = null,
            Vector2? rampUVOffset = null, int sortKey = 0, Quaternion? orientation = null,
            float tapering = 0, float twirling = 0
        ) {
            m = Arithmetic.Clamp(m, 2, count);

            var rsb = GetRasterShapeBatch(
                layer, worldSpace, blendState, texture, samplerState, rampTexture, rampUVOffset
            );
            rsb.Add(new RasterShapeDrawCall {
                Type = RasterShapeType.Star,
                SortKey = sortKey,
                WorldSpace = worldSpace ?? WorldSpace,
                A = center, B = new Vector2(count, m),
                C = new Vector2(twirling, 0f),
                Radius = new Vector2(radius, tapering),
                OutlineSize = outlineRadius,
                InnerColor = innerColor,
                OuterColor = outerColor.GetValueOrDefault(innerColor),
                OutlineColor = outlineColor.GetValueOrDefault(Color.Transparent),
                GammaMinusOne = Config.RasterGammaMinusOne,
                BlendIn = colorSpace ?? RasterColorSpace,
                Fill = fill,
                AnnularRadius = annularRadius ?? 0,
                Shadow = shadow.GetValueOrDefault(),
                TextureBounds = textureRegion ?? Bounds.Unit,
                TextureSettings = textureSettings ?? default(RasterTextureSettings),
                SoftOutline = RasterSoftOutlines,
                Orientation = orientation ?? default
            });
            return rsb;
        }

        public IBitmapBatch GetBitmapBatch (
            int? layer = null, bool? worldSpace = null, BlendState blendState = null, 
            SamplerState samplerState = null, DepthStencilState depthStencilState = null, 
            RasterizerState rasterizerState = null, Material customMaterial = null, SamplerState samplerState2 = null,
            int? capacity = null
        ) {
            if (Config.Materials == null)
                throw new InvalidOperationException("You cannot use the argumentless ImperativeRenderer constructor.");
            if (Container.FrameIndex != Config.FrameIndex)
                throw new Exception("This renderer was created for a previous frame");

            var actualLayer = layer.GetValueOrDefault(Config.Layer);
            var actualWorldSpace = worldSpace.GetValueOrDefault(WorldSpace);
            var desiredBlendState = blendState ?? BlendState;
            var desiredSamplerState1 = samplerState ?? Config.SamplerState;
            var desiredSamplerState2 = samplerState2 ?? Config.SamplerState2;

            if (LowPriorityMaterialOrdering)
                desiredSamplerState1 = desiredSamplerState2 = null;

            CachedBatch cacheEntry;
            if (!TryGetCachedBatch<IBitmapBatch>(
                out cacheEntry,
                LowPriorityMaterialOrdering ? CachedBatchType.MultimaterialBitmap : CachedBatchType.Bitmap,
                layer: actualLayer,
                worldSpace: actualWorldSpace,
                rasterizerState: rasterizerState ?? RasterizerState,
                depthStencilState: depthStencilState ?? DepthStencilState,
                blendState: desiredBlendState,
                samplerState1: desiredSamplerState1,
                samplerState2: desiredSamplerState2,
                extraData: customMaterial
            ) || ((IBitmapBatch)cacheEntry.Batch).BitmapMarginSize != Config.BitmapMarginSize) {
                Material material;

                if (customMaterial != null) {
                    material = Config.Materials.Get(
                        customMaterial, rasterizerState ?? Config.RasterizerState, depthStencilState ?? Config.DepthStencilState, desiredBlendState
                    );
                } else {
                    material = Config.Materials.GetBitmapMaterial(
                        actualWorldSpace,
                        rasterizerState ?? Config.RasterizerState, depthStencilState ?? Config.DepthStencilState, desiredBlendState, UseDiscard
                    );
                }

                IBitmapBatch bb;
                if (LowPriorityMaterialOrdering) {
                    var mmbb = MultimaterialBitmapBatch.New(
                        Config.Container, actualLayer, Material.Null, 
                        useZBuffer: UseZBuffer, depthPrePass: DepthPrePass, worldSpace: actualWorldSpace
                    );
                    mmbb.MaterialParameters.ReplaceWith(ref Parameters);
                    bb = mmbb;
                } else {
                    var _bb = BitmapBatch.New(
                        Config.Container, actualLayer, material, 
                        samplerState: desiredSamplerState1, samplerState2: desiredSamplerState2, 
                        useZBuffer: UseZBuffer, zBufferOnlySorting: ZBufferOnlySorting, 
                        depthPrePass: DepthPrePass, worldSpace: actualWorldSpace,
                        capacity: capacity
                    );
                    _bb.MaterialParameters.ReplaceWith(ref Parameters);
                    bb = _bb;
                }

                bb.BitmapMarginSize = BitmapMarginSize;
                bb.Sorter = DeclarativeSorter;
                cacheEntry.Batch = (Batch)bb;
                Cache.InsertAtFront(ref cacheEntry, -1);
            }

            if (AutoIncrementLayer && !layer.HasValue)
                Config.Layer += 1;

            return (IBitmapBatch)cacheEntry.Batch;
        }

        public GeometryBatch GetGeometryBatch (int? layer, bool? worldSpace, BlendState blendState, Material customMaterial = null) {
            if (Config.Materials == null)
                throw new InvalidOperationException("You cannot use the argumentless ImperativeRenderer constructor.");
            if (Container.FrameIndex != Config.FrameIndex)
                throw new Exception("This renderer was created for a previous frame");

            var actualLayer = layer.GetValueOrDefault(Config.Layer);
            var actualWorldSpace = worldSpace.GetValueOrDefault(WorldSpace);
            var desiredBlendState = blendState ?? BlendState;

            CachedBatch cacheEntry;
            if (!TryGetCachedBatch<GeometryBatch>(
                out cacheEntry,
                CachedBatchType.Geometry,
                layer: actualLayer,
                worldSpace: actualWorldSpace,
                rasterizerState: Config.RasterizerState,
                depthStencilState: Config.DepthStencilState,
                blendState: desiredBlendState,
                samplerState1: null,
                samplerState2: null,
                extraData: customMaterial
            )) {
                Material material;

                if (customMaterial != null) {
                    material = Config.Materials.Get(
                        customMaterial, Config.RasterizerState, Config.DepthStencilState, desiredBlendState
                    );
                } else {
                    material = Config.Materials.GetGeometryMaterial(
                        actualWorldSpace,
                        rasterizerState: Config.RasterizerState,
                        depthStencilState: Config.DepthStencilState,
                        blendState: desiredBlendState
                    );
                }

                var b = GeometryBatch.New(Config.Container, actualLayer, material);
                b.Dispose();
                b.MaterialParameters.ReplaceWith(ref Parameters);
                cacheEntry.Batch = b;
                Cache.InsertAtFront(ref cacheEntry, -1);
            }

            if (AutoIncrementLayer && !layer.HasValue)
                Config.Layer += 1;

            return (GeometryBatch)cacheEntry.Batch;
        }

        public RasterShapeBatch GetRasterShapeBatch (
            int? layer = null, bool? worldSpace = null, BlendState blendState = null, Texture2D texture = null, 
            SamplerState samplerState = null, Texture2D rampTexture = null, Vector2? rampUVOffset = null
        ) {
            if (Config.Materials == null)
                throw new InvalidOperationException("You cannot use the argumentless ImperativeRenderer constructor.");
            if (Container.FrameIndex != Config.FrameIndex)
                throw new Exception("This renderer was created for a previous frame");

            var actualLayer = layer.GetValueOrDefault(Config.Layer);
            var actualWorldSpace = worldSpace.GetValueOrDefault(WorldSpace);
            var desiredBlendState = blendState ?? BlendState;
            var desiredSamplerState = samplerState ?? Config.SamplerState;

            // HACK: Look, alright, it's complicated
            if (
                (desiredBlendState == BlendState.AlphaBlend) ||
                (desiredBlendState == BlendState.NonPremultiplied) ||
                (desiredBlendState == null)
            )
                desiredBlendState = RenderStates.RasterShapeAlphaBlend;
            else if (
                (desiredBlendState == BlendState.Additive) ||
                (desiredBlendState == RenderStates.AdditiveBlend) ||
                (desiredBlendState == RenderStates.AdditiveBlendNonPremultiplied)
            )
                desiredBlendState = RenderStates.RasterShapeAdditiveBlend;
            else if (desiredBlendState == RenderStates.SubtractiveBlend)
                desiredBlendState = RenderStates.RasterShapeSubtractiveBlend;
            else if (desiredBlendState == RenderStates.MaxBlendValue)
                desiredBlendState = RenderStates.RasterShapeMaxBlend;
            else
                ;

            CachedBatch cacheEntry;
            if (!TryGetCachedBatch<RasterShapeBatch>(
                out cacheEntry,
                CachedBatchType.RasterShape,
                layer: actualLayer,
                worldSpace: actualWorldSpace,
                rasterizerState: Config.RasterizerState,
                depthStencilState: Config.DepthStencilState,
                blendState: desiredBlendState,
                samplerState1: desiredSamplerState,
                samplerState2: null,
                extraData: texture
            ) || (((RasterShapeBatch)cacheEntry.Batch).RampTexture != rampTexture)
              || (((RasterShapeBatch)cacheEntry.Batch).RampUVOffset != (rampUVOffset ?? Vector2.Zero))
              || (((RasterShapeBatch)cacheEntry.Batch).DitheringSettings.HasValue != DisableDithering)
              || !((RasterShapeBatch)cacheEntry.Batch).CompositesEqual(Config.RasterComposites)
            ) {
                // FIXME: The way this works will cause churn when mixing textured and untextured shape batches
                //  oh well
                var batch = RasterShapeBatch.New(
                    Config.Container, actualLayer, Config.Materials, texture, desiredSamplerState,
                    Config.RasterizerState, Config.DepthStencilState, desiredBlendState, rampTexture,
                    rampUVOffset
                );
                batch.Composites = Config.RasterComposites;
                batch.Dispose();
                if (DisableDithering)
                    batch.DitheringSettings = DitheringSettings.Disable;
                else
                    batch.DitheringSettings = null;
                batch.MaterialParameters.ReplaceWith(ref Parameters);
                // FIXME: why the hell
                batch.UseUbershader = RasterUseUbershader;
                cacheEntry.Batch = batch;
                Cache.InsertAtFront(ref cacheEntry, -1);
            }

            if (AutoIncrementLayer && !layer.HasValue)
                Config.Layer += 1;

            return (RasterShapeBatch)cacheEntry.Batch;
        }

        public RasterStrokeBatch GetRasterStrokeBatch (
            int? layer, bool? worldSpace, BlendState blendState, ref RasterBrush brush
        ) {
            if (Config.Materials == null)
                throw new InvalidOperationException("You cannot use the argumentless ImperativeRenderer constructor.");

            var actualLayer = layer.GetValueOrDefault(Config.Layer);
            var actualWorldSpace = worldSpace.GetValueOrDefault(WorldSpace);
            var desiredBlendState = blendState ?? BlendState;

            // HACK: Look, alright, it's complicated
            if (
                (desiredBlendState == BlendState.AlphaBlend) || 
                (desiredBlendState == BlendState.NonPremultiplied) ||
                (desiredBlendState == null)
            )
                desiredBlendState = RenderStates.RasterShapeAlphaBlend;
            else if (desiredBlendState == BlendState.Additive)
                desiredBlendState = RenderStates.RasterShapeAdditiveBlend;
            else if (desiredBlendState == RenderStates.SubtractiveBlend)
                desiredBlendState = RenderStates.RasterShapeSubtractiveBlend;
            else if (desiredBlendState == RenderStates.MaxBlendValue)
                desiredBlendState = RenderStates.RasterShapeMaxBlend;

            CachedBatch cacheEntry;
            if (!TryGetCachedBatch<RasterStrokeBatch>(
                out cacheEntry,
                CachedBatchType.RasterStroke,
                layer: actualLayer,
                worldSpace: actualWorldSpace,
                rasterizerState: Config.RasterizerState,
                depthStencilState: Config.DepthStencilState,
                blendState: desiredBlendState,
                samplerState1: brush.NozzleSamplerState,
                samplerState2: null,
                extraData: brush.NozzleAtlas ?? brush.Ramp
            ) || !((RasterStrokeBatch)cacheEntry.Batch).Brush.Equals(ref brush)
              || !((RasterStrokeBatch)cacheEntry.Batch).DitheringSettings.HasValue != DisableDithering
            ) {
                var batch = RasterStrokeBatch.New(
                    Config.Container, actualLayer, Config.Materials, ref brush,
                    Config.RasterizerState, Config.DepthStencilState, desiredBlendState
                );
                batch.Dispose();
                if (DisableDithering)
                    batch.DitheringSettings = DitheringSettings.Disable;
                else
                    batch.DitheringSettings = null;
                batch.BlendInLinearSpace = RasterBlendInLinearSpace;
                batch.MaterialParameters.ReplaceWith(ref Parameters);
                cacheEntry.Batch = batch;
                Cache.InsertAtFront(ref cacheEntry, -1);
            }

            if (AutoIncrementLayer && !layer.HasValue)
                Config.Layer += 1;

            return (RasterStrokeBatch)cacheEntry.Batch;
        }

        /// <param name="biases">(Size, Flow, Hardness, Color)</param>
        public void RasterizeStroke (
            RasterStrokeType type, Vector2 a, Vector2 b, pSRGBColor colorA, pSRGBColor colorB, RasterBrush brush,
            float? seed = null, Vector4? taper = null, Vector4? biases = null, int? layer = null, bool? worldSpace = null,
            RasterShapeColorSpace? colorSpace = null, BlendState blendState = null, int sortKey = 0
        ) => RasterizeStroke(
            type, a, b, colorA, colorB, ref brush, 
            seed, taper, biases, layer, worldSpace, 
            colorSpace, blendState, sortKey
        );

        /// <param name="biases">(Size, Flow, Hardness, Color)</param>
        public void RasterizeStroke (
            RasterStrokeType type, Vector2 a, Vector2 b, pSRGBColor colorA, pSRGBColor colorB, ref RasterBrush brush,
            float? seed = null, Vector4? taper = null, Vector4? biases = null, int? layer = null, bool? worldSpace = null, 
            RasterShapeColorSpace? colorSpace = null, BlendState blendState = null, int sortKey = 0
        ) {
            using (var rsb = GetRasterStrokeBatch(
                layer, worldSpace, blendState, ref brush
            ))
                rsb.Add(new RasterStrokeDrawCall {
                    Type = type,
                    A = a,
                    B = b,
                    ColorA = colorA,
                    ColorB = colorB,
                    Seed = seed ?? rsb.Count,
                    TaperRanges = taper ?? default,
                    Biases = biases ?? default,
                    BlendIn = colorSpace ?? RasterShapeColorSpace.LinearRGB,
                    SortKey = sortKey,
                    WorldSpace = worldSpace ?? WorldSpace
                });
        }

        /// <param name="biases">(Size, Flow, Hardness, Color)</param>
        public void RasterizeStroke (
            ArraySegment<RasterPolygonVertex> vertices, pSRGBColor colorA, pSRGBColor colorB, RasterBrush brush,
            float? seed = null, Vector4? taper = null, Vector4? biases = null, int? layer = null, bool? worldSpace = null,
            RasterShapeColorSpace? colorSpace = null, BlendState blendState = null, int sortKey = 0,
            Matrix? vertexTransform = null, Func<RasterPolygonVertex, RasterPolygonVertex> vertexModifier = null
        ) => RasterizeStroke(
            vertices, colorA, colorB, ref brush, 
            seed, taper, biases, layer, worldSpace, 
            colorSpace, blendState, sortKey, 
            vertexTransform, vertexModifier
        );

        /// <param name="biases">(Size, Flow, Hardness, Color)</param>
        public bool RasterizeStroke (
            ArraySegment<RasterPolygonVertex> vertices, pSRGBColor colorA, pSRGBColor colorB, ref RasterBrush brush,
            float? seed = null, Vector4? taper = null, Vector4? biases = null, int? layer = null, bool? worldSpace = null, 
            RasterShapeColorSpace? colorSpace = null, BlendState blendState = null, int sortKey = 0,
            Matrix? vertexTransform = null, Func<RasterPolygonVertex, RasterPolygonVertex> vertexModifier = null
        ) {
            if (vertices.Count < 2)
                return false;
            if (vertices.Count > 2048)
                throw new ArgumentOutOfRangeException("vertices.Count", "Vertex count may not exceed 2048");

            using (var rsb = GetRasterStrokeBatch(
                layer, worldSpace, blendState, ref brush
            )) {
                var containsBezier = rsb.AddPolygonVertices(vertices, out int indexOffset, out int count, vertexTransform, vertexModifier);
                rsb.Add(new RasterStrokeDrawCall {
                    Type = RasterStrokeType.Polygon,
                    PolygonIndexOffset = indexOffset,
                    PolygonVertexCount = count,
                    ColorA = colorA,
                    ColorB = colorB,
                    Seed = seed ?? rsb.Count,
                    TaperRanges = taper ?? default,
                    Biases = biases ?? default,
                    BlendIn = colorSpace ?? RasterShapeColorSpace.LinearRGB,
                    SortKey = sortKey,
                    WorldSpace = worldSpace ?? WorldSpace,
                    ContainsBezier = containsBezier
                });
            }

            return true;
        }

        public override string ToString () {
            var callCount = 0;
            for (int i = 0; i < Cache.Count; i++) {
                ref var cb = ref CachedBatches.ItemAtIndex(ref Cache, i);
                var lb = cb.Batch as IListBatch;
                if (lb == null)
                    continue;

                callCount += lb.Count;
            }

            return string.Format("IR @ [c:{0} l:{1}] b: {2} c: {3}", Config.Container, Config.Layer, Cache.Count, callCount);
        }

        public void UnsafeCopySomeState (in ImperativeRenderer renderer) {
            Config.Layer = renderer.Layer;
            NextSortKey = renderer.NextSortKey;
            Cache = renderer.Cache;
        }
    }
}
