using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework;
using System.Threading;
using Microsoft.Xna.Framework.Content;
using System.Reflection;

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

    public class DefaultMaterialSet : MaterialSetBase {
        public readonly ResourceContentManager BuiltInShaders;

        public Material ScreenSpaceBitmap, WorldSpaceBitmap;
        public Material ScreenSpaceGeometry, WorldSpaceGeometry;
        public Material ScreenSpaceLightmappedBitmap, WorldSpaceLightmappedBitmap;
        public Material ScreenSpaceHorizontalGaussianBlur5Tap, ScreenSpaceVerticalGaussianBlur5Tap;
        public Material WorldSpaceHorizontalGaussianBlur5Tap, WorldSpaceVerticalGaussianBlur5Tap;
        public Material Clear;

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

            ViewportScale = Vector2.One;
            ViewportPosition = Vector2.Zero;
            ProjectionMatrix = Matrix.Identity;
        }

        public Vector2 ViewportScale {
            get;
            set;
        }

        public Vector2 ViewportPosition {
            get;
            set;
        }

        public Matrix ProjectionMatrix {
            get;
            set;
        }

        // Call this method to apply any changes you've made to the three
        //  viewport configuration properties above.
        // Note that Clear batches automatically call this before clearing.
        public void ApplyShaderVariables () {
            foreach (var m in AllMaterials) {
                var em = m as IEffectMaterial;

                if (em == null)
                    continue;

                var e = em.Effect;
                if (e == null)
                    continue;

                e.Parameters["ViewportScale"].SetValue(ViewportScale);
                e.Parameters["ViewportPosition"].SetValue(ViewportPosition);
                e.Parameters["ProjectionMatrix"].SetValue(ProjectionMatrix);
            }
        }

        public override void Dispose () {
            base.Dispose();

            BuiltInShaders.Dispose();
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