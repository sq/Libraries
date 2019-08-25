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

namespace Squared.Render {
    [StructLayout(LayoutKind.Sequential)]
    public struct ViewTransform {
        public Matrix Projection;
        public Matrix ModelView;
        private Vector4 ScaleAndPosition;

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
            ModelView = Matrix.Identity
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
                (ModelView == rhs.ModelView);
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
        internal class ActiveViewTransformInfo {
            public readonly DefaultMaterialSet MaterialSet;
            public ViewTransform ViewTransform;
            public uint Id = 0;
            public Material ActiveMaterial;

            internal ActiveViewTransformInfo (DefaultMaterialSet materialSet) {
                MaterialSet = materialSet;
            }

            public bool AutoApply (Material m) {
                bool hasChanged = false;
                if (m.ActiveViewTransform != this) {
                    m.ActiveViewTransform = this;
                    hasChanged = true;
                } else {
                    hasChanged = m.ActiveViewTransformId != Id;
                }

                MaterialSet.ApplyViewTransformToMaterial(m, ref ViewTransform);
                m.ActiveViewTransformId = Id;
                return hasChanged;
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

            private static int HashNullable<T> (T o) where T : class {
                if (o == null)
                    return 0;
                else
                    return o.GetHashCode();
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
                    HashNullable(RasterizerState) ^
                    HashNullable(DepthStencilState) ^
                    HashNullable(BlendState);
            }
        }

        protected class MaterialCacheKeyComparer : IEqualityComparer<MaterialCacheKey> {
            public bool Equals (MaterialCacheKey x, MaterialCacheKey y) {
                return x.Equals(ref y);
            }

            public int GetHashCode (MaterialCacheKey obj) {
                return obj.GetHashCode();
            }
        }

        public readonly EmbeddedEffectProvider BuiltInShaders;
        public readonly ITimeProvider  TimeProvider;

        protected readonly MaterialDictionary<MaterialCacheKey> MaterialCache = new MaterialDictionary<MaterialCacheKey>(
            new MaterialCacheKeyComparer()
        );

        public Material Bitmap, BitmapWithDiscard;
        public Material ScreenSpaceBitmapToSRGB, WorldSpaceBitmapToSRGB;
        public Material ScreenSpaceBitmapWithLUT, WorldSpaceBitmapWithLUT;
        public Material ScreenSpaceShadowedBitmap, WorldSpaceShadowedBitmap;
        public Material ScreenSpacePalettedBitmap, WorldSpacePalettedBitmap;
        public Material ScreenSpaceHueBitmap, WorldSpaceHueBitmap;
        public Material ScreenSpaceSepiaBitmap, WorldSpaceSepiaBitmap;
        public Material ScreenSpaceShadowedBitmapWithDiscard, WorldSpaceShadowedBitmapWithDiscard;
        public Material ScreenSpaceStippledBitmap, WorldSpaceStippledBitmap;
        public Material ScreenSpacePalettedBitmapWithDiscard, WorldSpacePalettedBitmapWithDiscard;
        public Material ScreenSpaceHueBitmapWithDiscard, WorldSpaceHueBitmapWithDiscard;
        public Material ScreenSpaceGeometry, WorldSpaceGeometry;
        public Material ScreenSpaceTexturedGeometry, WorldSpaceTexturedGeometry;
        public Material ScreenSpaceLightmappedBitmap, WorldSpaceLightmappedBitmap;
        public Material RasterShape, RasterRectangle, RasterEllipse, RasterTriangle, RasterLine;
        public Material TexturedRasterShape, TexturedRasterRectangle, TexturedRasterEllipse, TexturedRasterTriangle, TexturedRasterLine;
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

        internal readonly ActiveViewTransformInfo ActiveViewTransform;

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

        public DefaultMaterialSet (RenderCoordinator coordinator, ITimeProvider timeProvider = null) {
            Coordinator = coordinator;
            ActiveViewTransform = new ActiveViewTransformInfo(this);
            _ApplyViewTransformDelegate = ApplyViewTransformToMaterial;
            _ApplyParamsDelegate        = ApplyParamsToMaterial;

            uViewport = NewTypedUniform<ViewTransform>("Viewport");
            uDithering = NewTypedUniform<DitheringSettings>("Dithering");

            DefaultDitheringSettings = new DitheringSettings {
                Unit = 255,
                Strength = 1.0f,
                FrameIndex = 0
            };

            TimeProvider = timeProvider ?? new DotNetTimeProvider();

            BuiltInShaders = new EmbeddedEffectProvider(coordinator);

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
   
            var bitmapShader = BuiltInShaders.Load("SquaredBitmapShader");
            var geometryShader = BuiltInShaders.Load("SquaredGeometryShader");
            var palettedShader = BuiltInShaders.Load("PalettedBitmap");
            var hslShader = BuiltInShaders.Load("HueBitmap");
            
            Bitmap = new Material(
                bitmapShader,
                "BitmapTechnique"
            );
            
            ScreenSpaceBitmapWithLUT = new Material(
                bitmapShader,
                "ScreenSpaceBitmapWithLUTTechnique"
            );

            WorldSpaceBitmapWithLUT = new Material(
                bitmapShader,
                "WorldSpaceBitmapWithLUTTechnique"
            );
            
            ScreenSpaceBitmapToSRGB = new Material(
                bitmapShader,
                "ScreenSpaceBitmapToSRGBTechnique"
            );

            WorldSpaceBitmapToSRGB = new Material(
                bitmapShader,
                "WorldSpaceBitmapToSRGBTechnique"
            );
            
            ScreenSpaceShadowedBitmap = new Material(
                bitmapShader,
                "ScreenSpaceShadowedBitmapTechnique"
            );
            ScreenSpaceShadowedBitmap.Parameters.ShadowOffset.SetValue(new Vector2(1, 1));

            WorldSpaceShadowedBitmap = new Material(
                bitmapShader,
                "WorldSpaceShadowedBitmapTechnique"
            );
            WorldSpaceShadowedBitmap.Parameters.ShadowOffset.SetValue(new Vector2(1, 1));
            
            ScreenSpaceShadowedBitmapWithDiscard = new Material(
                bitmapShader,
                "ScreenSpaceShadowedBitmapWithDiscardTechnique"
            );
            ScreenSpaceShadowedBitmapWithDiscard.Parameters.ShadowOffset.SetValue(new Vector2(1, 1));

            WorldSpaceShadowedBitmapWithDiscard = new Material(
                bitmapShader,
                "WorldSpaceShadowedBitmapWithDiscardTechnique"
            );
            WorldSpaceShadowedBitmapWithDiscard.Parameters.ShadowOffset.SetValue(new Vector2(1, 1));

            BitmapWithDiscard = new Material(
                bitmapShader,
                "BitmapWithDiscardTechnique"
            );

            ScreenSpaceStippledBitmap = new Material(
                bitmapShader,
                "ScreenSpaceStippledBitmapTechnique"
            );

            WorldSpaceStippledBitmap = new Material(
                bitmapShader,
                "WorldSpaceStippledBitmapTechnique"
            );

            ScreenSpacePalettedBitmap = new Material(
                palettedShader,
                "ScreenSpacePalettedBitmapTechnique"
            );

            WorldSpacePalettedBitmap = new Material(
                palettedShader,
                "WorldSpacePalettedBitmapTechnique"
            );

            ScreenSpacePalettedBitmapWithDiscard = new Material(
                palettedShader,
                "ScreenSpacePalettedBitmapWithDiscardTechnique"
            );

            WorldSpacePalettedBitmapWithDiscard = new Material(
                palettedShader,
                "WorldSpacePalettedBitmapWithDiscardTechnique"
            );

            ScreenSpaceHueBitmap = new Material(
                hslShader,
                "ScreenSpaceHueBitmapTechnique"
            );

            WorldSpaceHueBitmap = new Material(
                hslShader,
                "WorldSpaceHueBitmapTechnique"
            );

            ScreenSpaceHueBitmapWithDiscard = new Material(
                hslShader,
                "ScreenSpaceHueBitmapWithDiscardTechnique"
            );

            WorldSpaceHueBitmapWithDiscard = new Material(
                hslShader,
                "WorldSpaceHueBitmapWithDiscardTechnique"
            );

            ScreenSpaceSepiaBitmap = new Material(
                hslShader,
                "ScreenSpaceSepiaBitmapTechnique"
            );

            WorldSpaceSepiaBitmap = new Material(
                hslShader,
                "WorldSpaceSepiaBitmapTechnique"
            );

            ScreenSpaceGeometry = new Material(
                geometryShader,
                "ScreenSpaceUntextured"
            );

            WorldSpaceGeometry = new Material(
                geometryShader,
                "WorldSpaceUntextured"
            );

            ScreenSpaceTexturedGeometry = new Material(
                geometryShader,
                "ScreenSpaceTextured"
            );

            WorldSpaceTexturedGeometry = new Material(
                geometryShader,
                "WorldSpaceTextured"
            );

            var rasterShapeUbershader = BuiltInShaders.Load("RasterShapeUbershader");
            var rasterShapeEllipse = BuiltInShaders.Load("RasterShapeEllipse");
            var rasterShapeRectangle = BuiltInShaders.Load("RasterShapeRectangle");
            var rasterShapeLine = BuiltInShaders.Load("RasterShapeLine");
            var rasterShapeTriangle = BuiltInShaders.Load("RasterShapeTriangle");

            RasterShape = new Material(
                rasterShapeUbershader,
                "RasterShapeTechnique"
            );

            TexturedRasterShape = new Material(
                rasterShapeUbershader,
                "TexturedRasterShapeTechnique"
            );

            RasterRectangle = new Material(
                rasterShapeRectangle,
                "RasterRectangleTechnique"
            );

            TexturedRasterRectangle = new Material(
                rasterShapeRectangle,
                "TexturedRasterRectangleTechnique"
            );

            RasterEllipse = new Material(
                rasterShapeEllipse,
                "RasterEllipseTechnique"
            );

            TexturedRasterEllipse = new Material(
                rasterShapeEllipse,
                "TexturedRasterEllipseTechnique"
            );

            RasterLine = new Material(
                rasterShapeLine,
                "RasterLineTechnique"
            );

            TexturedRasterLine = new Material(
                rasterShapeLine,
                "TexturedRasterLineTechnique"
            );

            RasterTriangle = new Material(
                rasterShapeTriangle,
                "RasterTriangleTechnique"
            );

            TexturedRasterTriangle = new Material(
                rasterShapeTriangle,
                "TexturedRasterTriangleTechnique"
            );
            
            var lightmapShader = BuiltInShaders.Load("Lightmap");

            ScreenSpaceLightmappedBitmap = new Material(
                lightmapShader,
                "ScreenSpaceLightmappedBitmap"
            );

            WorldSpaceLightmappedBitmap = new Material(
                lightmapShader,
                "WorldSpaceLightmappedBitmap"
            );

            ScreenSpaceLightmappedsRGBBitmap = new Material(
                lightmapShader,
                "ScreenSpaceLightmappedsRGBBitmap"
            );

            WorldSpaceLightmappedsRGBBitmap = new Material(
                lightmapShader,
                "WorldSpaceLightmappedsRGBBitmap"
            );

            var blurShader = BuiltInShaders.Load("GaussianBlur");

            ScreenSpaceHorizontalGaussianBlur = new Material(
                blurShader,
                "ScreenSpaceHorizontalGaussianBlur"
            );

            ScreenSpaceVerticalGaussianBlur = new Material(
                blurShader,
                "ScreenSpaceVerticalGaussianBlur"
            );

            ScreenSpaceRadialGaussianBlur = new Material(
                blurShader,
                "ScreenSpaceRadialGaussianBlur"
            );

            WorldSpaceHorizontalGaussianBlur = new Material(
                blurShader,
                "WorldSpaceHorizontalGaussianBlur"
            );

            WorldSpaceVerticalGaussianBlur = new Material(
                blurShader,
                "WorldSpaceVerticalGaussianBlur"
            );

            WorldSpaceRadialGaussianBlur = new Material(
                blurShader,
                "WorldSpaceRadialGaussianBlur"
            );

            AutoSetViewTransform();
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
        public void PushViewTransform (ViewTransform viewTransform) {
            ViewTransformStack.Push(viewTransform);
            ApplyViewTransform(ref viewTransform, !LazyViewTransformChanges);
        }

        /// <summary>
        /// Immediately changes the view transform of the material set, without waiting for a clear.
        /// </summary>
        public void PushViewTransform (ref ViewTransform viewTransform) {
            ViewTransformStack.Push(viewTransform);
            ApplyViewTransform(ref viewTransform, !LazyViewTransformChanges);
        }

        /// <summary>
        /// Immediately restores the previous view transform of the material set, without waiting for a clear.
        /// </summary>
        public ViewTransform PopViewTransform () {
            var result = ViewTransformStack.Pop();
            var current = ViewTransformStack.Peek();
            ApplyViewTransform(ref current, !LazyViewTransformChanges);
            return result;
        }

        private FrameParams? LastAppliedFrameParams;
        private ViewTransform? LastAppliedViewTransform;
        private bool? LastIsOpenGL;

        /// <summary>
        /// Instantly sets the view transform of all material(s) owned by this material set to the ViewTransform field's current value.
        /// Also sets other parameters like Time.
        /// <param name="force">Overrides the LazyViewTransformChanges configuration variable if it's set</param>
        /// </summary>
        public void ApplyShaderVariables (bool force = true, int? frameIndex = null) {
            var @params = new FrameParams {
                Seconds = (float)TimeProvider.Seconds,
                FrameIndex = frameIndex
            };

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


        public void SetLUTs (Material m, ColorLUT lut1, ColorLUT lut2 = null, float lut2Weight = 0) {
            var p = m.Effect.Parameters;
            p["LUT1"].SetValue(lut1);
            p["LUT2"].SetValue(lut2);
            p["LUTResolutions"].SetValue(new Vector2(lut1 != null ? lut1.Resolution : 1, lut2 != null ? lut2.Resolution : 1));
            p["LUT2Weight"].SetValue(lut2Weight);
        }


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
                    var scaled = unscaled * 10;
                    // We reduce error in the shader (from small values becoming denormals) 
                    //  by scaling the range up a bit
                    WeightBuffer[i] = (float)scaled;
                }

                var p = m.Effect.Parameters;
                p["TapCount"]?.SetValue(weightCount);
                p["TapWeights"]?.SetValue(WeightBuffer);
                p["InverseTapDivisor"]?.SetValue((float)(1.0 / (sum * 10)));
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

        public void ApplyViewTransformToMaterial (Material m, ref ViewTransform viewTransform) {
            // HACK: Ensure all materials have this instance for lazy transform updates
            // This breaks lighting
            // m.ActiveViewTransform = ActiveViewTransform;

            uViewport.TrySet(m, ref viewTransform);

            /*
            // HACK: Compatibility with shaders that are compensating for FNA's struct uniform bugs
            var ep = m.Effect?.Parameters;
            ep?["ViewportModelView"]?.SetValue(viewTransform.ModelView);
            ep?["ViewportProjection"]?.SetValue(viewTransform.Projection);
            ep?["ViewportScale"]?.SetValue(viewTransform.Scale);
            ep?["ViewportPosition"]?.SetValue(viewTransform.Position);
            */
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
            ActiveViewTransform.Id++;
            var am = ActiveViewTransform.ActiveMaterial;

            if (force || (am == null)) {
                LastAppliedViewTransform = viewTransform;
                ForEachMaterial(_ApplyViewTransformDelegate, ref viewTransform);
            } else if (am != null)
                am.Flush();
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
            if (!MaterialCache.TryGetValue(key, out result)) {
                result = baseMaterial.SetStates(rasterizerState, depthStencilState, blendState);
                MaterialCache.Add(key, result);
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
            MaterialCache.Clear();
        }
    }

    public class DefaultMaterialSetEffectParameters {
        public readonly EffectParameter ViewportPosition, ViewportScale;
        public readonly EffectParameter ProjectionMatrix, ModelViewMatrix;
        public readonly EffectParameter BitmapTextureSize, HalfTexel;
        public readonly EffectParameter BitmapTextureSize2, HalfTexel2;
        public readonly EffectParameter ShadowColor, ShadowOffset, LightmapUVOffset;
        public readonly EffectParameter Time, FrameIndex, DitherStrength;
        public readonly EffectParameter HalfPixelOffset;
        public readonly EffectParameter RenderTargetDimensions;
        public readonly EffectParameter Palette, PaletteSize;

        public DefaultMaterialSetEffectParameters (Effect effect) {
            var viewport = effect.Parameters["Viewport"];

            if (viewport != null) {
                ViewportPosition = viewport.StructureMembers["Position"];
                ViewportScale = viewport.StructureMembers["Scale"];
                ProjectionMatrix = viewport.StructureMembers["Projection"];
                ModelViewMatrix = viewport.StructureMembers["ModelView"];
            }

            BitmapTextureSize = effect.Parameters["BitmapTextureSize"];
            HalfTexel = effect.Parameters["HalfTexel"];
            BitmapTextureSize2 = effect.Parameters["BitmapTextureSize2"];
            HalfTexel2 = effect.Parameters["HalfTexel2"];
            Time = effect.Parameters["Time"];
            FrameIndex = effect.Parameters["FrameIndex"];
            ShadowColor = effect.Parameters["GlobalShadowColor"];
            ShadowOffset = effect.Parameters["ShadowOffset"];
            LightmapUVOffset = effect.Parameters["LightmapUVOffset"];
            DitherStrength = effect.Parameters["DitherStrength"];
            HalfPixelOffset = effect.Parameters["HalfPixelOffset"];
            RenderTargetDimensions = effect.Parameters["__RenderTargetDimensions__"];
            Palette = effect.Parameters["Palette"];
            PaletteSize = effect.Parameters["PaletteSize"];
        }

        public void SetPalette (Texture2D palette) {
            Palette?.SetValue(palette);
            PaletteSize?.SetValue(new Vector2(palette.Width, palette.Height));
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
    }
}