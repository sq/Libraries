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

namespace Squared.Render {
    public interface IEffectMaterial {
        Effect Effect {
            get;
        }
        MaterialSetEffectParameters Parameters {
            get;
        }
    }

    public interface IMaterialCollection {
        void ForEachMaterial<T> (Action<Material, T> action, T userData);
        void ForEachMaterial<T> (RefMaterialAction<T> action, ref T userData);
    }

    public delegate void RefMaterialAction<T> (Material material, ref T userData);

    public class MaterialList : List<Material>, IMaterialCollection, IDisposable {
        public void ForEachMaterial<T> (Action<Material, T> action, T userData) {
            foreach (var material in this)
                action(material, userData);
        }

        public void ForEachMaterial<T> (RefMaterialAction<T> action, ref T userData) {
            foreach (var material in this)
                action(material, ref userData);
        }

        public void Dispose () {
            foreach (var material in this)
                material.Dispose();

            Clear();
        }
    }

    public class MaterialDictionary<TKey> : Dictionary<TKey, Material>, IDisposable, IMaterialCollection {
        public MaterialDictionary () 
            : base() {
        }

        public MaterialDictionary (IEqualityComparer<TKey> comparer)
            : base(comparer) {
        }

        public void Dispose () {
            foreach (var value in Values)
                value.Dispose();

            Clear();
        }

        public void ForEachMaterial<T> (Action<Material, T> action, T userData) {
            foreach (var material in Values)
                action(material, userData);
        }

        public void ForEachMaterial<T> (RefMaterialAction<T> action, ref T userData) {
            foreach (var material in Values)
                action(material, ref userData);
        }
    }

    public abstract class MaterialSetBase : IDisposable {
        private   readonly object Lock = new object();
        protected readonly MaterialList ExtraMaterials = new MaterialList();

        public readonly Func<Material>[] AllMaterialFields;
        public readonly Func<IEnumerable<Material>>[] AllMaterialSequences;
        public readonly Func<IMaterialCollection>[] AllMaterialCollections;

        public MaterialSetBase() 
            : base() {

            BuildMaterialSets(out AllMaterialFields, out AllMaterialSequences, out AllMaterialCollections);
        }

        protected void BuildMaterialSets (
            out Func<Material>[] materialFields, 
            out Func<IEnumerable<Material>>[] materialSequences,
            out Func<IMaterialCollection>[] materialCollections 
        ) {
            var sequences = new List<Func<IEnumerable<Material>>>();
            var fields = new List<Func<Material>>();
            var collections = new List<Func<IMaterialCollection>>(); 

            var tMaterial = typeof(Material);
            var tMaterialDictionary = typeof(MaterialDictionary<>);

            var tMaterialCollection = typeof(IMaterialCollection);

            sequences.Add(() => this.ExtraMaterials);
            collections.Add(() => this.ExtraMaterials);

            foreach (var field in this.GetType().GetFields()) {
                var f = field;

                if (field.FieldType == tMaterial ||
                    tMaterial.IsAssignableFrom(field.FieldType) ||
                    field.FieldType.IsSubclassOf(tMaterial)
                ) {
                    fields.Add(
                        () => f.GetValue(this) as Material
                    );
                } else if (
                    field.FieldType.IsGenericType && 
                    field.FieldType.GetGenericTypeDefinition() == tMaterialDictionary
                ) {
                    var dictType = field.FieldType;
                    var valuesProperty = dictType.GetProperty("Values");

                    sequences.Add(
                        () => {
                            var dict = f.GetValue(this);
                            if (dict == null)
                                return null;

                            // Generics, bluhhhhh
                            var values = valuesProperty.GetValue(dict, null)
                                as IEnumerable<Material>;

                            return values;
                        }
                    );
                    collections.Add(() => (IMaterialCollection)f.GetValue(this));
                } else if (
                    tMaterialCollection.IsAssignableFrom(field.FieldType)
                ) {
                    collections.Add(() => (IMaterialCollection)f.GetValue(this));
                }
            }

            materialFields = fields.ToArray();
            materialSequences = sequences.ToArray();
            materialCollections = collections.ToArray();
        }

        public void ForEachMaterial<T> (Action<Material, T> action, T userData) {
            lock (Lock) {
                foreach (var field in AllMaterialFields) {
                    var material = field();
                    if (material != null)
                        action(material, userData);
                }

                foreach (var collection in AllMaterialCollections) {
                    var coll = collection();
                    if (coll == null)
                        continue;

                    coll.ForEachMaterial(action, userData);
                }
            }
        }

        public void ForEachMaterial<T> (RefMaterialAction<T> action, ref T userData) {
            lock (Lock) {
                foreach (var field in AllMaterialFields) {
                    var material = field();
                    if (material != null)
                        action(material, ref userData);
                }

                foreach (var collection in AllMaterialCollections) {
                    var coll = collection();
                    if (coll == null)
                        continue;

                    coll.ForEachMaterial(action, ref userData);
                }
            }
        }

        public IEnumerable<Material> AllMaterials {
            get {
                foreach (var field in AllMaterialFields) {
                    var material = field();
                    if (material != null)
                        yield return material;
                }

                foreach (var sequence in AllMaterialSequences) {
                    var seq = sequence();
                    if (seq == null)
                        continue;

                    foreach (var material in seq)
                        if (material != null)
                            yield return material;
                }
            }
        }

        public void Add (Material extraMaterial) {
            lock (Lock)
                ExtraMaterials.Add(extraMaterial);
        }

        public bool Remove (Material extraMaterial) {
            lock (Lock)
                return ExtraMaterials.Remove(extraMaterial);
        }

        public virtual void Dispose () {
            lock (Lock)
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
        
        public readonly ContentManager BuiltInShaders;
        public readonly ITimeProvider  TimeProvider;

        protected readonly MaterialDictionary<MaterialCacheKey> MaterialCache = new MaterialDictionary<MaterialCacheKey>(
            new MaterialCacheKeyComparer()
        );

        public Material ScreenSpaceBitmap, WorldSpaceBitmap;
        public Material ScreenSpaceBitmapWithDiscard, WorldSpaceBitmapWithDiscard;
        public Material ScreenSpaceGeometry, WorldSpaceGeometry;
        public Material ScreenSpaceLightmappedBitmap, WorldSpaceLightmappedBitmap;
        public Material ScreenSpaceHorizontalGaussianBlur5Tap, ScreenSpaceVerticalGaussianBlur5Tap;
        public Material WorldSpaceHorizontalGaussianBlur5Tap, WorldSpaceVerticalGaussianBlur5Tap;
        public Material Clear;

        protected readonly Action<Material, float> _ApplyTimeDelegate;
        protected readonly RefMaterialAction<ViewTransform> _ApplyViewTransformDelegate; 
        protected readonly Stack<ViewTransform> ViewTransformStack = new Stack<ViewTransform>();

        protected ViewTransform? CurrentlyAppliedViewTransform = null;

        public DefaultMaterialSet (IServiceProvider serviceProvider) {
            _ApplyViewTransformDelegate = ApplyViewTransformToMaterial;
            _ApplyTimeDelegate          = ApplyTimeToMaterial;

            TimeProvider = (ITimeProvider)serviceProvider.GetService(typeof(ITimeProvider))
                ?? new DotNetTimeProvider();

#if SDL2 // `Content/SquaredRender/` folder -flibit
            BuiltInShaders = new ContentManager(serviceProvider, "Content/SquaredRender");
#else
            BuiltInShaders = new ResourceContentManager(serviceProvider, Shaders.ResourceManager);
#endif

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

            ScreenSpaceBitmapWithDiscard = new EffectMaterial(
                bitmapShader,
                "ScreenSpaceBitmapWithDiscardTechnique"
            );

            WorldSpaceBitmapWithDiscard = new EffectMaterial(
                bitmapShader,
                "WorldSpaceBitmapWithDiscardTechnique"
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

            var gds = serviceProvider.GetService(typeof(IGraphicsDeviceService)) as IGraphicsDeviceService;
            if (gds != null)
                ViewTransformStack.Push(ViewTransform.CreateOrthographic(
                    gds.GraphicsDevice.PresentationParameters.BackBufferWidth,
                    gds.GraphicsDevice.PresentationParameters.BackBufferHeight
                ));
            else
                ViewTransformStack.Push(ViewTransform.Default);
        }

        public ViewTransform ViewTransform {
            get {
                return ViewTransformStack.Peek();
            }
            set {
                ViewTransformStack.Pop();
                ViewTransformStack.Push(value);
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
            ApplyViewTransform(ref viewTransform);
        }

        /// <summary>
        /// Immediately changes the view transform of the material set, without waiting for a clear.
        /// </summary>
        public void PushViewTransform (ref ViewTransform viewTransform) {
            ViewTransformStack.Push(viewTransform);
            ApplyViewTransform(ref viewTransform);
        }

        /// <summary>
        /// Immediately restores the previous view transform of the material set, without waiting for a clear.
        /// </summary>
        public ViewTransform PopViewTransform () {
            var result = ViewTransformStack.Pop();
            ApplyShaderVariables();
            return result;
        }

        /// <summary>
        /// Sets the view transform of all material(s) owned by this material set to the ViewTransform field's current value.
        /// Also sets other parameters like Time.
        /// Clear batches automatically call this function for you.
        /// </summary>
        public void ApplyShaderVariables () {
            var vt = ViewTransformStack.Peek();
            ApplyViewTransform(ref vt);

            float timeSeconds = (float)TimeProvider.Seconds;
            ForEachMaterial(_ApplyTimeDelegate, timeSeconds);
        }

        private static void ApplyTimeToMaterial (Material m, float time) {
            var em = m as IEffectMaterial;

            if (em == null)
                return;

            var e = em.Effect;
            if (e == null)
                return;

            var p = em.Parameters.Time;
            if (p != null)
                p.SetValue(time);
        }

        private static void ApplyViewTransformToMaterial (Material m, ref ViewTransform viewTransform) {
            var em = m as IEffectMaterial;

            if (em == null)
                return;

            var e = em.Effect;
            if (e == null)
                return;

            em.Parameters.ViewportScale.SetValue(viewTransform.Scale);
            em.Parameters.ViewportPosition.SetValue(viewTransform.Position);

            em.Parameters.ProjectionMatrix.SetValue(viewTransform.Projection);
            em.Parameters.ModelViewMatrix.SetValue(viewTransform.ModelView);
        }

        /// <summary>
        /// Manually sets the view transform of all material(s) owned by this material set without changing the ViewTransform field.
        /// </summary>
        /// <param name="viewTransform">The view transform to apply.</param>
        public void ApplyViewTransform (ref ViewTransform viewTransform) {
            if (
                CurrentlyAppliedViewTransform.HasValue &&
                CurrentlyAppliedViewTransform.Value.Equals(ref viewTransform)
            ) {
                // Already applied. Don't apply again.
                return;
            } else {
                CurrentlyAppliedViewTransform = viewTransform;
            }

            ForEachMaterial(_ApplyViewTransformDelegate, ref viewTransform);
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

        public virtual void Flush () {
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
        public readonly MaterialSetEffectParameters Parameters;

        public EffectMaterial (Effect effect, string techniqueName)
            : base() {

            if (techniqueName != null) {
                Effect = effect.Clone();
                var technique = Effect.Techniques[techniqueName];
                
                if (technique != null)
                    Effect.CurrentTechnique = technique;
                else {
                    throw new ArgumentException("techniqueName");
                }
            } else {
                Effect = effect;
            }

            // FIXME: This should probably never be null.
            if (Effect != null)
                Parameters = new MaterialSetEffectParameters(Effect);
        }

        public EffectMaterial (Effect effect)
            : this(effect, null) {
        }

        public override void Begin (DeviceManager deviceManager) {
            base.Begin(deviceManager);

            if (Effect.GraphicsDevice != deviceManager.Device)
                throw new InvalidOperationException();

            Flush();
        }

        public override void Flush () {
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

        MaterialSetEffectParameters IEffectMaterial.Parameters {
            get {
                return Parameters;
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

        public override void Flush() {
            BaseMaterial.Flush();
            base.Flush();
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

        MaterialSetEffectParameters IEffectMaterial.Parameters {
            get {
                var em = BaseMaterial as IEffectMaterial;
                if (em != null)
                    return em.Parameters;
                else
                    return null;
            }
        }

        Material IDerivedMaterial.BaseMaterial {
            get { return BaseMaterial; }
        }
    }

    public class MaterialSetEffectParameters {
        public readonly EffectParameter ViewportPosition, ViewportScale;
        public readonly EffectParameter ProjectionMatrix, ModelViewMatrix;
        public readonly EffectParameter BitmapTextureSize, HalfTexel;
        public readonly EffectParameter Time;

        public MaterialSetEffectParameters (Effect effect) {
            ViewportPosition = effect.Parameters["ViewportPosition"];
            ViewportScale = effect.Parameters["ViewportScale"];
            ProjectionMatrix = effect.Parameters["ProjectionMatrix"];
            ModelViewMatrix = effect.Parameters["ModelViewMatrix"];
            BitmapTextureSize = effect.Parameters["BitmapTextureSize"];
            HalfTexel = effect.Parameters["HalfTexel"];
            Time = effect.Parameters["Time"];
        }
    }
}