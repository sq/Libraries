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
using Squared.Render.RasterShape;

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

        public static ViewTransform CreatePerspective (int x, int y, int width, int height, float zNearPlane = 0.0001f, float zFarPlane = 1) {
            float offsetX = -0.0f;
            float offsetY = -0.0f;
            float offsetX2 = offsetX;
            float offsetY2 = offsetY;
            var projection = Matrix.CreatePerspectiveOffCenter(offsetX, width + offsetX2, height + offsetY2, offsetY, zNearPlane, zFarPlane);
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
                (InputAndOutputZRanges.FastEquals(in rhs.InputAndOutputZRanges));
        }

        public override bool Equals (object obj) {
            if (obj is ViewTransform vt)
                return Equals(ref vt);
            else
                return false;
        }

        public override int GetHashCode () {
            return Scale.GetHashCode() ^ Position.GetHashCode() ^ Projection.GetHashCode() ^ ModelView.GetHashCode();
        }

        public override string ToString () {
            return string.Format("ViewTransform pos={0} scale={1}", Position, Scale);
        }
    }

    public sealed class ActiveViewTransformInfo {
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
        public static readonly DitheringSettings Disable = new DitheringSettings {
            Strength = 0f,
            Power = 16
        };

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

        public bool Equals (ref DitheringSettings rhs) {
            return (StrengthUnitAndIndex == rhs.StrengthUnitAndIndex) &&
                (BandSizeAndRange == rhs.BandSizeAndRange);
        }

        public override bool Equals (object obj) {
            if (obj is DitheringSettings ds)
                return Equals(ref ds);
            else
                return false;
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
        /// Enables preloading for raster stroke shaders.
        /// </summary>
        public static bool PreloadAllRasterStrokeShaders = false;
        /// <summary>
        /// Enables preloading for common raster stroke shaders.
        /// </summary>
        public static bool PreloadCommonRasterStrokeShaders = false;
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

            public bool Equals (in MaterialCacheKey rhs) {
                return (Material == rhs.Material) &&
                    (RasterizerState == rhs.RasterizerState) &&
                    (DepthStencilState == rhs.DepthStencilState) &&
                    (BlendState == rhs.BlendState);
            }

            public override bool Equals (object obj) {
                if (obj is MaterialCacheKey mck) {
                    return Equals(in mck);
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
                return x.Equals(in y);
            }

            public int GetHashCode (MaterialCacheKey obj) {
                return obj.GetHashCode();
            }
        }

        public readonly EffectProvider BuiltInShaders;
        public readonly EffectManifest BuiltInShaderManifest;

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
        public Material DistanceFieldText, DistanceFieldOutlinedBitmap;
        public Material HighlightColorBitmap, CrossfadeBitmap;
        // Porter-duff compositing
        public Material UnderBitmap, OverBitmap, AtopBitmap;
        public Material MaskedBitmap, GradientMaskedBitmap;
        public Material ScreenSpaceGeometry, WorldSpaceGeometry;
        public Material ScreenSpaceTexturedGeometry, WorldSpaceTexturedGeometry;
        public Material ScreenSpaceLightmappedBitmap, WorldSpaceLightmappedBitmap;
        public Material RasterShapeUbershader;
        public Material JumpFloodInit, JumpFloodJump, JumpFloodResolve;
        internal readonly Dictionary<RasterShaderKey, RasterShape.RasterShader> RasterShapeMaterials =
            new Dictionary<RasterShaderKey, RasterShape.RasterShader>(new RasterShaderKey.Comparer());
        internal readonly Dictionary<int, RasterStroke.StrokeShader[]> RasterStrokeMaterials =
            new Dictionary<int, RasterStroke.StrokeShader[]>();
        /// <summary>
        /// Make sure to resolve your lightmap to sRGB before using it with this, otherwise your lighting
        ///  will have really terrible banding in dark areas.
        /// </summary>
        public Material ScreenSpaceLightmappedsRGBBitmap, WorldSpaceLightmappedsRGBBitmap;
        public Material ScreenSpaceHorizontalGaussianBlur, ScreenSpaceVerticalGaussianBlur, ScreenSpaceRadialGaussianBlur;
        public Material WorldSpaceHorizontalGaussianBlur, WorldSpaceVerticalGaussianBlur, WorldSpaceRadialGaussianBlur;
        public Material GaussianOutlined, GaussianOutlinedWithDiscard, RadialMaskSoftening;
        public Material Clear, SetScissor, SetViewport;

        private readonly RefMaterialAction<FrameParams> _ApplyParamsDelegate;
        protected readonly RefMaterialAction<ViewTransform> _ApplyViewTransformDelegate; 
        protected readonly UnorderedList<ViewTransform> ViewTransformStack = new UnorderedList<ViewTransform>();

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
            internal DitheringSettings DitheringSettings;

            public override int GetHashCode () {
                return Seconds.GetHashCode();
            }

            public bool Equals (ref FrameParams rhs) {
                return (Seconds == rhs.Seconds) && (FrameIndex == rhs.FrameIndex) &&
                    DitheringSettings.Equals(ref rhs.DitheringSettings);
            }

            public override bool Equals (object obj) {
                if (obj is FrameParams fp)
                    return Equals(ref fp);
                else
                    return false;
            }
        }

        private Material NewMaterial (Effect effect, string techniqueName) {
            var result = new Material(effect, techniqueName);
            return result;
        }

        public DefaultMaterialSet (RenderCoordinator coordinator) {
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

            BuiltInShaders = new EffectProvider(Assembly.GetExecutingAssembly(), coordinator);
            BuiltInShaderManifest = BuiltInShaders.ReadManifest();

            Clear = new Material(
                null, null,
                new Action<DeviceManager>[] { (dm) => ApplyShaderVariables(false, dm) }
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
            var compositedShader = BuiltInShaders.Load("CompositedBitmap");
            var jumpFloodShader = BuiltInShaders.Load("JumpFlood");

            Bitmap = NewMaterial(
                bitmapShader,
                "BitmapTechnique"
            );

            HighlightColorBitmap = NewMaterial(
                compositedShader,
                "HighlightColorBitmapTechnique"
            );

            CrossfadeBitmap = NewMaterial(
                compositedShader,
                "CrossfadeBitmapTechnique"
            );

            AtopBitmap = NewMaterial(
                compositedShader,
                "AtopBitmapTechnique"
            );

            OverBitmap = NewMaterial(
                compositedShader,
                "OverBitmapTechnique"
            );

            UnderBitmap = NewMaterial(
                compositedShader,
                "UnderBitmapTechnique"
            );

            MaskedBitmap = NewMaterial(
                compositedShader,
                "MaskedBitmapTechnique"
            );

            GradientMaskedBitmap = NewMaterial(
                compositedShader,
                "GradientMaskedBitmapTechnique"
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

            DistanceFieldText = NewMaterial(
                bitmapShader,
                "DistanceFieldTextTechnique"
            );
            DistanceFieldText.Parameters.ShadowOffset.SetValue(defaultOffset);

            DistanceFieldOutlinedBitmap = NewMaterial(
                bitmapShader,
                "DistanceFieldOutlinedBitmapTechnique"
            );
            DistanceFieldOutlinedBitmap.Parameters.ShadowOffset.SetValue(defaultOffset);

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

            LoadRasterStrokeMaterials();

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

            JumpFloodInit = NewMaterial(
                jumpFloodShader,
                "JumpFloodInit"
            );

            JumpFloodJump = NewMaterial(
                jumpFloodShader,
                "JumpFloodJump"
            );

            JumpFloodResolve = NewMaterial(
                jumpFloodShader,
                "JumpFloodResolve"
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
                AtopBitmap,
                UnderBitmap,
                OverBitmap,
                MaskedBitmap,
                GradientMaskedBitmap,
                OutlinedBitmap,
                OutlinedBitmapWithDiscard,
                DistanceFieldOutlinedBitmap,
                JumpFloodInit,
                JumpFloodJump,
                JumpFloodResolve
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
                GaussianOutlined,
                GaussianOutlinedWithDiscard,
                RadialMaskSoftening
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

            GaussianOutlined = NewMaterial(
                blurShader,
                "GaussianOutlined"
            );

            GaussianOutlinedWithDiscard = NewMaterial(
                blurShader,
                "GaussianOutlinedWithDiscard"
            );

            RadialMaskSoftening = NewMaterial(
                blurShader,
                "RadialMaskSoftening"
            );
        }

        private void LoadRasterStrokeVariant (
            string name, RasterStroke.RasterStrokeType type
        ) {
            string untexturedName = BuiltInShaderManifest.FindVariant(name, "Untextured", "1"),
                texturedName = BuiltInShaderManifest.FindVariant(name, "Textured", "1"),
                untexturedShadowedName = BuiltInShaderManifest.FindVariant(name, "UntexturedShadowed", "1"),
                texturedShadowedName = BuiltInShaderManifest.FindVariant(name, "TexturedShadowed", "1"),
                untexturedBezierName = BuiltInShaderManifest.FindVariant(name, "UntexturedBezier", "1"),
                texturedBezierName = BuiltInShaderManifest.FindVariant(name, "TexturedBezier", "1");

            Effect untextured = BuiltInShaders.Load(untexturedName),
                textured = BuiltInShaders.Load(texturedName),
                untexturedShadowed = untexturedShadowedName == null ? null : BuiltInShaders.Load(untexturedShadowedName, true, true),
                texturedShadowed = texturedShadowedName == null ? null : BuiltInShaders.Load(texturedShadowedName, true, true),
                untexturedBezier = untexturedBezierName == null ? null : BuiltInShaders.Load(untexturedBezierName, true, true),
                texturedBezier = texturedBezierName == null ? null : BuiltInShaders.Load(texturedBezierName, true, true);
            Material material1 = NewMaterial(untextured, untexturedName),
                material2 = NewMaterial(textured, texturedName),
                material3 = untexturedShadowed == null ? null : NewMaterial(untexturedShadowed, untexturedShadowedName),
                material4 = texturedShadowed == null ? null : NewMaterial(texturedShadowed, texturedShadowedName),
                material5 = untexturedBezier == null ? null : NewMaterial(untexturedBezier, untexturedBezierName),
                material6 = texturedBezier == null ? null : NewMaterial(texturedBezier, texturedBezierName);

            material1.Name = $"{type}_Untextured";
            material2.Name = $"{type}_Textured";
            if (material3 != null)
                material3.Name = $"{type}_UntexturedShadowed";
            if (material4 != null)
                material4.Name = $"{type}_TexturedShadowed";
            if (material5 != null)
                material5.Name = $"{type}_UntexturedBezier";
            if (material6 != null)
                material6.Name = $"{type}_TexturedBezier";

            var strokeHint = new Material.PipelineHint {
                HasIndices = true,
                VertexFormats = new Type[] {
                    typeof(CornerVertex),
                    typeof(RasterStroke.RasterStrokeVertex)
                },
                VertexTextureFormats = 
                    (type == RasterStroke.RasterStrokeType.Polygon)
                        ? new SurfaceFormat[] {
                            (SurfaceFormat)9999,
                            (SurfaceFormat)9999,
                            SurfaceFormat.Vector4
                        }
                        : default
            };

            if (
                PreloadCommonRasterStrokeShaders ||
                PreloadAllRasterStrokeShaders ||
                PreloadAllShaders
            ) {
                material1.HintPipeline = strokeHint;
                material2.HintPipeline = strokeHint;

                if (PreloadAllRasterStrokeShaders || PreloadAllShaders) {
                    if (material3 != null)
                        material3.HintPipeline = strokeHint;
                    if (material4 != null)
                        material4.HintPipeline = strokeHint;
                    if (material5 != null)
                        material5.HintPipeline = strokeHint;
                    if (material6 != null)
                        material6.HintPipeline = strokeHint;
                }
            }

            RasterStrokeMaterials[(int)type] = new[] {
                new RasterStroke.StrokeShader(material1),
                new RasterStroke.StrokeShader(material2),
                untexturedShadowed == null ? null : new RasterStroke.StrokeShader(material3),
                texturedShadowed == null ? null : new RasterStroke.StrokeShader(material4),
                untexturedBezier == null ? null : new RasterStroke.StrokeShader(material5),
                texturedBezier == null ? null : new RasterStroke.StrokeShader(material6),
            };
            Add(material1);
            Add(material2);
            if (material3 != null)
                Add(material3);
            if (material4 != null)
                Add(material4);
            if (material5 != null)
                Add(material5);
            if (material6 != null)
                Add(material6);
        }

        private void LoadRasterStrokeMaterials () {
            LoadRasterStrokeVariant("RasterStrokeLine", RasterStroke.RasterStrokeType.LineSegment);
            LoadRasterStrokeVariant("RasterStrokeRectangle", RasterStroke.RasterStrokeType.Rectangle);
            LoadRasterStrokeVariant("RasterStrokePolygon", RasterStroke.RasterStrokeType.Polygon);
        }

        private void LoadRasterShapeVariantFromManifest (RasterShapeType? type, bool shadowed, bool textured, bool simple = false, bool ramp = false) {
            string typeName = type == null ? "type" : $"TYPE_{type}",
                variantName = "NORMAL";

            if (simple) {
                if (shadowed)
                    variantName = "SIMPLE_SHADOWED";
                else
                    variantName = "SIMPLE";
            } else if (textured) {
                if (shadowed)
                    variantName = "TEXTURED_SHADOWED";
                else
                    variantName = "TEXTURED";
            } else if (ramp) {
                if (shadowed)
                    variantName = "RAMP_SHADOWED";
                else
                    variantName = "RAMP";
            } else if (shadowed)
                variantName = "SHADOWED";

            var materialName = $"{type}_{variantName}";
            variantName = "VARIANT_" + variantName;

            var name = BuiltInShaderManifest.FindVariant(
                "RasterShapeVariants",
                "EVALUATE_TYPE", typeName,
                variantName, "1"
            );
            if (name == null)
                return;

            var shader = BuiltInShaders.Load(name, true, false);
            var material = NewMaterial(shader, name);
            material.Name = materialName;
            FinishLoadingRasterShapeVariant(type, shadowed, textured, simple, ramp, material);
        }

        private void LoadRasterShapeVariant (
            Effect shader, string techniqueName, RasterShape.RasterShapeType? type, bool shadowed, bool textured, bool simple = false, bool ramp = false
        ) {
            if ((simple || ramp) && !shader.Techniques.Any(t => t.Name == techniqueName))
                return;

            var material = NewMaterial(shader, techniqueName);
            FinishLoadingRasterShapeVariant(type, shadowed, textured, simple, ramp, material);
        }

        private void FinishLoadingRasterShapeVariant (RasterShapeType? type, bool shadowed, bool textured, bool simple, bool ramp, Material material) {
            var key = new RasterShaderKey {
                Type = type,
                Shadowed = shadowed,
                Textured = textured,
                Simple = simple,
                HasRamp = ramp
            };
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
            // RasterShapeMaterials isn't automatically enumerated
            Add(material, true);
        }

        private void LoadRasterShapeVariantsFromManifest (
            RasterShapeType? type
        ) {
            LoadRasterShapeVariantFromManifest(type, false, false);
            LoadRasterShapeVariantFromManifest(type, false, true);
            LoadRasterShapeVariantFromManifest(type, true, false);
            LoadRasterShapeVariantFromManifest(type, true, true);
            LoadRasterShapeVariantFromManifest(type, shadowed: false, textured: false, simple: true);
            LoadRasterShapeVariantFromManifest(type, shadowed: true, textured: false, simple: true);
            LoadRasterShapeVariantFromManifest(type, shadowed: false, textured: false, ramp: true);
            LoadRasterShapeVariantFromManifest(type, shadowed: true, textured: false, ramp: true);
        }

        private void LoadRasterShapeMaterials () {
            LoadRasterShapeVariantsFromManifest(null);
            LoadRasterShapeVariantsFromManifest(RasterShapeType.Rectangle);
            LoadRasterShapeVariantsFromManifest(RasterShapeType.Ellipse);
            LoadRasterShapeVariantsFromManifest(RasterShapeType.LineSegment);
            LoadRasterShapeVariantsFromManifest(RasterShapeType.Triangle);
            LoadRasterShapeVariantsFromManifest(RasterShapeType.Polygon);

            RasterShapeUbershader = RasterShapeMaterials[new RasterShaderKey { Type = null, Shadowed = false, Textured = false }].Material;
        }

        protected override void QueuePendingRegistrationHandler () {
            Coordinator.BeforePrepare(PerformPendingRegistrations);
        }

        public void AutoSetViewTransform () {
            ViewTransformStack.Clear();

            ViewTransformStack.Add(ViewTransform.CreateOrthographic(
                Coordinator.Device.PresentationParameters.BackBufferWidth,
                Coordinator.Device.PresentationParameters.BackBufferHeight
            ));
        }

        public int ViewTransformStackDepth => ViewTransformStack.Count;

        public ref readonly ViewTransform ViewTransform {
            get {
                return ref ViewTransformStack.DangerousReadItem(ViewTransformStack.Count - 1);
            }
        }

        private ref ViewTransform ViewTransformMutable {
            get {
                return ref ViewTransformStack.DangerousItem(ViewTransformStack.Count - 1);
            }
        }

        public void SetViewTransform (ViewTransform value) {
            ref var item = ref ViewTransformMutable;
            item = value;
            ApplyViewTransform(ref item, !LazyViewTransformChanges);
        }

        public void SetViewTransform (ref ViewTransform value) {
            ref var item = ref ViewTransformMutable;
            item = value;
            ApplyViewTransform(ref item, !LazyViewTransformChanges);
        }

        public Vector2 ViewportZRange {
            get {
                return ViewTransform.ZRange;
            }
            set {
                ref var item = ref ViewTransformMutable;
                item.ZRange = value;
            }
        }

        public Vector2 ViewportScale {
            get {
                return ViewTransform.Scale;
            }
            set {
                ref var item = ref ViewTransformMutable;
                item.Scale = value;
            }
        }

        public Vector2 ViewportPosition {
            get {
                return ViewTransform.Position;
            }
            set {
                ref var item = ref ViewTransformMutable;
                item.Position = value;
            }
        }

        public Matrix ProjectionMatrix {
            get {
                return ViewTransform.Projection;
            }
            set {
                ref var item = ref ViewTransformMutable;
                item.Projection = value;
            }
        }

        public Matrix ModelViewMatrix {
            get {
                return ViewTransform.ModelView;
            }
            set {
                ref var item = ref ViewTransformMutable;
                item.ModelView = value;
            }
        }

        /// <summary>
        /// Immediately changes the view transform of the material set, without waiting for a clear.
        /// If the viewTransform argument is null, the current transform is pushed instead.
        /// </summary>
        public void PushViewTransform (ViewTransform? viewTransform) {
            var vt = viewTransform ?? ViewTransform;
            ViewTransformStack.Add(vt);
            ApplyViewTransform(ref vt, !LazyViewTransformChanges);
        }

        /// <summary>
        /// Immediately changes the view transform of the material set, without waiting for a clear.
        /// </summary>
        public void PushViewTransform (ViewTransform viewTransform, bool force = false) {
            ViewTransformStack.Add(viewTransform);
            ApplyViewTransform(viewTransform, force || !LazyViewTransformChanges);
        }

        /// <summary>
        /// Immediately changes the view transform of the material set, without waiting for a clear.
        /// If the viewTransform argument is null, the current transform is pushed instead.
        /// </summary>
        public void PushViewTransform (ref ViewTransform? viewTransform) {
            var vt = viewTransform ?? ViewTransform;
            ViewTransformStack.Add(vt);
            ApplyViewTransform(ref vt, !LazyViewTransformChanges);
        }

        /// <summary>
        /// Immediately changes the view transform of the material set, without waiting for a clear.
        /// </summary>
        public void PushViewTransform (ref ViewTransform viewTransform, bool force = false, bool defer = false) {
            ViewTransformStack.Add(viewTransform);
            if (!defer)
                ApplyViewTransform(viewTransform, force || !LazyViewTransformChanges);
        }

        /// <summary>
        /// Immediately restores the previous view transform of the material set, without waiting for a clear.
        /// </summary>
        public void PopViewTransform (out ViewTransform previous, bool force = false, bool defer = false) {
            previous = ViewTransform;
            ViewTransformStack.DangerousRemoveAt(ViewTransformStack.Count - 1);
            if (!defer)
                ApplyViewTransform(ViewTransform, force || !LazyViewTransformChanges);
        }

        /// <summary>
        /// Immediately restores the previous view transform of the material set, without waiting for a clear.
        /// </summary>
        public void PopViewTransform (bool force = false, bool defer = false) {
            ViewTransformStack.DangerousRemoveAt(ViewTransformStack.Count - 1);
            if (!defer)
                ApplyViewTransform(ViewTransform, force || !LazyViewTransformChanges);
        }

        private bool HasAppliedFrameParams, HasAppliedViewTransform;
        private FrameParams LastAppliedFrameParams;
        private ViewTransform LastAppliedViewTransform;
        private bool FlushViewTransformForFrameParamsChange;
        private int LastRenderTargetChangeIndex;
        private int? LastAppliedFrameIndex;

        /// <summary>
        /// Instantly sets the view transform of all material(s) owned by this material set to the ViewTransform field's current value.
        /// Also sets other parameters like Time.
        /// <param name="force">Overrides the LazyViewTransformChanges configuration variable if it's set</param>
        /// </summary>
        public void ApplyShaderVariables (bool force, DeviceManager dm) {
            if (LastAppliedFrameIndex != dm.FrameIndex) {
                LastAppliedFrameIndex = dm.FrameIndex;
                // HACK: Ensure we reset at the start of each frame
                HasAppliedFrameParams = HasAppliedViewTransform = false;
                BuildMaterialCache();
            }

            var @params = new FrameParams {
                Seconds = dm.RenderStartTimeSeconds,
                FrameIndex = dm.FrameIndex,
                DitheringSettings = DefaultDitheringSettings
            };
            @params.DitheringSettings.FrameIndex = dm.FrameIndex;

            FlushViewTransformForFrameParamsChange = true;

            if (!HasAppliedFrameParams ||
                !LastAppliedFrameParams.Equals(ref @params)
            ) {
                HasAppliedFrameParams = true;
                LastAppliedFrameParams = @params;
                ForEachMaterial(_ApplyParamsDelegate, ref @params);
            }

            // HACK
            ref var vt = ref ViewTransformMutable;
            if (!HasAppliedViewTransform ||
                !LastAppliedViewTransform.Equals(ref vt)) {
                HasAppliedViewTransform = true;
                ApplyViewTransform(ref vt, force || !LazyViewTransformChanges);
            }
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
        /// <param name="meanFactor">Biases the filter kernel towards a mean (average of all the pixels) instead of a gaussian blur</param>
        /// <param name="gain">A factor to increase or decrease the output of the filter kernel (will brighten or darken the resulting image)</param>
        public void SetGaussianBlurParameters (Material m, double sigma, int tapCount, float meanFactor = 0f, float gain = 1.0f) {
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
                    scratch.Data[i] = Arithmetic.Lerp(value, 1f / tapCount, meanFactor);
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
                p["InverseTapDivisorsAndSigma"]?.SetValue(new Vector3(
                    (float)inverseDivisor * gain, (float)inverseDivisor2 * gain, (float)sigma
                ));
            }
        }

        private void ApplyParamsToMaterial (Material m, ref FrameParams @params) {
            if (m.Parameters == null)
                return;

            m.Parameters.Time?.SetValue(@params.Seconds);
            if (@params.FrameIndex.HasValue)
                m.Parameters.FrameIndex?.SetValue((float)@params.FrameIndex.Value);

            if (!m.Parameters.DitheringInitialized) {
                m.Parameters.DitheringInitialized = true;
                m.Parameters.Dithering = GetUniformBinding(m, this.uDithering);
            }
            if (m.Parameters.Dithering != null)
                m.Parameters.Dithering.Value = @params.DitheringSettings;
        }

        internal void ApplyViewTransformToMaterial (Material m, ref ViewTransform viewTransform) {
            if (m.Parameters == null)
                return;

            uViewport.TrySet(m, ref viewTransform);

            if (m.Parameters.InverseModelView != null) {
                Matrix.Invert(ref viewTransform.ModelView, out var temp);
                m.Parameters.InverseModelView.SetValue(temp);
            }

            if (m.Parameters.InverseProjection != null) {
                // FIXME: Cache these in the viewtransform
                Matrix.Invert(ref viewTransform.Projection, out var temp);
                m.Parameters.InverseProjection.SetValue(temp);
            }
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
        /// <param name="clone">Clones the return value and adds it to this material set.</param>
        /// <returns>The material with state(s) applied.</returns>
        public Material Get (Material baseMaterial, RasterizerState rasterizerState = null, DepthStencilState depthStencilState = null, BlendState blendState = null, bool clone = false) {
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
            if (clone) {
                result = result?.Clone();
                Add(result);
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
        public Vector2   TexelSize;
        public Texture2D BitmapTexture;
        public Texture2D SecondTexture;
        public Vector4   ShadowColor;
        public Vector2   ShadowOffset;
        public float     ShadowMipBias;
    }
}