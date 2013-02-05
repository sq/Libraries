using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Game;
using Squared.Render.Evil;

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
            DepthStencilState depthStencilState = null,
            BlendState blendState = null
        ) {
            return new DelegateMaterial(
                inner,
                new [] {
                    MakeDelegate(rasterizerState, depthStencilState, blendState)
                },
                new Action<DeviceManager>[0]
            );
        }
    }

    public struct ImperativeRenderer {
        public IBatchContainer Container;
        public DefaultMaterialSet Materials;

        public int Layer;
        public DepthStencilState DepthStencilState;
        public RasterizerState RasterizerState;
        public BlendState BlendState;
        public SamplerState SamplerState;
        public bool WorldSpace;
        public bool UseZBuffer;
        public bool AutoIncrementLayer;
        public bool AutoIncrementSortKey;

        private float NextSortKey;
        private Batch PreviousBatch;

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
            bool autoIncrementLayer = false
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
            WorldSpace = worldSpace;
            AutoIncrementSortKey = autoIncrementSortKey;
            AutoIncrementLayer = autoIncrementLayer;
            NextSortKey = 0;
            PreviousBatch = null;
        }


        public ImperativeRenderer MakeSubgroup (bool nextLayer = true, Action<DeviceManager, object> before = null, Action<DeviceManager, object> after = null, object userData = null) {
            var result = this;
            var group = BatchGroup.New(Container, Layer, before: before, after: after, userData: userData);
            group.Dispose();
            result.Container = group;
            result.Layer = 0;

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

        public ImperativeRenderer ForRenderTarget (RenderTarget2D renderTarget) {
            var result = this;
            var group = BatchGroup.ForRenderTarget(Container, Layer, renderTarget);
            group.Dispose();
            result.Container = group;
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
            BlendState blendState = null, SamplerState samplerState = null
        ) {
            Draw(ref drawCall, layer, worldSpace, blendState, samplerState);
        }

        public void Draw (
            ref BitmapDrawCall drawCall, 
            int? layer = null, bool? worldSpace = null,
            BlendState blendState = null, SamplerState samplerState = null
        ) {
            if (Container == null)
                throw new InvalidOperationException("You cannot use the argumentless ImperativeRenderer constructor.");
            else if (Container.IsDisposed)
                throw new ObjectDisposedException("The container this ImperativeRenderer is drawing into has been disposed.");

            if (AutoIncrementSortKey) {
                drawCall.SortKey = NextSortKey;
                NextSortKey += 1;
            }

            using (var batch = GetBitmapBatch(
                layer, worldSpace,
                blendState, samplerState
            ))
                batch.Add(ref drawCall);
        }


        public void Draw (
            Texture2D texture, Vector2 position,
            Rectangle? sourceRectangle = null, Color? multiplyColor = null, Color addColor = default(Color),
            float rotation = 0, Vector2? scale = null, Vector2 origin = default(Vector2),
            bool mirrorX = false, bool mirrorY = false, float sortKey = 0,
            int? layer = null, bool? worldSpace = null, 
            BlendState blendState = null, SamplerState samplerState = null
        ) {
            var drawCall = new BitmapDrawCall(texture, position);
            if (sourceRectangle.HasValue)
                drawCall.TextureRegion = texture.BoundsFromRectangle(sourceRectangle.Value);
            drawCall.MultiplyColor = multiplyColor.GetValueOrDefault(Color.White);
            drawCall.AddColor = addColor;
            drawCall.Rotation = rotation;
            drawCall.Scale = scale.GetValueOrDefault(Vector2.One);
            drawCall.Origin = origin;
            if (mirrorX || mirrorY)
                drawCall.Mirror(mirrorX, mirrorY);
            drawCall.SortKey = sortKey;

            Draw(ref drawCall, layer: layer, worldSpace: worldSpace, blendState: blendState, samplerState: samplerState);
        }

        public void Draw (
            Texture2D texture, float x, float y,
            Rectangle? sourceRectangle = null, Color? multiplyColor = null, Color addColor = default(Color),
            float rotation = 0, float scaleX = 1, float scaleY = 1, float originX = 0, float originY = 0,
            bool mirrorX = false, bool mirrorY = false, float sortKey = 0,
            int? layer = null, bool? worldSpace = null,
            BlendState blendState = null, SamplerState samplerState = null
        ) {
            var drawCall = new BitmapDrawCall(texture, new Vector2(x, y));
            if (sourceRectangle.HasValue)
                drawCall.TextureRegion = texture.BoundsFromRectangle(sourceRectangle.Value);
            drawCall.MultiplyColor = multiplyColor.GetValueOrDefault(Color.White);
            drawCall.AddColor = addColor;
            drawCall.Rotation = rotation;
            drawCall.Scale = new Vector2(scaleX, scaleY);
            drawCall.Origin = new Vector2(originX, originY);
            if (mirrorX || mirrorY)
                drawCall.Mirror(mirrorX, mirrorY);
            drawCall.SortKey = sortKey;

            Draw(ref drawCall, layer: layer, worldSpace: worldSpace, blendState: blendState, samplerState: samplerState);
        }

        public void Draw (
            Texture2D texture, Rectangle destRectangle,
            Rectangle? sourceRectangle = null, Color? multiplyColor = null, Color addColor = default(Color),
            float rotation = 0, float originX = 0, float originY = 0,
            bool mirrorX = false, bool mirrorY = false, float sortKey = 0,
            int? layer = null, bool? worldSpace = null,
            BlendState blendState = null, SamplerState samplerState = null
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
            if (mirrorX || mirrorY)
                drawCall.Mirror(mirrorX, mirrorY);
            drawCall.SortKey = sortKey;

            Draw(ref drawCall, layer: layer, worldSpace: worldSpace, blendState: blendState, samplerState: samplerState);
        }


        public void DrawString (
            SpriteFont font, string text,
            Vector2 position, Color? color = null, float scale = 1, float? sortKey = null,
            int characterSkipCount = 0, int characterLimit = int.MaxValue,
            int? layer = null, bool? worldSpace = null,
            BlendState blendState = null, SamplerState samplerState = null
        ) {
            var spacing = font.Spacing;
            var lineSpacing = font.LineSpacing;
            FontUtils.FontFields privateFields;
            font.GetPrivateFields(out privateFields);

            var characterOffset = Vector2.Zero;

            var defaultCharacter = font.DefaultCharacter;
            int defaultCharacterIndex = -1;
            if (defaultCharacter.HasValue)
                defaultCharacterIndex = privateFields.Characters.BinarySearch(defaultCharacter.Value);

            var drawCall = new BitmapDrawCall(
                privateFields.Texture, default(Vector2), default(Bounds), color.GetValueOrDefault(Color.White), scale
            );

            if (sortKey.HasValue) {
                drawCall.SortKey = sortKey.Value;
            } else if (AutoIncrementSortKey) {
                drawCall.SortKey = NextSortKey;
                NextSortKey += 1;
            }

            float rectScaleX = 1f / privateFields.Texture.Width;
            float rectScaleY = 1f / privateFields.Texture.Height;

            using (var batch = GetBitmapBatch(layer, worldSpace, blendState, samplerState))
            for (int i = 0, l = text.Length; i < l; i++) {
                var ch = text[i];

                var lineBreak = false;
                if (ch == '\r') {
                    if (((i + 1) < l) && (text[i + 1] == '\n'))
                        i += 1;

                    lineBreak = true;
                } else if (ch == '\n') {
                    lineBreak = true;
                }

                if (lineBreak) {
                    characterOffset.X = 0;
                    characterOffset.Y += lineSpacing;
                }

                characterOffset.X += spacing;

                var charIndex = privateFields.Characters.BinarySearch(ch);
                if (charIndex < 0)
                    charIndex = defaultCharacterIndex;

                if (charIndex < 0)
                    continue;

                var kerning = privateFields.Kerning[charIndex];
                var leftSideBearing = kerning.X;
                var glyphWidth = kerning.Y;
                var rightSideBearing = kerning.Z;

                characterOffset.X += leftSideBearing * scale;

                if (characterSkipCount <= 0) {
                    if (characterLimit <= 0)
                        break;

                    var glyphRect = privateFields.GlyphRectangles[charIndex];
                    var cropRect = privateFields.CropRectangles[charIndex];

                    drawCall.TextureRegion = privateFields.Texture.BoundsFromRectangle(ref glyphRect);
                    drawCall.Position = new Vector2(
                        position.X + (cropRect.X + characterOffset.X) * scale,
                        position.Y + (cropRect.Y + characterOffset.Y) * scale
                    );

                    batch.Add(ref drawCall);

                    characterLimit--;
                } else {
                    characterSkipCount--;
                }

                characterOffset.X += (glyphWidth + rightSideBearing) * scale;
            }
        }


        public void FillRectangle (
            Rectangle rectangle, Color fillColor,
            int? layer = null, bool? worldSpace = null,
            BlendState blendState = null
        ) {
            FillRectangle(new Bounds(rectangle), fillColor, layer, worldSpace, blendState);
        }

        public void FillRectangle (
            Bounds bounds, Color fillColor,
            int? layer = null, bool? worldSpace = null,
            BlendState blendState = null
        ) {
            using (var gb = GetGeometryBatch<VertexPositionColor>(layer, worldSpace, blendState))
                gb.AddFilledQuad(bounds, fillColor);
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
            using (var gb = GetGeometryBatch<VertexPositionColor>(layer, worldSpace, blendState))
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
            using (var gb = GetGeometryBatch<VertexPositionColor>(
                layer, worldSpace, blendState
            ))
                gb.AddLine(start, end, firstColor, secondColor);
        }


        private BitmapBatch GetBitmapBatch (int? layer, bool? worldSpace, BlendState blendState, SamplerState samplerState) {
            if (Materials == null)
                throw new InvalidOperationException("You cannot use the argumentless ImperativeRenderer constructor.");

            var material = Materials.GetBitmapMaterial(
                worldSpace.GetValueOrDefault(WorldSpace),
                rasterizerState: RasterizerState,
                depthStencilState: DepthStencilState,
                blendState: blendState ?? BlendState
            );
            var pbb = PreviousBatch as BitmapBatch;
            var actualLayer = layer.GetValueOrDefault(Layer);

            if (
                (pbb != null) &&
                (pbb.Container == Container) &&
                (pbb.Layer == actualLayer) &&
                (pbb.Material == material) &&
                (pbb.SamplerState == (samplerState ?? SamplerState)) &&
                (pbb.UseZBuffer == UseZBuffer)
            )
                return pbb;

            var result = BitmapBatch.New(Container, actualLayer, material, samplerState ?? SamplerState, UseZBuffer);
            PreviousBatch = result;

            if (AutoIncrementLayer && !layer.HasValue)
                Layer += 1;

            return result;
        }

        private GeometryBatch<T> GetGeometryBatch<T> (int? layer, bool? worldSpace, BlendState blendState) 
            where T : struct, IVertexType {

            if (Materials == null)
                throw new InvalidOperationException("You cannot use the argumentless ImperativeRenderer constructor.");

            var material = Materials.GetGeometryMaterial(
                worldSpace.GetValueOrDefault(WorldSpace),
                rasterizerState: RasterizerState,
                depthStencilState: DepthStencilState,
                blendState: blendState ?? BlendState
            );
            var pgb = PreviousBatch as GeometryBatch<T>;
            var actualLayer = layer.GetValueOrDefault(Layer);

            if (
                (pgb != null) &&
                (pgb.Container == Container) &&
                (pgb.Layer == actualLayer) &&
                (pgb.Material == material)
            )
                return pgb;

            var result = GeometryBatch<T>.New(Container, actualLayer, material);
            PreviousBatch = result;

            if (AutoIncrementLayer && !layer.HasValue)
                Layer += 1;

            return result;
        }
    }
}
