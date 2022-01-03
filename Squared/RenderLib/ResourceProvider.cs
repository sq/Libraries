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
using Squared.Util;

namespace Squared.Render.Resources {
    public delegate void ResourceLoadCompleteHandler (string name, object resource, long waitDuration, long preloadDuration, long createDuration);

    public abstract class ResourceProvider<T> : IDisposable
        where T : class 
    {
        private object PendingLoadLock = new object();
        private int _PendingLoads;
        public int PendingLoads {
            get {
                lock (PendingLoadLock)
                    return _PendingLoads;
            }
        }

        protected class CreateWorkItem : IWorkItem {
            public static WorkItemConfiguration Configuration =>
                new WorkItemConfiguration {
                    MaxConcurrency = 2,
                    DefaultStepCount = 1
                };

            public Future<T> Future;
            public ResourceProvider<T> Provider;
            public Stream Stream;
            public string Name;
            public object Data, PreloadedData;
            public bool Optional;
            public bool Async;
            public long WaitDuration, PreloadDuration;

            void IWorkItem.Execute () {
                var createStarted = Provider.Now;
                Future<T> instance = null;
                using (Stream) {
                    try {
                        // Console.WriteLine($"CreateInstance('{Name}') on thread {Thread.CurrentThread.Name}");
                        instance = Provider.CreateInstance(Name, Stream, Data, PreloadedData, Async);
                        if (instance.Completed)
                            OnCompleted(instance);
                        else
                            instance.RegisterOnComplete(OnCompleted);
                    } catch (Exception exc) {
                        Future.SetResult2(default(T), ExceptionDispatchInfo.Capture(exc));
                    } finally {
                        lock (Provider.PendingLoadLock)
                            Provider._PendingLoads--;
                    }
                }

                void OnCompleted (IFuture _) {
                    var createElapsed = Provider.Now - createStarted;
                    instance.GetResult(out T value, out Exception err);
                    if (err == null)
                        Provider.FireLoadEvent(Name, value, WaitDuration, PreloadDuration, createElapsed);
                    Future.SetResult(value, err);
                }
            }
        }

        protected class PreloadWorkItem : IWorkItem {
            public static WorkItemConfiguration Configuration =>
                new WorkItemConfiguration {
                    ConcurrencyPadding = 2,
                    DefaultStepCount = 1
                };

            public Future<T> Future;
            public ResourceProvider<T> Provider;
            public string Name;
            public object Data;
            public bool Optional;
            public bool Async;
            public long StartedWhen;

            void IWorkItem.Execute () {
                Stream stream = null;
                try {
                    Exception exc = null;
                    if (!Provider.StreamSource.TryGetStream(Name, Optional, out stream, out exc)) {
                        Future.SetResult2(
                            default(T), (exc != null) 
                                ? ExceptionDispatchInfo.Capture(exc) 
                                : null
                        );
                        lock (Provider.PendingLoadLock)
                            Provider._PendingLoads--;
                        return;
                    }

                    // Console.WriteLine($"PreloadInstance('{Name}') on thread {Thread.CurrentThread.Name}");
                    var now = Provider.Now;
                    var waitDuration = now - StartedWhen;
                    var preloadedData = Provider.PreloadInstance(Name, stream, Data);
                    var preloadElapsed = Provider.Now - now;
                    var item = new CreateWorkItem {
                        Future = Future,
                        Provider = Provider,
                        Name = Name,
                        Data = Data,
                        Optional = Optional,
                        Stream = stream,
                        PreloadedData = preloadedData,
                        Async = Async,
                        WaitDuration = waitDuration,
                        PreloadDuration = preloadElapsed
                    };
                    Provider.CreateQueue.Enqueue(ref item);
                } catch (Exception exc) {
                    if (stream != null)
                        stream.Dispose();
                    Future.SetResult2(default(T), ExceptionDispatchInfo.Capture(exc));
                }
            }
        }

        public readonly RenderCoordinator Coordinator;
        public bool IsDisposed { get; private set; }
        public readonly bool EnableThreadedPreload = true, 
            EnableThreadedCreate = false;

        protected struct CacheEntry {
            public string Name;
            public Future<T> Future;
            public object Data;
        }

        protected readonly Dictionary<string, CacheEntry> Cache = new Dictionary<string, CacheEntry>();
        protected object DefaultOptions;

        protected WorkQueue<PreloadWorkItem> PreloadQueue { get; private set; }
        protected WorkQueue<CreateWorkItem> CreateQueue { get; private set; }

        protected virtual object PreloadInstance (string name, Stream stream, object data) => null;
        protected abstract Future<T> CreateInstance (string name, Stream stream, object data, object preloadedData, bool async);

        public IResourceProviderStreamSource StreamSource { get; protected set; }

        public ITimeProvider TimeProvider = new DotNetTimeProvider();
        public event ResourceLoadCompleteHandler OnLoad;

        internal long Now => TimeProvider.Ticks;

        internal void FireLoadEvent (string name, object resource, long waitDuration, long preloadDuration, long createDuration) {
            if (OnLoad == null)
                return;

            OnLoad(name, resource, waitDuration, preloadDuration, createDuration);
        }

        protected ResourceProvider (
            IResourceProviderStreamSource source, RenderCoordinator coordinator, 
            bool enableThreadedPreload, bool enableThreadedCreate
        ) {
            if (coordinator == null)
                throw new ArgumentNullException("A render coordinator is required", "coordinator");
            StreamSource = source;
            Coordinator = coordinator;

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

        protected bool Evict (string name) {
            CacheEntry ce;
            lock (Cache) {
                if (!Cache.TryGetValue(name, out ce)) {
                    name = StreamSource.FixupName(name, true);
                    if (!Cache.TryGetValue(name, out ce))
                        return false;
                }

                Cache.Remove(name);
            }
            // FIXME: Release the resource? Dispose the future?
            return true;
        }

        public void SetStreamSource (IResourceProviderStreamSource source, bool clearCache) {
            if (source == StreamSource)
                return;
            if (source == null)
                throw new ArgumentNullException("source");
            if (clearCache)
                ClearCache();
            StreamSource = source;
        }

        public T LoadSyncUncached (string name, object data, bool optional, out Exception exception) {
            Stream stream;
            if (StreamSource.TryGetStream(name, optional, out stream, out exception)) {
                var started = Now;
                var preloadedData = PreloadInstance(name, stream, data);
                var createStarted = Now;
                var future = CreateInstance(name, stream, data, preloadedData, false);
                var finished = Now;
                if (IsDisposed) {
                    Coordinator.DisposeResource(future.Result as IDisposable);
                    return default(T);
                } else {
                    FireLoadEvent(name, future.Result, 0, createStarted - started, finished - createStarted);
                    return future.Result;
                }
            } else {
                return default(T);
            }
        }

        private CacheEntry MakeCacheEntry (string name, object data) {
            return new CacheEntry {
                Name = name,
                Data = data,
                Future = new Future<T>()
            };
        }

        private Future<T> GetFutureForResource (string name, object data, bool cached, out bool performLoad) {
            name = StreamSource.FixupName(name, true);
            performLoad = false;
            CacheEntry entry;
            if (cached) {
                lock (Cache) {
                    if (!Cache.TryGetValue(name, out entry)) {
                        Cache[name] = entry = MakeCacheEntry(name, data);
                        performLoad = true;
                    }
                }
            } else {
                entry = MakeCacheEntry(name, data);
                performLoad = true;
            }

            return entry.Future;
        }

        public Future<T> LoadAsync (string name, object data, bool cached = true, bool optional = false) {
            var future = GetFutureForResource(name, data, cached, out bool performLoad);

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
            var future = GetFutureForResource(name, data, cached, out bool performLoad);

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
                WaitForLoadSync(future, name, data);
            }

            return future.Result2;
        }

        protected virtual void WaitForLoadSync (Future<T> future, string name, object data) {
            using (var evt = future.GetCompletionEvent())
                evt.Wait();
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

        public void AddAllInstancesTo<U> (ICollection<U> result)
            where U : T
        {
            lock (Cache)
                foreach (var entry in Cache.Values) {
                    if (!entry.Future.Completed || entry.Future.Failed)
                        continue;
                    result.Add((U)entry.Future.Result);
                }
        }

        public U Reduce<U> (Func<U, string, T, object, U> f, U initialValue = default(U)) {
            var result = initialValue;
            lock (Cache)
                foreach (var entry in Cache.Values) {
                    if (!entry.Future.Completed || entry.Future.Failed)
                        continue;
                    result = f(result, entry.Name, entry.Future.Result, entry.Data);
                }
            return result;
        }

        public void ClearCache () {
            lock (Cache) {
                foreach (var entry in Cache.Values) {
                    entry.Future.RegisterOnComplete((f) => {
                        try {
                            if (f.Completed && !f.Failed)
                                Coordinator.DisposeResource(f.Result2 as IDisposable);
                        } catch {
                        }
                    });
                }

                Cache.Clear();
            }
        }

        public virtual void Dispose () {
            IsDisposed = true;

            ClearCache();
        }
    }

    public interface IResourceProviderStreamSource {
        string FixupName (string name, bool stripExtension);
        string[] GetNames ();
        bool TryGetStream (string name, bool optional, out Stream result, out Exception error);
    }

    public class FileStreamProvider : IResourceProviderStreamSource {
        public string Path;
        public string Prefix { get; set; }
        public string[] Extensions { get; protected set; }
        public SearchOption Options;

        public FileStreamProvider (string path, string prefix = null, string[] extensions = null, bool recursive = false) {
            Path = path;
            Extensions = extensions;
            Options = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        }

        protected string Filter {
            get {
                if ((Extensions?.Length ?? 0) == 0)
                    return "*";

                return string.Join(";", Extensions.Select(e => "*." + e));
            }
        }

        public string[] GetNames () {
            return Directory.GetFiles(Path, Filter, Options);
        }

        public string FixupName (string name, bool stripExtension) {
            if (stripExtension && name.Contains("."))
                name = name.Replace(System.IO.Path.GetExtension(name), "");
            name = name.Replace('/', '\\');
            if (System.IO.Path.DirectorySeparatorChar != '\\')
                name = name.Replace('\\', System.IO.Path.DirectorySeparatorChar);
            return name;
        }

        public bool TryGetStream (string name, bool optional, out Stream result, out Exception exception) {
            var pathsSearched = new DenseList<string>();

            result = null;
            exception = null;
            string candidateStreamName;
            foreach (var extension in Extensions) {
                candidateStreamName = System.IO.Path.Combine(Path, FixupName((Prefix ?? "") + name + extension, false));
                if (File.Exists(candidateStreamName)) {
                    try {
                        result = File.Open(candidateStreamName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        break;
                    } catch (Exception exc) {
                        exception = exception ?? exc;
                    }
                } else
                    pathsSearched.Add(candidateStreamName);
            }

            if (result == null) {
                candidateStreamName = System.IO.Path.Combine(Path, FixupName((Prefix ?? "") + name, false));
                if (File.Exists(candidateStreamName)) {
                    try {
                        result = File.Open(candidateStreamName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    } catch (Exception exc) {
                        exception = exception ?? exc;
                    }
                } else
                    pathsSearched.Add(candidateStreamName);
            }

            if (result == null) {
                if (optional) {
                    return false;
                } else {
                    var sb = new StringBuilder();
                    sb.Append("Could not find file '");
                    sb.Append(name);
                    sb.AppendLine("'. Searched the following paths:");
                    foreach (var path in pathsSearched)
                        sb.AppendLine(path);
                    exception = exception ?? new FileNotFoundException(sb.ToString(), name);
                    return false;
                }
            } else {
                return true;
            }
        }
    }

    public class EmbeddedResourceStreamProvider : IResourceProviderStreamSource {
        public readonly Assembly Assembly;
        public string Prefix { get; set; }
        public string Suffix { get; protected set; }

        public EmbeddedResourceStreamProvider (Assembly assembly, string prefix = null, string suffix = null) {
            Assembly = assembly;
            Prefix = prefix ?? "";
            Suffix = suffix ?? Suffix ?? "";
        }

        public string[] GetNames () {
            return (
                from n in Assembly.GetManifestResourceNames()
                where n.StartsWith(Prefix) && n.EndsWith(Suffix)
                let filtered = n.Substring(0, n.Length - Suffix.Length)
                select filtered
            ).ToArray();
        }

        public string FixupName (string name, bool stripExtension) {
            if (stripExtension && name.Contains("."))
                name = name.Replace(Path.GetExtension(name), "");
            name = name.Replace('/', '\\');
            return name;
        }

        public bool TryGetStream (string name, bool optional, out Stream result, out Exception exception) {
            var streamName = FixupName((Prefix ?? "") + name + Suffix, false);
            exception = null;
            result = Assembly.GetManifestResourceStream(streamName);
            if (result == null) {
                if (optional) {
                    return false;
                } else {
                    exception = new FileNotFoundException($"No manifest resource stream with this name found: {streamName}", name);
                    return false;
                }
            } else {
                return true;
            }
        }
    }

    public class EffectProvider : ResourceProvider<Effect> {
        public EffectProvider (Assembly assembly, RenderCoordinator coordinator) 
            : this (
                new EmbeddedResourceStreamProvider(assembly, suffix: ".fx.bin"), coordinator 
            ) {
        }

        public EffectProvider (IResourceProviderStreamSource provider, RenderCoordinator coordinator)
            : base(provider, coordinator, enableThreadedCreate: false, enableThreadedPreload: true) 
        {
        }

        protected override Future<Effect> CreateInstance (string name, Stream stream, object data, object preloadedData, bool async) {
            lock (Coordinator.CreateResourceLock)
                return new Future<Effect>(EffectUtils.EffectFromFxcOutput(Coordinator.Device, stream));
        }
    }

    public class FallbackStreamProvider : IResourceProviderStreamSource {
        public IResourceProviderStreamSource[] Sources { get; protected set; }

        public FallbackStreamProvider (params IResourceProviderStreamSource[] sources) {
            Sources = sources;
        }

        public string[] GetNames () {
            var result = new List<string>();
            foreach (var source in Sources)
                result.AddRange(source.GetNames());
            return result.ToArray();
        }

        public string FixupName (string name, bool stripExtension) {
            // FIXME
            foreach (var source in Sources) {
                var result = source.FixupName(name, stripExtension);
                if (result != null)
                    return result;
            }
            return null;
        }

        public bool TryGetStream (string name, bool optional, out Stream result, out Exception error) {
            result = null;
            var errors = new DenseList<Exception>();

            foreach (var source in Sources) {
                if (source.TryGetStream(name, optional, out result, out Exception temp)) {
                    error = null;
                    return true;
                }

                errors.Add(temp);
            }

            if (errors.Count > 1) {
                // Reverse the list of errors since the last fallback is usually the most important
                error = new AggregateException(errors.Reverse().ToArray());
            } else if (errors.Count == 1) {
                error = errors[0];
            } else {
                error = new Exception("No results found");
            }
            return false;
        }
    }
}
