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

namespace Squared.Render {
    public interface IEffectMaterial {
        Effect Effect {
            get;
        }
    }

    public class MaterialDictionary<TKey> : Dictionary<TKey, Material>, IDisposable {
        public MaterialDictionary () 
            : base() {
        }

        public MaterialDictionary (IEqualityComparer<TKey> comparer)
            : base(comparer) {
        }

        public void Dispose () {
            foreach (var value in base.Values)
                value.Dispose();

            Clear();
        }
    }

    public abstract class MaterialSetBase : IDisposable {
        protected List<Material> ExtraMaterials = new List<Material>();
        protected FieldInfo[] MaterialFields;
        protected FieldInfo[] MaterialDictionaryFields;

        public MaterialSetBase() 
            : base() {

            BuildFieldList();
        }

        protected void BuildFieldList () {
            var fields = new List<FieldInfo>();
            var dictFields = new List<FieldInfo>();

            var tMaterial = typeof(Material);
            var tMaterialDictionary = typeof(MaterialDictionary<>);

            foreach (var field in this.GetType().GetFields()) {
                if (field.FieldType == tMaterial ||
                    tMaterial.IsAssignableFrom(field.FieldType) ||
                    field.FieldType.IsSubclassOf(tMaterial)
                ) {
                    fields.Add(field);
                } else if (field.FieldType.IsGenericType && field.FieldType.GetGenericTypeDefinition() == tMaterialDictionary) {
                    dictFields.Add(field);
                }
            }

            MaterialFields = fields.ToArray();
            MaterialDictionaryFields = dictFields.ToArray();
        }

        public IEnumerable<Material> AllMaterials {
            get {
                foreach (var field in MaterialFields) {
                    var material = field.GetValue(this) as Material;
                    if (material != null)
                        yield return material;
                }

                foreach (var dictField in MaterialDictionaryFields) {
                    var dict = dictField.GetValue(this);
                    if (dict == null)
                        continue;

                    // Generics, bluhhhhh
                    var values = dict.GetType().
                        GetProperty("Values").GetValue(dict, null) 
                        as IEnumerable<Material>;

                    if (values == null)
                        continue;

                    foreach (var material in values)
                        if (material != null)
                            yield return material;
                }

                foreach (var material in ExtraMaterials)
                    yield return material;
            }
        }

        public void Add (Material extraMaterial) {
            ExtraMaterials.Add(extraMaterial);
        }

        public bool Remove (Material extraMaterial) {
            return ExtraMaterials.Remove(extraMaterial);
        }

        public virtual void Dispose () {
            foreach (var material in AllMaterials)
                material.Dispose();
        }
    }

    public struct ViewTransform {
        public Vector2 Scale, Position;
        public Matrix Projection, ModelView;

        public static readonly ViewTransform Default = new ViewTransform {
            Scale = Vector2.One,
            Position = Vector2.Zero,
            Projection = Matrix.Identity,
            ModelView = Matrix.Identity
        };

        public static ViewTransform CreateOrthographic (Viewport viewport) {
            return new ViewTransform {
                Scale = Vector2.One,
                Position = Vector2.Zero,
                Projection = Matrix.CreateOrthographicOffCenter(viewport.X, viewport.Width, viewport.Height, viewport.Y, viewport.MinDepth, viewport.MaxDepth),
                ModelView = Matrix.Identity
            };
        }

        public static ViewTransform CreateOrthographic (int screenWidth, int screenHeight, float zNearPlane = 0, float zFarPlane = 1) {
            return new ViewTransform {
                Scale = Vector2.One,
                Position = Vector2.Zero,
                Projection = Matrix.CreateOrthographicOffCenter(0, screenWidth, screenHeight, 0, zNearPlane, zFarPlane),
                ModelView = Matrix.Identity
            };
        }
    }

    public class DefaultMaterialSet : MaterialSetBase {
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

            public bool Equals (MaterialCacheKey rhs) {
                return (Material == rhs.Material) &&
                    (RasterizerState == rhs.RasterizerState) &&
                    (DepthStencilState == rhs.DepthStencilState) &&
                    (BlendState == rhs.BlendState);
            }

            public override bool Equals (object obj) {
                if (obj is MaterialCacheKey)
                    return Equals((MaterialCacheKey)obj);
                else
                    return base.Equals(obj);
            }

            public override int GetHashCode () {
                return Material.GetHashCode() ^
                    HashNullable(RasterizerState) ^
                    HashNullable(DepthStencilState) ^
                    HashNullable(BlendState);
            }
        }

        public readonly ResourceContentManager BuiltInShaders;

        protected readonly Dictionary<MaterialCacheKey, Material> MaterialCache = new Dictionary<MaterialCacheKey, Material>();

        public Material ScreenSpaceBitmap, WorldSpaceBitmap;
        public Material ScreenSpaceGeometry, WorldSpaceGeometry;
        public Material ScreenSpaceLightmappedBitmap, WorldSpaceLightmappedBitmap;
        public Material ScreenSpaceHorizontalGaussianBlur5Tap, ScreenSpaceVerticalGaussianBlur5Tap;
        public Material WorldSpaceHorizontalGaussianBlur5Tap, WorldSpaceVerticalGaussianBlur5Tap;
        public Material Clear;

        public ViewTransform ViewTransform;

        public DefaultMaterialSet (IServiceProvider serviceProvider) {
            BuiltInShaders = new ResourceContentManager(serviceProvider, Shaders.ResourceManager);

            Clear = new DelegateMaterial(
                new NullMaterial(),
                new Action<DeviceManager>[] { (dm) => ApplyShaderVariables() }, 
                null
            );

            var bitmapShader = BuiltInShaders.Load<Effect>("SquaredBitmapShader");
            var geometryShader = BuiltInShaders.Load<Effect>("SquaredGeometryShader");

            ScreenSpaceBitmap = new EffectMaterial(
                bitmapShader,
                "ScreenSpaceBitmapTechnique"
            );

            WorldSpaceBitmap = new EffectMaterial(
                bitmapShader,
                "WorldSpaceBitmapTechnique"
            );

            ScreenSpaceGeometry = new EffectMaterial(
                geometryShader,
                "ScreenSpaceUntextured"
            );

            WorldSpaceGeometry = new EffectMaterial(
                geometryShader,
                "WorldSpaceUntextured"
            );

            var lightmapShader = BuiltInShaders.Load<Effect>("Lightmap");

            ScreenSpaceLightmappedBitmap = new EffectMaterial(
                lightmapShader,
                "ScreenSpaceLightmappedBitmap"
            );

            WorldSpaceLightmappedBitmap = new EffectMaterial(
                lightmapShader,
                "WorldSpaceLightmappedBitmap"
            );

            var blurShader = BuiltInShaders.Load<Effect>("GaussianBlur");

            ScreenSpaceHorizontalGaussianBlur5Tap = new EffectMaterial(
                blurShader,
                "ScreenSpaceHorizontalGaussianBlur5Tap"
            );

            ScreenSpaceVerticalGaussianBlur5Tap = new EffectMaterial(
                blurShader,
                "ScreenSpaceVerticalGaussianBlur5Tap"
            );

            WorldSpaceHorizontalGaussianBlur5Tap = new EffectMaterial(
                blurShader,
                "WorldSpaceHorizontalGaussianBlur5Tap"
            );

            WorldSpaceVerticalGaussianBlur5Tap = new EffectMaterial(
                blurShader,
                "WorldSpaceVerticalGaussianBlur5Tap"
            );

            ViewTransform = ViewTransform.Default;
        }

        public Vector2 ViewportScale {
            get {
                return ViewTransform.Scale;
            }
            set {
                ViewTransform.Scale = value;
            }
        }

        public Vector2 ViewportPosition {
            get {
                return ViewTransform.Position;
            }
            set {
                ViewTransform.Position = value;
            }
        }

        public Matrix ProjectionMatrix {
            get {
                return ViewTransform.Projection;
            }
            set {
                ViewTransform.Projection = value;
            }
        }

        public Matrix ModelViewMatrix {
            get {
                return ViewTransform.ModelView;
            }
            set {
                ViewTransform.ModelView = value;
            }
        }

        /// <summary>
        /// Sets the view transform of all material(s) owned by this material set to the ViewTransform field's current value.
        /// Clear batches automatically call this function for you.
        /// </summary>
        public void ApplyShaderVariables () {
            ApplyViewTransform(ref ViewTransform);
        }

        /// <summary>
        /// Manually sets the view transform of all material(s) owned by this material set without changing the ViewTransform field.
        /// </summary>
        /// <param name="viewTransform">The view transform to apply.</param>
        public void ApplyViewTransform (ref ViewTransform viewTransform) {
            foreach (var m in AllMaterials) {
                var em = m as IEffectMaterial;

                if (em == null)
                    continue;

                var e = em.Effect;
                if (e == null)
                    continue;

                e.Parameters["ViewportScale"].SetValue(viewTransform.Scale);
                e.Parameters["ViewportPosition"].SetValue(viewTransform.Position);
                e.Parameters["ProjectionMatrix"].SetValue(viewTransform.Projection);
                e.Parameters["ModelViewMatrix"].SetValue(viewTransform.ModelView);
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

    public interface IDerivedMaterial {
        Material BaseMaterial { get; }
    }

    public class NullMaterial : Material {
        public NullMaterial()
            : base() {
        }

        public override void Begin(DeviceManager deviceManager) {
        }

        public override void End(DeviceManager deviceManager) {
        }
    }

    public class Material : IDisposable {
        private static int _NextMaterialID;

        public readonly int MaterialID;

        protected bool _IsDisposed;

        public Material () {
            MaterialID = Interlocked.Increment(ref _NextMaterialID);
            
            _IsDisposed = false;
        }

        public virtual void Begin (DeviceManager deviceManager) {
        }

        public virtual void End (DeviceManager deviceManager) {
        }

        public virtual void Dispose () {
            _IsDisposed = true;
        }

        public bool IsDisposed {
            get {
                return _IsDisposed;
            }
        }
    }

    public class EffectMaterial : Material, IEffectMaterial {
        public readonly Effect Effect;

        public EffectMaterial (Effect effect, string techniqueName)
            : base() {

            if (techniqueName != null) {
                Effect = effect.Clone();
                Effect.CurrentTechnique = Effect.Techniques[techniqueName];
            } else {
                Effect = effect;
            }
        }

        public EffectMaterial (Effect effect)
            : this(effect, null) {
        }

        public override void Begin (DeviceManager deviceManager) {
            base.Begin(deviceManager);

            if (Effect.GraphicsDevice != deviceManager.Device)
                throw new InvalidOperationException();

            deviceManager.CurrentEffect = Effect;
            Effect.CurrentTechnique.Passes[0].Apply();
        }

        public override void End (DeviceManager deviceManager) {
            if (Effect.GraphicsDevice != deviceManager.Device)
                throw new InvalidOperationException();

            base.End(deviceManager);
        }

        Effect IEffectMaterial.Effect {
            get {
                return Effect;
            }
        }

        public override void Dispose () {
            /*
            if (Effect != null)
                Effect.Dispose();
             */
            base.Dispose();
        }
    }

    public class DelegateMaterial : Material, IDerivedMaterial, IEffectMaterial {
        public readonly Material BaseMaterial;
        public readonly Action<DeviceManager>[] BeginHandlers;
        public readonly Action<DeviceManager>[] EndHandlers;

        public DelegateMaterial (
            Action<DeviceManager>[] beginHandlers,
            Action<DeviceManager>[] endHandlers
        )
            : base() {
            BeginHandlers = beginHandlers;
            EndHandlers = endHandlers;
        }

        public DelegateMaterial (
            Material baseMaterial,
            Action<DeviceManager>[] beginHandlers,
            Action<DeviceManager>[] endHandlers
        )
            : this(beginHandlers, endHandlers) {
            BaseMaterial = baseMaterial;
        }

        public override void Begin (DeviceManager deviceManager) {
            if (BaseMaterial != null)
                BaseMaterial.Begin(deviceManager);
            else
                base.Begin(deviceManager);

            if (BeginHandlers != null)
                foreach (var handler in BeginHandlers)
                    handler(deviceManager);
        }

        public override void End (DeviceManager deviceManager) {
            if (EndHandlers != null)
                foreach (var handler in EndHandlers)
                    handler(deviceManager);

            if (BaseMaterial != null)
                BaseMaterial.End(deviceManager);
            else
                base.End(deviceManager);
        }

        Effect IEffectMaterial.Effect {
            get {
                var em = BaseMaterial as IEffectMaterial;
                if (em != null)
                    return em.Effect;
                else
                    return null;
            }
        }

        Material IDerivedMaterial.BaseMaterial {
            get { return BaseMaterial; }
        }
    }
}