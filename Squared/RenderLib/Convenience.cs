using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Game;
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
        private struct CachedBatch {
            public Batch Batch;

            public readonly Type BatchType;
            public readonly IBatchContainer Container;
            public readonly int Layer;
            public readonly bool WorldSpace;
            public readonly BlendState BlendState;
            public readonly SamplerState SamplerState;
            public readonly bool UseZBuffer;
            public readonly RasterizerState RasterizerState;
            public readonly DepthStencilState DepthStencilState;
            public readonly Material CustomMaterial;
            public readonly int HashCode;

            public CachedBatch (
                Type batchType,
                IBatchContainer container,
                int layer,
                bool worldSpace,
                RasterizerState rasterizerState,
                DepthStencilState depthStencilState,
                BlendState blendState,
                SamplerState samplerState,
                Material customMaterial,
                bool useZBuffer
            ) {
                Batch = null;
                BatchType = batchType;
                Container = container;
                Layer = layer;
                WorldSpace = worldSpace;
                RasterizerState = rasterizerState;
                DepthStencilState = depthStencilState;
                BlendState = blendState;
                SamplerState = samplerState;
                CustomMaterial = customMaterial;
                UseZBuffer = useZBuffer;

                HashCode = Container.GetHashCode() ^ 
                    Layer.GetHashCode();

                if (BlendState != null)
                    HashCode ^= BlendState.GetHashCode();

                if (SamplerState != null)
                    HashCode ^= SamplerState.GetHashCode();

                if (CustomMaterial != null)
                    HashCode ^= CustomMaterial.GetHashCode();
            }

            public bool KeysEqual (ref CachedBatch rhs) {
                return (
                    (BatchType == rhs.BatchType) &&
                    (Container == rhs.Container) &&
                    (Layer == rhs.Layer) &&
                    (WorldSpace == rhs.WorldSpace) &&
                    (BlendState == rhs.BlendState) &&
                    (SamplerState == rhs.SamplerState) &&
                    (UseZBuffer == rhs.UseZBuffer) &&
                    (RasterizerState == rhs.RasterizerState) &&
                    (DepthStencilState == rhs.DepthStencilState) &&
                    (CustomMaterial == rhs.CustomMaterial)
                );
            }

            public override int GetHashCode() {
                return HashCode;
            }
        }

        private struct CachedBatches {
            public const int Capacity = 4;

            public int Count;
            public CachedBatch Batch0, Batch1, Batch2, Batch3;

            public bool TryGet<T> (
                out CachedBatch result,
                IBatchContainer container, 
                int layer, 
                bool worldSpace, 
                RasterizerState rasterizerState, 
                DepthStencilState depthStencilState, 
                BlendState blendState, 
                SamplerState samplerState, 
                Material customMaterial,
                bool useZBuffer
            ) {
                CachedBatch itemAtIndex, searchKey;

                searchKey = new CachedBatch(
                    typeof(T),
                    container,
                    layer,
                    worldSpace,
                    rasterizerState,
                    depthStencilState,
                    blendState,
                    samplerState,
                    customMaterial,
                    useZBuffer
                );

                for (var i = 0; i < Count; i++) {
                    GetItemAtIndex(i, out itemAtIndex);

                    if (itemAtIndex.HashCode != searchKey.HashCode)
                        continue;

                    if (itemAtIndex.KeysEqual(ref searchKey)) {
                        result = itemAtIndex;
                        InsertAtFront(ref itemAtIndex, i);
                        return (result.Batch != null);
                    }
                }

                result = searchKey;
                return false;
            }

            public void InsertAtFront (ref CachedBatch item, int? previousIndex) {
                // No-op
                if (previousIndex == 0)
                    return;
                else if (Count == 0) {
                    SetItemAtIndex(0, ref item);
                    Count += 1;
                    return;
                }

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

                if (!previousIndex.HasValue) {
                    if (Count < Capacity)
                        Count += 1;
                }
            }

            private void GetItemAtIndex(int index, out CachedBatch result) {
                switch (index) {
                    case 0:
                        result = Batch0;
                        break;
                    case 1:
                        result = Batch1;
                        break;
                    case 2:
                        result = Batch2;
                        break;
                    case 3:
                        result = Batch3;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException("index");
                }
            }

            private void SetItemAtIndex (int index, ref CachedBatch value) {
                switch (index) {
                    case 0:
                        Batch0 = value;
                        break;
                    case 1:
                        Batch1 = value;
                        break;
                    case 2:
                        Batch2 = value;
                        break;
                    case 3:
                        Batch3 = value;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException("index");
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
        public bool WorldSpace;
        public bool UseZBuffer;
        public bool AutoIncrementLayer;
        public bool AutoIncrementSortKey;

        public Sorter<BitmapDrawCall> DeclarativeSorter;

        private BitmapSortKey NextSortKey;
        private CachedBatches Cache;

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
            WorldSpace = worldSpace;
            AutoIncrementSortKey = autoIncrementSortKey;
            AutoIncrementLayer = autoIncrementLayer;
            NextSortKey = new BitmapSortKey(tags, 0);
            Cache = new CachedBatches();
            DeclarativeSorter = declarativeSorter;
        }

        public Tags DefaultTags {
            get {
                return NextSortKey.Tags;
            }
            set {
                NextSortKey.Tags = value;
            }
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

        public ImperativeRenderer ForRenderTarget (RenderTarget2D renderTarget, Action<DeviceManager, object> before = null, Action<DeviceManager, object> after = null, object userData = null) {
            var result = this;
            var group = BatchGroup.ForRenderTarget(Container, Layer, renderTarget, before, after, userData);
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
            BlendState blendState = null, SamplerState samplerState = null,
            Material material = null
        ) {
            Draw(ref drawCall, layer, worldSpace, blendState, samplerState, material);
        }

        public void Draw (
            ref BitmapDrawCall drawCall, 
            int? layer = null, bool? worldSpace = null,
            BlendState blendState = null, SamplerState samplerState = null,
            Material material = null
        ) {
            if (Container == null)
                throw new InvalidOperationException("You cannot use the argumentless ImperativeRenderer constructor.");
            else if (Container.IsDisposed)
                throw new ObjectDisposedException("The container this ImperativeRenderer is drawing into has been disposed.");

            using (var batch = GetBitmapBatch(
                layer, worldSpace,
                blendState, samplerState,
                material
            ))
                batch.Add(ref drawCall);
        }


        public void Draw (
            Texture2D texture, Vector2 position,
            Rectangle? sourceRectangle = null, Color? multiplyColor = null, Color addColor = default(Color),
            float rotation = 0, Vector2? scale = null, Vector2 origin = default(Vector2),
            bool mirrorX = false, bool mirrorY = false, BitmapSortKey? sortKey = null,
            int? layer = null, bool? worldSpace = null, 
            BlendState blendState = null, SamplerState samplerState = null,
            Material material = null
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

            drawCall.SortKey = sortKey.GetValueOrDefault(NextSortKey);
            if (AutoIncrementSortKey)
                NextSortKey.Order += 1;

            Draw(ref drawCall, layer: layer, worldSpace: worldSpace, blendState: blendState, samplerState: samplerState, material: material);
        }

        public void Draw (
            Texture2D texture, float x, float y,
            Rectangle? sourceRectangle = null, Color? multiplyColor = null, Color addColor = default(Color),
            float rotation = 0, float scaleX = 1, float scaleY = 1, float originX = 0, float originY = 0,
            bool mirrorX = false, bool mirrorY = false, BitmapSortKey? sortKey = null,
            int? layer = null, bool? worldSpace = null,
            BlendState blendState = null, SamplerState samplerState = null,
            Material material = null
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

            drawCall.SortKey = sortKey.GetValueOrDefault(NextSortKey);
            if (AutoIncrementSortKey)
                NextSortKey.Order += 1;

            Draw(ref drawCall, layer: layer, worldSpace: worldSpace, blendState: blendState, samplerState: samplerState);
        }

        public void Draw (
            Texture2D texture, Rectangle destRectangle,
            Rectangle? sourceRectangle = null, Color? multiplyColor = null, Color addColor = default(Color),
            float rotation = 0, float originX = 0, float originY = 0,
            bool mirrorX = false, bool mirrorY = false, BitmapSortKey? sortKey = null,
            int? layer = null, bool? worldSpace = null,
            BlendState blendState = null, SamplerState samplerState = null,
            Material material = null
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

            drawCall.SortKey = sortKey.GetValueOrDefault(NextSortKey);
            if (AutoIncrementSortKey)
                NextSortKey.Order += 1;

            Draw(ref drawCall, layer: layer, worldSpace: worldSpace, blendState: blendState, samplerState: samplerState, material: material);
        }

        public void DrawMultiple (
            ArraySegment<BitmapDrawCall> drawCalls,
            Vector2? offset = null, Color? multiplyColor = null, Color? addColor = null, BitmapSortKey? sortKey = null,
            int? layer = null, bool? worldSpace = null,
            BlendState blendState = null, SamplerState samplerState = null, Vector2? scale = null,
            Material material = null
        ) {
            using (var batch = GetBitmapBatch(layer, worldSpace, blendState, samplerState, material))
                batch.AddRange(
                    drawCalls.Array, drawCalls.Offset, drawCalls.Count,
                    offset: offset, multiplyColor: multiplyColor, addColor: addColor, sortKey: sortKey,
                    scale: scale
                );
        }

        public void DrawString (
            SpriteFont font, string text,
            Vector2 position, Color? color = null, float scale = 1, BitmapSortKey? sortKey = null,
            int characterSkipCount = 0, int characterLimit = int.MaxValue,
            int? layer = null, bool? worldSpace = null,
            BlendState blendState = null, SamplerState samplerState = null
        ) {
            using (var buffer = BufferPool<BitmapDrawCall>.Allocate(text.Length)) {
                var layout = font.LayoutString(
                    text, new ArraySegment<BitmapDrawCall>(buffer.Data),
                    position, color, scale, sortKey.GetValueOrDefault(NextSortKey),
                    characterSkipCount, characterLimit, alignToPixels: true
                );

                DrawMultiple(
                    layout,
                    layer: layer, worldSpace: worldSpace, blendState: blendState, samplerState: samplerState
                );

                buffer.Clear();
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
            using (var gb = GetGeometryBatch(layer, worldSpace, blendState))
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


        public BitmapBatch GetBitmapBatch (int? layer, bool? worldSpace, BlendState blendState, SamplerState samplerState, Material customMaterial) {
            if (Materials == null)
                throw new InvalidOperationException("You cannot use the argumentless ImperativeRenderer constructor.");

            var actualLayer = layer.GetValueOrDefault(Layer);
            var actualWorldSpace = worldSpace.GetValueOrDefault(WorldSpace);
            var desiredBlendState = blendState ?? BlendState;
            var desiredSamplerState = samplerState ?? SamplerState;

            CachedBatch cacheEntry;
            if (!Cache.TryGet<BitmapBatch>(
                out cacheEntry,
                container: Container,
                layer: actualLayer,
                worldSpace: actualWorldSpace,
                rasterizerState: RasterizerState,
                depthStencilState: DepthStencilState,
                blendState: desiredBlendState,
                samplerState: desiredSamplerState,
                customMaterial: customMaterial,
                useZBuffer: UseZBuffer
            )) {
                Material material;

                if (customMaterial != null) {
                    material = Materials.Get(
                        customMaterial, RasterizerState, DepthStencilState, desiredBlendState
                    );
                } else {
                    material = Materials.GetBitmapMaterial(
                        actualWorldSpace,
                        RasterizerState, DepthStencilState, desiredBlendState
                    );
                }

                cacheEntry.Batch = BitmapBatch.New(Container, actualLayer, material, desiredSamplerState, desiredSamplerState, UseZBuffer);
                Cache.InsertAtFront(ref cacheEntry, null);
            }

            if (AutoIncrementLayer && !layer.HasValue)
                Layer += 1;

            return (BitmapBatch)cacheEntry.Batch;
        }

        public GeometryBatch GetGeometryBatch (int? layer, bool? worldSpace, BlendState blendState) {
            if (Materials == null)
                throw new InvalidOperationException("You cannot use the argumentless ImperativeRenderer constructor.");

            var actualLayer = layer.GetValueOrDefault(Layer);
            var actualWorldSpace = worldSpace.GetValueOrDefault(WorldSpace);
            var desiredBlendState = blendState ?? BlendState;

            CachedBatch cacheEntry;
            if (!Cache.TryGet<GeometryBatch>(
                out cacheEntry,
                container: Container,
                layer: actualLayer,
                worldSpace: actualWorldSpace,
                rasterizerState: RasterizerState,
                depthStencilState: DepthStencilState,
                blendState: desiredBlendState,
                samplerState: null,
                customMaterial: null,
                useZBuffer: UseZBuffer
            )) {
                var material = Materials.GetGeometryMaterial(
                    actualWorldSpace,
                    rasterizerState: RasterizerState,
                    depthStencilState: DepthStencilState,
                    blendState: desiredBlendState
                );

                cacheEntry.Batch = GeometryBatch.New(Container, actualLayer, material);
                Cache.InsertAtFront(ref cacheEntry, null);
            }

            if (AutoIncrementLayer && !layer.HasValue)
                Layer += 1;

            return (GeometryBatch)cacheEntry.Batch;
        }
    }

    public class Atlas : IEnumerable<Atlas.Cell> {
        public struct Cell {
            public readonly Atlas Atlas;
            public readonly int Index;
            public readonly Bounds Bounds;
            public readonly Rectangle Rectangle;

            public Cell (Atlas atlas, int index, ref Bounds bounds, ref Rectangle rectangle) {
                Atlas = atlas;
                Index = index;
                Bounds = bounds;
                Rectangle = rectangle;
            }

            public Texture2D Texture {
                get {
                    return Atlas.Texture;
                }
            }

            public static implicit operator Texture2D (Cell cell) {
                return cell.Atlas.Texture;
            }

            public static implicit operator Rectangle (Cell cell) {
                return cell.Rectangle;
            }

            public static implicit operator Bounds (Cell cell) {
                return cell.Bounds;
            }
        }

        public struct SubRegion {
            public readonly Atlas Atlas;
            public readonly int Left, Top, Width, Height;

            public SubRegion (Atlas atlas, int left, int top, int width, int height) {
                Atlas = atlas;
                Left = left;
                Top = top;
                Width = width;
                Height = height;

                if (width <= 0)
                    throw new ArgumentOutOfRangeException("width");
                if (height <= 0)
                    throw new ArgumentOutOfRangeException("height");
            }

            public int Count {
                get {
                    return Width * Height;
                }
            }

            public Cell this[int index] {
                get {
                    int x = index % Width;
                    int y = index / Width;

                    var offsetX = Left + x;
                    var offsetY = Top + y;

                    return Atlas[offsetX, offsetY];
                }
            }

            public Cell this[int x, int y] {
                get {
                    var offsetX = Left + x;
                    var offsetY = Top + y;

                    return Atlas[offsetX, offsetY];
                }
            }
        }

        public readonly Texture2D Texture;
        public readonly int CellWidth, CellHeight;
        public readonly int MarginLeft, MarginTop, MarginRight, MarginBottom;
        public readonly int WidthInCells, HeightInCells;

        private readonly List<Cell> Cells = new List<Cell>();  

        public Atlas (
            Texture2D texture, int cellWidth, int cellHeight,
            int marginLeft = 0, int marginTop = 0,
            int marginRight = 0, int marginBottom = 0
        ) {
            Texture = texture;
            CellWidth = cellWidth;
            CellHeight = cellHeight;
            MarginLeft = marginLeft;
            MarginTop = marginTop;
            MarginRight = marginRight;
            MarginBottom = marginBottom;

            if (texture == null)
                throw new ArgumentNullException("texture");
            if (cellWidth <= 0)
                throw new ArgumentOutOfRangeException("cellWidth");
            if (cellHeight <= 0)
                throw new ArgumentOutOfRangeException("cellHeight");

            WidthInCells = InteriorWidth / CellWidth;
            HeightInCells = InteriorHeight / CellHeight;

            GenerateCells();
        }

        public static Atlas FromCount (
            Texture2D texture, int countX, int countY,
            int marginLeft = 0, int marginTop = 0,
            int marginRight = 0, int marginBottom = 0
        ) {
            var w = texture.Width - (marginLeft + marginRight);
            var h = texture.Height - (marginTop + marginTop);

            return new Atlas(
                texture, w / countX, h / countY,
                marginLeft, marginTop, marginRight, marginBottom
            );
        }

        private int InteriorWidth {
            get {
                return Texture.Width - (MarginLeft + MarginRight);
            }
        }

        private int InteriorHeight {
            get {
                return Texture.Height - (MarginTop + MarginBottom);
            }
        }

        public int Count {
            get {
                return WidthInCells * HeightInCells;
            }
        }

        public Cell this[int index] {
            get {
                return Cells[index];
            }
        }

        public Cell this[int x, int y] {
            get {
                if ((x < 0) || (x >= WidthInCells))
                    throw new ArgumentOutOfRangeException("x");
                if ((y < 0) || (y >= HeightInCells))
                    throw new ArgumentOutOfRangeException("y");

                int index = (y * WidthInCells) + x;
                return Cells[index];
            }
        }

        private void GenerateCells () {
            for (int y = 0, i = 0; y < HeightInCells; y++) {
                for (int x = 0; x < WidthInCells; x++, i++) {
                    var rectangle = new Rectangle(
                        (CellWidth * x) + MarginLeft,
                        (CellHeight * y) + MarginTop,
                        CellWidth, CellHeight
                    );
                    var bounds = Texture.BoundsFromRectangle(ref rectangle);
                    var cell = new Cell(this, i, ref bounds, ref rectangle);

                    Cells.Add(cell);
                }
            }
        }

        public List<Cell>.Enumerator GetEnumerator () {
            return Cells.GetEnumerator();
        }

        IEnumerator<Cell> IEnumerable<Cell>.GetEnumerator () {
            return Cells.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator () {
            return Cells.GetEnumerator();
        }
    }
}
