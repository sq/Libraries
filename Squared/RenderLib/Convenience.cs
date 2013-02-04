using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework.Graphics;

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

        public static readonly RasterizerState ScissorOnly = new RasterizerState {
            CullMode = CullMode.None,
            ScissorTestEnable = true
        };    
    }

    public static class MaterialUtil {
        public static Action<DeviceManager> MakeDelegate (int index, SamplerState state) {
            return (dm) => { dm.Device.SamplerStates[index] = state; };
        }

        public static Action<DeviceManager> MakeDelegate (RasterizerState state) {
            return (dm) => { dm.Device.RasterizerState = state; };
        }

        public static Action<DeviceManager> MakeDelegate (DepthStencilState state) {
            return (dm) => { dm.Device.DepthStencilState = state; };
        }

        public static Action<DeviceManager> MakeDelegate (BlendState state) {
            return (dm) => { dm.Device.BlendState = state; };
        }

        public static Action<DeviceManager> MakeDelegate (
            RasterizerState rasterizerState = null, 
            DepthStencilState depthStencilState = null, 
            BlendState blendState = null
        ) {
            return (dm) => {
                if (rasterizerState != null)
                    dm.Device.RasterizerState = rasterizerState;

                if (depthStencilState != null)
                    dm.Device.DepthStencilState = depthStencilState;

                if (blendState != null)
                    dm.Device.BlendState = blendState;
            };
        }

        public static DelegateMaterial SetStates (
            this Material inner, 
            RasterizerState rasterizerState = null,
            DepthStencilState depthState = null,
            BlendState blendState = null
        ) {
            return new DelegateMaterial(
                inner,
                new [] {
                    MakeDelegate(rasterizerState, depthState, blendState)
                },
                new Action<DeviceManager>[0]
            );
        }
    }

    public struct BitmapRenderer {
        public readonly IBatchContainer Container;

        public readonly int DefaultLayer;
        public readonly Material DefaultMaterial;
        public readonly SamplerState DefaultSamplerState;
        public readonly bool UseZBuffer;
        public readonly bool AutoIncrementSortKey;

        private float NextSortKey;
        private BitmapBatch PreviousBatch;

        public BitmapRenderer (
            IBatchContainer container, 
            int defaultLayer = 0, 
            Material defaultMaterial = null, 
            SamplerState defaultSamplerState = null, 
            bool useZBuffer = false,
            bool autoIncrementSortKey = false
        ) {
            if (container == null)
                throw new ArgumentNullException("container");

            Container = container;
            DefaultLayer = defaultLayer;
            DefaultMaterial = defaultMaterial;
            DefaultSamplerState = defaultSamplerState;
            UseZBuffer = useZBuffer;
            AutoIncrementSortKey = autoIncrementSortKey;
            NextSortKey = 0;
            PreviousBatch = null;
        }

        public void Draw (BitmapDrawCall drawCall, int? layer = null, Material material = null, SamplerState samplerState = null) {
            Draw(ref drawCall, layer, material, samplerState);
        }

        public void Draw (ref BitmapDrawCall drawCall, int? layer = null, Material material = null, SamplerState samplerState = null) {
            if (Container == null)
                throw new InvalidOperationException("You cannot use the argumentless BitmapRenderer constructor.");

            if (AutoIncrementSortKey) {
                drawCall.SortKey = NextSortKey;
                NextSortKey += 1;
            }

            using (var batch = GetBatch( 
                layer.GetValueOrDefault(DefaultLayer), 
                material ?? DefaultMaterial, 
                samplerState ?? DefaultSamplerState
            ))
                batch.Add(ref drawCall);
        }

        private BitmapBatch GetBatch (int layer, Material material, SamplerState samplerState) {
            if (
                (PreviousBatch != null) &&
                (PreviousBatch.Layer == layer) &&
                (PreviousBatch.Material == material) &&
                (PreviousBatch.SamplerState == samplerState)
            )
                return PreviousBatch;

            return PreviousBatch = BitmapBatch.New(Container, layer, material, samplerState, UseZBuffer);
        }
    }
}
