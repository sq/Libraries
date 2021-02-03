using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Graphics;
using Squared.Render.Evil;
using Squared.Threading;

namespace Squared.Render {
    public abstract class EmbeddedResourceProvider<T> : IDisposable
        where T : class, IDisposable {

        private object PendingLoadLock = new object();
        private int _PendingLoads;
        public int PendingLoads {
            get {
                lock (PendingLoadLock)
                    return _PendingLoads;
            }
        }

        protected class CreateWorkItem : IWorkItem {
            public Future<T> Future;
            public EmbeddedResourceProvider<T> Provider;
            public Stream Stream;
            public string Name;
            public object Data, PreloadedData;
            public bool Optional;
            public bool Async;

            void IWorkItem.Execute () {
                using (Stream) {
                    try {
                        // Console.WriteLine($"CreateInstance('{Name}') on thread {Thread.CurrentThread.Name}");
                        var instance = Provider.CreateInstance(Stream, Data, PreloadedData, Async);
                        if (instance.Completed)
                            Future.SetResult2(instance.Result, null);
                        else
                            instance.RegisterOnComplete((_) => {
                                instance.GetResult(out T value, out Exception err);
                                Future.SetResult(value, err);
                            });
                    } catch (Exception exc) {
                        Future.SetResult2(default(T), ExceptionDispatchInfo.Capture(exc));
                    } finally {
                        lock (Provider.PendingLoadLock)
                            Provider._PendingLoads--;
                    }
                }
            }
        }

        protected class PreloadWorkItem : IWorkItem {
            public Future<T> Future;
            public EmbeddedResourceProvider<T> Provider;
            public string Name;
            public object Data;
            public bool Optional;
            public bool Async;

            void IWorkItem.Execute () {
                Stream stream = null;
                try {
                    Exception exc = null;
                    if (!Provider.GetStream(Name, Optional, out stream, out exc)) {
                        Future.SetResult2(
                            default(T), (exc != null) 
                                ? ExceptionDispatchInfo.Capture(exc) 
                                : null
                        );
                        return;
                    }

                    // Console.WriteLine($"PreloadInstance('{Name}') on thread {Thread.CurrentThread.Name}");
                    var preloadedData = Provider.PreloadInstance(stream, Data);
                    var item = new CreateWorkItem {
                        Future = Future,
                        Provider = Provider,
                        Name = Name,
                        Data = Data,
                        Optional = Optional,
                        Stream = stream,
                        PreloadedData = preloadedData,
                        Async = Async
                    };
                    Provider.CreateQueue.Enqueue(ref item);
                } catch (Exception exc) {
                    if (stream != null)
                        stream.Dispose();
                    Future.SetResult2(default(T), ExceptionDispatchInfo.Capture(exc));
                }
            }
        }

        public readonly Assembly Assembly;
        public readonly RenderCoordinator Coordinator;
        public string Prefix { get; set; }
        public string Suffix { get; protected set; }
        public bool IsDisposed { get; private set; }
        public readonly bool EnableThreadedPreload = true, 
            EnableThreadedCreate = false;

        protected readonly Dictionary<string, Future<T>> Cache = new Dictionary<string, Future<T>>();
        protected object DefaultOptions;

        protected WorkQueue<PreloadWorkItem> PreloadQueue { get; private set; }
        protected WorkQueue<CreateWorkItem> CreateQueue { get; private set; }

        public EmbeddedResourceProvider (
            Assembly assembly, RenderCoordinator coordinator, 
            bool enableThreadedPreload = true, bool enableThreadedCreate = false
        ) {
            if (coordinator == null)
                throw new ArgumentNullException("A render coordinator is required", "coordinator");
            Assembly = assembly;
            Coordinator = coordinator;
            if (Suffix == null)
                Suffix = "";

            EnableThreadedPreload = enableThreadedPreload;

            switch (coordinator.GraphicsBackendName) {
                case "D3D9":
                case "D3D11":
                case "Vulkan":
                    EnableThreadedCreate = enableThreadedCreate && true;
                    break;
                default:
                    EnableThreadedCreate = enableThreadedCreate && false;
                    break;
            }

            PreloadQueue = coordinator.ThreadGroup.GetQueueForType<PreloadWorkItem>(forMainThread: !EnableThreadedPreload);
            CreateQueue = coordinator.ThreadGroup.GetQueueForType<CreateWorkItem>(forMainThread: !EnableThreadedCreate);
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

        protected virtual object PreloadInstance (Stream stream, object data) => null;
        protected abstract Future<T> CreateInstance (Stream stream, object data, object preloadedData, bool async);

        private bool GetStream (string name, bool optional, out Stream result, out Exception exception) {
            var streamName = FixupName((Prefix ?? "") + name + Suffix, false);
            exception = null;
            result = Assembly.GetManifestResourceStream(streamName);
            if (result == null) {
                if (optional) {
                    return false;
                } else {
                    exception = new FileNotFoundException($"No manifest resource stream with this name found: {streamName}", name);
                    Console.WriteLine(exception.Message);
                    foreach (var sn in Assembly.GetManifestResourceNames())
                        Console.WriteLine(sn);
                    Console.WriteLine();
                    return false;
                }
            } else {
                return true;
            }
        }

        public T LoadSyncUncached (string name, object data, bool optional, out Exception exception) {
            Stream stream;
            if (GetStream(name, optional, out stream, out exception)) {
                var preloadedData = PreloadInstance(stream, data);
                var future = CreateInstance(stream, data, preloadedData, false);
                if (IsDisposed) {
                    Coordinator.DisposeResource(future.Result);
                    return default(T);
                } else {
                    return future.Result;
                }
            } else {
                return default(T);
            }
        }

        private string FixupName (string name, bool stripExtension) {
            if (stripExtension && name.Contains("."))
                name = name.Replace(Path.GetExtension(name), "");
            name = name.Replace('/', '\\');
            return name;
        }

        private Future<T> GetFutureForResource (string name, bool cached, out bool performLoad) {
            name = FixupName(name, true);
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
                _PendingLoads++;
                var workItem = new PreloadWorkItem {
                    Provider = this,
                    Future = future,
                    Name = name,
                    Data = data,
                    Optional = optional,
                    Async = true
                };
                PreloadQueue.Enqueue(workItem);
            }

            return future;
        }

        public T LoadSync (string name, object data, bool cached, bool optional) {
            var future = GetFutureForResource(name, cached, out bool performLoad);

            if (performLoad) {
                lock (PendingLoadLock)
                    _PendingLoads++;
                try {
                    var instance = LoadSyncUncached(name, data, optional, out Exception exc);
                    future.SetResult(instance, exc);
                } finally {
                    lock (PendingLoadLock)
                        _PendingLoads--;
                }
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

            lock (Cache) {
                foreach (var future in Cache.Values) {
                    if (!future.Completed)
                        continue;
                    if (!future.Failed)
                        Coordinator.DisposeResource(future.Result2);
                }

                Cache.Clear();
            }
        }
    }

    public class EmbeddedEffectProvider : EmbeddedResourceProvider<Effect> {
        public EmbeddedEffectProvider (Assembly assembly, RenderCoordinator coordinator) 
            : base(assembly, coordinator, enableThreadedCreate: false) {
            Suffix = ".fx";
        }

        public EmbeddedEffectProvider (RenderCoordinator coordinator) 
            : base(Assembly.GetCallingAssembly(), coordinator, enableThreadedCreate: false) {
            Suffix = ".fx";
        }

        protected override Future<Effect> CreateInstance (Stream stream, object data, object preloadedData, bool async) {
            lock (Coordinator.CreateResourceLock)
                return new Future<Effect>(EffectUtils.EffectFromFxcOutput(Coordinator.Device, stream));
        }
    }
}
