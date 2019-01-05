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
using Squared.Render.Evil;

namespace Squared.Render {
    public sealed class Material : IDisposable {
        internal readonly UniformBindingTable UniformBindings = new UniformBindingTable();

        public static readonly Material Null = new Material(null);

        public readonly Effect Effect;
        public readonly bool   OwnsEffect;

        public readonly Thread OwningThread;

        public readonly DefaultMaterialSetEffectParameters  Parameters;

        public readonly Action<DeviceManager>[] BeginHandlers;
        public readonly Action<DeviceManager>[] EndHandlers;

        private static int _NextMaterialID;
        public readonly int MaterialID;

        internal DefaultMaterialSet.ActiveViewTransformInfo ActiveViewTransform;
        internal uint ActiveViewTransformId;

        protected bool _IsDisposed;

        private Material () {
            MaterialID = Interlocked.Increment(ref _NextMaterialID);
            
            _IsDisposed = false;
        }

        public Material (
            Effect effect, string techniqueName = null, 
            Action<DeviceManager>[] beginHandlers = null,
            Action<DeviceManager>[] endHandlers = null
        ) : this() {
            if (techniqueName != null) {
                Effect = effect.Clone();
                var technique = Effect.Techniques[techniqueName];
                
                if (technique != null)
                    Effect.CurrentTechnique = technique;
                else {
                    throw new ArgumentException("No technique named " + techniqueName, "techniqueName");
                }
            } else {
                Effect = effect;
            }

            OwningThread = Thread.CurrentThread;

            // FIXME: This should probably never be null.
            if (Effect != null) {
                Parameters = new DefaultMaterialSetEffectParameters(Effect);
            }

            BeginHandlers = beginHandlers;
            EndHandlers   = endHandlers;
        }

        public Material WrapWithHandlers (
            Action<DeviceManager>[] additionalBeginHandlers = null,
            Action<DeviceManager>[] additionalEndHandlers = null
        ) {
            var newBeginHandlers = BeginHandlers;
            var newEndHandlers = EndHandlers;

            if (newBeginHandlers == null)
                newBeginHandlers = additionalBeginHandlers;
            else if (additionalBeginHandlers != null)
                newBeginHandlers = Enumerable.Concat(BeginHandlers, additionalBeginHandlers).ToArray();

            if (newEndHandlers == null)
                newEndHandlers = additionalEndHandlers;
            else if (additionalEndHandlers != null)
                newEndHandlers = Enumerable.Concat(additionalEndHandlers, EndHandlers).ToArray();

            return new Material(
                Effect, null,
                newBeginHandlers, newEndHandlers
            );
        }

        public Material Clone () {
            var newEffect = Effect.Clone();
            newEffect.CurrentTechnique = newEffect.Techniques[Effect.CurrentTechnique.Name];

            return new Material(
                newEffect, null,
                BeginHandlers, EndHandlers
            );
        }

        internal bool AutoApplyCurrentViewTransform () {
            if (ActiveViewTransform == null)
                return false;
            return ActiveViewTransform.AutoApply(this);
        }

        private void CheckDevice (DeviceManager deviceManager) {
            if (Effect == null)
                return;

            if (Effect.GraphicsDevice != deviceManager.Device)
                throw new InvalidOperationException();
        }

        public void Begin (DeviceManager deviceManager) {
            CheckDevice(deviceManager);
            if (ActiveViewTransform != null)
                ActiveViewTransform.ActiveMaterial = this;

            Flush();

            if (BeginHandlers != null)
                foreach (var handler in BeginHandlers)
                    handler(deviceManager);

            if (AutoApplyCurrentViewTransform())
                Flush(false);
        }

        public void Flush (bool autoApplyViewTransform = true) {
            if (autoApplyViewTransform)
                AutoApplyCurrentViewTransform();

            if (Effect != null) {
                UniformBinding.FlushEffect(Effect);

                var currentTechnique = Effect.CurrentTechnique;
                currentTechnique.Passes[0].Apply();
            }
        }

        public void End (DeviceManager deviceManager) {
            CheckDevice(deviceManager);

            if (EndHandlers != null)
                foreach (var handler in EndHandlers)
                    handler(deviceManager);
        }

        public void Dispose () {
            if (_IsDisposed)
                return;

            if (OwnsEffect)
                Effect.Dispose();

            _IsDisposed = true;
        }

        public bool IsDisposed {
            get {
                return _IsDisposed;
            }
        }

        public override string ToString () {
            if (Effect == null) {
                return "NullEffect #" + MaterialID;
            } else {
                return string.Format(
                    "{3} #{0} ({1}.{2})", 
                    MaterialID, Effect.Name, Effect.CurrentTechnique.Name, 
                    ((BeginHandlers == null) && (EndHandlers == null))
                        ? "EffectMaterial"
                        : "DelegateEffectMaterial"
                );
            }
        }
    }
}