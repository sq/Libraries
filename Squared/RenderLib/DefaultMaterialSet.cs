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
        public Vector2 Scale;
        public Vector2 Position;

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
            return new ViewTransform {
                Scale = Vector2.One,
                Position = Vector2.Zero,
                Projection = Matrix.CreateOrthographicOffCenter(offsetX, width + offsetX2, height + offsetY2, offsetY, zNearPlane, zFarPlane),
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

        public override string ToString () {
            return string.Format("ViewTransform pos={0} scale={1}", Position, Scale);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DitheringSettings {
        public float Strength;
        private float _Unit, _InvUnit;
        public float FrameIndex;
        private float _BandSizeMinus1;
        public float RangeMin;
        private float _RangeMaxMinus1;

        public float RangeMax {
            get {
                return _RangeMaxMinus1 + 1;
            }
            set {
                _RangeMaxMinus1 = value - 1;
            }
        }

        public float BandSize {
            get {
                return _BandSizeMinus1 + 1;
            }
            set {
                _BandSizeMinus1 = value - 1;
            }
        }

        /// <summary>
        /// Determines the scale of values before dithering. Set to 255 for 8 bit RGBA, 65535 for 16 bit RGBA, 
        ///  or some other random value if you love weird visuals
        /// </summary>
        public float Unit {
            get {
                return _Unit;
            }
            set {
                _Unit = value;
                if (_Unit < 1)
                    _Unit = 1;
                _InvUnit = 1.0f / _Unit;
            }
        }

        /// <summary>
        /// Automatically sets Unit for you based on a power of two (8 for 8 bits, etc)
        /// </summary>
        public int Power {
            set {
                _Unit = (1 << value) - 1;
                if (_Unit < 1)
                    _Unit = 1;
                _InvUnit = 1.0f / _Unit;
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

        public Material ScreenSpaceBitmap, WorldSpaceBitmap;
        public Material ScreenSpaceBitmapToSRGB, WorldSpaceBitmapToSRGB;
        public Material ScreenSpaceBitmapWithLUT, WorldSpaceBitmapWithLUT;
        public Material ScreenSpaceShadowedBitmap, WorldSpaceShadowedBitmap;
        public Material ScreenSpaceBitmapWithDiscard, WorldSpaceBitmapWithDiscard;
        public Material ScreenSpaceGeometry, WorldSpaceGeometry;
        public Material ScreenSpaceTexturedGeometry, WorldSpaceTexturedGeometry;
        public Material ScreenSpaceLightmappedBitmap, WorldSpaceLightmappedBitmap;
        public Material ScreenSpaceEllipse, WorldSpaceEllipse;
        /// <summary>
        /// Make sure to resolve your lightmap to sRGB before using it with this, otherwise your lighting
        ///  will have really terrible banding in dark areas.
        /// </summary>
        public Material ScreenSpaceLightmappedsRGBBitmap, WorldSpaceLightmappedsRGBBitmap;
        public Material ScreenSpaceHorizontalGaussianBlur5Tap, ScreenSpaceVerticalGaussianBlur5Tap;
        public Material WorldSpaceHorizontalGaussianBlur5Tap, WorldSpaceVerticalGaussianBlur5Tap;
        public Material Clear, SetScissor;

        private readonly Action<Material, FrameParams> _ApplyParamsDelegate;
        protected readonly RefMaterialAction<ViewTransform> _ApplyViewTransformDelegate; 
        protected readonly Stack<ViewTransform> ViewTransformStack = new Stack<ViewTransform>();

        /// <summary>
        /// If true, view transform changes are lazily applied at the point each material is activated
        ///  instead of being eagerly applied to all materials whenever you change the view transform
        /// </summary>
        public bool LazyViewTransformChanges = true;

        /// <summary>
        /// Controls the strength of dithering applied to the result of the lightmapped bitmap materials.
        /// </summary>
        public DitheringSettings DefaultDitheringSettings;

        internal readonly ActiveViewTransformInfo ActiveViewTransform;

        internal readonly TypedUniform<ViewTransform> uViewport;
        internal readonly TypedUniform<DitheringSettings> uDithering;

        public readonly RenderCoordinator Coordinator;

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
   
            var bitmapShader = BuiltInShaders.Load("SquaredBitmapShader");
            var geometryShader = BuiltInShaders.Load("SquaredGeometryShader");
            var ellipseShader = BuiltInShaders.Load("Ellipse");
            
            ScreenSpaceBitmap = new Material(
                bitmapShader,
                "ScreenSpaceBitmapTechnique"
            );

            WorldSpaceBitmap = new Material(
                bitmapShader,
                "WorldSpaceBitmapTechnique"
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
            ScreenSpaceShadowedBitmap.Parameters.ShadowColor.SetValue(new Vector4(0, 0, 0, 1));
            ScreenSpaceShadowedBitmap.Parameters.ShadowOffset.SetValue(new Vector2(2, 2));

            WorldSpaceShadowedBitmap = new Material(
                bitmapShader,
                "WorldSpaceShadowedBitmapTechnique"
            );
            WorldSpaceShadowedBitmap.Parameters.ShadowColor.SetValue(new Vector4(0, 0, 0, 1));
            WorldSpaceShadowedBitmap.Parameters.ShadowOffset.SetValue(new Vector2(2, 2));

            ScreenSpaceBitmapWithDiscard = new Material(
                bitmapShader,
                "ScreenSpaceBitmapWithDiscardTechnique"
            );

            WorldSpaceBitmapWithDiscard = new Material(
                bitmapShader,
                "WorldSpaceBitmapWithDiscardTechnique"
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

            ScreenSpaceEllipse = new Material(
                ellipseShader,
                "ScreenSpaceEllipse"
            );

            WorldSpaceEllipse = new Material(
                ellipseShader,
                "WorldSpaceEllipse"
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

            ScreenSpaceHorizontalGaussianBlur5Tap = new Material(
                blurShader,
                "ScreenSpaceHorizontalGaussianBlur5Tap"
            );

            ScreenSpaceVerticalGaussianBlur5Tap = new Material(
                blurShader,
                "ScreenSpaceVerticalGaussianBlur5Tap"
            );

            WorldSpaceHorizontalGaussianBlur5Tap = new Material(
                blurShader,
                "WorldSpaceHorizontalGaussianBlur5Tap"
            );

            WorldSpaceVerticalGaussianBlur5Tap = new Material(
                blurShader,
                "WorldSpaceVerticalGaussianBlur5Tap"
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
                !LastAppliedFrameParams.Value.Equals(@params)) {
                LastAppliedFrameParams = @params;
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

        private void ApplyParamsToMaterial (Material m, FrameParams @params) {
            m.Parameters?.Time?.SetValue(@params.Seconds);
            if (@params.FrameIndex.HasValue)
                m.Parameters?.FrameIndex?.SetValue((float)@params.FrameIndex.Value);

            var ds = DefaultDitheringSettings;
            ds.FrameIndex = @params.FrameIndex.GetValueOrDefault(0);

            uDithering.TrySet(m, ref ds);
        }

        public void ApplyViewTransformToMaterial (Material m, ref ViewTransform viewTransform) {
            uViewport.TrySet(m, ref viewTransform);

            // HACK: Compatibility with shaders that are compensating for FNA's struct uniform bugs
            var ep = m.Effect?.Parameters;
            ep?["ViewportModelView"]?.SetValue(viewTransform.ModelView);
            ep?["ViewportProjection"]?.SetValue(viewTransform.Projection);
            ep?["ViewportScale"]?.SetValue(viewTransform.Scale);
            ep?["ViewportPosition"]?.SetValue(viewTransform.Position);
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

        public Material GetBitmapMaterial (bool worldSpace, RasterizerState rasterizerState = null, DepthStencilState depthStencilState = null, BlendState blendState = null) {
            return Get(
                worldSpace ? WorldSpaceBitmap : ScreenSpaceBitmap,
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
            ShadowColor = effect.Parameters["ShadowColor"];
            ShadowOffset = effect.Parameters["ShadowOffset"];
            LightmapUVOffset = effect.Parameters["LightmapUVOffset"];
            DitherStrength = effect.Parameters["DitherStrength"];
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