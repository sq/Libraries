using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Graphics;
using Squared.Render.Evil;

namespace Squared.Render {
    public class EmbeddedEffectProvider : IDisposable {
        public readonly Assembly Assembly;
        public readonly RenderCoordinator Coordinator;
        private readonly Dictionary<string, Effect> Cache = new Dictionary<string, Effect>();

        public EmbeddedEffectProvider (Assembly assembly, RenderCoordinator coordinator) {
            if (coordinator == null)
                throw new ArgumentNullException("A render coordinator is required", "coordinator");
            Assembly = assembly;
            Coordinator = coordinator;
        }

        public EmbeddedEffectProvider (RenderCoordinator coordinator) 
            : this(Assembly.GetCallingAssembly(), coordinator) {
        }

        public void Dispose () {
            foreach (var value in Cache.Values)
                Coordinator.DisposeResource(value);

            Cache.Clear();
        }

        public Effect Load (string name) {
            Effect result;
            if (!Cache.TryGetValue(name, out result)) {
                var streamName = name + ".fx";
                using (var stream = Assembly.GetManifestResourceStream(streamName)) {
                    lock (Coordinator.CreateResourceLock)
                        result = EffectUtils.EffectFromFxcOutput(Coordinator.Device, stream);
                    Cache[name] = result;
                }
            }
            return result;
        }
    }
}
