using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Game;
using Squared.Render.RasterShape;
using Squared.Render.Text;
using Squared.Util;
using Squared.Util.DeclarativeSort;

namespace Squared.Render.Convenience {
    public static class RenderStates {
        public static readonly BlendState SubtractiveBlend = new BlendState {
            AlphaBlendFunction = BlendFunction.Add,
            AlphaDestinationBlend = Blend.One,
            AlphaSourceBlend = Blend.One,
            ColorBlendFunction = BlendFunction.ReverseSubtract,
            ColorDestinationBlend = Blend.One,
            ColorSourceBlend = Blend.One
        };

        public static readonly BlendState SubtractiveBlendNonPremultiplied = new BlendState {
            AlphaBlendFunction = BlendFunction.Add,
            AlphaDestinationBlend = Blend.One,
            AlphaSourceBlend = Blend.One,
            ColorBlendFunction = BlendFunction.ReverseSubtract,
            ColorDestinationBlend = Blend.One,
            ColorSourceBlend = Blend.SourceAlpha
        };

        public static readonly BlendState AdditiveBlend = new BlendState {
            AlphaBlendFunction = BlendFunction.Add,
            AlphaDestinationBlend = Blend.One,
            AlphaSourceBlend = Blend.One,
            ColorBlendFunction = BlendFunction.Add,
            ColorDestinationBlend = Blend.One,
            ColorSourceBlend = Blend.One
        };

        public static readonly BlendState AdditiveBlendNonPremultiplied = new BlendState {
            AlphaBlendFunction = BlendFunction.Add,
            AlphaDestinationBlend = Blend.One,
            AlphaSourceBlend = Blend.One,
            ColorBlendFunction = BlendFunction.Add,
            ColorDestinationBlend = Blend.One,
            ColorSourceBlend = Blend.SourceAlpha
        };


        public static readonly BlendState RasterShapeAlphaBlend = new BlendState {
            AlphaBlendFunction = BlendFunction.Add,
            // FIXME: Is this right? It's at least better than nothing
            AlphaDestinationBlend = Blend.One,
            AlphaSourceBlend = Blend.One,
            ColorBlendFunction = BlendFunction.Add,
            ColorDestinationBlend = Blend.InverseSourceAlpha,
            ColorSourceBlend = Blend.SourceAlpha
        };

        public static readonly BlendState RasterShapeAdditiveBlend = new BlendState {
            AlphaBlendFunction = BlendFunction.Add,
            AlphaDestinationBlend = Blend.One,
            AlphaSourceBlend = Blend.One,
            ColorBlendFunction = BlendFunction.Add,
            ColorDestinationBlend = Blend.One,
            ColorSourceBlend = Blend.SourceAlpha
        };

        public static readonly BlendState RasterShapeSubtractiveBlend = new BlendState {
            AlphaBlendFunction = BlendFunction.Add,
            AlphaDestinationBlend = Blend.One,
            AlphaSourceBlend = Blend.One,
            ColorBlendFunction = BlendFunction.ReverseSubtract,
            ColorDestinationBlend = Blend.One,
            ColorSourceBlend = Blend.SourceAlpha
        };

        public static readonly BlendState RasterShapeMaxBlend = new BlendState {
            AlphaBlendFunction = BlendFunction.Add,
            AlphaDestinationBlend = Blend.One,
            AlphaSourceBlend = Blend.One,
            ColorBlendFunction = BlendFunction.Max,
            ColorDestinationBlend = Blend.One,
            ColorSourceBlend = Blend.SourceAlpha
        };


        public static readonly BlendState MaxBlendValue = new BlendState {
            AlphaBlendFunction = BlendFunction.Max,
            AlphaDestinationBlend = Blend.One,
            AlphaSourceBlend = Blend.One,
            ColorBlendFunction = BlendFunction.Max,
            ColorDestinationBlend = Blend.One,
            ColorSourceBlend = Blend.One
        };

        public static readonly BlendState MinBlendValue = new BlendState {
            AlphaBlendFunction = BlendFunction.Min,
            AlphaDestinationBlend = Blend.One,
            AlphaSourceBlend = Blend.One,
            ColorBlendFunction = BlendFunction.Min,
            ColorDestinationBlend = Blend.One,
            ColorSourceBlend = Blend.One
        };

        public static readonly BlendState MaxBlend = new BlendState {
            AlphaBlendFunction = BlendFunction.Add,
            AlphaDestinationBlend = Blend.One,
            AlphaSourceBlend = Blend.One,
            ColorBlendFunction = BlendFunction.Max,
            ColorDestinationBlend = Blend.One,
            ColorSourceBlend = Blend.One
        };

        public static readonly BlendState MinBlend = new BlendState {
            AlphaBlendFunction = BlendFunction.Add,
            AlphaDestinationBlend = Blend.One,
            AlphaSourceBlend = Blend.One,
            ColorBlendFunction = BlendFunction.Min,
            ColorDestinationBlend = Blend.One,
            ColorSourceBlend = Blend.One
        };

        public static readonly BlendState DrawNone = new BlendState {
            AlphaBlendFunction = BlendFunction.Add,
            AlphaDestinationBlend = Blend.One,
            AlphaSourceBlend = Blend.Zero,
            ColorBlendFunction = BlendFunction.Add,
            ColorDestinationBlend = Blend.One,
            ColorSourceBlend = Blend.Zero,
            ColorWriteChannels = ColorWriteChannels.None
        };

        public static readonly RasterizerState ScissorOnly = new RasterizerState {
            CullMode = CullMode.None,
            ScissorTestEnable = true
        };

        public static readonly DepthStencilState StencilWrite = new DepthStencilState {
            StencilEnable = true,
            StencilFunction = CompareFunction.Always,
            StencilPass = StencilOperation.Replace,
            ReferenceStencil = 1,
            DepthBufferEnable = false
        };

        public static readonly DepthStencilState StencilIntersection = new DepthStencilState {
            StencilEnable = true,
            StencilFunction = CompareFunction.Equal,
            StencilPass = StencilOperation.Keep,
            StencilFail = StencilOperation.Zero,
            ReferenceStencil = 1,
            DepthBufferEnable = false
        };

        public static readonly DepthStencilState StencilTest = new DepthStencilState {
            StencilEnable = true,
            StencilFunction = CompareFunction.NotEqual,
            StencilWriteMask = 0,
            StencilPass = StencilOperation.Keep,
            ReferenceStencil = 0,
            StencilFail = StencilOperation.Keep,
            DepthBufferEnable = false
        };

        /// <summary>
        /// Provides a sampler state appropriate for rendering text. The mip bias is adjusted to preserve sharpness.
        /// </summary>
        public static readonly SamplerState Text = new SamplerState {
            AddressU = TextureAddressMode.Clamp,
            AddressV = TextureAddressMode.Clamp,
            AddressW = TextureAddressMode.Clamp,
            Filter = TextureFilter.Linear,
            MipMapLevelOfDetailBias = -0.75f
        };
    }

    public class MaterialStateSet {
        public BlendState BlendState;
        public DepthStencilState DepthStencilState;
        public RasterizerState RasterizerState;
        public SamplerState SamplerState1, SamplerState2, SamplerState3;

        public void Apply (DeviceManager dm) {
            var dev = dm.Device;
            if (BlendState != null)
                dev.BlendState = BlendState;
            if (DepthStencilState != null)
                dev.DepthStencilState = DepthStencilState;
            if (RasterizerState != null)
                dev.RasterizerState = RasterizerState;
            if (SamplerState1 != null)
                dev.SamplerStates[0] = SamplerState1;
            if (SamplerState2 != null)
                dev.SamplerStates[1] = SamplerState2;
            if (SamplerState3 != null)
                dev.SamplerStates[2] = SamplerState3;
        }
    }

    public static class MaterialUtil {
        public static void ClearTexture (this EffectParameterCollection p, string parameterName) {
            if (p == null)
                return;
            var param = p[parameterName];
            if (param == null)
                return;
            param.SetValue((Texture2D)null);
        }

        public static void ClearTextures (this EffectParameterCollection p, params string[] parameterNames) {
            if (p == null)
                return;
            foreach (var name in parameterNames) {
                var param = p[name];
                if (param == null)
                    continue;
                param.SetValue((Texture2D)null);
            }
        }

        public static Action<DeviceManager> MakeDelegate (int index, SamplerState state) {
            return (dm) => { dm.Device.SamplerStates[index] = state; };
        }

        public static Action<DeviceManager> MakeDelegate (
            BlendState blendState = null,
            DepthStencilState depthStencilState = null, 
            RasterizerState rasterizerState = null, 
            SamplerState samplerState1 = null,
            SamplerState samplerState2 = null,
            SamplerState samplerState3 = null
        ) {
            var mss = new MaterialStateSet {
                RasterizerState = rasterizerState,
                DepthStencilState = depthStencilState,
                BlendState = blendState,
                SamplerState1 = samplerState1,
                SamplerState2 = samplerState2,
                SamplerState3 = samplerState3
            };
            return mss.Apply;
        }

        public static Material SetStates (
            this Material inner, 
            BlendState blendState = null,
            DepthStencilState depthStencilState = null, 
            RasterizerState rasterizerState = null, 
            SamplerState samplerState1 = null,
            SamplerState samplerState2 = null,
            SamplerState samplerState3 = null
        ) {
            var mss = new MaterialStateSet();

            var numBeginHandlers = (inner.BeginHandlers != null) ? inner.BeginHandlers.Length + 1 : 1;
            var handlers = new List<Action<DeviceManager>>(numBeginHandlers);
            if (inner.BeginHandlers != null)
            foreach (var bh in inner.BeginHandlers) {
                var bhs = bh.Target as MaterialStateSet;

                if (bhs != null) {
                    mss.RasterizerState = bhs.RasterizerState ?? mss.RasterizerState;
                    mss.DepthStencilState = bhs.DepthStencilState ?? mss.DepthStencilState;
                    mss.BlendState = bhs.BlendState ?? mss.BlendState;
                    mss.SamplerState1 = bhs.SamplerState1 ?? mss.SamplerState1;
                    mss.SamplerState2 = bhs.SamplerState2 ?? mss.SamplerState2;
                    mss.SamplerState3 = bhs.SamplerState3 ?? mss.SamplerState3;
                } else {
                    handlers.Add(bh);
                }
            }

            mss.BlendState = blendState ?? mss.BlendState;
            mss.DepthStencilState = depthStencilState ?? mss.DepthStencilState;
            mss.RasterizerState = rasterizerState ?? mss.RasterizerState;
            mss.SamplerState1 = samplerState1 ?? mss.SamplerState1;
            mss.SamplerState2 = samplerState2 ?? mss.SamplerState2;
            mss.SamplerState3 = samplerState3 ?? mss.SamplerState3;

            handlers.Add(mss.Apply);

            var result = new Material(
                inner.Effect, null,
                handlers.ToArray(), inner.EndHandlers
            ) {
                DelegatedHintPipeline = inner,
                Name = inner.Name
            };
            return result;
        }
    }

    public struct ImperativeRenderer {
        private struct CachedBatch {
            public IBatch Batch;

            private CachedBatchType BatchType;
            private IBatchContainer Container;
            private int Layer;
            private BlendState BlendState;
            private SamplerState SamplerState;
            private RasterizerState RasterizerState;
            private DepthStencilState DepthStencilState;
            private Material CustomMaterial;
            private bool WorldSpace;
            private bool UseZBuffer;
            private bool ZBufferOnlySorting;
            private bool DepthPrePass;

            public CachedBatch (
                CachedBatchType cbt,
                IBatchContainer container,
                int layer,
                bool worldSpace,
                RasterizerState rasterizerState,
                DepthStencilState depthStencilState,
                BlendState blendState,
                SamplerState samplerState,
                Material customMaterial,
                bool useZBuffer,
                bool zBufferOnlySorting,
                bool depthPrePass
            ) {
                Batch = null;
                BatchType = cbt;
                Container = container;
                Layer = layer;
                // FIXME: Mask if multimaterial?
                WorldSpace = worldSpace;
                UseZBuffer = useZBuffer;
                ZBufferOnlySorting = zBufferOnlySorting;
                DepthPrePass = depthPrePass;

                if (cbt != CachedBatchType.MultimaterialBitmap) {
                    RasterizerState = rasterizerState;
                    DepthStencilState = depthStencilState;
                    BlendState = blendState;
                    SamplerState = samplerState;
                    CustomMaterial = customMaterial;
                } else {
                    RasterizerState = null;
                    DepthStencilState = null;
                    BlendState = null;
                    SamplerState = null;
                    CustomMaterial = null;
                }
            }

            public bool KeysEqual (ref CachedBatch rhs) {
                var result = (
                    (BatchType == rhs.BatchType) &&
                    (Container == rhs.Container) &&
                    (Layer == rhs.Layer) &&
                    (WorldSpace == rhs.WorldSpace) &&
                    (BlendState == rhs.BlendState) &&
                    (UseZBuffer == rhs.UseZBuffer) &&
                    (ZBufferOnlySorting == rhs.ZBufferOnlySorting) &&
                    (RasterizerState == rhs.RasterizerState) &&
                    (DepthStencilState == rhs.DepthStencilState) &&
                    (CustomMaterial == rhs.CustomMaterial) &&
                    (DepthPrePass == rhs.DepthPrePass)
                );

                return result;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public override bool Equals (object obj) {
                if (obj is CachedBatch) {
                    var cb = (CachedBatch)obj;
                    return KeysEqual(ref cb);
                }

                return false;
            }

            public override string ToString () {
                return string.Format("{0} (layer={1} material={2})", Batch, Layer, CustomMaterial);
            }
        }

        private enum CachedBatchType {
            Bitmap,
            MultimaterialBitmap,
            Geometry,
            RasterShape
        }

        private struct CachedBatches {
            public const int Capacity = 4;

            public int Count;
            private CachedBatch Batch0, Batch1, Batch2, Batch3;

            public bool TryGet<T> (
                out CachedBatch result,
                CachedBatchType cbt,
                IBatchContainer container, 
                int layer, 
                bool worldSpace, 
                RasterizerState rasterizerState, 
                DepthStencilState depthStencilState, 
                BlendState blendState, 
                SamplerState samplerState, 
                Material customMaterial,
                bool useZBuffer,
                bool zBufferOnlySorting,
                bool depthPrePass
            ) {
                CachedBatch searchKey;

                searchKey = new CachedBatch(
                    cbt,
                    container,
                    layer,
                    worldSpace,
                    rasterizerState,
                    depthStencilState,
                    blendState,
                    samplerState,
                    customMaterial,
                    useZBuffer,
                    zBufferOnlySorting,
                    depthPrePass
                );

                int i;
                if (Batch0.KeysEqual(ref searchKey)) {
                    result = Batch0;
                    i = 0;
                } else if (Batch1.KeysEqual(ref searchKey)) {
                    result = Batch1;
                    i = 1;
                } else if (Batch2.KeysEqual(ref searchKey)) {
                    result = Batch2;
                    i = 2;
                } else if (Batch3.KeysEqual(ref searchKey)) {
                    result = Batch3;
                    i = 3;
                } else {
                    result = searchKey;
                    return false;
                }

                InsertAtFront(ref result, i);
                return (result.Batch != null);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void InsertAtFront (ref CachedBatch item, int previousIndex) {
                // No-op
                if (previousIndex == 0)
                    return;
                else if (Count == 0) {
                    SetItemAtIndex(0, ref item);
                    Count += 1;
                    return;
                }

                InsertAtFront_Slow(ref item, previousIndex);
            }

            private void InsertAtFront_Slow (ref CachedBatch item, int previousIndex) {
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

                    CachedBatch temp;
                    GetItemAtIndex(i, out temp);
                    SetItemAtIndex(writePosition, ref temp);
                    writePosition -= 1;
                }

                SetItemAtIndex(0, ref item);

                if (previousIndex < 0) {
                    if (Count < Capacity)
                        Count += 1;
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool GetItemAtIndex(int index, out CachedBatch result) {
                switch (index) {
                    default:
                        result = Batch0;
                        return (index == 0);
                    case 1:
                        result = Batch1;
                        return true;
                    case 2:
                        result = Batch2;
                        return true;
                    case 3:
                        result = Batch3;
                        return true;
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]        
            private bool SetItemAtIndex (int index, ref CachedBatch value) {
                switch (index) {
                    case 0:
                        Batch0 = value;
                        return true;
                    case 1:
                        Batch1 = value;
                        return true;
                    case 2:
                        Batch2 = value;
                        return true;
                    case 3:
                        Batch3 = value;
                        return true;
                    default:
                        return false;
                }
            }
        }

        public IBatchContainer Container;
        public DefaultMaterialSet Materials;

        public int Layer;
        public DepthStencilState DepthStencilState;
        public RasterizerState RasterizerState;
        public BlendState BlendState;
        public SamplerState SamplerState;

        /// <summary>
        /// Overrides the default material used to draw bitmaps if no custom material has been specified.
        /// </summary>
        public Material DefaultBitmapMaterial;

        /// <summary>
        /// Uses world-space coordinates.
        /// </summary>
        public bool WorldSpace;

        /// <summary>
        /// Generates z coordinates so that the z buffer can be used to order draw calls.
        /// </summary>
        public bool UseZBuffer;

        /// <summary>
        /// Disables draw call sorting and relies on the z buffer to maintain ordering.
        /// </summary>
        public bool ZBufferOnlySorting;

        /// <summary>
        /// If z-buffering is enabled, only a depth buffer generating pass will happen, not color rendering.
        /// </summary>
        public bool DepthPrePass;

        /// <summary>
        /// Increments the Layer after each drawing operation.
        /// </summary>
        public bool AutoIncrementLayer;

        /// <summary>
        /// Increments the sorting key's order value after each drawing operation.
        /// </summary>
        public bool AutoIncrementSortKey;

        /// <summary>
        /// If true, materials are last in the sort order instead of first.
        /// This allows precise ordering of bitmaps by sort key, regardless of material.
        /// </summary>
        public bool LowPriorityMaterialOrdering;

        /// <summary>
        /// Specifies a custom set of declarative sorting rules used to order draw calls.
        /// </summary>
        public Sorter<BitmapDrawCall> DeclarativeSorter;

        private DrawCallSortKey NextSortKey;
        private CachedBatches Cache;

        /// <summary>
        /// Bitmaps will use a shader with discard by default. Discard ensures transparent pixels are not drawn.
        /// </summary>
        public bool UseDiscard;

        private float RasterOutlineGammaMinusOne;

        /// <summary>
        /// If set, outlines on raster shapes will be soft instead of hard.
        /// </summary>
        public bool RasterSoftOutlines;

        /// <summary>
        /// If set, raster shapes will be drawn using the generic ubershader in a single large pass.
        /// This is slower for large shapes but produces fewer draw calls.
        /// </summary>
        public bool RasterUseUbershader;

        /// <summary>
        /// If true, raster shape colors will be converted from sRGB to linear space before
        ///  blending and then converted back to sRGB for rendering.
        /// If false, colors will be directly blended. This might look bad.
        /// </summary>
        public bool RasterBlendInLinearSpace;

        /// <summary>
        /// Specifies default shadow settings for raster shapes. Can be overridden on a per-call basis.
        /// </summary>
        public RasterShadowSettings RasterShadow;

        /// <summary>
        /// If set, newly created batches will be expanded to have capacity for this many items once they
        ///  contain more than a handful of items. If you know you will be drawing large numbers of items this 
        ///  can reduce overhead, as long as the vast majority of them end up in the same batch (via layer + state sorting).
        /// </summary>
        public int? BitmapBatchInitialCapacity;

        public ImperativeRenderer (
            IBatchContainer container,
            DefaultMaterialSet materials,
            int layer = 0, 
            RasterizerState rasterizerState = null,
            DepthStencilState depthStencilState = null,
            BlendState blendState = null,
            SamplerState samplerState = null,
            bool worldSpace = true,
            bool useZBuffer = false,
            bool autoIncrementSortKey = false,
            bool autoIncrementLayer = false,
            bool lowPriorityMaterialOrdering = false,
            bool zBufferOnlySorting = false,
            bool depthPrePass = false,
            Sorter<BitmapDrawCall> declarativeSorter = null,
            Tags tags = default(Tags)
        ) {
            if (container == null)
                throw new ArgumentNullException("container");
            if (materials == null)
                throw new ArgumentNullException("materials");

            Container = container;
            Materials = materials;
            Layer = layer;
            RasterizerState = rasterizerState;
            DepthStencilState = depthStencilState;
            BlendState = blendState;
            SamplerState = samplerState;
            UseZBuffer = useZBuffer;
            ZBufferOnlySorting = zBufferOnlySorting;
            DepthPrePass = depthPrePass;
            WorldSpace = worldSpace;
            AutoIncrementSortKey = autoIncrementSortKey;
            AutoIncrementLayer = autoIncrementLayer;
            NextSortKey = new DrawCallSortKey(tags, 0);
            Cache = new CachedBatches();
            LowPriorityMaterialOrdering = lowPriorityMaterialOrdering;
            DeclarativeSorter = declarativeSorter;
            RasterOutlineGammaMinusOne = 0;
            RasterBlendInLinearSpace = true;
            RasterSoftOutlines = false;
            RasterUseUbershader = false;
            RasterShadow = default(RasterShadowSettings);
            UseDiscard = false;
            DefaultBitmapMaterial = null;
            BitmapBatchInitialCapacity = null;
        }

        /// <summary>
        /// Applies gamma correction to outlines to make them look sharper.
        /// </summary>
        public float RasterOutlineGamma {
            get {
                return RasterOutlineGammaMinusOne + 1;
            }
            set {
                RasterOutlineGammaMinusOne = value - 1;
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
            DefaultMaterialSet materialSet = null, ViewTransform? viewTransform = null, string name = null
        ) {
            var result = this;
            var group = BatchGroup.New(
                Container, Layer, before: before, after: after, userData: userData,
                materialSet: materialSet, viewTransform: viewTransform, name: name
            );
            group.Dispose();
            result.Container = group;
            result.Layer = 0;
            result.LowPriorityMaterialOrdering = LowPriorityMaterialOrdering;

            if (nextLayer)
                Layer += 1;

            return result;
        }

        public ImperativeRenderer Clone (bool nextLayer = true) {
            var result = this;

            if (nextLayer)
                Layer += 1;

            return result;
        }

        public ImperativeRenderer ForRenderTarget (
            AutoRenderTarget renderTarget, Action<DeviceManager, object> before = null, Action<DeviceManager, object> after = null, 
            object userData = null, string name = null, int? layer = null, IBatchContainer newContainer = null
        ) {
            var result = this;
            var group = BatchGroup.ForRenderTarget(newContainer ?? Container, layer ?? Layer, renderTarget, before, after, userData, name: name);
            group.Dispose();
            result.Container = group;
            // FIXME: is this ever correct?
            result.Layer = 0;

            Layer += 1;

            return result;
        }

        public ImperativeRenderer ForRenderTarget (
            RenderTarget2D renderTarget, Action<DeviceManager, object> before = null, Action<DeviceManager, object> after = null, 
            object userData = null, string name = null, int? layer = null, IBatchContainer newContainer = null
        ) {
            var result = this;
            var group = BatchGroup.ForRenderTarget(newContainer ?? Container, layer ?? Layer, renderTarget, before, after, userData, name: name);
            group.Dispose();
            result.Container = group;
            // FIXME: is this ever correct?
            result.Layer = 0;

            Layer += 1;

            return result;
        }


        /// <summary>
        /// Adds a clear batch. Note that this will *always* advance the layer unless you specify a layer index explicitly.
        /// </summary>
        public void Clear (
            int? layer = null,
            Color? color = null,
            float? z = null,
            int? stencil = null
        ) {
            int _layer = layer.GetValueOrDefault(Layer);

            ClearBatch.AddNew(Container, _layer, Materials.Clear, color, z, stencil);

            if (!layer.HasValue)
                Layer += 1;
        }


        public void Draw (
            BitmapDrawCall drawCall, 
            int? layer = null, bool? worldSpace = null,
            BlendState blendState = null, SamplerState samplerState = null,
            DepthStencilState depthStencilState = null, RasterizerState rasterizerState = null,
            Material material = null
        ) {
            Draw(ref drawCall, layer, worldSpace, blendState, samplerState, depthStencilState, rasterizerState, material);
        }

        public void Draw (
            ref BitmapDrawCall drawCall, 
            int? layer = null, bool? worldSpace = null,
            BlendState blendState = null, SamplerState samplerState = null,
            DepthStencilState depthStencilState = null, RasterizerState rasterizerState = null,
            Material material = null
        ) {
            if (Container == null)
                throw new InvalidOperationException("You cannot use the argumentless ImperativeRenderer constructor.");
            else if (Container.IsReleased)
                throw new ObjectDisposedException("The container this ImperativeRenderer is drawing into has been disposed.");

            // FIXME: Mutates the argument :(
            if (worldSpace.HasValue)
                drawCall.WorldSpace = worldSpace;

            using (var batch = GetBitmapBatch(
                layer, worldSpace,
                blendState, samplerState, depthStencilState ?? DepthStencilState, rasterizerState ?? RasterizerState, material ?? DefaultBitmapMaterial
            )) {
                if (LowPriorityMaterialOrdering) {
                    if (material != null)
                        material = Materials.Get(material, rasterizerState ?? RasterizerState, depthStencilState ?? DepthStencilState, blendState ?? BlendState);
                    else
                        material = Materials.GetBitmapMaterial(worldSpace ?? WorldSpace, rasterizerState ?? RasterizerState, depthStencilState ?? DepthStencilState, blendState ?? BlendState, UseDiscard);

                    var mmbb = (MultimaterialBitmapBatch)batch;
                    mmbb.Add(ref drawCall, material, samplerState, samplerState);
                } else {
                    batch.Add(ref drawCall);
                }
            }
        }

        public void Draw (
            IDynamicTexture texture, Vector2 position,
            Rectangle? sourceRectangle = null, Color? multiplyColor = null, Color addColor = default(Color),
            float rotation = 0, Vector2? scale = null, Vector2 origin = default(Vector2),
            bool mirrorX = false, bool mirrorY = false, DrawCallSortKey? sortKey = null,
            int? layer = null, bool? worldSpace = null, 
            BlendState blendState = null, SamplerState samplerState = null,
            DepthStencilState depthStencilState = null, RasterizerState rasterizerState = null,
            Material material = null
        ) {
            var textureSet = new TextureSet(new AbstractTextureReference(texture));
            var textureRegion = Bounds.Unit;
            if (sourceRectangle.HasValue) {
                var sourceRectangleValue = sourceRectangle.Value;
                textureRegion = GameExtensionMethods.BoundsFromRectangle(texture.Width, texture.Height, ref sourceRectangleValue);
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

            Draw(ref drawCall, layer: layer, worldSpace: worldSpace, blendState: blendState, samplerState: samplerState, depthStencilState: depthStencilState, rasterizerState: rasterizerState, material: material);
        }

        public void Draw (
            Texture2D texture, Vector2 position,
            Rectangle? sourceRectangle = null, Color? multiplyColor = null, Color addColor = default(Color),
            float rotation = 0, Vector2? scale = null, Vector2 origin = default(Vector2),
            bool mirrorX = false, bool mirrorY = false, DrawCallSortKey? sortKey = null,
            int? layer = null, bool? worldSpace = null, 
            BlendState blendState = null, SamplerState samplerState = null,
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

            Draw(ref drawCall, layer: layer, worldSpace: worldSpace, blendState: blendState, samplerState: samplerState, depthStencilState: depthStencilState, rasterizerState: rasterizerState, material: material);
        }

        public void Draw (
            Texture2D texture, float x, float y,
            Rectangle? sourceRectangle = null, Color? multiplyColor = null, Color addColor = default(Color),
            float rotation = 0, float scaleX = 1, float scaleY = 1, float originX = 0, float originY = 0,
            bool mirrorX = false, bool mirrorY = false, DrawCallSortKey? sortKey = null,
            int? layer = null, bool? worldSpace = null,
            BlendState blendState = null, SamplerState samplerState = null,
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

            Draw(ref drawCall, layer: layer, worldSpace: worldSpace, blendState: blendState, samplerState: samplerState, depthStencilState: depthStencilState, rasterizerState: rasterizerState);
        }

        public void Draw (
            Texture2D texture, Rectangle destRectangle,
            Rectangle? sourceRectangle = null, Color? multiplyColor = null, Color addColor = default(Color),
            float rotation = 0, float originX = 0, float originY = 0,
            bool mirrorX = false, bool mirrorY = false, DrawCallSortKey? sortKey = null,
            int? layer = null, bool? worldSpace = null,
            BlendState blendState = null, SamplerState samplerState = null,
            DepthStencilState depthStencilState = null, RasterizerState rasterizerState = null,
            Material material = null, Vector4 userData = default(Vector4)
        ) {
            var drawCall = new BitmapDrawCall(texture, new Vector2(destRectangle.X, destRectangle.Y));
            if (sourceRectangle.HasValue) {
                var sr = sourceRectangle.Value;
                drawCall.TextureRegion = texture.BoundsFromRectangle(ref sr);
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

            Draw(ref drawCall, layer: layer, worldSpace: worldSpace, blendState: blendState, samplerState: samplerState, depthStencilState: depthStencilState, rasterizerState: rasterizerState, material: material);
        }

        public void DrawMultiple (
            ArraySegment<BitmapDrawCall> drawCalls,
            Vector2? offset = null, Color? multiplyColor = null, Color? addColor = null, DrawCallSortKey? sortKey = null,
            int? layer = null, bool? worldSpace = null,
            BlendState blendState = null, SamplerState samplerState = null, 
            DepthStencilState depthStencilState = null, RasterizerState rasterizerState = null,
            Material material = null, Vector2? scale = null, Vector4? userData = null
        ) {
            using (var batch = GetBitmapBatch(layer, worldSpace, blendState, samplerState, depthStencilState ?? DepthStencilState, rasterizerState ?? RasterizerState, material ?? DefaultBitmapMaterial)) {
                if (LowPriorityMaterialOrdering) {
                    if (material != null)
                        material = Materials.Get(material, rasterizerState ?? RasterizerState, depthStencilState ?? DepthStencilState, blendState ?? BlendState);
                    else
                        material = Materials.GetBitmapMaterial(worldSpace ?? WorldSpace, rasterizerState ?? RasterizerState, depthStencilState ?? DepthStencilState, blendState ?? BlendState, UseDiscard);

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
                        scale: scale
                    );
                }
            }
        }

        public void DrawString (
            SpriteFont font, string text,
            Vector2 position, Color? color = null, float scale = 1, DrawCallSortKey? sortKey = null,
            int characterSkipCount = 0, int characterLimit = int.MaxValue,
            int? layer = null, bool? worldSpace = null,
            BlendState blendState = null, SamplerState samplerState = null, Material material = null
        ) {
            using (var buffer = BufferPool<BitmapDrawCall>.Allocate(text.Length)) {
                var layout = font.LayoutString(
                    text, new ArraySegment<BitmapDrawCall>(buffer.Data),
                    position, color, scale, sortKey.GetValueOrDefault(NextSortKey),
                    characterSkipCount, characterLimit, alignToPixels: true
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
            IGlyphSource glyphSource, string text,
            Vector2 position, Color? color = null, float scale = 1, DrawCallSortKey? sortKey = null,
            int characterSkipCount = 0, int characterLimit = int.MaxValue,
            int? layer = null, bool? worldSpace = null,
            BlendState blendState = null, SamplerState samplerState = null,
            Material material = null
        ) {
            using (var buffer = BufferPool<BitmapDrawCall>.Allocate(text.Length)) {
                var layout = glyphSource.LayoutString(
                    text, new ArraySegment<BitmapDrawCall>(buffer.Data),
                    position, color, scale, sortKey.GetValueOrDefault(NextSortKey),
                    characterSkipCount, characterLimit, alignToPixels: true
                );

                DrawMultiple(
                    layout,
                    layer: layer, worldSpace: worldSpace, blendState: blendState, samplerState: samplerState,
                    material: material
                );

                buffer.Clear();
            }
        }

        public void SetScissor (Rectangle? rectangle, int? layer = null) {
            SetScissorBatch.AddNew(Container, layer.GetValueOrDefault(Layer), Materials.SetScissor, rectangle);

            if (AutoIncrementLayer && !layer.HasValue)
                Layer += 1;
        }

        public void SetViewport (Rectangle? rectangle, bool updateViewTransform, int? layer = null) {
            SetViewportBatch.AddNew(Container, layer.GetValueOrDefault(Layer), Materials.SetViewport, rectangle, updateViewTransform, Materials);

            if (AutoIncrementLayer && !layer.HasValue)
                Layer += 1;
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

        private float ConvertFillMode (RasterFillMode fillMode, float fillAngle) {
            float fillModeF = (int)fillMode;
            if (fillMode >= RasterFillMode.Angular) {
                fillAngle = Arithmetic.WrapExclusive(fillAngle, 0, 360);
                fillModeF += (float)(fillAngle);
            }
            return fillModeF;
        }

        public void RasterizeEllipse (
            Vector2 center, Vector2 radius, pSRGBColor innerColor, pSRGBColor? outerColor = null,
            RasterFillMode fillMode = RasterFillMode.Natural,
            float fillOffset = 0, float fillSize = 1, Vector2? fillGradientPower = null,
            float fillAngle = 0, float? annularRadius = null,
            RasterShadowSettings? shadow = null,
            int? layer = null, bool? worldSpace = null, bool? blendInLinearSpace = null,
            BlendState blendState = null, Texture2D texture = null,
            Bounds? textureRegion = null, SamplerState samplerState = null
        ) {
            var fillModeF = ConvertFillMode(fillMode, fillAngle);
            using (var rsb = GetRasterShapeBatch(
                layer, worldSpace, blendState, texture, samplerState
            ))
                rsb.Add(new RasterShapeDrawCall {
                    Type = RasterShapeType.Ellipse,
                    WorldSpace = worldSpace ?? WorldSpace,
                    A = center,
                    B = radius,
                    C = new Vector2(fillModeF, 0),
                    OutlineSize = 0,
                    InnerColor = innerColor,
                    OuterColor = outerColor.GetValueOrDefault(innerColor),
                    OutlineColor = outerColor.GetValueOrDefault(innerColor),
                    OutlineGammaMinusOne = RasterOutlineGammaMinusOne,
                    BlendInLinearSpace = blendInLinearSpace ?? RasterBlendInLinearSpace,
                    FillGradientPowerMinusOne = (fillGradientPower ?? Vector2.One) - Vector2.One,
                    FillMode = fillModeF,
                    FillOffset = fillOffset,
                    FillSize = fillSize,
                    AnnularRadius = annularRadius ?? 0,
                    Shadow = shadow ?? RasterShadow,
                    // FIXME
                    TextureBounds = Bounds.Unit,
                    SoftOutline = RasterSoftOutlines
                });
        }

        public void RasterizeEllipse (
            Vector2 center, Vector2 radius, float outlineRadius,
            pSRGBColor innerColor, pSRGBColor outerColor, pSRGBColor outlineColor,
            RasterFillMode fillMode = RasterFillMode.Natural,
            float fillOffset = 0, float fillSize = 1, Vector2? fillGradientPower = null,
            float fillAngle = 0, float? annularRadius = null,
            RasterShadowSettings? shadow = null,
            int? layer = null, bool? worldSpace = null, bool? blendInLinearSpace = null,
            BlendState blendState = null, Texture2D texture = null,
            Bounds? textureRegion = null, SamplerState samplerState = null
        ) {
            var fillModeF = ConvertFillMode(fillMode, fillAngle);
            using (var eb = GetRasterShapeBatch(
                layer, worldSpace, blendState, texture, samplerState
            ))
                eb.Add(new RasterShapeDrawCall {
                    Type = RasterShapeType.Ellipse,
                    WorldSpace = worldSpace ?? WorldSpace,
                    A = center,
                    B = radius,
                    C = new Vector2(fillModeF, fillOffset),
                    OutlineSize = outlineRadius,
                    InnerColor = innerColor,
                    OuterColor = outerColor,
                    OutlineColor = outlineColor,
                    OutlineGammaMinusOne = RasterOutlineGammaMinusOne,
                    BlendInLinearSpace = blendInLinearSpace ?? RasterBlendInLinearSpace,
                    FillGradientPowerMinusOne = (fillGradientPower ?? Vector2.One) - Vector2.One,
                    FillMode = fillModeF,
                    FillOffset = fillOffset,
                    FillSize = fillSize,
                    AnnularRadius = annularRadius ?? 0,
                    Shadow = shadow ?? RasterShadow,
                    // FIXME
                    TextureBounds = Bounds.Unit,
                    SoftOutline = RasterSoftOutlines
                });
        }

        public void RasterizeLineSegment (
            Vector2 a, Vector2 b, float radius, pSRGBColor innerColor, pSRGBColor? outerColor = null,
            bool gradientAlongLine = false, RasterFillMode fillMode = RasterFillMode.Natural,
            float fillOffset = 0, float fillSize = 1, Vector2? fillGradientPower = null,
            float fillAngle = 0, float? annularRadius = null,
            RasterShadowSettings? shadow = null,
            int? layer = null, bool? worldSpace = null, bool? blendInLinearSpace = null,
            BlendState blendState = null, Texture2D texture = null,
            Bounds? textureRegion = null, SamplerState samplerState = null
        ) {
            var fillModeF = ConvertFillMode(fillMode, fillAngle);
            using (var rsb = GetRasterShapeBatch(
                layer, worldSpace, blendState, texture, samplerState
            ))
                rsb.Add(new RasterShapeDrawCall {
                    Type = RasterShapeType.LineSegment,
                    WorldSpace = worldSpace ?? WorldSpace,
                    A = a, B = b,
                    C = new Vector2(gradientAlongLine ? 1 : 0, 0),
                    Radius = new Vector2(radius, 0),
                    OutlineSize = 0,
                    InnerColor = innerColor,
                    OuterColor = outerColor.GetValueOrDefault(innerColor),
                    OutlineColor = outerColor.GetValueOrDefault(innerColor),
                    OutlineGammaMinusOne = RasterOutlineGammaMinusOne,
                    BlendInLinearSpace = blendInLinearSpace ?? RasterBlendInLinearSpace,
                    FillGradientPowerMinusOne = (fillGradientPower ?? Vector2.One) - Vector2.One,
                    FillMode = fillModeF,
                    FillOffset = fillOffset,
                    FillSize = fillSize,
                    AnnularRadius = annularRadius ?? 0,
                    Shadow = shadow ?? RasterShadow,
                    // FIXME
                    TextureBounds = Bounds.Unit,
                    SoftOutline = RasterSoftOutlines
                });
        }

        public void RasterizeLineSegment (
            Vector2 a, Vector2 b, float startRadius, float? endRadius, float outlineRadius,
            pSRGBColor innerColor, pSRGBColor outerColor, pSRGBColor outlineColor,
            bool gradientAlongLine = false, RasterFillMode fillMode = RasterFillMode.Natural,
            float fillOffset = 0, float fillSize = 1, Vector2? fillGradientPower = null,
            float fillAngle = 0, float? annularRadius = null,
            RasterShadowSettings? shadow = null,
            int? layer = null, bool? worldSpace = null, bool? blendInLinearSpace = null,
            BlendState blendState = null, Texture2D texture = null,
            Bounds? textureRegion = null, SamplerState samplerState = null
        ) {
            var fillModeF = ConvertFillMode(fillMode, fillAngle);
            float _endRadius = endRadius.GetValueOrDefault(startRadius);
            float maxRadius = Math.Max(startRadius, _endRadius);

            using (var rsb = GetRasterShapeBatch(
                layer, worldSpace, blendState, texture, samplerState
            ))
                rsb.Add(new RasterShapeDrawCall {
                    Type = RasterShapeType.LineSegment,
                    WorldSpace = worldSpace ?? WorldSpace,
                    A = a, B = b,
                    C = new Vector2(gradientAlongLine ? 1 : 0, startRadius - maxRadius),
                    Radius = new Vector2(maxRadius, _endRadius - maxRadius),
                    OutlineSize = outlineRadius,
                    InnerColor = innerColor,
                    OuterColor = outerColor,
                    OutlineColor = outlineColor,
                    OutlineGammaMinusOne = RasterOutlineGammaMinusOne,
                    BlendInLinearSpace = blendInLinearSpace ?? RasterBlendInLinearSpace,
                    FillGradientPowerMinusOne = (fillGradientPower ?? Vector2.One) - Vector2.One,
                    FillMode = fillModeF,
                    FillOffset = fillOffset,
                    FillSize = fillSize,
                    AnnularRadius = annularRadius ?? 0,
                    Shadow = shadow ?? RasterShadow,
                    // FIXME
                    TextureBounds = Bounds.Unit,
                    SoftOutline = RasterSoftOutlines
                });
        }

        public void RasterizeRectangle (
            Vector2 tl, Vector2 br, float radius,
            pSRGBColor innerColor, pSRGBColor? outerColor = null,
            RasterFillMode fillMode = RasterFillMode.Natural,
            float fillOffset = 0, float fillSize = 1, Vector2? fillGradientPower = null,
            float fillAngle = 0, float? annularRadius = null,
            RasterShadowSettings? shadow = null,
            int? layer = null, bool? worldSpace = null, bool? blendInLinearSpace = null,
            BlendState blendState = null, Texture2D texture = null,
            Bounds? textureRegion = null, SamplerState samplerState = null
        ) {
            var fillModeF = ConvertFillMode(fillMode, fillAngle);
            using (var rsb = GetRasterShapeBatch(
                layer, worldSpace, blendState, texture, samplerState
            ))
                rsb.Add(new RasterShapeDrawCall {
                    Type = RasterShapeType.Rectangle,
                    WorldSpace = worldSpace ?? WorldSpace,
                    A = tl, B = br,
                    C = new Vector2(radius),
                    Radius = new Vector2(radius),
                    OutlineSize = 0,
                    InnerColor = innerColor,
                    OuterColor = outerColor.GetValueOrDefault(innerColor),
                    OutlineColor = outerColor.GetValueOrDefault(innerColor),
                    OutlineGammaMinusOne = RasterOutlineGammaMinusOne,
                    BlendInLinearSpace = blendInLinearSpace ?? RasterBlendInLinearSpace,
                    FillGradientPowerMinusOne = (fillGradientPower ?? Vector2.One) - Vector2.One,
                    FillMode = fillModeF,
                    FillOffset = fillOffset,
                    FillSize = fillSize,
                    AnnularRadius = annularRadius ?? 0,
                    Shadow = shadow ?? RasterShadow,
                    // FIXME
                    TextureBounds = Bounds.Unit,
                    SoftOutline = RasterSoftOutlines
                });
        }

        public void RasterizeRectangle (
            Vector2 tl, Vector2 br, float radius, float outlineRadius,
            pSRGBColor innerColor, pSRGBColor outerColor, pSRGBColor outlineColor,
            RasterFillMode fillMode = RasterFillMode.Natural,
            float fillOffset = 0, float fillSize = 1, Vector2? fillGradientPower = null,
            float fillAngle = 0, float? annularRadius = null,
            RasterShadowSettings? shadow = null,
            int? layer = null, bool? worldSpace = null, bool? blendInLinearSpace = null,
            BlendState blendState = null, Texture2D texture = null,
            Bounds? textureRegion = null, SamplerState samplerState = null
        ) {
            var fillModeF = ConvertFillMode(fillMode, fillAngle);
            using (var rsb = GetRasterShapeBatch(
                layer, worldSpace, blendState, texture, samplerState
            ))
                rsb.Add(new RasterShapeDrawCall {
                    Type = RasterShapeType.Rectangle,
                    WorldSpace = worldSpace ?? WorldSpace,
                    A = tl, B = br,
                    C = new Vector2(radius),
                    Radius = new Vector2(radius),
                    OutlineSize = outlineRadius,
                    InnerColor = innerColor,
                    OuterColor = outerColor,
                    OutlineColor = outlineColor,
                    OutlineGammaMinusOne = RasterOutlineGammaMinusOne,
                    BlendInLinearSpace = blendInLinearSpace ?? RasterBlendInLinearSpace,
                    FillGradientPowerMinusOne = (fillGradientPower ?? Vector2.One) - Vector2.One,
                    FillMode = fillModeF,
                    FillOffset = fillOffset,
                    FillSize = fillSize,
                    AnnularRadius = annularRadius ?? 0,
                    Shadow = shadow ?? RasterShadow,
                    // FIXME
                    TextureBounds = Bounds.Unit,
                    SoftOutline = RasterSoftOutlines
                });
        }

        public void RasterizeRectangle (
            Vector2 tl, Vector2 br, Vector4 radiusCW, float outlineRadius,
            pSRGBColor innerColor, pSRGBColor outerColor, pSRGBColor outlineColor,
            RasterFillMode fillMode = RasterFillMode.Natural,
            float fillOffset = 0, float fillSize = 1, Vector2? fillGradientPower = null,
            float fillAngle = 0, float? annularRadius = null,
            RasterShadowSettings? shadow = null,
            int? layer = null, bool? worldSpace = null, bool? blendInLinearSpace = null,
            BlendState blendState = null, Texture2D texture = null,
            Bounds? textureRegion = null, SamplerState samplerState = null
        ) {
            var fillModeF = ConvertFillMode(fillMode, fillAngle);
            using (var rsb = GetRasterShapeBatch(
                layer, worldSpace, blendState, texture, samplerState
            ))
                rsb.Add(new RasterShapeDrawCall {
                    Type = RasterShapeType.Rectangle,
                    WorldSpace = worldSpace ?? WorldSpace,
                    A = tl, B = br,
                    C = new Vector2(radiusCW.X, radiusCW.Y),
                    Radius = new Vector2(radiusCW.Z, radiusCW.W),
                    OutlineSize = outlineRadius,
                    InnerColor = innerColor,
                    OuterColor = outerColor,
                    OutlineColor = outlineColor,
                    OutlineGammaMinusOne = RasterOutlineGammaMinusOne,
                    BlendInLinearSpace = blendInLinearSpace ?? RasterBlendInLinearSpace,
                    FillGradientPowerMinusOne = (fillGradientPower ?? Vector2.One) - Vector2.One,
                    FillMode = fillModeF,
                    FillOffset = fillOffset,
                    FillSize = fillSize,
                    AnnularRadius = annularRadius ?? 0,
                    Shadow = shadow ?? RasterShadow,
                    // FIXME
                    TextureBounds = Bounds.Unit,
                    SoftOutline = RasterSoftOutlines
                });
        }

        public void RasterizeTriangle (
            Vector2 a, Vector2 b, Vector2 c, float radius, 
            pSRGBColor innerColor, pSRGBColor? outerColor = null,
            RasterFillMode fillMode = RasterFillMode.Natural,
            float fillOffset = 0, float fillSize = 1, Vector2? fillGradientPower = null,
            float fillAngle = 0, float? annularRadius = null,
            RasterShadowSettings? shadow = null,
            int? layer = null, bool? worldSpace = null, bool? blendInLinearSpace = null,
            BlendState blendState = null, Texture2D texture = null,
            Bounds? textureRegion = null, SamplerState samplerState = null
        ) {
            var fillModeF = ConvertFillMode(fillMode, fillAngle);
            using (var rsb = GetRasterShapeBatch(
                layer, worldSpace, blendState, texture, samplerState
            ))
                rsb.Add(new RasterShapeDrawCall {
                    Type = RasterShapeType.Triangle,
                    WorldSpace = worldSpace ?? WorldSpace,
                    A = a, B = b, C = c,
                    Radius = new Vector2(radius, fillOffset),
                    OutlineSize = 0,
                    InnerColor = innerColor,
                    OuterColor = outerColor.GetValueOrDefault(innerColor),
                    OutlineColor = outerColor.GetValueOrDefault(innerColor),
                    OutlineGammaMinusOne = RasterOutlineGammaMinusOne,
                    BlendInLinearSpace = blendInLinearSpace ?? RasterBlendInLinearSpace,
                    FillGradientPowerMinusOne = (fillGradientPower ?? Vector2.One) - Vector2.One,
                    FillMode = fillModeF,
                    FillOffset = fillOffset,
                    FillSize = fillSize,
                    AnnularRadius = annularRadius ?? 0,
                    Shadow = shadow ?? RasterShadow,
                    // FIXME
                    TextureBounds = Bounds.Unit,
                    SoftOutline = RasterSoftOutlines
                });
        }

        public void RasterizeTriangle (
            Vector2 a, Vector2 b, Vector2 c, float radius, float outlineRadius,
            pSRGBColor innerColor, pSRGBColor outerColor, pSRGBColor outlineColor,
            RasterFillMode fillMode = RasterFillMode.Natural,
            float fillOffset = 0, float fillSize = 1, Vector2? fillGradientPower = null,
            float fillAngle = 0, float? annularRadius = null,
            RasterShadowSettings? shadow = null,
            int? layer = null, bool? worldSpace = null, bool? blendInLinearSpace = null,
            BlendState blendState = null, Texture2D texture = null,
            Bounds? textureRegion = null, SamplerState samplerState = null
        ) {
            var fillModeF = ConvertFillMode(fillMode, fillAngle);
            using (var rsb = GetRasterShapeBatch(
                layer, worldSpace, blendState, texture, samplerState
            ))
                rsb.Add(new RasterShapeDrawCall {
                    Type = RasterShapeType.Triangle,
                    WorldSpace = worldSpace ?? WorldSpace,
                    A = a, B = b, C = c,
                    Radius = new Vector2(radius, fillOffset),
                    OutlineSize = outlineRadius,
                    InnerColor = innerColor,
                    OuterColor = outerColor,
                    OutlineColor = outlineColor,
                    OutlineGammaMinusOne = RasterOutlineGammaMinusOne,
                    BlendInLinearSpace = blendInLinearSpace ?? RasterBlendInLinearSpace,
                    FillGradientPowerMinusOne = (fillGradientPower ?? Vector2.One) - Vector2.One,
                    FillMode = fillModeF,
                    FillOffset = fillOffset,
                    FillSize = fillSize,
                    AnnularRadius = annularRadius ?? 0,
                    Shadow = shadow ?? RasterShadow,
                    // FIXME
                    TextureBounds = Bounds.Unit,
                    SoftOutline = RasterSoftOutlines
                });
        }

        public void RasterizeQuadraticBezier (
            Vector2 a, Vector2 b, Vector2 c, float radius, pSRGBColor color,
            RasterFillMode fillMode = RasterFillMode.Natural,
            float fillOffset = 0, float fillSize = 1, Vector2? fillGradientPower = null,
            float fillAngle = 0, float? annularRadius = null,
            RasterShadowSettings? shadow = null,
            int? layer = null, bool? worldSpace = null, bool? blendInLinearSpace = null,
            BlendState blendState = null, Texture2D texture = null,
            Bounds? textureRegion = null, SamplerState samplerState = null
        ) {
            var fillModeF = ConvertFillMode(fillMode, fillAngle);
            using (var rsb = GetRasterShapeBatch(
                layer, worldSpace, blendState, texture, samplerState
            ))
                rsb.Add(new RasterShapeDrawCall {
                    Type = RasterShapeType.QuadraticBezier,
                    WorldSpace = worldSpace ?? WorldSpace,
                    A = a, B = b, C = c,
                    Radius = new Vector2(radius),
                    OutlineSize = 0,
                    InnerColor = color,
                    OuterColor = color,
                    OutlineColor = color,
                    OutlineGammaMinusOne = RasterOutlineGammaMinusOne,
                    BlendInLinearSpace = blendInLinearSpace ?? RasterBlendInLinearSpace,
                    FillGradientPowerMinusOne = (fillGradientPower ?? Vector2.One) - Vector2.One,
                    FillMode = fillModeF,
                    FillOffset = fillOffset,
                    FillSize = fillSize,
                    AnnularRadius = annularRadius ?? 0,
                    Shadow = shadow ?? RasterShadow,
                    // FIXME
                    TextureBounds = Bounds.Unit,
                    SoftOutline = RasterSoftOutlines
                });
        }

        public void RasterizeQuadraticBezier (
            Vector2 a, Vector2 b, Vector2 c, float radius, float outlineRadius,
            pSRGBColor innerColor, pSRGBColor outerColor, pSRGBColor outlineColor,
            RasterFillMode fillMode = RasterFillMode.Natural,
            float fillOffset = 0, float fillSize = 1, Vector2? fillGradientPower = null,
            float fillAngle = 0, float? annularRadius = null,
            RasterShadowSettings? shadow = null,
            int? layer = null, bool? worldSpace = null, bool? blendInLinearSpace = null,
            BlendState blendState = null, Texture2D texture = null,
            Bounds? textureRegion = null, SamplerState samplerState = null
        ) {
            var fillModeF = ConvertFillMode(fillMode, fillAngle);
            using (var rsb = GetRasterShapeBatch(
                layer, worldSpace, blendState, texture, samplerState
            ))
                rsb.Add(new RasterShapeDrawCall {
                    Type = RasterShapeType.QuadraticBezier,
                    WorldSpace = worldSpace ?? WorldSpace,
                    A = a, B = b, C = c,
                    Radius = new Vector2(radius),
                    OutlineSize = outlineRadius,
                    InnerColor = innerColor,
                    OuterColor = outerColor,
                    OutlineColor = outlineColor,
                    OutlineGammaMinusOne = RasterOutlineGammaMinusOne,
                    BlendInLinearSpace = blendInLinearSpace ?? RasterBlendInLinearSpace,
                    FillGradientPowerMinusOne = (fillGradientPower ?? Vector2.One) - Vector2.One,
                    FillMode = fillModeF,
                    FillOffset = fillOffset,
                    FillSize = fillSize,
                    AnnularRadius = annularRadius ?? 0,
                    Shadow = shadow ?? RasterShadow,
                    // FIXME
                    TextureBounds = Bounds.Unit,
                    SoftOutline = RasterSoftOutlines
                });
        }

        /// <param name="center">The center point of the shape.</param>
        /// <param name="startAngleDegrees">The start angle of the arc (in degrees)</param>
        /// <param name="sizeDegrees">The length of the arc (in degrees), relative to the start angle. A start of 80 + a length of 20 = an arc from 80 to 100.</param>
        /// <param name="ringRadius">The distance from the center point at which the ring will be centered.</param>
        /// <param name="fillRadius">The radius of the body of the ring (it will be centered around the circle located at ringRadius).</param>
        /// <param name="outlineRadius">The radius of the outline at the outside of the body of the ring.</param>
        public void RasterizeArc (
            Vector2 center, float startAngleDegrees, float sizeDegrees, 
            float ringRadius, float fillRadius, float outlineRadius,
            pSRGBColor innerColor, pSRGBColor? outerColor = null, pSRGBColor? outlineColor = null, 
            RasterFillMode fillMode = RasterFillMode.Natural,
            float fillOffset = 0, float fillSize = 1, Vector2? fillGradientPower = null,
            float fillAngle = 0, float? annularRadius = null,
            RasterShadowSettings? shadow = null,
            int? layer = null, bool? worldSpace = null, bool? blendInLinearSpace = null,
            BlendState blendState = null, Texture2D texture = null,
            Bounds? textureRegion = null, SamplerState samplerState = null
        ) {
            var fillModeF = ConvertFillMode(fillMode, fillAngle);
            var centerAngleDegrees = startAngleDegrees + (sizeDegrees / 2);
            var centerAngleRadians = MathHelper.ToRadians(centerAngleDegrees);
            var sizeRadians = MathHelper.ToRadians(sizeDegrees / 2);
            var b = new Vector2((float)Math.Sin(centerAngleRadians), (float)Math.Cos(centerAngleRadians));
            var c = new Vector2((float)Math.Sin(sizeRadians), (float)Math.Cos(sizeRadians));

            using (var rsb = GetRasterShapeBatch(
                layer, worldSpace, blendState, texture, samplerState
            ))
                rsb.Add(new RasterShapeDrawCall {
                    Type = RasterShapeType.Arc,
                    WorldSpace = worldSpace ?? WorldSpace,
                    A = center, B = b, C = c,
                    // HACK: Inverse order because the shader uses radius.x for bounding box math
                    Radius = new Vector2(ringRadius, fillRadius),
                    OutlineSize = outlineRadius,
                    InnerColor = innerColor,
                    OuterColor = outerColor.GetValueOrDefault(innerColor),
                    OutlineColor = outlineColor.GetValueOrDefault(Color.Transparent),
                    OutlineGammaMinusOne = RasterOutlineGammaMinusOne,
                    BlendInLinearSpace = blendInLinearSpace ?? RasterBlendInLinearSpace,
                    FillGradientPowerMinusOne = (fillGradientPower ?? Vector2.One) - Vector2.One,
                    FillMode = fillModeF,
                    FillOffset = fillOffset,
                    FillSize = fillSize,
                    AnnularRadius = annularRadius ?? 0,
                    Shadow = shadow ?? RasterShadow,
                    // FIXME
                    TextureBounds = Bounds.Unit,
                    SoftOutline = RasterSoftOutlines
                });
        }

        public IBitmapBatch GetBitmapBatch (int? layer, bool? worldSpace, BlendState blendState, SamplerState samplerState, DepthStencilState depthStencilState = null, RasterizerState rasterizerState = null, Material customMaterial = null) {
            if (Materials == null)
                throw new InvalidOperationException("You cannot use the argumentless ImperativeRenderer constructor.");

            var actualLayer = layer.GetValueOrDefault(Layer);
            var actualWorldSpace = worldSpace.GetValueOrDefault(WorldSpace);
            var desiredBlendState = blendState ?? BlendState;
            var desiredSamplerState = samplerState ?? SamplerState;

            if (LowPriorityMaterialOrdering)
                desiredSamplerState = null;

            CachedBatch cacheEntry;
            if (!Cache.TryGet<IBitmapBatch>(
                out cacheEntry,
                LowPriorityMaterialOrdering ? CachedBatchType.MultimaterialBitmap : CachedBatchType.Bitmap,
                container: Container,
                layer: actualLayer,
                worldSpace: actualWorldSpace,
                rasterizerState: rasterizerState ?? RasterizerState,
                depthStencilState: depthStencilState ?? DepthStencilState,
                blendState: desiredBlendState,
                samplerState: desiredSamplerState,
                customMaterial: customMaterial,
                useZBuffer: UseZBuffer,
                depthPrePass: DepthPrePass,
                zBufferOnlySorting: ZBufferOnlySorting
            )) {
                Material material;

                if (customMaterial != null) {
                    material = Materials.Get(
                        customMaterial, rasterizerState ?? RasterizerState, depthStencilState ?? DepthStencilState, desiredBlendState
                    );
                } else {
                    material = Materials.GetBitmapMaterial(
                        actualWorldSpace,
                        rasterizerState ?? RasterizerState, depthStencilState ?? DepthStencilState, desiredBlendState, UseDiscard
                    );
                }

                IBitmapBatch bb;
                if (LowPriorityMaterialOrdering) {
                    bb = MultimaterialBitmapBatch.New(
                        Container, actualLayer, Material.Null, 
                        useZBuffer: UseZBuffer, depthPrePass: DepthPrePass, worldSpace: actualWorldSpace
                    );
                } else {
                    bb = BitmapBatch.New(
                        Container, actualLayer, material, desiredSamplerState, desiredSamplerState, 
                        useZBuffer: UseZBuffer, zBufferOnlySorting: ZBufferOnlySorting, 
                        depthPrePass: DepthPrePass, worldSpace: actualWorldSpace
                    );
                    if (BitmapBatchInitialCapacity.HasValue)
                        ((BitmapBatch)bb).EnsureCapacity(BitmapBatchInitialCapacity.Value, true);
                }

                bb.Sorter = DeclarativeSorter;
                cacheEntry.Batch = bb;
                Cache.InsertAtFront(ref cacheEntry, -1);
            }

            if (AutoIncrementLayer && !layer.HasValue)
                Layer += 1;

            return (IBitmapBatch)cacheEntry.Batch;
        }

        public GeometryBatch GetGeometryBatch (int? layer, bool? worldSpace, BlendState blendState, Material customMaterial = null) {
            if (Materials == null)
                throw new InvalidOperationException("You cannot use the argumentless ImperativeRenderer constructor.");

            var actualLayer = layer.GetValueOrDefault(Layer);
            var actualWorldSpace = worldSpace.GetValueOrDefault(WorldSpace);
            var desiredBlendState = blendState ?? BlendState;

            CachedBatch cacheEntry;
            if (!Cache.TryGet<GeometryBatch>(
                out cacheEntry,
                CachedBatchType.Geometry,
                container: Container,
                layer: actualLayer,
                worldSpace: actualWorldSpace,
                rasterizerState: RasterizerState,
                depthStencilState: DepthStencilState,
                blendState: desiredBlendState,
                samplerState: null,
                customMaterial: customMaterial,
                useZBuffer: UseZBuffer,
                zBufferOnlySorting: ZBufferOnlySorting,
                depthPrePass: DepthPrePass
            )) {
                Material material;

                if (customMaterial != null) {
                    material = Materials.Get(
                        customMaterial, RasterizerState, DepthStencilState, desiredBlendState
                    );
                } else {
                    material = Materials.GetGeometryMaterial(
                        actualWorldSpace,
                        rasterizerState: RasterizerState,
                        depthStencilState: DepthStencilState,
                        blendState: desiredBlendState
                    );
                }
                
                cacheEntry.Batch = GeometryBatch.New(Container, actualLayer, material);
                Cache.InsertAtFront(ref cacheEntry, -1);
            }

            if (AutoIncrementLayer && !layer.HasValue)
                Layer += 1;

            return (GeometryBatch)cacheEntry.Batch;
        }

        public RasterShapeBatch GetRasterShapeBatch (int? layer, bool? worldSpace, BlendState blendState, Texture2D texture, SamplerState samplerState) {
            if (Materials == null)
                throw new InvalidOperationException("You cannot use the argumentless ImperativeRenderer constructor.");

            var actualLayer = layer.GetValueOrDefault(Layer);
            var actualWorldSpace = worldSpace.GetValueOrDefault(WorldSpace);
            var desiredBlendState = blendState ?? BlendState;
            var desiredSamplerState = samplerState ?? SamplerState;

            // HACK: Look, alright, it's complicated
            if ((desiredBlendState == BlendState.AlphaBlend) || (desiredBlendState == null))
                desiredBlendState = RenderStates.RasterShapeAlphaBlend;
            else if (desiredBlendState == BlendState.Additive)
                desiredBlendState = RenderStates.RasterShapeAdditiveBlend;
            else if (desiredBlendState == RenderStates.SubtractiveBlend)
                desiredBlendState = RenderStates.RasterShapeSubtractiveBlend;
            else if (desiredBlendState == RenderStates.MaxBlendValue)
                desiredBlendState = RenderStates.RasterShapeMaxBlend;

            CachedBatch cacheEntry;
            if (!Cache.TryGet<RasterShapeBatch>(
                out cacheEntry,
                CachedBatchType.RasterShape,
                container: Container,
                layer: actualLayer,
                worldSpace: actualWorldSpace,
                rasterizerState: RasterizerState,
                depthStencilState: DepthStencilState,
                blendState: desiredBlendState,
                samplerState: desiredSamplerState,
                customMaterial: null,
                useZBuffer: UseZBuffer,
                zBufferOnlySorting: ZBufferOnlySorting,
                depthPrePass: DepthPrePass
            ) || (((RasterShapeBatch)cacheEntry.Batch).Texture != texture)) {
                // FIXME: The way this works will cause churn when mixing textured and untextured shape batches
                //  oh well
                var batch = RasterShapeBatch.New(
                    Container, actualLayer, Materials, texture, desiredSamplerState,
                    RasterizerState, DepthStencilState, desiredBlendState
                );
                // FIXME: why the hell
                batch.UseUbershader = RasterUseUbershader;
                cacheEntry.Batch = batch;
                Cache.InsertAtFront(ref cacheEntry, -1);
            }

            if (AutoIncrementLayer && !layer.HasValue)
                Layer += 1;

            return (RasterShapeBatch)cacheEntry.Batch;
        }

        public override string ToString () {
            var callCount = 0;
            for (int i = 0; i < Cache.Count; i++) {
                CachedBatch cb;
                if (Cache.GetItemAtIndex(i, out cb)) {
                    var lb = cb.Batch as IListBatch;
                    if (lb == null)
                        continue;

                    callCount += lb.Count;
                }
            }

            return string.Format("ImperativeRenderer [{0}:{1}] {2} batch(es) {3} drawcall(s)", Container, Layer, Cache.Count, callCount);
        }
    }
}
