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
        DefaultMaterialSetEffectParameters Parameters {
            get;
        }
    }

    public class EffectMaterial : Material, IEffectMaterial {
        public readonly Effect Effect;
        public readonly DefaultMaterialSetEffectParameters Parameters;

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
                Parameters = new DefaultMaterialSetEffectParameters(Effect);
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

        DefaultMaterialSetEffectParameters IEffectMaterial.Parameters {
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

        public override string ToString () {
            return string.Format("EffectMaterial #{0} ({1}.{2})", base.MaterialID, Effect.Name, Effect.CurrentTechnique.Name);
        }
    }
}