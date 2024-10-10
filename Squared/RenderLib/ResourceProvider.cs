﻿using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Graphics;
using Squared.Render.Evil;
using Squared.Task;
using Squared.Threading;
using Squared.Util;
using Squared.Util.Ini;
using Squared.Util.Testing;
using Squared.Util.Text;

namespace Squared.Render.Resources {
    public delegate void ResourceLoadStartHandler (ResourceLoadInfo info);
    public delegate void ResourceLoadCompleteHandler (ResourceLoadInfo info, object resource);

    public enum ResourceLoadStatus : int {
        Uninitialized,
        Queued,
        OpeningStream,
        Preloading,
        Preloaded,
        Creating,
        Created,
        Finished,
        Failed,
    }

    public class ResourceLoadInfo {
        public Type ResourceType;
        public readonly string Name;
        public readonly object Data;
        public readonly long QueuedWhen;
        public readonly bool Optional;
        public readonly bool Async;

        public long QueuedDuration, PreloadDuration, CreateDuration;

        public Exception FailureReason { get; internal set; }
        public long StatusChangedWhen { get; private set; }
        public ResourceLoadStatus Status { get; private set; }
        public bool AsyncOperationQueued { get; internal set; }

        internal ResourceLoadInfo (Type resourceType, string name, object data, long now, bool optional, bool async) {
            ResourceType = resourceType;
            Name = name;
            Data = data;
            Optional = optional;
            Async = async;
            SetStatus(ResourceLoadStatus.Queued, now);
        }

        internal bool SetStatus (ResourceLoadStatus newStatus, long now) {
            if (Status == newStatus)
                return false;
            if (Status == ResourceLoadStatus.Queued)
                QueuedDuration = now - StatusChangedWhen;
            else if (Status == ResourceLoadStatus.Preloading)
                PreloadDuration = now - StatusChangedWhen;
            else if (Status == ResourceLoadStatus.Creating)
                CreateDuration = now - StatusChangedWhen;

            Status = newStatus;
            StatusChangedWhen = now;
            return true;
        }

        public string ToString (long now) {
            var elapsed = now - QueuedWhen;
            if ((Status != ResourceLoadStatus.Failed) && (Status != ResourceLoadStatus.Finished))
                return $"{Status} {ResourceType} '{Name}' with data {Data} queued for {elapsed / Time.SecondInTicks} second(s)";
            else
                return ToString();
        }

        public override string ToString () {
            return $"{Status} {ResourceType} '{Name}' with data {Data}";
        }
    }

    public abstract class ResourceProvider<T> : IDisposable
        where T : class 
    {
        public FaultInjector FaultInjector;

        protected struct EntryEnumerator : IEnumerator<CacheEntry>, IEnumerable<CacheEntry> {
            public readonly ResourceProvider<T> Provider;

            private Dictionary<ImmutableAbstractString, SecondLevelCache>.ValueCollection.Enumerator FirstLevel;
            private SecondLevelCache.ValueCollection.Enumerator SecondLevel;
            private bool IsInitialized;

            public EntryEnumerator (ResourceProvider<T> provider)
                : this () 
            {
                Provider = provider;
            }

            public CacheEntry Current {
                get;
                private set; 
            }
            object IEnumerator.Current => Current;

            public void Dispose () {
                IsInitialized = false;
                SecondLevel.Dispose();
                FirstLevel.Dispose();
            }

            public IEnumerator<CacheEntry> GetEnumerator () => this;
            IEnumerator IEnumerable.GetEnumerator () => this;

            public bool MoveNext () {
                if (!IsInitialized) {
                    IsInitialized = true;
                    FirstLevel = Provider.FirstLevelCache.Values.GetEnumerator();
                    if (!FirstLevel.MoveNext())
                        return false;
                    SecondLevel = FirstLevel.Current.Values.GetEnumerator();
                }

                while (!SecondLevel.MoveNext()) {
                    if (!FirstLevel.MoveNext())
                        return false;

                    SecondLevel = FirstLevel.Current.Values.GetEnumerator();
                }

                Current = SecondLevel.Current;

                return true;
            }

            public void Reset () {
                Dispose();
            }
        }

        protected class SecondLevelCache : Dictionary<CacheKey, CacheEntry> {
            public bool Retain;

            public SecondLevelCache (ResourceProvider<T> provider)
                : base(provider.Comparer) {
            }
        }

        protected sealed class CacheKeyComparer : IEqualityComparer<CacheKey> {
            public readonly ResourceProvider<T> Provider;

            public CacheKeyComparer (ResourceProvider<T> provider) {
                Provider = provider;
            }

            public bool Equals (CacheKey x, CacheKey y) {
                if (!Provider.PathComparer.Equals(x.Name.Value, y.Name.Value))
                    return false;

                return Provider.AreKeysEqual(ref x, ref y);
            }

            public int GetHashCode (CacheKey obj) {
                return Provider.PathComparer.GetHashCode(obj.Name.Value);
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
            public ResourceLoadInfo LoadInfo;
            public object PreloadedData;
            public bool StreamIsDisposed;

            void IWorkItem.Execute (ThreadGroup group) {
                Future<T> instance = null;
                try {
                    LoadInfo.AsyncOperationQueued = false;
                    // Console.WriteLine($"CreateInstance('{Name}') on thread {Thread.CurrentThread.Name}");
                    LoadInfo.SetStatus(ResourceLoadStatus.Creating, Provider.Now);
                    instance = Provider.CreateInstance(LoadInfo.Name, Stream, LoadInfo.Data, PreloadedData, LoadInfo.Async);
                    if (instance.Completed)
                        OnCompleted(instance);
                    else {
                        LoadInfo.AsyncOperationQueued = true;
                        instance.RegisterOnComplete(OnCompleted);
                    }
                } catch (Exception exc) {
                    Provider.NotifyLoadFailed(LoadInfo, exc);
                    Provider.SetFutureResult2(Future, default, ExceptionDispatchInfo.Capture(exc));
                    if (!StreamIsDisposed) {
                        StreamIsDisposed = true;
                        Provider.StreamSource.DisposeStream(Stream);
                    }
                } finally {
                    if (!LoadInfo.Async && !StreamIsDisposed) {
                        StreamIsDisposed = true;
                        Provider.StreamSource.DisposeStream(Stream);
                    }
                }

                void OnCompleted (IFuture _) {
                    LoadInfo.AsyncOperationQueued = false;
                    instance.GetResult(out T value, out Exception err);
                    if (err == null)
                        LoadInfo.SetStatus(ResourceLoadStatus.Created, Provider.Now);
                    try {
                        Provider.SetFutureResult(Future, value, err);
                    } finally {
                        if (err != null)
                            Provider.NotifyLoadFailed(LoadInfo, err);
                        else
                            Provider.NotifyLoadCompleted(LoadInfo, value);
                        if (!StreamIsDisposed) {
                            StreamIsDisposed = true;
                            Provider.StreamSource.DisposeStream(Stream);
                        }
                    }
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
            public ResourceLoadInfo LoadInfo;
            public long StartedWhen;

            void IWorkItem.Execute (ThreadGroup group) {
                Stream stream = null;
                try {
                    LoadInfo.AsyncOperationQueued = false;
                    Exception exc = null;
                    LoadInfo.SetStatus(ResourceLoadStatus.OpeningStream, Provider.Now);
                    if (!Provider.TryGetStream(LoadInfo.Name, LoadInfo.Data, LoadInfo.Optional, out stream, out exc)) {
                        if (LoadInfo.Optional && (exc is FileNotFoundException))
                            Provider.SetFutureResult(Future, default, null);
                        else
                            Provider.SetFutureResult2(
                                Future, default, (exc != null)
                                    ? ExceptionDispatchInfo.Capture(exc)
                                    : null
                            );
                        Provider.NotifyLoadFailed(LoadInfo, exc);
                        return;
                    }

                    // Console.WriteLine($"PreloadInstance('{Name}') on thread {Thread.CurrentThread.Name}");
                    LoadInfo.SetStatus(ResourceLoadStatus.Preloading, Provider.Now);
                    var preloadedData = Provider.PreloadInstance(LoadInfo.Name, stream, LoadInfo.Data);
                    LoadInfo.SetStatus(ResourceLoadStatus.Preloaded, Provider.Now);

                    var item = new CreateWorkItem {
                        Future = Future,
                        Provider = Provider,
                        LoadInfo = LoadInfo,
                        Stream = stream,
                        PreloadedData = preloadedData,
                    };
                    LoadInfo.AsyncOperationQueued = true;
                    Provider.CreateQueue.Enqueue(ref item);
                } catch (Exception exc) {
                    Provider.StreamSource.DisposeStream(stream);
                    Provider.SetFutureResult2(Future, default, ExceptionDispatchInfo.Capture(exc));
                }
            }
        }

        private List<ResourceLoadInfo> PendingLoads = new List<ResourceLoadInfo>();

        public int PendingLoadCount {
            get {
                lock (PendingLoads)
                    return PendingLoads.Count;
            }
        }

        /// <summary>
        /// If set, completion events will be posted to this scheduler's work queue.
        /// </summary>
        public Squared.Task.TaskScheduler CompletionScheduler;

        public readonly RenderCoordinator Coordinator;
        public bool IsDisposed { get; private set; }
        public readonly bool EnableThreadedPreload = true, 
            EnableThreadedCreate = false;

        protected struct CacheKey {
            public ImmutableAbstractString Name;
            public object Data;
        }

        protected struct CacheEntry {
            public ImmutableAbstractString Name;
            public Future<T> Future;
            public object Data;
        }

        protected readonly CacheKeyComparer Comparer;
        protected readonly ImmutableAbstractStringLookup<SecondLevelCache> FirstLevelCache = 
            new ImmutableAbstractStringLookup<SecondLevelCache>(false);
        protected object DefaultOptions;

        protected WorkQueue<PreloadWorkItem> PreloadQueue { get; private set; }
        protected WorkQueue<CreateWorkItem> CreateQueue { get; private set; }

        protected virtual object PreloadInstance (string name, Stream stream, object data) => null;
        protected abstract Future<T> CreateInstance (string name, Stream stream, object data, object preloadedData, bool async);

        public IResourceProviderStreamSource StreamSource { get; protected set; }

        public PathNameComparer PathComparer = new PathNameComparer(true, true);
        public ITimeProvider TimeProvider = new DotNetTimeProvider();
        public event ResourceLoadStartHandler OnLoadStart;
        public event ResourceLoadCompleteHandler OnLoad;

        internal long Now => TimeProvider.Ticks;

        protected void SetFutureResult<U> (Future<U> future, U result, Exception error) {
            if ((CompletionScheduler == null) || (Thread.CurrentThread == CompletionScheduler.MainThread))
                future.SetResult(result, error);
            else
                CompletionScheduler.QueueWorkItem(() => future.SetResult(result, error));
        }

        protected void SetFutureResult2<U> (Future<U> future, U result, ExceptionDispatchInfo errorInfo) {
            if ((CompletionScheduler == null) || (Thread.CurrentThread == CompletionScheduler.MainThread))
                future.SetResult2(result, errorInfo);
            else
                CompletionScheduler.QueueWorkItem(() => future.SetResult2(result, errorInfo));
        }

        internal void FireStartEvent (ResourceLoadInfo info) {
            if (OnLoadStart == null)
                return;

            OnLoadStart(info);
        }

        internal void FireLoadEvent (ResourceLoadInfo info, object resource) {
            if (OnLoad == null)
                return;

            OnLoad(info, resource);
        }

        internal void NotifyLoadCompleted (ResourceLoadInfo info, object resource) {
            lock (PendingLoads)
                PendingLoads.Remove(info);

            info.FailureReason = null;
            if (info.SetStatus(ResourceLoadStatus.Finished, Now))
                FireLoadEvent(info, resource);
        }

        internal void NotifyLoadFailed (ResourceLoadInfo info, Exception reason) {
            lock (PendingLoads)
                PendingLoads.Remove(info);

            info.FailureReason = reason;
            if (info.SetStatus(ResourceLoadStatus.Failed, Now))
                FireLoadEvent(info, null);
        }

        public void ForEachPendingLoad (Action<ResourceLoadInfo> handler) {
            lock (PendingLoads)
                foreach (var item in PendingLoads)
                    handler(item);
        }

        protected ResourceProvider (
            IResourceProviderStreamSource source, RenderCoordinator coordinator, 
            bool enableThreadedPreload, bool enableThreadedCreate
        ) {
            if (coordinator == null)
                throw new ArgumentNullException(nameof(coordinator));
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            StreamSource = source;
            Coordinator = coordinator;

            EnableThreadedPreload = enableThreadedPreload;
            EnableThreadedCreate = enableThreadedCreate && coordinator.GraphicsBackendIsThreadingSafe;

            Comparer = new CacheKeyComparer(this);
            PreloadQueue = coordinator.ThreadGroup.GetQueueForType<PreloadWorkItem>(forMainThread: !EnableThreadedPreload);
            CreateQueue = coordinator.ThreadGroup.GetQueueForType<CreateWorkItem>(forMainThread: !EnableThreadedCreate);
        }

        protected virtual CacheKey MakeKey (ImmutableAbstractString name, object data) {
            return new CacheKey { Name = name, Data = data ?? DefaultOptions };
        }

        /// <summary>
        /// Override this if you wish to take load options into account when looking items up in the cache
        /// </summary>
        protected virtual bool AreKeysEqual (ref CacheKey lhs, ref CacheKey rhs) {
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

        /// <summary>
        /// This method will be called to filter load data that was passed in from the outside before it is passed to PreloadInstance
        /// </summary>
        protected virtual object FilterData (AbstractString name, object data) {
            return data;
        }

        public T LoadSyncUncached (AbstractString name, object data, bool optional, out Exception exception) =>
            LoadSyncUncached(name, data, optional, out exception, false);

        private T LoadSyncUncached (AbstractString name, object data, bool optional, out Exception exception, bool dataFiltered) {
            FaultInjector?.Step();

            try {
                if (!dataFiltered)
                    data = FilterData(name, data);
            } catch (Exception exc) {
                var info2 = RecordPendingLoad(name, data, optional, false);
                NotifyLoadFailed(info2, exc);
                exception = exc;
                return default(T);
            }

            var info = RecordPendingLoad(name, data, optional, false);
            try {
                info.SetStatus(ResourceLoadStatus.OpeningStream, Now);
                if (TryGetStream(info.Name, data, optional, out var stream, out exception)) {
                    info.SetStatus(ResourceLoadStatus.Preloading, Now);
                    var preloadedData = PreloadInstance(info.Name, stream, data);
                    info.SetStatus(ResourceLoadStatus.Preloaded, Now);
                    info.SetStatus(ResourceLoadStatus.Creating, Now);
                    var future = CreateInstance(info.Name, stream, data, preloadedData, false);
                    info.SetStatus(ResourceLoadStatus.Created, Now);
                    NotifyLoadCompleted(info, future.Result);
                    if (IsDisposed && future.CompletedSuccessfully) {
                        Coordinator.DisposeResource(future.Result as IDisposable);
                        return default(T);
                    } else {
                        return future.Result;
                    }
                } else {
                    return default(T);
                }
            } catch (Exception exc) {
                NotifyLoadFailed(info, exc);
                exception = exc;
                return default(T);
            }
        }

        /// <summary>
        /// This method will be called to try and locate a stream for loading a resource.
        /// You can use this to customize stream selection behavior or provide a custom fallback if no stream is found in the stream source.
        /// </summary>
        protected virtual bool TryGetStream (string name, object data, bool optional, out Stream stream, out Exception exception) {
            return StreamSource.TryGetStream(name, optional, out stream, out exception);
        }

        private CacheEntry MakeCacheEntry (ImmutableAbstractString name, object data) {
            return new CacheEntry {
                Name = name,
                Data = data,
                Future = new Future<T>()
            };
        }

        protected SecondLevelCache GetSecondLevelCache (AbstractString name, bool createIfMissing) {
            SecondLevelCache slc;
            lock (FirstLevelCache) {
                if (!FirstLevelCache.TryGetValue(name, out slc)) {
                    if (createIfMissing)
                        FirstLevelCache[name] = slc = new SecondLevelCache(this);
                }
            }
            return slc;
        }

        private Future<T> GetFutureForResource (AbstractString name, object data, bool cached, bool createIfMissing, out bool performLoad) {
            performLoad = false;
            // HACK: Probe with mutable string
            var key = MakeKey(name.AsImmutable(true), data);
            CacheEntry entry;
            if (cached) {
                var slc = GetSecondLevelCache(name, createIfMissing);
                if (slc != null) {
                    lock (slc) {
                        if (!slc.TryGetValue(key, out entry)) {
                            if (createIfMissing) {
                                // Then store using immutable string
                                key = MakeKey(name.AsImmutable(), data);
                                slc[key] = entry = MakeCacheEntry(key.Name, data);
                            }
                            performLoad = true;
                        }
                    }
                } else {
                    entry = MakeCacheEntry(name.AsImmutable(), data);
                    performLoad = true;
                }
            } else {
                entry = MakeCacheEntry(name.AsImmutable(), data);
                performLoad = true;
            }

            return entry.Future;
        }

        public Future<T> LoadAsync (AbstractString name, object data, bool cached = true, bool optional = false) {
            var exc = FaultInjector?.StepNonThrowing();
            if (exc != null) {
                var result = new Future<T>();
                SetFutureResult(result, default, exc);
                return result;
            }

            data = FilterData(name, data);
            var future = GetFutureForResource(name, data, cached, true, out bool performLoad);

            if (performLoad) {
                var workItem = new PreloadWorkItem {
                    Provider = this,
                    Future = future,
                    LoadInfo = RecordPendingLoad(name, data, optional, true)
                };
                PreloadQueue.Enqueue(workItem);
            }

            return future;
        }

        protected Future<T> ReloadAsync (AbstractString name, object data) {
            var result = new Future<T>();
            var workItem = new PreloadWorkItem {
                Provider = this,
                Future = result,
                LoadInfo = RecordPendingLoad(name, data, false, true)
            };
            PreloadQueue.Enqueue(workItem);
            return result;
        }

        private ResourceLoadInfo RecordPendingLoad (AbstractString name, object data, bool optional, bool async) {
            var result = new ResourceLoadInfo(typeof(T), name.ToString(), data, Now, optional, async);
            lock (PendingLoads)
                PendingLoads.Add(result);
            return result;
        }

        public bool TryGetExisting (AbstractString name, object data, out T result) {
            data = FilterData(name, data);
            var f = GetFutureForResource(name, data, true, false, out _);
            if (f == null) {
                result = default;
                return false;
            }
            return f.GetResult(out result, out _);
        }

        public bool TryGetExisting (AbstractString name, out T result, out object data) {
            result = default;
            data = default;

            var slc = GetSecondLevelCache(name, false);
            if (slc == null)
                return false;

            lock (slc) {
                if (slc.Count != 1)
                    return false;

                var item = slc.First();
                data = item.Key.Data;
                return item.Value.Future.GetResult(out result);
            }
        }

        public T LoadSync (AbstractString name, object data, bool cached, bool optional) {
            try {
                data = FilterData(name, data);
            } catch (Exception exc) {
                var info2 = RecordPendingLoad(name, data, optional, false);
                NotifyLoadFailed(info2, exc);
                throw;
            }

            var future = GetFutureForResource(name, data, cached, true, out bool performLoad);

            if (performLoad) {
                T instance = LoadSyncUncached(name, data, optional, out Exception exc, true);
                SetFutureResult(future, instance, exc);
            } else if (!future.Completed) {
                WaitForLoadSync(future, name, data);
            }

            return future.Result2;
        }

        protected virtual void WaitForLoadSync (Future<T> future, AbstractString name, object data) {
            using (var evt = future.GetCompletionEvent())
                evt.Wait();
        }

        public Future<T> LoadAsync (AbstractString name, bool cached = true, bool optional = false) {
            return LoadAsync(name, DefaultOptions, cached, optional);
        }

        public T Load (AbstractString name) {
            return LoadSync(name, DefaultOptions, true, false);
        }

        public T Load (AbstractString name, bool cached) {
            return LoadSync(name, DefaultOptions, cached, false);
        }

        public T Load (AbstractString name, bool cached, bool optional) {
            return LoadSync(name, DefaultOptions, cached, optional);
        }

        protected EntryEnumerator Entries () {
            return new EntryEnumerator(this);
        }

        public virtual void AddAllInstancesTo (ICollection<T> result) {
            lock (FirstLevelCache)
                foreach (var entry in Entries()) {
                    if (!entry.Future.GetResult(out var instance))
                        continue;
                    result.Add(instance);
                }
        }

        public U Reduce<U> (Func<U, ImmutableAbstractString, T, object, U> f, U initialValue = default(U)) {
            var result = initialValue;
            lock (FirstLevelCache)
                foreach (var entry in Entries()) {
                    if (!entry.Future.CompletedSuccessfully)
                        continue;
                    result = f(result, entry.Name, entry.Future.Result, entry.Data);
                }
            return result;
        }

        public void ClearCache () {
            lock (FirstLevelCache) {
                foreach (var entry in Entries()) {
                    entry.Future.RegisterOnComplete((f) => {
                        try {
                            if (f.CompletedSuccessfully)
                                Coordinator.DisposeResource(f.Result2 as IDisposable);
                        } catch {
                        }
                    });
                }

                FirstLevelCache.Clear();
            }
        }

        public virtual void Dispose () {
            IsDisposed = true;

            ClearCache();
        }
    }

    public interface IResourceProviderStreamSource {
        string[] GetNames (bool asFullPaths = false);
        bool TryGetStream (string name, bool optional, out Stream result, out Exception error, bool exactName = false);
        void DisposeStream (Stream stream);
    }

    public class FileStreamProvider : IResourceProviderStreamSource {
        private static readonly ConditionalWeakTable<Stream, string> StreamPaths = 
            new ConditionalWeakTable<Stream, string>();
        private readonly ConditionalWeakTable<Stream, IDisposable> StreamParents = 
            new ConditionalWeakTable<Stream, IDisposable>();

        public string Path;
        public string Prefix { get; set; }
        public string[] Extensions { get; protected set; }
        public SearchOption Options;
        public bool UseMmap;

        private volatile int _StreamsOpen;
        public int StreamsOpen => _StreamsOpen;

        public FileStreamProvider (string path, string prefix = null, string[] extensions = null, bool recursive = false, bool mmap = false) {
            Path = path;
            Extensions = extensions;
            Options = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            UseMmap = mmap;
        }

        protected string Filter {
            get {
                if ((Extensions?.Length ?? 0) == 0)
                    return "*";

                return string.Join(";", Extensions.Select(e => "*" + e));
            }
        }

        public string[] GetNames (bool asFullPaths) {
            if (!Directory.Exists(Path))
                return Array.Empty<string>();
            var result = new DenseList<string>();
            foreach (var file in Directory.EnumerateFiles(Path, Filter, Options)) {
                if (!asFullPaths && file.StartsWith(Path)) {
                    if (file[Path.Length] == System.IO.Path.DirectorySeparatorChar)
                        result.Add(file.Substring(Path.Length + 1));
                    else
                        result.Add(file.Substring(Path.Length));
                } else
                    result.Add(file);
            }
            return result.ToArray();
        }

        // Only used before opening a file now
        private string FixupName (string name) {
            var otherSeparator = System.IO.Path.DirectorySeparatorChar == '\\'
                ? '/' : '\\';
            name = name.Replace(otherSeparator, System.IO.Path.DirectorySeparatorChar);
            return name;
        }

        public static bool TryGetStreamPath (Stream stream, out string path) {
            return StreamPaths.TryGetValue(stream, out path);
        }

        /// <summary>
        /// Disposes the stream along with any additional resources associated with the stream.
        /// </summary>
        /// <param name="stream"></param>
        public void DisposeStream (Stream stream) {
            if (stream == null)
                return;

            stream.Dispose();
            if (StreamParents.TryGetValue(stream, out var parent)) {
                Interlocked.Decrement(ref _StreamsOpen);
                parent?.Dispose();
                StreamParents.Remove(stream);
            }

            StreamPaths.Remove(stream);
        }

        private Stream OpenFile (string path) {
            var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            Stream result;
            if (UseMmap) {
                var mmf = MemoryMappedFile.CreateFromFile(stream, null, 0, MemoryMappedFileAccess.Read, HandleInheritability.None, false);
                // FIXME: Dispose the mmap when disposing the stream?
                result = mmf.CreateViewStream(0, 0, MemoryMappedFileAccess.Read);
                StreamParents.Add(result, mmf);
                Interlocked.Increment(ref _StreamsOpen);
            } else {
                result = stream;
            }
            StreamPaths.Add(result, path);
            return result;
        }

        public bool TryGetStream (string name, bool optional, out Stream result, out Exception exception, bool exactName = false) {
            var pathsSearched = new DenseList<string>();

            result = null; 
            exception = null;
            string candidateStreamName;
            foreach (var extension in Extensions) {
                candidateStreamName = System.IO.Path.Combine(Path, FixupName(exactName ? name : (Prefix ?? "") + name + extension));
                if (File.Exists(candidateStreamName)) {
                    try {
                        result = OpenFile(candidateStreamName);
                        break;
                    } catch (Exception exc) {
                        exception = exception ?? exc;
                    }
                } else
                    pathsSearched.Add(candidateStreamName);
            }

            if (result == null) {
                candidateStreamName = System.IO.Path.Combine(Path, FixupName((Prefix ?? "") + name));
                if (File.Exists(candidateStreamName)) {
                    try {
                        result = OpenFile(candidateStreamName);
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

        public string[] GetNames (bool asFullPaths) {
            return (
                from n in Assembly.GetManifestResourceNames()
                where n.StartsWith(Prefix) && n.EndsWith(Suffix)
                let filtered = n.Substring(0, n.Length - Suffix.Length)
                select filtered
            ).ToArray();
        }

        private string FixupName (string name) {
            return name.Replace('/', '\\');
        }

        public void DisposeStream (Stream stream) {
        }

        public bool TryGetStream (string name, bool optional, out Stream result, out Exception exception, bool exactName = false) {
            var streamName = FixupName(exactName ? name : (Prefix ?? "") + name + Suffix);
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

    public class ZipResourceStreamProvider : IResourceProviderStreamSource, IDisposable {
        public readonly ZipArchive Archive;
        public string Prefix { get; set; }
        public string Suffix { get; set; }

        public ZipResourceStreamProvider (Stream stream, string prefix = null, string suffix = null)
            : this (new ZipArchive(stream, ZipArchiveMode.Read, false), prefix, suffix) {
        }

        public ZipResourceStreamProvider (ZipArchive archive, string prefix = null, string suffix = null) {
            Archive = archive;
            Prefix = prefix ?? "";
            Suffix = suffix ?? Suffix ?? "";
        }

        public void Dispose () {
            Archive.Dispose();
        }

        public string[] GetNames (bool asFullPaths) {
            return (
                from e in Archive.Entries
                let n = e.FullName
                where n.StartsWith(Prefix) && n.EndsWith(Suffix)
                let filtered = n.Substring(0, n.Length - Suffix.Length)
                select filtered
            ).ToArray();
        }

        private string FixupName (string name) {
            return name.Replace('/', '\\');
        }

        public void DisposeStream (Stream stream) {
        }

        public bool TryGetStream (string name, bool optional, out Stream result, out Exception exception, bool exactName = false) {
            var streamName = FixupName(exactName ? name : (Prefix ?? "") + name + Suffix);
            exception = null;
            var entry = Archive.GetEntry(streamName);
            result = entry?.Open();
            if (result == null) {
                if (optional) {
                    return false;
                } else {
                    exception = new FileNotFoundException($"No file with this name found: {streamName}", name);
                    return false;
                }
            } else {
                return true;
            }
        }
    }

    public class FallbackStreamProvider : IResourceProviderStreamSource {
        public IResourceProviderStreamSource[] Sources { get; protected set; }

        public FallbackStreamProvider (params IResourceProviderStreamSource[] sources) {
            Sources = sources;
        }

        public string[] GetNames (bool asFullPaths) {
            var result = new List<string>();
            foreach (var source in Sources)
                result.AddRange(source.GetNames(asFullPaths));
            return result.ToArray();
        }

        public void DisposeStream (Stream stream) {
            foreach (var provider in Sources)
                provider.DisposeStream(stream);
        }

        public bool TryGetStream (string name, bool optional, out Stream result, out Exception error, bool exactName = false) {
            result = null;
            var errors = new DenseList<Exception>();

            foreach (var source in Sources) {
                if (source.TryGetStream(name, optional, out result, out Exception temp, exactName)) {
                    error = null;
                    return true;
                }

                if (temp != null)
                    errors.Add(temp);
            }

            if (errors.Count > 1) {
                // Reverse the list of errors since the last fallback is usually the most important
                error = new AggregateException(errors.Reverse().ToArray());
            } else if (errors.Count == 1) {
                error = errors[0];
            } else {
                error = new FileNotFoundException("The file could not be located in any of the stream sources", name);
            }
            return false;
        }
    }
}
