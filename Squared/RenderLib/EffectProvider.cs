using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Graphics;
using Squared.Render.Evil;
using Squared.Threading;
using Squared.Util.Ini;

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
                            entry = new Entry(line.SectionName);
                            Entries.Add(entry);
                            break;
                        case IniLineType.Value:
                            if (!includeFxcParams && line.Key.Equals("FxcParams", StringComparison.OrdinalIgnoreCase))
                                continue;
                            entry.Dict[string.Intern(line.Key)] = (line.Value.Length <= 2) ? string.Intern(line.Value) : line.Value;
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

        public EffectManifest ReadManifest () {
            if (!StreamSource.TryGetStream("manifest.ini", true, out var stream, out var error, exactName: true)) {
                if (error != null)
                    throw error;
                else
                    return null;
            }

            using (stream)
                return new EffectManifest(stream);
        }

        protected override Future<Effect> CreateInstance (string name, Stream stream, object data, object preloadedData, bool async) {
            lock (Coordinator.CreateResourceLock)
                return new Future<Effect>(EffectUtils.EffectFromFxcOutput(Coordinator.Device, stream));
        }
    }
}
