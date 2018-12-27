using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Graphics;
using Squared.Render.Evil;

namespace Squared.Render {
    public abstract class EmbeddedResourceProvider<T> : IDisposable
        where T : class, IDisposable {
        public readonly Assembly Assembly;
        public readonly RenderCoordinator Coordinator;
        private readonly Dictionary<string, T> Cache = new Dictionary<string, T>();
        public string Prefix { get; set; }
        public string Suffix { get; protected set; }

        protected object DefaultOptions;

        public EmbeddedResourceProvider (Assembly assembly, RenderCoordinator coordinator) {
            if (coordinator == null)
                throw new ArgumentNullException("A render coordinator is required", "coordinator");
            Assembly = assembly;
            Coordinator = coordinator;
            if (Suffix == null)
                Suffix = "";
        }

        public List<string> GetNames (string prefix = null) {
            if (prefix == null)
                prefix = Prefix;

            return (
                from n in Assembly.GetManifestResourceNames()
                where n.StartsWith(prefix) && n.EndsWith(Suffix)
                let filtered = n.Substring(0, n.Length - Suffix.Length)
                select filtered
            ).ToList();
        }

        protected abstract T CreateInstance (Stream stream, object data);

        protected T Load (string name, object data) {
            T result;
            if (!Cache.TryGetValue(name, out result)) {
                var streamName = (Prefix ?? "") + name + Suffix;
                using (var stream = Assembly.GetManifestResourceStream(streamName)) {
                    if (stream == null)
                        throw new FileNotFoundException("No manifest resource stream with this name found", name);
                    result = CreateInstance(stream, data);
                    Cache[name] = result;
                }
            }
            return result;
        }

        public T Load (string name) {
            return Load(name, DefaultOptions);
        }

        public void Dispose () {
            foreach (var value in Cache.Values)
                Coordinator.DisposeResource(value);

            Cache.Clear();
        }
    }

    public class EmbeddedEffectProvider : EmbeddedResourceProvider<Effect> {
        public EmbeddedEffectProvider (Assembly assembly, RenderCoordinator coordinator) 
            : base(assembly, coordinator) {
            Suffix = ".fx";
        }

        public EmbeddedEffectProvider (RenderCoordinator coordinator) 
            : base(Assembly.GetCallingAssembly(), coordinator) {
            Suffix = ".fx";
        }

        protected override Effect CreateInstance (Stream stream, object data) {
            lock (Coordinator.CreateResourceLock)
                return EffectUtils.EffectFromFxcOutput(Coordinator.Device, stream);
        }
    }
}
