using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework;
using System.Threading;
using Microsoft.Xna.Framework.Content;

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
        public MaterialSetBase() 
            : base() {
        }

        public IEnumerable<KeyValuePair<string, Material>> AllMaterials {
            get {
                var tMaterial = typeof(Material);
                var tMaterialDictionary = typeof(MaterialDictionary<>);

                foreach (var field in this.GetType().GetFields()) {
                    if (field.FieldType == tMaterial || 
                        tMaterial.IsAssignableFrom(field.FieldType) ||
                        field.FieldType.IsSubclassOf(tMaterial)
                    ) {
                        var material = field.GetValue(this) as Material;
                        if (material != null)
                            yield return new KeyValuePair<string, Material>(
                                field.Name,
                                material
                            );
                    } else if (field.FieldType.IsGenericType && field.FieldType.GetGenericTypeDefinition() == tMaterialDictionary) {
                        var dict = field.GetValue(this);
                        if (dict != null) {
                            // Stupid generics :-(
                            var values = dict.GetType().GetProperty("Values").GetValue(dict, null) as IEnumerable<Material>;

                            int i = 0;
                            foreach (var material in values) {
                                if (material != null)
                                    yield return new KeyValuePair<string, Material>(
                                        String.Format("{0}[{1}]", field.Name, i),
                                        material
                                    );

                                i++;
                            }
                        }
                    }
                }
            }
        }

        public void Dispose () {
            foreach (var material in AllMaterials)
                material.Value.Dispose();
        }
    }

    public class DefaultMaterialSet : MaterialSetBase {
        public Material ScreenSpaceBitmap, WorldSpaceBitmap;
        public Material ScreenSpaceGeometry, WorldSpaceGeometry;
        public Material Clear;

        public DefaultMaterialSet (ContentManager content) {
            Clear = new DelegateMaterial(
                new NullMaterial(),
                new Action<DeviceManager>[] { SetShaderVariables }, 
                null
            );

            ScreenSpaceBitmap = new EffectMaterial(
                content.Load<Effect>("SquaredBitmapShader"), 
                "ScreenSpaceBitmapTechnique"
            );

            WorldSpaceBitmap = new EffectMaterial(
                content.Load<Effect>("SquaredBitmapShader"),
                "WorldSpaceBitmapTechnique"
            );

            ScreenSpaceGeometry = new EffectMaterial(
                content.Load<Effect>("SquaredGeometryShader"),
                "ScreenSpaceUntextured"
            );

            WorldSpaceGeometry = new EffectMaterial(
                content.Load<Effect>("SquaredGeometryShader"),
                "WorldSpaceUntextured"
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

        protected void SetShaderVariables(DeviceManager deviceManager) {
            foreach (var kvp in AllMaterials) {
                var em = kvp.Value as IEffectMaterial;

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

        public static Action<DeviceManager> MakeDelegate (RasterizerState state) {
            return (dm) => { dm.Device.RasterizerState = state; };
        }

        public static Action<DeviceManager> MakeDelegate (DepthStencilState state) {
            return (dm) => { dm.Device.DepthStencilState = state; };
        }

        public static Action<DeviceManager> MakeDelegate (BlendState state) {
            return (dm) => { dm.Device.BlendState = state; };
        }

        public static Action<DeviceManager> MakeDelegate (RasterizerState rasterState, DepthStencilState depthState, BlendState blendState) {
            return (dm) => { 
                dm.Device.RasterizerState = rasterState;
                dm.Device.DepthStencilState = depthState;
                dm.Device.BlendState = blendState;
            };
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