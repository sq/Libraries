using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Graphics;
using Squared.Render.Convenience;
using Squared.Render.Evil;
using Squared.Threading;
using Squared.Util.Ini;
using Squared.Util.Text;

namespace Squared.Render.Resources {
    public class EffectManifest {
        public class Entry {
            public readonly string Name;
            public readonly Dictionary<string, string> Dict = new Dictionary<string, string>(StringComparer.Ordinal);

            public Entry (string name) {
                Name = name;
            }

            public override string ToString () =>
                $"EffectManifest Entry '{Name}' ({Dict.Count} values))";
        }

        public readonly List<Entry> Entries = new List<Entry>();

        public EffectManifest (Stream stream, bool includeFxcParams = false) {
            using (var reader = new IniReader(stream, false)) {
                Entry entry = null;
                foreach (var line in reader) {
                    switch (line.Type) {
                        case IniLineType.Section:
                            entry = new Entry(line.SectionName.ToString());
                            Entries.Add(entry);
                            break;
                        case IniLineType.Value:
                            if (!includeFxcParams && line.Key.TextEquals("FxcParams", StringComparison.OrdinalIgnoreCase))
                                continue;
                            entry.Dict[string.Intern(line.Key.ToString())] = 
                                (line.Value.Length <= 2) 
                                    ? string.Intern(line.Value.ToString()) 
                                    : line.Value.ToString();
                            break;
                    }
                }
            }
        }

        internal string FindVariant (string name, params string[] pairs) {
            if ((pairs.Length % 2) == 1)
                throw new ArgumentOutOfRangeException("pairs", "must provide a sequence of name, value pairs");

            string result = null;
            foreach (var entry in Entries) {
                if (entry.Dict["Name"] != name)
                    continue;

                bool pairMismatch = false;
                for (int i = 0; i < pairs.Length - 1; i+=2) {
                    string key = pairs[i], value = pairs[i + 1];
                    if (
                        !entry.Dict.TryGetValue(key, out string actualValue) ||
                        !actualValue.Trim().Equals(value, StringComparison.OrdinalIgnoreCase)
                    ) {
                        pairMismatch = true;
                        break;
                    }
                }

                if (pairMismatch)
                    continue;
                if (result != null)
                    throw new Exception("Found two matching manifest entries for this shader");

                result = entry.Dict["TechniqueName"];
            }

            return result;
        }
    }

    public class EffectProvider : ResourceProvider<Effect> {
        public IResourceProviderStreamSource HotReloadSource;

        private static IResourceProviderStreamSource MakeStreamProvider (Assembly assembly) {
            var suffix = ".fx.bin";
            var shaderStream = assembly.GetManifestResourceStream("shaders.zip");
            if (shaderStream != null)
                return new ZipResourceStreamProvider(shaderStream, suffix: suffix);
            else {
                System.Diagnostics.Debug.WriteLine($"WARNING: No shaders.zip found in assembly '{assembly.FullName}'. Falling back to per-effect manifest resources");
                return new EmbeddedResourceStreamProvider(assembly, suffix: suffix);
            }
        }

        public EffectProvider (Assembly assembly, RenderCoordinator coordinator) 
            : this (
                MakeStreamProvider(assembly), coordinator 
            ) {
        }

        public EffectProvider (IResourceProviderStreamSource provider, RenderCoordinator coordinator)
            : base(provider, coordinator, enableThreadedCreate: false, enableThreadedPreload: true) 
        {
        }

        private bool TryGetStreamImpl (string path, bool optional, out Stream stream, out Exception error, bool exactName = false) {
            if (HotReloadSource != null) {
                if (HotReloadSource.TryGetStream(path, optional, out stream, out error, exactName))
                    return true;
            }

            return StreamSource.TryGetStream(path, optional, out stream, out error, exactName);
        }

        protected override bool TryGetStream (string name, object data, bool optional, out Stream stream, out Exception exception) =>
            TryGetStreamImpl(name, optional, out stream, out exception);

        public EffectManifest ReadManifest () {
            if (!TryGetStreamImpl("manifest.ini", true, out var stream, out var error, exactName: true)) {
                if (error != null)
                    throw error;
                else
                    return null;
            }

            using (stream)
                return new EffectManifest(stream);
        }

        protected override Future<Effect> CreateInstance (string name, Stream stream, object data, object preloadedData, bool async) {
            // FIXME: Remove this lock, it may not be necessary now
            lock (Coordinator.UseResourceLock)
                return new Future<Effect>(EffectUtils.EffectFromFxcOutput(Coordinator.Device, stream, name));
        }

        internal int HotReloadVersion;

        public void BeginHotReload () {
            HotReloadVersion++;
        }

        public Material LoadMaterial (
            MaterialSetBase materialSet,
            AbstractString effectName, string techniqueName = null,
            bool optional = false, BlendState blendState = null
        ) {
            var effect = GetEffect(null, false);
            var material = new Material(effect, techniqueName);
            material.GetEffectForReload = GetEffect;

            Effect GetEffect (Material referenceMaterial, bool requiresClone) {
                var enableCache = HotReloadVersion == (referenceMaterial?.HotReloadVersion ?? 0);
                var effect = Load(effectName, enableCache, optional);
                if ((effect == null) && optional)
                    return null;

                if (requiresClone)
                    effect = effect.Clone();

                if (techniqueName == null) {
                    if (effect.Techniques[effectName.ToString()] != null)
                        techniqueName = effectName.ToString();
                    else
                        techniqueName = effect.Techniques.FirstOrDefault()?.Name;
                }

                var technique = effect.Techniques[referenceMaterial?.TechniqueName ?? techniqueName];
                if (technique == null) {
                    if (optional)
                        return null;
                    else
                        throw new KeyNotFoundException($"No technique named '{techniqueName}' in effect '{effectName}'");
                }

                if (referenceMaterial != null)
                    referenceMaterial.HotReloadVersion = HotReloadVersion;
                return effect;
            }

            material.BlendState = blendState;
            materialSet?.Add(material);
            return material;
        }
    }
}
