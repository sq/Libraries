using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework;
using System.Threading;
using Microsoft.Xna.Framework.Content;

namespace Squared.Render {
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
                    if (field.FieldType == tMaterial) {
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
        public EffectMaterial ScreenSpaceBitmap, WorldSpaceBitmap;
        public EffectMaterial ScreenSpaceGeometry, WorldSpaceGeometry;
        public Material Clear;

        public DefaultMaterialSet (ContentManager content) {
            var deviceService = (IGraphicsDeviceService)content.ServiceProvider.GetService(typeof(IGraphicsDeviceService));

            Clear = new DelegateMaterial(
                new NullMaterial(),
                new Action<DeviceManager>[] { SetShaderVariables }, 
                null
            );

            var bitmapVertex = new VertexDeclaration(deviceService.GraphicsDevice, BitmapVertex.Elements);

            ScreenSpaceBitmap = new EffectMaterial(
                bitmapVertex, 
                content.Load<Effect>("SquaredBitmapShader"), 
                "ScreenSpaceBitmapTechnique"
            );

            WorldSpaceBitmap = new EffectMaterial(
                bitmapVertex,
                content.Load<Effect>("SquaredBitmapShader"),
                "WorldSpaceBitmapTechnique"
            );

            var geometryVertex = new VertexDeclaration(deviceService.GraphicsDevice, VertexPositionColor.VertexElements);

            ScreenSpaceGeometry = new EffectMaterial(
                geometryVertex,
                content.Load<Effect>("SquaredGeometryShader"),
                "ScreenSpaceUntextured"
            );

            WorldSpaceGeometry = new EffectMaterial(
                geometryVertex,
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
            var e = ScreenSpaceBitmap.Effect;
            e.Parameters["ViewportScale"].SetValue(ViewportScale);
            e.Parameters["ViewportPosition"].SetValue(ViewportPosition);
            e.Parameters["ProjectionMatrix"].SetValue(ProjectionMatrix);
            e.CommitChanges();
        }
    }

    public interface IDerivedMaterial {
        Material BaseMaterial { get; }
    }

    public class NullMaterial : Material {
        public NullMaterial()
            : base(null) {
        }

        public override void Begin(DeviceManager deviceManager) {
        }

        public override void End(DeviceManager deviceManager) {
        }
    }

    public class Material : IDisposable {
        private static int _NextMaterialID;

        public readonly int MaterialID;
        public readonly VertexDeclaration VertexDeclaration;

        protected bool _IsDisposed;

        public Material (VertexDeclaration vertexDeclaration) {
            MaterialID = Interlocked.Increment(ref _NextMaterialID);
            VertexDeclaration = vertexDeclaration;

            _IsDisposed = false;
        }

        public virtual void Begin (DeviceManager deviceManager) {
            deviceManager.Device.VertexDeclaration = VertexDeclaration;
        }

        public virtual void End (DeviceManager deviceManager) {
        }

        public virtual void Dispose () {
            _IsDisposed = true;

            /*
            if (VertexDeclaration != null)
                VertexDeclaration.Dispose();
             */
        }

        public bool IsDisposed {
            get {
                return _IsDisposed;
            }
        }
    }

    public class EffectMaterial : Material {
        public readonly Effect Effect;

        public EffectMaterial (VertexDeclaration vertexDeclaration, Effect effect, string techniqueName)
            : base(vertexDeclaration) {

            if (techniqueName != null) {
                Effect = effect.Clone(effect.GraphicsDevice);
                Effect.CurrentTechnique = Effect.Techniques[techniqueName];
            } else {
                Effect = effect;
            }
        }

        public EffectMaterial (VertexDeclaration vertexDeclaration, Effect effect)
            : this(vertexDeclaration, effect, null) {
        }

        public override void Begin (DeviceManager deviceManager) {
            base.Begin(deviceManager);

            if (Effect.GraphicsDevice != deviceManager.Device)
                throw new InvalidOperationException();

            deviceManager.CurrentEffect = Effect;
            Effect.Begin();
            Effect.CurrentTechnique.Passes[0].Begin();
        }

        public override void End (DeviceManager deviceManager) {
            if (Effect.GraphicsDevice != deviceManager.Device)
                throw new InvalidOperationException();

            Effect.CurrentTechnique.Passes[0].End();
            Effect.End();

            base.End(deviceManager);
        }

        public override void Dispose () {
            /*
            if (Effect != null)
                Effect.Dispose();
             */
            base.Dispose();
        }
    }

    public class DelegateMaterial : Material, IDerivedMaterial {
        public readonly Material BaseMaterial;
        public readonly Action<DeviceManager>[] BeginHandlers;
        public readonly Action<DeviceManager>[] EndHandlers;

        public DelegateMaterial (
            VertexDeclaration vertexDeclaration,
            Action<DeviceManager>[] beginHandlers,
            Action<DeviceManager>[] endHandlers
        )
            : base(vertexDeclaration) {
            BeginHandlers = beginHandlers;
            EndHandlers = endHandlers;
        }

        public DelegateMaterial (
            Material baseMaterial,
            Action<DeviceManager>[] beginHandlers,
            Action<DeviceManager>[] endHandlers
        )
            : this((VertexDeclaration)null, beginHandlers, endHandlers) {
            BaseMaterial = baseMaterial;
        }

        public DelegateMaterial(
            Action<DeviceManager>[] beginHandlers,
            Action<DeviceManager>[] endHandlers
        )
            : this((VertexDeclaration)null, beginHandlers, endHandlers) {
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

        Material IDerivedMaterial.BaseMaterial {
            get { return BaseMaterial; }
        }
    }
}