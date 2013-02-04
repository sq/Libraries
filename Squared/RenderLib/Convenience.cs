using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Game;

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
        public IBatchContainer Container;

        public int Layer;
        public Material Material;
        public SamplerState SamplerState;
        public bool UseZBuffer;
        public bool AutoIncrementSortKey;

        private float NextSortKey;
        private BitmapBatch PreviousBatch;

        public BitmapRenderer (
            IBatchContainer container, 
            int layer = 0, 
            Material material = null, 
            SamplerState samplerState = null, 
            bool useZBuffer = false,
            bool autoIncrementSortKey = false
        ) {
            if (container == null)
                throw new ArgumentNullException("container");

            Container = container;
            Layer = layer;
            Material = material;
            SamplerState = samplerState;
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
            else if (Container.IsDisposed)
                throw new ObjectDisposedException("The container this BitmapRenderer is drawing into has been disposed.");

            if (AutoIncrementSortKey) {
                drawCall.SortKey = NextSortKey;
                NextSortKey += 1;
            }

            using (var batch = GetBatch( 
                layer.GetValueOrDefault(Layer), 
                material ?? Material, 
                samplerState ?? SamplerState
            ))
                batch.Add(ref drawCall);
        }

        public void Draw (
            Texture2D texture, Vector2 position,
            Rectangle? sourceRectangle = null, Color? multiplyColor = null, Color addColor = default(Color),
            float rotation = 0, Vector2? scale = null, Vector2 origin = default(Vector2),
            bool mirrorX = false, bool mirrorY = false, int sortKey = 0,
            int? layer = null, Material material = null, SamplerState samplerState = null
        ) {
            var drawCall = new BitmapDrawCall(texture, position);
            if (sourceRectangle.HasValue)
                drawCall.TextureRegion = texture.BoundsFromRectangle(sourceRectangle.Value);
            drawCall.MultiplyColor = multiplyColor.GetValueOrDefault(drawCall.MultiplyColor);
            drawCall.AddColor = addColor;
            drawCall.Rotation = rotation;
            drawCall.Scale = scale.GetValueOrDefault(Vector2.One);
            drawCall.Origin = origin;
            if (mirrorX || mirrorY)
                drawCall.Mirror(mirrorX, mirrorY);
            drawCall.SortKey = sortKey;

            Draw(ref drawCall, layer: layer, material: material, samplerState: samplerState);
        }

        public void Draw (
            Texture2D texture, float x, float y,
            Rectangle? sourceRectangle = null, Color? multiplyColor = null, Color addColor = default(Color),
            float rotation = 0, float scaleX = 1, float scaleY = 1, float originX = 0, float originY = 0,
            bool mirrorX = false, bool mirrorY = false, int sortKey = 0,
            int? layer = null, Material material = null, SamplerState samplerState = null
        ) {
            var drawCall = new BitmapDrawCall(texture, new Vector2(x, y));
            if (sourceRectangle.HasValue)
                drawCall.TextureRegion = texture.BoundsFromRectangle(sourceRectangle.Value);
            drawCall.MultiplyColor = multiplyColor.GetValueOrDefault(drawCall.MultiplyColor);
            drawCall.AddColor = addColor;
            drawCall.Rotation = rotation;
            drawCall.Scale = new Vector2(scaleX, scaleY);
            drawCall.Origin = new Vector2(originX, originY);
            if (mirrorX || mirrorY)
                drawCall.Mirror(mirrorX, mirrorY);
            drawCall.SortKey = sortKey;

            Draw(ref drawCall, layer: layer, material: material, samplerState: samplerState);
        }

        private BitmapBatch GetBatch (int layer, Material material, SamplerState samplerState) {
            if (
                (PreviousBatch != null) &&
                (PreviousBatch.Container == Container) &&
                (PreviousBatch.Layer == layer) &&
                (PreviousBatch.Material == material) &&
                (PreviousBatch.SamplerState == samplerState) &&                
                (PreviousBatch.UseZBuffer == UseZBuffer)
            )
                return PreviousBatch;

            return PreviousBatch = BitmapBatch.New(Container, layer, material, samplerState, UseZBuffer);
        }
    }
}
