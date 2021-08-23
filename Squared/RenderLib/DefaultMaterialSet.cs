using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework;
using System.Threading;
using Microsoft.Xna.Framework.Content;
using System.Reflection;
using Squared.Render.Convenience;
using Squared.Util;
using System.Runtime.InteropServices;
using Squared.Game;
using Squared.Render.Resources;

namespace Squared.Render {
    [StructLayout(LayoutKind.Sequential)]
    public struct ViewTransform {
        public Matrix Projection;
        public Matrix ModelView;

        internal Vector4 ScaleAndPosition;
        internal Vector4 InputAndOutputZRanges;

        public float MinimumZ {
            get {
                return InputAndOutputZRanges.X;
            }
            set {
                InputAndOutputZRanges.X = value;
            }
        }

        public float MaximumZ {
            get {
                return InputAndOutputZRanges.Y + 1;
            }
            set {
                InputAndOutputZRanges.Y = value - 1;
            }
        }

        public void ResetZRanges () {
            InputAndOutputZRanges = Vector4.Zero;
        }

        public Vector2 ZRange {
            get {
                return new Vector2(InputAndOutputZRanges.X, InputAndOutputZRanges.Y + 1);
            }
            set {
                InputAndOutputZRanges.X = value.X;
                InputAndOutputZRanges.Y = value.Y - 1;
            }
        }

        public Vector2 OutputZRange {
            get {
                return new Vector2(InputAndOutputZRanges.Z, InputAndOutputZRanges.W + 1);
            }
            set {
                InputAndOutputZRanges.Z = value.X;
                InputAndOutputZRanges.W = value.Y - 1;
            }
        }

        public Vector2 Scale {
            get {
                return new Vector2(ScaleAndPosition.X, ScaleAndPosition.Y);
            }
            set {
                ScaleAndPosition.X = value.X;
                ScaleAndPosition.Y = value.Y;
            }
        }

        public Vector2 Position {
            get {
                return new Vector2(ScaleAndPosition.Z, ScaleAndPosition.W);
            }
            set {
                ScaleAndPosition.Z = value.X;
                ScaleAndPosition.W = value.Y;
            }
        }

        public static readonly ViewTransform Default = new ViewTransform {
            Scale = Vector2.One,
            Position = Vector2.Zero,
            Projection = Matrix.Identity,
            ModelView = Matrix.Identity,
            InputAndOutputZRanges = Vector4.Zero
        };

        public static ViewTransform CreateOrthographic (Viewport viewport) {
            return CreateOrthographic(viewport.X, viewport.Y, viewport.Width, viewport.Height, viewport.MinDepth, viewport.MaxDepth);
        }

        public static ViewTransform CreateOrthographic (int screenWidth, int screenHeight, float zNearPlane = 0, float zFarPlane = 1) {
            return CreateOrthographic(0, 0, screenWidth, screenHeight, zNearPlane, zFarPlane);
        }

        public static ViewTransform CreateOrthographic (int x, int y, int width, int height, float zNearPlane = 0, float zFarPlane = 1) {
            float offsetX = -0.0f;
            float offsetY = -0.0f;
            float offsetX2 = offsetX;
            float offsetY2 = offsetY;
            var projection = Matrix.CreateOrthographicOffCenter(offsetX, width + offsetX2, height + offsetY2, offsetY, zNearPlane, zFarPlane);
            // FIXME: Why the heck is the default -1????? This makes no sense
            projection.M33 = 1;
            return new ViewTransform {
                Scale = Vector2.One,
                Position = Vector2.Zero,
                Projection = projection,
                ModelView = Matrix.Identity
            };
        }

        public bool Equals (ref ViewTransform rhs) {
            return (Scale == rhs.Scale) &&
                (Position == rhs.Position) &&
                (Projection == rhs.Projection) &&
                (ModelView == rhs.ModelView) &&
                (InputAndOutputZRanges.FastEquals(ref rhs.InputAndOutputZRanges));
        }

        public override bool Equals (object obj) {
            if (!(obj is ViewTransform))
                return false;

            var vt = (ViewTransform)obj;
            return Equals(ref vt);
        }

        public override int GetHashCode () {
            return Scale.GetHashCode() ^ Position.GetHashCode() ^ Projection.GetHashCode() ^ ModelView.GetHashCode();
        }

        public override string ToString () {
            return string.Format("ViewTransform pos={0} scale={1}", Position, Scale);
        }
    }

    public class ActiveViewTransformInfo {
        public readonly DefaultMaterialSet MaterialSet;
        public ViewTransform ViewTransform;
        public uint Id = 0;
        public Material ActiveMaterial;

        internal ActiveViewTransformInfo (DefaultMaterialSet materialSet) {
            MaterialSet = materialSet;
        }

        public bool AutoApply (Material m) {
            bool hasChanged = m.ActiveViewTransformId != Id;
            MaterialSet.ApplyViewTransformToMaterial(m, ref ViewTransform);
            m.ActiveViewTransformId = Id;
            return hasChanged;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DitheringSettings {
        private Vector4 StrengthUnitAndIndex;
        private Vector4 BandSizeAndRange;

        public float Strength {
            get {
                return StrengthUnitAndIndex.X;
            }
            set {
                StrengthUnitAndIndex.X = value;
            }
        }

        public float FrameIndex {
            get {
                return StrengthUnitAndIndex.W;
            }
            set {
                StrengthUnitAndIndex.W = value;
            }
        }

        public float RangeMin {
            get {
                return BandSizeAndRange.Y;
            }
            set {
                BandSizeAndRange.Y = value;
            }
        }

        public float RangeMax {
            get {
                return BandSizeAndRange.Z + 1;
            }
            set {
                BandSizeAndRange.Z = value - 1;
            }
        }

        public float BandSize {
            get {
                return BandSizeAndRange.X + 1;
            }
            set {
                BandSizeAndRange.X = value - 1;
            }
        }

        /// <summary>
        /// Determines the scale of values before dithering. Set to 255 for 8 bit RGBA, 65535 for 16 bit RGBA, 
        ///  or some other random value if you love weird visuals
        /// </summary>
        public float Unit {
            get {
                return StrengthUnitAndIndex.Y;
            }
            set {
                StrengthUnitAndIndex.Y = value;
                if (StrengthUnitAndIndex.Y < 1)
                    StrengthUnitAndIndex.Y = 1;
                StrengthUnitAndIndex.Z = 1.0f / StrengthUnitAndIndex.Y;
            }
        }

        /// <summary>
        /// Automatically sets Unit for you based on a power of two (8 for 8 bits, etc)
        /// </summary>
        public int Power {
            set {
                StrengthUnitAndIndex.Y = (1 << value) - 1;
                if (StrengthUnitAndIndex.Y < 1)
                    StrengthUnitAndIndex.Y = 1;
                StrengthUnitAndIndex.Z = 1.0f / StrengthUnitAndIndex.Y;
            }
        }
    }

    public class DefaultMaterialSet : MaterialSetBase {
        /// <summary>
        /// Enables preloading for raster shape shaders. This is pretty expensive.
        /// </summary>
        public static bool PreloadAllRasterShapeShaders = false;
        /// <summary>
        /// Enables preloading for common raster shape shaders. This is... less expensive.
        /// </summary>
        public static bool PreloadCommonRasterShapeShaders = false;
        /// <summary>
        /// Enables preloading for filter shaders (blurs, dithering, etc.)
        /// </summary>
        public static bool PreloadFilterShaders = false;
        /// <summary>
        /// Enables preloading all shaders.
        /// </summary>
        public static bool PreloadAllShaders = false;

        public struct RasterShaderKey {
            public sealed class Comparer : IEqualityComparer<RasterShaderKey> {
                public bool Equals (RasterShaderKey x, RasterShaderKey y) {
                    return x.Equals(y);
                }

                public int GetHashCode (RasterShaderKey obj) {
                    return obj.GetHashCode();
                }
            }

            public RasterShape.RasterShapeType? Type;
            public bool Shadowed, Textured, Simple, HasRamp;

            public bool Equals (RasterShaderKey rhs) {
                return (Type == rhs.Type) &&
                    (Shadowed == rhs.Shadowed) &&
                    (Textured == rhs.Textured) &&
                    (Simple == rhs.Simple) &&
                    (HasRamp == rhs.HasRamp);
            }

            public override bool Equals (object obj) {
                if (obj is RasterShaderKey)
                    return Equals((RasterShaderKey)obj);
                else
                    return false;
            }

            public override int GetHashCode () {
                return (int)(Type ?? 0) | (Shadowed ? 64 : 0) | (Textured ? 128 : 0) | (Simple ? 256 : 0);
            }
        }

        protected struct MaterialCacheKey {
            public readonly Material Material;
            public readonly RasterizerState RasterizerState;
            public readonly DepthStencilState DepthStencilState;
            public readonly BlendState BlendState;

            public MaterialCacheKey (Material material, RasterizerState rasterizerState, DepthStencilState depthStencilState, BlendState blendState) {
                Material = material;
                RasterizerState = rasterizerState;
                DepthStencilState = depthStencilState;
                BlendState = blendState;
            }

            private static int HashNullable<T> (T o, int shift) where T : class {
                if (o == null)
                    return 0;
                else
                    return o.GetHashCode() << shift;
            }

            public bool Equals (ref MaterialCacheKey rhs) {
                return (Material == rhs.Material) &&
                    (RasterizerState == rhs.RasterizerState) &&
                    (DepthStencilState == rhs.DepthStencilState) &&
                    (BlendState == rhs.BlendState);
            }

            public override bool Equals (object obj) {
                if (obj is MaterialCacheKey) {
                    var mck = (MaterialCacheKey)obj;
                    return Equals(ref mck);
                } else
                    return base.Equals(obj);
            }

            public override int GetHashCode () {
                return Material.GetHashCode() ^
                    HashNullable(RasterizerState, 2) ^
                    HashNullable(DepthStencilState, 4) ^
                    HashNullable(BlendState, 6);
            }
        }

        protected sealed class MaterialCacheKeyComparer : IEqualityComparer<MaterialCacheKey> {
            public bool Equals (MaterialCacheKey x, MaterialCacheKey y) {
                return x.Equals(ref y);
            }

            public int GetHashCode (MaterialCacheKey obj) {
                return obj.GetHashCode();
            }
        }

        public readonly EffectProvider BuiltInShaders;
        public readonly ITimeProvider  TimeProvider;

        protected readonly MaterialDictionary<MaterialCacheKey> MaterialDictionary = new MaterialDictionary<MaterialCacheKey>(
            new MaterialCacheKeyComparer()
        );

        public Material Bitmap, BitmapWithDiscard;
        public Material ScreenSpaceBitmapToSRGB, WorldSpaceBitmapToSRGB;
        public Material ScreenSpaceBitmapWithLUT, WorldSpaceBitmapWithLUT;
        public Material ScreenSpaceShadowedBitmap, WorldSpaceShadowedBitmap;
        public Material ScreenSpacePalettedBitmap, WorldSpacePalettedBitmap;
        public Material ScreenSpaceHueBitmap, WorldSpaceHueBitmap;
        public Material ScreenSpaceSepiaBitmap, WorldSpaceSepiaBitmap;
        public Material ScreenSpaceSepiaBitmapWithDiscard, WorldSpaceSepiaBitmapWithDiscard;
        public Material ScreenSpaceShadowedBitmapWithDiscard, WorldSpaceShadowedBitmapWithDiscard;
        public Material ScreenSpaceStippledBitmap, WorldSpaceStippledBitmap;
        public Material ScreenSpacePalettedBitmapWithDiscard, WorldSpacePalettedBitmapWithDiscard;
        public Material ScreenSpaceHueBitmapWithDiscard, WorldSpaceHueBitmapWithDiscard;
        public Material OutlinedBitmap, OutlinedBitmapWithDiscard;
        public Material HighlightColorBitmap, CrossfadeBitmap;
        // Porter-duff compositing
        public Material UnderBitmap, OverBitmap;
        public Material ScreenSpaceGeometry, WorldSpaceGeometry;
        public Material ScreenSpaceTexturedGeometry, WorldSpaceTexturedGeometry;
        public Material ScreenSpaceLightmappedBitmap, WorldSpaceLightmappedBitmap;
        public Material RasterShapeUbershader;
        public readonly Dictionary<RasterShaderKey, RasterShape.RasterShader> RasterShapeMaterials =
            new Dictionary<RasterShaderKey, RasterShape.RasterShader>(new RasterShaderKey.Comparer());
        /// <summary>
        /// Make sure to resolve your lightmap to sRGB before using it with this, otherwise your lighting
        ///  will have really terrible banding in dark areas.
        /// </summary>
        public Material ScreenSpaceLightmappedsRGBBitmap, WorldSpaceLightmappedsRGBBitmap;
        public Material ScreenSpaceHorizontalGaussianBlur, ScreenSpaceVerticalGaussianBlur, ScreenSpaceRadialGaussianBlur;
        public Material WorldSpaceHorizontalGaussianBlur, WorldSpaceVerticalGaussianBlur, WorldSpaceRadialGaussianBlur;
        public Material Clear, SetScissor, SetViewport;

        private readonly Action<Material, FrameParams> _ApplyParamsDelegate;
        protected readonly RefMaterialAction<ViewTransform> _ApplyViewTransformDelegate; 
        protected readonly Stack<ViewTransform> ViewTransformStack = new Stack<ViewTransform>();

        /// <summary>
        /// If true, view transform changes are lazily applied at the point each material is activated
        ///  instead of being eagerly applied to all materials whenever you change the view transform
        /// </summary>
        public bool LazyViewTransformChanges = true;

        /// <summary>
        /// Controls the strength of dithering applied to the result of the lightmapped bitmap materials, along with
        ///  LUTs, sRGB conversions, and raster shapes.
        /// </summary>
        public DitheringSettings DefaultDitheringSettings;

        public readonly ActiveViewTransformInfo ActiveViewTransform;

        internal readonly TypedUniform<ViewTransform> uViewport;
        internal readonly TypedUniform<DitheringSettings> uDithering;

        public readonly RenderCoordinator Coordinator;

        // FIXME: Should we have separate materials?
        public Material ScreenSpaceBitmap {
            get {
                return Bitmap;
            }
        }
        public Material WorldSpaceBitmap {
            get {
                return Bitmap;
            }
        }
        public Material ScreenSpaceBitmapWithDiscard {
            get {
                return BitmapWithDiscard;
            }
        }
        public Material WorldSpaceBitmapWithDiscard {
            get {
                return BitmapWithDiscard;
            }
        }

        private struct FrameParams {
            public float Seconds;
            public int? FrameIndex;

            public bool Equals (FrameParams rhs) {
                return (Seconds == rhs.Seconds) && (FrameIndex == rhs.FrameIndex);
            }

            public override bool Equals (object obj) {
                if (!(obj is FrameParams))
                    return false;

                return Equals((FrameParams)obj);
            }
        }

        private Material NewMaterial (Effect effect, string techniqueName) {
            var result = new Material(effect, techniqueName);
            return result;
        }

        public DefaultMaterialSet (RenderCoordinator coordinator, ITimeProvider timeProvider = null) {
            Coordinator = coordinator;
            ActiveViewTransform = new ActiveViewTransformInfo(this);
            // HACK
            coordinator.Manager.DeviceManager.ActiveViewTransform = ActiveViewTransform;
            _ApplyViewTransformDelegate = ApplyViewTransformToMaterial;
            _ApplyParamsDelegate = ApplyParamsToMaterial;

            uViewport = NewTypedUniform<ViewTransform>("Viewport");
            uDithering = NewTypedUniform<DitheringSettings>("Dithering");

            DefaultDitheringSettings = new DitheringSettings {
                Unit = 255,
                Strength = 1.0f,
                FrameIndex = 0
            };

            TimeProvider = timeProvider ?? new DotNetTimeProvider();

            BuiltInShaders = new EffectProvider(Assembly.GetExecutingAssembly(), coordinator);

            Clear = new Material(
                null, null,
                new Action<DeviceManager>[] { (dm) => ApplyShaderVariables(false, dm.FrameIndex) }
            );

            SetScissor = new Material(
                null, null
            );

            SetViewport = new Material(
                null, null
            );

            var bitmapHint = new Material.PipelineHint {
                HasIndices = true,
                VertexFormats = new Type[] {
                    typeof(CornerVertex),
                    typeof(BitmapVertex)
                }
            };

            var bitmapShader = BuiltInShaders.Load("SquaredBitmapShader");
            var geometryShader = BuiltInShaders.Load("SquaredGeometryShader");
            var palettedShader = BuiltInShaders.Load("PalettedBitmap");
            var hslShader = BuiltInShaders.Load("HueBitmap");
            var stippledShader = BuiltInShaders.Load("StippledBitmap");

            Bitmap = NewMaterial(
                bitmapShader,
                "BitmapTechnique"
            );

            HighlightColorBitmap = NewMaterial(
                bitmapShader,
                "HighlightColorBitmapTechnique"
            );

            CrossfadeBitmap = NewMaterial(
                bitmapShader,
                "CrossfadeBitmapTechnique"
            );

            OverBitmap = NewMaterial(
                bitmapShader,
                "OverBitmapTechnique"
            );

            UnderBitmap = NewMaterial(
                bitmapShader,
                "UnderBitmapTechnique"
            );

            ScreenSpaceBitmapWithLUT = NewMaterial(
                bitmapShader,
                "ScreenSpaceBitmapWithLUTTechnique"
            );

            WorldSpaceBitmapWithLUT = NewMaterial(
                bitmapShader,
                "WorldSpaceBitmapWithLUTTechnique"
            );

            ScreenSpaceBitmapToSRGB = NewMaterial(
                bitmapShader,
                "ScreenSpaceBitmapToSRGBTechnique"
            );

            WorldSpaceBitmapToSRGB = NewMaterial(
                bitmapShader,
                "WorldSpaceBitmapToSRGBTechnique"
            );

            var defaultOffset = Vector2.One;

            ScreenSpaceShadowedBitmap = NewMaterial(
                bitmapShader,
                "ScreenSpaceShadowedBitmapTechnique"
            );
            ScreenSpaceShadowedBitmap.Parameters.ShadowOffset.SetValue(defaultOffset);

            WorldSpaceShadowedBitmap = NewMaterial(
                bitmapShader,
                "WorldSpaceShadowedBitmapTechnique"
            );
            WorldSpaceShadowedBitmap.Parameters.ShadowOffset.SetValue(defaultOffset);

            ScreenSpaceShadowedBitmapWithDiscard = NewMaterial(
                bitmapShader,
                "ScreenSpaceShadowedBitmapWithDiscardTechnique"
            );
            ScreenSpaceShadowedBitmapWithDiscard.Parameters.ShadowOffset.SetValue(defaultOffset);

            WorldSpaceShadowedBitmapWithDiscard = NewMaterial(
                bitmapShader,
                "WorldSpaceShadowedBitmapWithDiscardTechnique"
            );
            WorldSpaceShadowedBitmapWithDiscard.Parameters.ShadowOffset.SetValue(defaultOffset);

            OutlinedBitmap = NewMaterial(
                bitmapShader,
                "OutlinedBitmapTechnique"
            );
            OutlinedBitmap.Parameters.ShadowOffset.SetValue(defaultOffset);

            OutlinedBitmapWithDiscard = NewMaterial(
                bitmapShader,
                "OutlinedBitmapWithDiscardTechnique"
            );
            OutlinedBitmapWithDiscard.Parameters.ShadowOffset.SetValue(defaultOffset);

            BitmapWithDiscard = NewMaterial(
                bitmapShader,
                "BitmapWithDiscardTechnique"
            );

            ScreenSpaceStippledBitmap = NewMaterial(
                stippledShader,
                "ScreenSpaceStippledBitmapTechnique"
            );

            WorldSpaceStippledBitmap = NewMaterial(
                stippledShader,
                "WorldSpaceStippledBitmapTechnique"
            );

            ScreenSpacePalettedBitmap = NewMaterial(
                palettedShader,
                "ScreenSpacePalettedBitmapTechnique"
            );

            WorldSpacePalettedBitmap = NewMaterial(
                palettedShader,
                "WorldSpacePalettedBitmapTechnique"
            );

            ScreenSpacePalettedBitmapWithDiscard = NewMaterial(
                palettedShader,
                "ScreenSpacePalettedBitmapWithDiscardTechnique"
            );

            WorldSpacePalettedBitmapWithDiscard = NewMaterial(
                palettedShader,
                "WorldSpacePalettedBitmapWithDiscardTechnique"
            );

            LoadHSLMaterials(hslShader);

            LoadBlurMaterials();

            ScreenSpaceGeometry = NewMaterial(
                geometryShader,
                "ScreenSpaceUntextured"
            );

            WorldSpaceGeometry = NewMaterial(
                geometryShader,
                "WorldSpaceUntextured"
            );

            ScreenSpaceTexturedGeometry = NewMaterial(
                geometryShader,
                "ScreenSpaceTextured"
            );

            WorldSpaceTexturedGeometry = NewMaterial(
                geometryShader,
                "WorldSpaceTextured"
            );

            LoadRasterShapeMaterials();

            var lightmapShader = BuiltInShaders.Load("Lightmap");

            ScreenSpaceLightmappedBitmap = NewMaterial(
                lightmapShader,
                "ScreenSpaceLightmappedBitmap"
            );

            WorldSpaceLightmappedBitmap = NewMaterial(
                lightmapShader,
                "WorldSpaceLightmappedBitmap"
            );

            ScreenSpaceLightmappedsRGBBitmap = NewMaterial(
                lightmapShader,
                "ScreenSpaceLightmappedsRGBBitmap"
            );

            WorldSpaceLightmappedsRGBBitmap = NewMaterial(
                lightmapShader,
                "WorldSpaceLightmappedsRGBBitmap"
            );


            var bitmapMaterials = new[] {
                Bitmap,
                ScreenSpaceBitmapToSRGB,
                WorldSpaceBitmapToSRGB,
                ScreenSpaceShadowedBitmap,
                WorldSpaceShadowedBitmap,
                ScreenSpaceShadowedBitmapWithDiscard,
                WorldSpaceShadowedBitmapWithDiscard,
                BitmapWithDiscard,
                CrossfadeBitmap,
                UnderBitmap,
                OverBitmap,
                OutlinedBitmap,
                OutlinedBitmapWithDiscard
            };

            var filterMaterials = new[] {
                ScreenSpaceBitmapWithLUT,
                WorldSpaceBitmapWithLUT,
                ScreenSpaceStippledBitmap,
                WorldSpaceStippledBitmap,
                ScreenSpacePalettedBitmap,
                WorldSpacePalettedBitmap,
                ScreenSpacePalettedBitmapWithDiscard,
                WorldSpacePalettedBitmapWithDiscard,
                ScreenSpaceHueBitmap,
                WorldSpaceHueBitmap,
                ScreenSpaceHueBitmapWithDiscard,
                WorldSpaceHueBitmapWithDiscard,
                ScreenSpaceSepiaBitmap,
                WorldSpaceSepiaBitmap,
                HighlightColorBitmap,
                ScreenSpaceLightmappedBitmap,
                ScreenSpaceLightmappedsRGBBitmap,
                ScreenSpaceHorizontalGaussianBlur,
                ScreenSpaceVerticalGaussianBlur,
                ScreenSpaceRadialGaussianBlur,
                WorldSpaceLightmappedBitmap,
                WorldSpaceLightmappedsRGBBitmap,
                WorldSpaceHorizontalGaussianBlur,
                WorldSpaceVerticalGaussianBlur,
                WorldSpaceRadialGaussianBlur,
            };

            foreach (var m in bitmapMaterials)
                m.HintPipeline = bitmapHint;

            if (PreloadFilterShaders || PreloadAllShaders)
                foreach (var m in filterMaterials)
                    m.HintPipeline = bitmapHint;

            AutoSetViewTransform();
        }

        private void LoadHSLMaterials (Effect hslShader) {
            ScreenSpaceHueBitmap = NewMaterial(
                            hslShader,
                            "ScreenSpaceHueBitmapTechnique"
                        );

            WorldSpaceHueBitmap = NewMaterial(
                hslShader,
                "WorldSpaceHueBitmapTechnique"
            );

            ScreenSpaceHueBitmapWithDiscard = NewMaterial(
                hslShader,
                "ScreenSpaceHueBitmapWithDiscardTechnique"
            );

            WorldSpaceHueBitmapWithDiscard = NewMaterial(
                hslShader,
                "WorldSpaceHueBitmapWithDiscardTechnique"
            );

            ScreenSpaceSepiaBitmap = NewMaterial(
                hslShader,
                "ScreenSpaceSepiaBitmapTechnique"
            );

            WorldSpaceSepiaBitmap = NewMaterial(
                hslShader,
                "WorldSpaceSepiaBitmapTechnique"
            );

            ScreenSpaceSepiaBitmapWithDiscard = NewMaterial(
                hslShader,
                "ScreenSpaceSepiaBitmapWithDiscardTechnique"
            );

            WorldSpaceSepiaBitmapWithDiscard = NewMaterial(
                hslShader,
                "WorldSpaceSepiaBitmapWithDiscardTechnique"
            );
        }

        private void LoadBlurMaterials () {
            var blurShader = BuiltInShaders.Load("GaussianBlur");

            ScreenSpaceHorizontalGaussianBlur = NewMaterial(
                blurShader,
                "ScreenSpaceHorizontalGaussianBlur"
            );

            ScreenSpaceVerticalGaussianBlur = NewMaterial(
                blurShader,
                "ScreenSpaceVerticalGaussianBlur"
            );

            ScreenSpaceRadialGaussianBlur = NewMaterial(
                blurShader,
                "ScreenSpaceRadialGaussianBlur"
            );

            WorldSpaceHorizontalGaussianBlur = NewMaterial(
                blurShader,
                "WorldSpaceHorizontalGaussianBlur"
            );

            WorldSpaceVerticalGaussianBlur = NewMaterial(
                blurShader,
                "WorldSpaceVerticalGaussianBlur"
            );

            WorldSpaceRadialGaussianBlur = NewMaterial(
                blurShader,
                "WorldSpaceRadialGaussianBlur"
            );
        }

        private void LoadRasterShapeVariant (
            Effect shader, string techniqueName, RasterShape.RasterShapeType? type, bool shadowed, bool textured, bool simple = false, bool ramp = false
        ) {
            if ((simple || ramp) && !shader.Techniques.Any(t => t.Name == techniqueName))
                return;

            var key = new RasterShaderKey {
                Type = type,
                Shadowed = shadowed,
                Textured = textured,
                Simple = simple,
                HasRamp = ramp
            };
            var material = NewMaterial(shader, techniqueName);
            var shapeHint = new Material.PipelineHint {
                HasIndices = true,
                VertexFormats = new Type[] {
                    typeof(CornerVertex),
                    typeof(RasterShape.RasterShapeVertex)
                }
            };
            if (
                PreloadAllRasterShapeShaders ||
                PreloadAllShaders ||
                (PreloadCommonRasterShapeShaders && !textured && !ramp)
            )
                material.HintPipeline = shapeHint;
            RasterShapeMaterials[key] = new RasterShape.RasterShader(material);
            Add(material);
        }

        private void LoadRasterShapeVariants (
            Effect shader, string techniqueSubstring, RasterShape.RasterShapeType? type
        ) {
            LoadRasterShapeVariant(shader, techniqueSubstring + "Technique", type, false, false);
            LoadRasterShapeVariant(shader, "Textured" + techniqueSubstring + "Technique", type, false, true);
            LoadRasterShapeVariant(shader, "Shadowed" + techniqueSubstring + "Technique", type, true, false);
            LoadRasterShapeVariant(shader, "ShadowedTextured" + techniqueSubstring + "Technique", type, true, true);
            LoadRasterShapeVariant(shader, techniqueSubstring + "SimpleTechnique", type, shadowed: false, textured: false, simple: true);
            LoadRasterShapeVariant(shader, "Shadowed" + techniqueSubstring + "SimpleTechnique", type, shadowed: true, textured: false, simple: true);
            LoadRasterShapeVariant(shader, techniqueSubstring + "RampTechnique", type, shadowed: false, textured: false, ramp: true);
            LoadRasterShapeVariant(shader, "Shadowed" + techniqueSubstring + "RampTechnique", type, shadowed: true, textured: false, ramp: true);
        }

        private void LoadRasterShapeMaterials () {
            var rasterShapeUbershader = BuiltInShaders.Load("RasterShapeUbershader");
            var rasterShapeEllipse = BuiltInShaders.Load("RasterShapeEllipse");
            var rasterShapeRectangle = BuiltInShaders.Load("RasterShapeRectangle");
            var rasterShapeLine = BuiltInShaders.Load("RasterShapeLine");
            var rasterShapeTriangle = BuiltInShaders.Load("RasterShapeTriangle");
            var rasterShapePolygon = BuiltInShaders.Load("RasterShapePolygon");

            LoadRasterShapeVariants(
                rasterShapeUbershader, "RasterShape", null
            );
            LoadRasterShapeVariants(
                rasterShapeRectangle, "RasterRectangle", RasterShape.RasterShapeType.Rectangle
            );
            LoadRasterShapeVariants(
                rasterShapeEllipse, "RasterEllipse", RasterShape.RasterShapeType.Ellipse
            );
            LoadRasterShapeVariants(
                rasterShapeLine, "RasterLine", RasterShape.RasterShapeType.LineSegment
            );
            LoadRasterShapeVariants(
                rasterShapeTriangle, "RasterTriangle", RasterShape.RasterShapeType.Triangle
            );
            LoadRasterShapeVariants(
                rasterShapePolygon, "RasterPolygon", RasterShape.RasterShapeType.Polygon
            );

            RasterShapeUbershader = RasterShapeMaterials[new RasterShaderKey { Type = null, Shadowed = false, Textured = false }].Material;
        }

        protected override void QueuePendingRegistrationHandler () {
            Coordinator.BeforePrepare(PerformPendingRegistrations);
        }

        public void AutoSetViewTransform () {
            ViewTransformStack.Clear();

            ViewTransformStack.Push(ViewTransform.CreateOrthographic(
                Coordinator.Device.PresentationParameters.BackBufferWidth,
                Coordinator.Device.PresentationParameters.BackBufferHeight
            ));
        }

        public int ViewTransformStackDepth => ViewTransformStack.Count;

        public ViewTransform ViewTransform {
            get {
                return ViewTransformStack.Peek();
            }
            set {
                ViewTransformStack.Pop();
                ViewTransformStack.Push(value);
                ApplyViewTransform(value, !LazyViewTransformChanges);
            }
        }

        public Vector2 ViewportZRange {
            get {
                return ViewTransform.ZRange;
            }
            set {
                var vt = ViewTransformStack.Peek();
                vt.ZRange = value;
                ViewTransform = vt;
            }
        }

        public Vector2 ViewportScale {
            get {
                return ViewTransform.Scale;
            }
            set {
                var vt = ViewTransformStack.Peek();
                vt.Scale = value;
                ViewTransform = vt;
            }
        }

        public Vector2 ViewportPosition {
            get {
                return ViewTransform.Position;
            }
            set {
                var vt = ViewTransformStack.Peek();
                vt.Position = value;
                ViewTransform = vt;
            }
        }

        public Matrix ProjectionMatrix {
            get {
                return ViewTransform.Projection;
            }
            set {
                var vt = ViewTransformStack.Peek();
                vt.Projection = value;
                ViewTransform = vt;
            }
        }

        public Matrix ModelViewMatrix {
            get {
                return ViewTransform.ModelView;
            }
            set {
                var vt = ViewTransformStack.Peek();
                vt.ModelView = value;
                ViewTransform = vt;
            }
        }

        /// <summary>
        /// Immediately changes the view transform of the material set, without waiting for a clear.
        /// If the viewTransform argument is null, the current transform is pushed instead.
        /// </summary>
        public void PushViewTransform (ref ViewTransform? viewTransform) {
            var vt = viewTransform ?? ViewTransformStack.Peek();
            ViewTransformStack.Push(vt);
            ApplyViewTransform(ref vt, !LazyViewTransformChanges);
        }

        /// <summary>
        /// Immediately changes the view transform of the material set, without waiting for a clear.
        /// </summary>
        public void PushViewTransform (ViewTransform viewTransform, bool force = false) {
            ViewTransformStack.Push(viewTransform);
            ApplyViewTransform(ref viewTransform, force || !LazyViewTransformChanges);
        }

        /// <summary>
        /// Immediately changes the view transform of the material set, without waiting for a clear.
        /// </summary>
        public void PushViewTransform (ref ViewTransform viewTransform, bool force = false) {
            ViewTransformStack.Push(viewTransform);
            ApplyViewTransform(ref viewTransform, force || !LazyViewTransformChanges);
        }

        /// <summary>
        /// Immediately restores the previous view transform of the material set, without waiting for a clear.
        /// </summary>
        public void PopViewTransform (out ViewTransform previous, bool force = false) {
            previous = ViewTransformStack.Pop();
            var current = ViewTransformStack.Peek();
            ApplyViewTransform(ref current, force || !LazyViewTransformChanges);
        }

        /// <summary>
        /// Immediately restores the previous view transform of the material set, without waiting for a clear.
        /// </summary>
        public void PopViewTransform (bool force = false) {
            ViewTransformStack.Pop();
            var current = ViewTransformStack.Peek();
            ApplyViewTransform(ref current, force || !LazyViewTransformChanges);
        }

        private FrameParams? LastAppliedFrameParams;
        private ViewTransform? LastAppliedViewTransform;
        private bool? LastIsOpenGL;
        private bool FlushViewTransformForFrameParamsChange;
        private int LastRenderTargetChangeIndex;
        private int? LastAppliedFrameIndex;

        /// <summary>
        /// Instantly sets the view transform of all material(s) owned by this material set to the ViewTransform field's current value.
        /// Also sets other parameters like Time.
        /// <param name="force">Overrides the LazyViewTransformChanges configuration variable if it's set</param>
        /// </summary>
        public void ApplyShaderVariables (bool force = true, int? frameIndex = null) {
            if (LastAppliedFrameIndex != frameIndex) {
                LastAppliedFrameIndex = frameIndex;
                BuildMaterialCache();
            }

            var @params = new FrameParams {
                Seconds = (float)TimeProvider.Seconds,
                FrameIndex = frameIndex
            };

            FlushViewTransformForFrameParamsChange = true;

            if (!LastAppliedFrameParams.HasValue ||
                !LastAppliedFrameParams.Value.Equals(@params) ||
                LastIsOpenGL != Coordinator.IsOpenGL
            ) {
                LastAppliedFrameParams = @params;
                LastIsOpenGL = Coordinator.IsOpenGL;
                ForEachMaterial(_ApplyParamsDelegate, @params);
            }

            var vt = ViewTransformStack.Peek();
            if (!LastAppliedViewTransform.HasValue ||
                !LastAppliedViewTransform.Value.Equals(ref vt))
                ApplyViewTransform(ref vt, force || !LazyViewTransformChanges);
        }


        public void SetLUTs (Material m, ColorLUT lut1, ColorLUT lut2 = null, float lut2Weight = 0, int lutIndex1 = 0, int lutIndex2 = 0) {
            var p = m.Effect.Parameters;
            p["LUT1"].SetValue(lut1);
            p["LUT2"].SetValue(lut2);
            p["LUTResolutionsAndRowCounts"].SetValue(new Vector4(lut1?.Resolution ?? 1, lut2?.Resolution ?? 1, lut1?.RowCount ?? 1, lut2?.RowCount ?? 1));
            // TODO: Pass weight and indices in userdata
            p["LUT2Weight"].SetValue(lut2Weight);
            lutIndex1 = Arithmetic.Clamp(lutIndex1, 0, (lut1?.RowCount - 1) ?? 0);
            lutIndex2 = Arithmetic.Clamp(lutIndex2, 0, (lut2?.RowCount - 1) ?? 0);
            float offsetScale1 = (1.0f / lut1.Texture.Height) * lut1.Resolution,
                offsetScale2 = (1.0f / (lut2?.Texture?.Height ?? 1)) * (lut2?.Resolution ?? 1);
            p["LUTOffsets"]?.SetValue(
                new Vector4(0, lutIndex1 * offsetScale1, 0, lutIndex2 * offsetScale2)
            );
        }

        // TODO: Helper methods for building userdata vector4s for various purposes (HSL, etc)


        private const int MaxWeightCount = 10;
        private static readonly float[] WeightBuffer = new float[MaxWeightCount];

        private static double Gaussian (double sigma, double x) {
            double value = -(x * x) / (2.0 * sigma * sigma);
            return Math.Exp( value );
        }
 
        private static double IntegrateGaussian (double sigma, double a, double b)
        {
            return ((b - a) / 6.0) *
                (
                    Gaussian(sigma, a) + 4.0 * 
                    Gaussian(sigma, (a + b) / 2.0) + 
                    Gaussian(sigma, b)
                );
        }

        /// <summary>
        /// Configures a gaussian blur material.
        /// </summary>
        /// <param name="sigma">Governs the strength of the blur. Lower values are sharper.</param>
        /// <param name="tapCount">The number of samples ('taps') that will be read vertically or horizontally from the texture to compute each blurred sample.</param>
        public void SetGaussianBlurParameters (Material m, double sigma, int tapCount, float mipBias = 0) {
            int tapsMinusOne = tapCount - 1;
            int weightCount = 1 + (tapsMinusOne / 2);
            if ((weightCount < 1) || (weightCount > MaxWeightCount))
                throw new ArgumentException("Tap count out of range");
            if (tapCount / 2 * 2 == tapCount)
                throw new ArgumentException("Tap count must be odd");

            const double scale = 5;

            using (var scratch = BufferPool<double>.Allocate(tapCount)) {
                Array.Clear(scratch.Data, 0, scratch.Data.Length);

                double sum = 0;
                for (int i = 0; i < tapCount; i++) {
                    double x = i - (tapsMinusOne / 2.0);
                    double value = IntegrateGaussian(sigma, x - 0.5, x + 0.5);
                    scratch.Data[i] = value;
                    sum += scratch.Data[i];
                }

                for (int i = 0; i < weightCount; i++) {
                    var unscaled = scratch.Data[weightCount - i - 1];
                    var scaled = unscaled * scale;
                    // We reduce error in the shader (from small values becoming denormals) 
                    //  by scaling the range up a bit
                    WeightBuffer[i] = (float)scaled;
                }

                var p = m.Effect.Parameters;
                p["TapCount"]?.SetValue(weightCount);
                p["TapWeights"]?.SetValue(WeightBuffer);
                var divisor = (sum * scale);
                var inverseDivisor = 1.0 / divisor;
                var inverseDivisor2 = inverseDivisor * inverseDivisor;
                p["InverseTapDivisors"]?.SetValue(new Vector2(
                    (float)inverseDivisor, (float)inverseDivisor2
                ));
            }
        }

        private void ApplyParamsToMaterial (Material m, FrameParams @params) {
            m.Parameters?.Time?.SetValue(@params.Seconds);
            if (@params.FrameIndex.HasValue)
                m.Parameters?.FrameIndex?.SetValue((float)@params.FrameIndex.Value);

            m.Parameters?.HalfPixelOffset?.SetValue(!Coordinator.IsOpenGL ? 1f : 0f);

            var ds = DefaultDitheringSettings;
            ds.FrameIndex = @params.FrameIndex.GetValueOrDefault(0);

            uDithering.TrySet(m, ref ds);
        }

        internal void ApplyViewTransformToMaterial (Material m, ref ViewTransform viewTransform) {
            uViewport.TrySet(m, ref viewTransform);
        }

        /// <summary>
        /// Lazily sets the view transform of all material(s) owned by this material set without changing the ViewTransform field.
        /// </summary>
        /// <param name="viewTransform">The view transform to apply.</param>
        /// <param name="force">Forcibly applies it now to all materials instead of lazily</param>
        public void ApplyViewTransform (ViewTransform viewTransform, bool force) {
            ApplyViewTransform(ref viewTransform, force);
        }

        /// <summary>
        /// Lazily sets the view transform of all material(s) owned by this material set without changing the ViewTransform field.
        /// </summary>
        /// <param name="viewTransform">The view transform to apply.</param>
        /// <param name="force">Forcibly applies it now to all materials instead of lazily</param>
        public void ApplyViewTransform (ref ViewTransform viewTransform, bool force) {
            ActiveViewTransform.ViewTransform = viewTransform;
            unchecked {
                ActiveViewTransform.Id++;
            }
            var am = ActiveViewTransform.ActiveMaterial;
            var rtci = Coordinator.Manager.DeviceManager.RenderTargetChangeIndex;

            if (force || (am == null) || FlushViewTransformForFrameParamsChange || (rtci != LastRenderTargetChangeIndex)) {
                FlushViewTransformForFrameParamsChange = false;
                LastAppliedViewTransform = viewTransform;
                LastRenderTargetChangeIndex = rtci;
                ForEachMaterial(_ApplyViewTransformDelegate, ref viewTransform);
            } else if (am != null) {
                ActiveViewTransform.AutoApply(am);
                am.Flush(Coordinator.Manager.DeviceManager);
            }
        }

        /// <summary>
        /// Returns a new version of a given material with rasterizer, depth/stencil, and blend state(s) optionally applied to it. This new version is cached.
        /// If no states are provided, the base material is returned.
        /// </summary>
        /// <param name="baseMaterial">The base material.</param>
        /// <param name="rasterizerState">The new rasterizer state, or null.</param>
        /// <param name="depthStencilState">The new depth/stencil state, or null.</param>
        /// <param name="blendState">The new blend state, or null.</param>
        /// <returns>The material with state(s) applied.</returns>
        public Material Get (Material baseMaterial, RasterizerState rasterizerState = null, DepthStencilState depthStencilState = null, BlendState blendState = null) {
            if (
                (rasterizerState == null) &&
                (depthStencilState == null) &&
                (blendState == null)
            )
                return baseMaterial;

            var key = new MaterialCacheKey(baseMaterial, rasterizerState, depthStencilState, blendState);
            Material result;
            if (!MaterialDictionary.TryGetValue(key, out result)) {
                result = baseMaterial.SetStates(blendState: blendState, depthStencilState: depthStencilState, rasterizerState: rasterizerState);
                MaterialDictionary.Add(key, result);
            }
            return result;
        }

        public Material GetBitmapMaterial (bool worldSpace, RasterizerState rasterizerState = null, DepthStencilState depthStencilState = null, BlendState blendState = null, bool discard = false) {
            return Get(
                discard ? BitmapWithDiscard : Bitmap,
                rasterizerState: rasterizerState,
                depthStencilState: depthStencilState,
                blendState: blendState
            );
        }

        public Material GetGeometryMaterial (bool worldSpace, RasterizerState rasterizerState = null, DepthStencilState depthStencilState = null, BlendState blendState = null) {
            return Get(
                worldSpace ? WorldSpaceGeometry : ScreenSpaceGeometry,
                rasterizerState: rasterizerState,
                depthStencilState: depthStencilState,
                blendState: blendState
            );
        }

        public override void Dispose () {
            base.Dispose();

            BuiltInShaders.Dispose();
            MaterialDictionary.Clear();
        }
    }

    public struct SquaredGeometryParameters {
        public Vector2   ViewportScale;
        public Vector2   ViewportPosition;
        public Matrix    ProjectionMatrix;
        public Matrix    ModelViewMatrix;
        public Texture2D BasicTexture;
    }

    public struct SquaredBitmapParameters {
        public Vector2   ViewportScale;
        public Vector2   ViewportPosition;
        public Matrix    ProjectionMatrix;
        public Matrix    ModelViewMatrix;
        public Vector2   BitmapTextureSize;
        public Vector2   HalfTexel;
        public Texture2D BitmapTexture;
        public Texture2D SecondTexture;
        public Vector4   ShadowColor;
        public Vector2   ShadowOffset;
        public float     ShadowMipBias;
    }
}