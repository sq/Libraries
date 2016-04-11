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
    public class Material : IDisposable {
        public readonly Effect Effect;
        public readonly bool   OwnsEffect;

        public readonly Thread OwningThread;
        private readonly ID3DXEffect _COMEffect;

        public readonly DefaultMaterialSetEffectParameters  Parameters;

        public readonly Action<DeviceManager>[] BeginHandlers;
        public readonly Action<DeviceManager>[] EndHandlers;

        private static int _NextMaterialID;
        public readonly int MaterialID;

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
                    throw new ArgumentException("techniqueName");
                }
            } else {
                Effect = effect;
            }

            OwningThread = Thread.CurrentThread;

            // FIXME: This should probably never be null.
            if (Effect != null) {
                Parameters = new DefaultMaterialSetEffectParameters(Effect);
                _COMEffect = Effect.GetID3DXEffect();
            } else {
                _COMEffect = null;
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
            return new Material(
                Effect, null,
                BeginHandlers, EndHandlers
            );
        }

        public ID3DXEffect COMEffect {
            get {
                if (Thread.CurrentThread != OwningThread)
                    throw new InvalidOperationException("Using the ID3DXEffect from a different thread is not gonna work.");

                return _COMEffect;
            }
        }

        private void CheckDevice (DeviceManager deviceManager) {
            if (Effect == null)
                return;

            if (Effect.GraphicsDevice != deviceManager.Device)
                throw new InvalidOperationException();
        }

        public virtual void Begin (DeviceManager deviceManager) {
            CheckDevice(deviceManager);

            Flush();

            if (BeginHandlers != null)
                foreach (var handler in BeginHandlers)
                    handler(deviceManager);
        }

        public virtual void Flush () {
            if (Effect != null) {
                UniformBinding.FlushEffect(Effect);

                var currentTechnique = Effect.CurrentTechnique;
                currentTechnique.Passes[0].Apply();
            }
        }

        public virtual void End (DeviceManager deviceManager) {
            CheckDevice(deviceManager);

            if (EndHandlers != null)
                foreach (var handler in EndHandlers)
                    handler(deviceManager);
        }

        public virtual void Dispose () {
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