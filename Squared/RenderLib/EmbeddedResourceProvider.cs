using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Graphics;
using Squared.Render.Evil;
using Squared.Threading;

namespace Squared.Render {
    public abstract class EmbeddedResourceProvider<T> : IDisposable
        where T : class, IDisposable {
        public readonly Assembly Assembly;
        public readonly RenderCoordinator Coordinator;
        public string Prefix { get; set; }
        public string Suffix { get; protected set; }
        public bool IsDisposed { get; private set; }

        protected readonly Dictionary<string, Future<T>> Cache = new Dictionary<string, Future<T>>();
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
                prefix = Prefix ?? "";

            return (
                from n in Assembly.GetManifestResourceNames()
                where n.StartsWith(prefix) && n.EndsWith(Suffix)
                let filtered = n.Substring(0, n.Length - Suffix.Length)
                select filtered
            ).ToList();
        }

        protected abstract T CreateInstance (Stream stream, object data);

        public T LoadSyncUncached (string name, object data, bool optional, out Exception exception) {
            var streamName = (Prefix ?? "") + name + Suffix;
            exception = null;
            using (var stream = Assembly.GetManifestResourceStream(streamName)) {
                if (stream == null) {
                    if (optional) {
                        return default(T);
                    } else {
                        exception = new FileNotFoundException("No manifest resource stream with this name found", name);
                        return default(T);
                    }
                } else {
                    var instance = CreateInstance(stream, data);
                    if (IsDisposed) {
                        Coordinator.DisposeResource(instance);
                        return null;
                    } else {
                        return instance;
                    }
                }
            }
        }

        private string FixupName (string name) {
            if (name.Contains("."))
                name = name.Replace(Path.GetExtension(name), "");
            return name;
        }

        private Future<T> GetFutureForResource (string name, bool cached, out bool performLoad) {
            name = FixupName(name);
            performLoad = false;
            Future<T> future;
            if (cached) {
                lock (Cache) {
                    if (!Cache.TryGetValue(name, out future)) {
                        Cache[name] = future = new Future<T>();
                        performLoad = true;
                    }
                }
            } else {
                future = new Future<T>();
                performLoad = true;
            }

            return future;
        }

        public Future<T> LoadAsync (string name, object data, bool cached = true, bool optional = false) {
            var future = GetFutureForResource(name, cached, out bool performLoad);

            if (performLoad) {
                ThreadPool.QueueUserWorkItem((_) => {
                    var instance = LoadSyncUncached(name, data, optional, out Exception exc);
                    future.SetResult(instance, null);
                });
            }

            return future;
        }

        public T LoadSync (string name, object data, bool cached, bool optional) {
            var future = GetFutureForResource(name, cached, out bool performLoad);

            if (performLoad) {
                var instance = LoadSyncUncached(name, data, optional, out Exception exc);
                future.SetResult(instance, null);
            } else if (!future.Completed) {
                using (var evt = future.GetCompletionEvent())
                    evt.Wait();
            }

            return future.Result2;
        }

        public Future<T> LoadAsync (string name, bool cached = true, bool optional = false) {
            return LoadAsync(name, DefaultOptions, cached, optional);
        }

        public T Load (string name) {
            return LoadSync(name, DefaultOptions, true, false);
        }

        public T Load (string name, bool cached) {
            return LoadSync(name, DefaultOptions, cached, false);
        }

        public T Load (string name, bool cached, bool optional) {
            return LoadSync(name, DefaultOptions, cached, optional);
        }

        public virtual void Dispose () {
            IsDisposed = true;

            lock (Cache)
            foreach (var future in Cache.Values) {
                if (!future.Completed)
                    continue;
                if (!future.Failed)
                    Coordinator.DisposeResource(future.Result2);
            }

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
