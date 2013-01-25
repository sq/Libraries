#pragma warning disable 0420 // a reference to a volatile field will not be treated as volatile

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Squared.Util;
using System.Threading;
using Microsoft.Xna.Framework.Graphics;
using Squared.Game;
using Microsoft.Xna.Framework;
using Squared.Render.Internal;

namespace Squared.Render {
    public static class RenderExtensionMethods {
        public static int ComputePrimitiveCount (this PrimitiveType primitiveType, int vertexCount) {
            switch (primitiveType) {
                case PrimitiveType.LineStrip:
                    return vertexCount - 1;
                case PrimitiveType.LineList:
                    return vertexCount / 2;
                case PrimitiveType.TriangleStrip:
                    return vertexCount - 2;
                case PrimitiveType.TriangleList:
                    return vertexCount / 3;
                default:
                    throw new ArgumentException();
            }
        }

        public static int ComputeVertexCount (this PrimitiveType primitiveType, int primitiveCount) {
            switch (primitiveType) {
                case PrimitiveType.LineStrip:
                    return primitiveCount + 1;
                case PrimitiveType.LineList:
                    return primitiveCount * 2;
                case PrimitiveType.TriangleStrip:
                    return primitiveCount + 2;
                case PrimitiveType.TriangleList:
                    return primitiveCount * 3;
                default:
                    throw new ArgumentException();
            }
        }
    }

    public interface IBatchContainer {
        void Add (Batch batch);
        RenderManager RenderManager { get; }
    }

    public sealed class DeviceManager {
        public struct ActiveMaterial : IDisposable {
            public readonly DeviceManager DeviceManager;
            public readonly Material Material;

            public ActiveMaterial (DeviceManager deviceManager, Material material) {
                DeviceManager = deviceManager;
                Material = material;

                if (deviceManager.CurrentMaterial != material) {
                    if (deviceManager.CurrentMaterial != null)
                        deviceManager.CurrentMaterial.End(deviceManager);

                    deviceManager.CurrentMaterial = material;

                    material.Begin(deviceManager);
                }
            }

            public void Dispose () {
            }
        }

        private readonly Stack<RenderTargetBinding[]> RenderTargetStack = new Stack<RenderTargetBinding[]>();

        public readonly GraphicsDevice Device;
        private Effect ParameterEffect;
        private Material CurrentMaterial;
        internal Effect CurrentEffect;

        public DeviceManager (GraphicsDevice device) {
            Device = device;
            CurrentEffect = null;
        }

        public void SetParameterEffect (Effect effect) {
            ParameterEffect = effect;
        }

        public void PushRenderTarget (RenderTarget2D newRenderTarget) {
            RenderTargetStack.Push(Device.GetRenderTargets());
            Device.SetRenderTarget(newRenderTarget);
        }

        public void PopRenderTarget () {
            Device.SetRenderTargets(RenderTargetStack.Pop());
        }

        public EffectParameterCollection SharedParameters {
            get {
                return ParameterEffect.Parameters;
            }
        }

        public EffectParameterCollection CurrentParameters {
            get {
                return CurrentEffect.Parameters;
            }
        }

        public ActiveMaterial ApplyMaterial (Material material) {
            return new ActiveMaterial(this, material);
        }

        public void Finish () {
            if (CurrentMaterial != null) {
                CurrentMaterial.End(this);
                CurrentMaterial = null;
            }

            CurrentEffect = null;

            for (var i = 0; i < 4; i++) {
                Device.Textures[i] = null;
            }

            Device.SetRenderTargets();
            Device.SetVertexBuffers();
        }
    }

    // Thread-safe
    public sealed class RenderManager {
        class WorkerThreadInfo {
            public volatile int Start, Count;
            public volatile Frame Frame;
        }

        public readonly DeviceManager DeviceManager;

        private int _FrameCount = 0;
        private Dictionary<Type, IArrayPoolAllocator> _ArrayAllocators = 
            new Dictionary<Type, IArrayPoolAllocator>(new ReferenceComparer<Type>());
        private Dictionary<Type, IBatchPool> _BatchAllocators =
            new Dictionary<Type, IBatchPool>(new ReferenceComparer<Type>());
        private FramePool _FrameAllocator;

        private WorkerThreadInfo[] _WorkerInfo;
#if XBOX
        private WorkerThread[] _WorkerThreads;
#else
        private Action[] _WorkerDelegates;
#endif

        public RenderManager (GraphicsDevice device) {
            DeviceManager = new DeviceManager(device);
            _FrameAllocator = new FramePool(this);

            int threadCount = Math.Max(2, Math.Min(8, Environment.ProcessorCount));
#if XBOX
            // XNA reserves two hardware threads for its own purposes
            threadCount -= 2;
#endif

            _WorkerInfo = new WorkerThreadInfo[threadCount];
            for (int i = 0; i < threadCount; i++)
                _WorkerInfo[i] = new WorkerThreadInfo();

#if XBOX
            _WorkerThreads = new WorkerThread[threadCount];

            for (int i = 0; i < _WorkerThreads.Length; i++) {
                _WorkerThreads[i] = new WorkerThread(WorkerThreadFunc, i);
                _WorkerThreads[i].Tag = _WorkerInfo[i];
            }
#else
            _WorkerDelegates = new Action[threadCount];

            for (int i = 0; i < threadCount; i++) {
                // Make a copy so that each delegate gets a different value
                int j = i;
                _WorkerDelegates[i] = () =>
                    WorkerThreadFunc(_WorkerInfo[j]);
            }
#endif
        }

#if XBOX
        private void WorkerThreadFunc (WorkerThread thread) {
            var info = thread.Tag as WorkerThreadInfo;
#else
        private void WorkerThreadFunc (WorkerThreadInfo info) {
#endif
            Frame frame;
            int start, count;
            lock (info) {
                frame = info.Frame;
                start = info.Start;
                count = info.Count;
            }

            try {
                frame.PrepareSubset(start, count);
            } finally {
                lock (info) {
                    info.Frame = null;
                    info.Start = info.Count = 0;
                }
            }
        }

        internal void ParallelPrepare (Frame frame) {
            int batchCount = frame.Batches.Count;
            int chunkSize = (int)Math.Ceiling((float)batchCount / _WorkerInfo.Length);

            for (int i = 0, j = 0; i < _WorkerInfo.Length; i++, j += chunkSize) {
                var info = _WorkerInfo[i];

                lock (info) {
                    if (info.Frame != null)
                        throw new InvalidOperationException("A render is already in progress");

                    info.Frame = frame;
                    info.Start = j;
                    info.Count = Math.Min(chunkSize, batchCount - j);
                }

#if XBOX
                _WorkerThreads[i].RequestWork();
#endif
            }

#if XBOX
            for (int i = 0; i < _WorkerThreads.Length; i++) {
                var wt = _WorkerThreads[i];

                wt.WaitForPendingWork();

                var info = wt.Tag as WorkerThreadInfo;
                lock (info) {
                    if (info.Frame != null)
                        throw new InvalidOperationException();
                }
            }
#else
            System.Threading.Tasks.Parallel.Invoke(
                new System.Threading.Tasks.ParallelOptions {
                    MaxDegreeOfParallelism = _WorkerInfo.Length
                },
                _WorkerDelegates
            );
#endif
        }

        internal int PickFrameIndex () {
            return Interlocked.Increment(ref _FrameCount);
        }

        internal void ReleaseFrame (Frame frame) {
            _FrameAllocator.Release(frame);
        }

        public Frame CreateFrame () {
            CollectAllocators();

            return _FrameAllocator.Allocate();
        }

        public void CollectAllocators () {
            lock (_ArrayAllocators)
                foreach (var allocator in _ArrayAllocators.Values)
                    allocator.Step();
        }

        public ArrayPoolAllocator<T> GetArrayAllocator<T> () {
            var t = typeof(T);
            IArrayPoolAllocator o;
            lock (_ArrayAllocators)
                _ArrayAllocators.TryGetValue(t, out o);

            ArrayPoolAllocator<T> result;
            if (o == null) {
                result = new ArrayPoolAllocator<T>();
                lock (_ArrayAllocators) {
                    if (!_ArrayAllocators.TryGetValue(t, out o))
                        _ArrayAllocators[t] = result;
                    else
                        result = (ArrayPoolAllocator<T>)o;
                }
            } else {
                result = (ArrayPoolAllocator<T>)o;
            }

            return result;
        }

        public T AllocateBatch<T> () 
            where T : Batch, new() {

            var t = typeof(T);
            IBatchPool p;
            lock (_BatchAllocators)
                _BatchAllocators.TryGetValue(t, out p);

            BatchPool<T> allocator;
            if (p == null) {
                var constructor = t.GetConstructor(new Type[0]);
                Func<IBatchPool, T> constructFn = (pool) => {
                    var result = constructor.Invoke(null) as T;
                    result.Pool = pool;
                    return result;
                };

                allocator = new BatchPool<T>(constructFn);

                lock (_BatchAllocators) {
                    if (!_BatchAllocators.TryGetValue(t, out p))
                        _BatchAllocators[t] = allocator;
                    else
                        allocator = (BatchPool<T>)p;
                }
            } else {
                allocator = (BatchPool<T>)p;
            }

            return allocator.Allocate();
        }
    }

    public sealed class BatchComparer : IComparer<Batch> {
        public int Compare (Batch x, Batch y) {
            int result = x.Layer - y.Layer;
            if (result == 0) {
                int mx = 0, my = 0;

                if (x.Material != null)
                    mx = x.Material.MaterialID;
                if (y.Material != null)
                    my = y.Material.MaterialID;

                result = mx - my;
            }

            return result;
        }
    }

    // Thread-safe
    public sealed class Frame : IDisposable, IBatchContainer {
        private const int State_Initialized = 0;
        private const int State_Preparing = 1;
        private const int State_Prepared = 2;
        private const int State_Drawing = 3;
        private const int State_Drawn = 4;
        private const int State_Disposed = 5;

        public static BatchComparer BatchComparer = new BatchComparer();

        private static ListPool<Batch> _ListPool = new ListPool<Batch>(
            8, 256, 4096
        );

        public RenderManager RenderManager;
        public int Index;

        public List<Batch> Batches;

        volatile int State = State_Disposed;

        // If you allocate a frame manually instead of using the pool, you'll need to initialize it
        //  yourself using Initialize (below).
        public Frame () {
        }

        RenderManager IBatchContainer.RenderManager {
            get {
                return RenderManager;
            }
        }

        internal void Initialize (RenderManager renderManager, int index) {
            Batches = _ListPool.Allocate();
            RenderManager = renderManager;
            Index = index;
            State = State_Initialized;
        }

        public void Add (Batch batch) {
            if (State != State_Initialized)
                throw new InvalidOperationException();

            lock (Batches)
                Batches.Add(batch);
        }

        public void Prepare (bool parallel) {
            if (Interlocked.Exchange(ref State, State_Preparing) != State_Initialized)
                throw new InvalidOperationException();

            Batches.Sort(BatchComparer);

            if (parallel)
                RenderManager.ParallelPrepare(this);
            else
                PrepareSubset(0, Batches.Count);

            if (Interlocked.Exchange(ref State, State_Prepared) != State_Preparing)
                throw new InvalidOperationException();
        }

        internal void PrepareSubset (int start, int count) {
            int end = start + count;

            for (int i = start; i < end; i++)
                Batches[i].Prepare();
        }

        public void Draw () {
            if (Interlocked.Exchange(ref State, State_Drawing) != State_Prepared)
                throw new InvalidOperationException();

            var dm = RenderManager.DeviceManager;
            var device = dm.Device;

            int c = Batches.Count;
            for (int i = 0; i < c; i++)
                Batches[i].IssueAndWrapExceptions(dm);

            dm.Finish();

            if (Interlocked.Exchange(ref State, State_Drawn) != State_Drawing)
                throw new InvalidOperationException();
        }

        public void Dispose () {
            if (State == State_Disposed)
                return;

            for (int i = 0; i < Batches.Count; i++)
                Batches[i].ReleaseResources();

            _ListPool.Release(ref Batches);

            RenderManager.ReleaseFrame(this);

            State = State_Disposed;
        }
    }

    public abstract class Batch : IDisposable {
        public static bool CaptureStackTraces = false;

        public StackTrace StackTrace;
        public IBatchContainer Container;
        public int Layer;
        public Material Material;

        internal int Index;
        internal bool ReleaseAfterDraw;
        internal bool Released;
        internal IBatchPool Pool;

        protected static int _BatchCount = 0;

        protected void Initialize (IBatchContainer container, int layer, Material material) {
            if ((material != null) && (material.IsDisposed))
                throw new ObjectDisposedException("material");

            StackTrace = null;
            Released = false;
            ReleaseAfterDraw = false;
            Container = container;
            Layer = layer;
            Material = material;

            Index = Interlocked.Increment(ref _BatchCount);

            container.Add(this);
        }

        /// <summary>
        /// Adds a previously-constructed batch to a new frame/container.
        /// Use this if you already created a batch in a previous frame and wish to use it again.
        /// </summary>
        public void Reuse (IBatchContainer newContainer, int? newLayer = null) {
            if (Released)
                throw new ObjectDisposedException("batch");

            Container = newContainer;

            if (newLayer.HasValue)
                Layer = newLayer.Value;
        }

        public void CaptureStack (int extraFramesToSkip) {
            if (CaptureStackTraces)
                StackTrace = new StackTrace(2 + extraFramesToSkip, true);
        }

        // This is where you should do any computation necessary to prepare a batch for rendering.
        // Examples: State sorting, computing vertices.
        public abstract void Prepare ();

        // This is where you send commands to the video card to render your batch.
        public abstract void Issue (DeviceManager manager);

        protected virtual void OnReleaseResources () {
            Released = true;

            if (Pool != null) {
                Pool.Release(this);
                Pool = null;
            }

            StackTrace = null;
            Container = null;
            Material = null;
            Index = -1;
        }

        public void ReleaseResources () {
            if (Released)
                throw new ObjectDisposedException("Batch");

            if (!ReleaseAfterDraw)
                return;

            OnReleaseResources();
        }

        /// <summary>
        /// Notifies the render manager that it should release the batch once the current frame is done drawing.
        /// You may opt to avoid calling this method in order to reuse a batch across multiple frames.
        /// </summary>
        public virtual void Dispose () {
            ReleaseAfterDraw = true;
        }

        public bool IsReusable {
            get {
                return !ReleaseAfterDraw;
            }
        }

        public bool IsReleased {
            get {
                return Released;
            }
        }
    }

    public interface IBatchPool {
        void Release (Batch batch);
    }

    public class BatchPool<T> : BaseObjectPool<T>, IBatchPool
        where T : Batch, new() {
        private UnorderedList<T> _Pool = new UnorderedList<T>();

        public readonly Func<IBatchPool, T> Allocator;

        public BatchPool (Func<IBatchPool, T> allocator) 
            : this (512) {
            Allocator = allocator;
        }

        public BatchPool (int poolCapacity)
            : base (poolCapacity) {
        }

        protected override T AllocateNew() {
            return Allocator(this);
        }

        void IBatchPool.Release (Batch batch) {
            Release((T)batch);
        }
    }

    public class FramePool : BaseObjectPool<Frame> {
        public readonly RenderManager RenderManager;

        public FramePool (RenderManager renderManager) {
            RenderManager = renderManager;
        }

        public override Frame Allocate () {
            var result = base.Allocate();
            result.Initialize(RenderManager, RenderManager.PickFrameIndex());
            return result;
        }

        protected override Frame AllocateNew() {
 	        return new Frame();
        }
    }

    public abstract class BaseObjectPool<T> 
        where T : class, new() {

        private UnorderedList<T> _Pool = new UnorderedList<T>();

        public readonly int PoolCapacity;

        public BaseObjectPool () 
            : this (512) {
        }

        public BaseObjectPool (int poolCapacity) {
            PoolCapacity = poolCapacity;
        }

        public virtual T Allocate () {
            T result = null;

            lock (_Pool)
                _Pool.TryPopFront(out result);

            if (result == null)
                result = AllocateNew();

            return result;
        }

        protected abstract T AllocateNew ();

        public virtual void Release (T obj) {
            lock (_Pool) {
                if (_Pool.Count > PoolCapacity)
                    return;

                _Pool.Add(obj);
            }
        }
    }

    public class ListPool<T> {
        private UnorderedList<List<T>> _Pool = new UnorderedList<List<T>>();

        public readonly int PoolCapacity;
        public readonly int InitialItemCapacity;
        public readonly int MaxItemCapacity;

        public ListPool (int poolCapacity, int initialItemCapacity, int maxItemCapacity) {
            PoolCapacity = poolCapacity;
            InitialItemCapacity = initialItemCapacity;
            MaxItemCapacity = maxItemCapacity;
        }

        public List<T> Allocate () {
            List<T> result = null;

            lock (_Pool)
                _Pool.TryPopFront(out result);

            if (result == null)
                result = new List<T>(InitialItemCapacity);

            return result;
        }

        public void Release (ref List<T> _list) {
            var list = _list;
            _list = null;

            if (list == null)
                return;

            list.Clear();

            if (list.Capacity > MaxItemCapacity)
                return;

            lock (_Pool) {
                if (_Pool.Count > PoolCapacity)
                    return;

                _Pool.Add(list);
            }
        }
    }

    public abstract class ListBatch<T> : Batch {
        protected List<T> _DrawCalls;

        private static ListPool<T> _ListPool = new ListPool<T>(
            256, 128, 1024
        );

        new protected void Initialize (IBatchContainer container, int layer, Material material) {
            base.Initialize(container, layer, material);

            _DrawCalls = _ListPool.Allocate();
        }

        protected void Add (ref T item) {
            _DrawCalls.Add(item);
        }

        protected override void OnReleaseResources() {
            _ListPool.Release(ref _DrawCalls);

            base.OnReleaseResources();
        }
    }

    public class ClearBatch : Batch {
        public Color? ClearColor;
        public float? ClearZ;
        public int? ClearStencil;

        public void Initialize (IBatchContainer container, int layer, Material material, Color? clearColor, float? clearZ, int? clearStencil) {
            base.Initialize(container, layer, material);
            ClearColor = clearColor;
            ClearZ = clearZ;
            ClearStencil = clearStencil;
        }

        public override void Prepare () {
        }

        public override void Issue (DeviceManager manager) {
            using (manager.ApplyMaterial(Material)) {
                var clearOptions = default(ClearOptions);

                if (ClearColor.HasValue)
                    clearOptions |= ClearOptions.Target;
                if (ClearZ.HasValue)
                    clearOptions |= ClearOptions.DepthBuffer;
                if (ClearStencil.HasValue)
                    clearOptions |= ClearOptions.Stencil;

                manager.Device.Clear(
                    clearOptions,
                    ClearColor.GetValueOrDefault(Color.Black), ClearZ.GetValueOrDefault(0), ClearStencil.GetValueOrDefault(0)
                );
            }
        }

        public static void AddNew (IBatchContainer container, int layer, Material material, Color? clearColor = null, float? clearZ = null, int? clearStencil = null) {
            if (container == null)
                throw new ArgumentNullException("container");

            var result = container.RenderManager.AllocateBatch<ClearBatch>();
            result.Initialize(container, layer, material, clearColor, clearZ, clearStencil);
            result.CaptureStack(0);
            result.Dispose();
        }
    }

    public class SetRenderTargetBatch : Batch {
        public RenderTarget2D RenderTarget;

        public void Initialize (IBatchContainer container, int layer, RenderTarget2D renderTarget) {
            base.Initialize(container, layer, null);
            RenderTarget = renderTarget;
        }

        public override void Prepare () {
        }

        public override void Issue (DeviceManager manager) {
            manager.Device.SetRenderTarget(RenderTarget);
        }

        [Obsolete("Use BatchGroup.ForRenderTarget instead.")]
        public static void AddNew (IBatchContainer container, int layer, RenderTarget2D renderTarget) {
            if (container == null)
                throw new ArgumentNullException("frame");

            var result = container.RenderManager.AllocateBatch<SetRenderTargetBatch>();
            result.Initialize(container, layer, renderTarget);
            result.CaptureStack(0);
            result.Dispose();
        }
    }

    public interface IPoolAllocator {
        void Collect ();
    }

    public interface IArrayPoolAllocator {
        void Step ();
    }

    // Thread-safe
    public class ArrayPoolAllocator<T> : IArrayPoolAllocator {
        public struct Allocation {
            public readonly int Origin;
            public readonly T[] Buffer;

            internal Allocation (int origin, T[] buffer) {
                Origin = origin;
                Buffer = buffer;
            }
        }

        public class Pool : UnorderedList<T[]> {
            public UnorderedList<Allocation> LiveAllocations = new UnorderedList<Allocation>(64);
            public readonly int AllocationSize;

            public Pool (int allocationSize, int capacity) 
                : base (capacity) {
                AllocationSize = allocationSize;
            }
        }

        public const int MinPower = 6;
        public const int MaxPower = 20;
        public const int PowerCount = (MaxPower - MinPower) + 1;
        public const int MinSize = 1 << MinPower;
        public const int MaxSize = 1 << MaxPower;
        public const int CollectionAge = 2;

        public const int DefaultPoolCapacity = 32;
        public const int MaxPoolCapacity = 512;

        private int StepIndex = 0;
        private Pool[] Pools = new Pool[PowerCount];

        public ArrayPoolAllocator () {
            for (int power = MinPower; power <= MaxPower; power++)
                Pools[power - MinPower] = new Pool(1 << power, DefaultPoolCapacity);
        }

        private static int IntLog2 (int x) {
            int l = 0;

            if (x >= 1 << 16) {
                x >>= 16;
                l |= 16;
            }
            if (x >= 1 << 8) { 
                x >>= 8; 
                l |= 8; 
            }
            if (x >= 1 << 4) { 
                x >>= 4; 
                l |= 4; 
            }
            if (x >= 1 << 2) { 
                x >>= 2; 
                l |= 2; 
            }
            if (x >= 1 << 1)
                l |= 1;
            return l;
        }

        private int SelectPool (int capacity) {
            int log2 = IntLog2(capacity) - MinPower + 1;
            if (log2 < 0)
                log2 = 0;

            for (int power = log2; power < PowerCount; power++) {
                var poolSize = Pools[power].AllocationSize;
                if (poolSize > capacity)
                    return power;
            }

            throw new InvalidOperationException("Allocation size out of range");
        }

        public Allocation Allocate (int capacity) {
            int poolId = SelectPool(capacity);
            var pool = Pools[poolId];

            T[] result;
            lock (pool)
                pool.TryPopFront(out result);

            if (result == null)
                result = new T[pool.AllocationSize];

            var a = new Allocation(StepIndex, result);
            lock (pool)
                pool.LiveAllocations.Add(a);
            return a;
        }

        public void Step () {
            int expirationThreshold = StepIndex - CollectionAge;
            StepIndex += 1;

            for (int power = 0; power < PowerCount; power++) {
                var pool = Pools[power];
                lock (pool) {

                    using (var e = pool.LiveAllocations.GetEnumerator())
                    while (e.MoveNext()) {
                        var a = e.Current;

                        if (a.Origin <= expirationThreshold) {
                            pool.Add(a.Buffer);
                            e.RemoveCurrent();
                        }
                    }
                }
            }
        }
    }

    public class BatchGroup : ListBatch<Batch>, IBatchContainer {
        Action<DeviceManager, object> _Before, _After;
        private object _UserData;

        public override void Prepare () {
            _DrawCalls.Sort(Frame.BatchComparer);

            foreach (var batch in _DrawCalls)
                batch.Prepare();
        }

        public override void Issue (DeviceManager manager) {
            if (_Before != null)
                _Before(manager, _UserData);

            try {
                foreach (var batch in _DrawCalls)
                    batch.IssueAndWrapExceptions(manager);
            } finally {
                if (_After != null)
                    _After(manager, _UserData);
            }
        }

        private void SetRenderTargetCallback (DeviceManager dm, object userData) {
            dm.PushRenderTarget((RenderTarget2D)userData);
        }

        private void RestoreRenderTargetCallback (DeviceManager dm, object userData) {
            dm.PopRenderTarget();
        }

        public static BatchGroup ForRenderTarget (IBatchContainer container, int layer, RenderTarget2D renderTarget) {
            if (container == null)
                throw new ArgumentNullException("container");

            var result = container.RenderManager.AllocateBatch<BatchGroup>();
            result.Initialize(container, layer, result.SetRenderTargetCallback, result.RestoreRenderTargetCallback, renderTarget);
            result.CaptureStack(0);

            return result;
        }

        public static BatchGroup New (IBatchContainer container, int layer, Action<DeviceManager, object> before = null, Action<DeviceManager, object> after = null, object userData = null) {
            if (container == null)
                throw new ArgumentNullException("container");

            var result = container.RenderManager.AllocateBatch<BatchGroup>();
            result.Initialize(container, layer, before, after, userData);
            result.CaptureStack(0);

            return result;
        }

        public void Initialize (IBatchContainer container, int layer, Action<DeviceManager, object> before, Action<DeviceManager, object> after, object userData) {
            base.Initialize(container, layer, null);

            _Before = before;
            _After = after;
            _UserData = userData;
        }

        RenderManager IBatchContainer.RenderManager {
            get {
                return Container.RenderManager;
            }
        }

        public void Add (Batch batch) {
            _DrawCalls.Add(batch);
        }

        protected override void OnReleaseResources () {
            foreach (var batch in _DrawCalls)
                batch.ReleaseResources();

            _DrawCalls.Clear();

            base.OnReleaseResources();
        }
    }

    public class BatchIssueFailedException : Exception {
        public readonly Batch Batch;

        public BatchIssueFailedException (Batch batch, Exception innerException) 
            : base (FormatMessage(batch, innerException), innerException) {

            Batch = batch;
        }

        public static string FormatMessage (Batch batch, Exception innerException) {
            if (batch.StackTrace != null)
                return String.Format(
                    "Failed to issue batch of type '{0}'. Stack trace at time of batch creation:{1}{2}", 
                    batch.GetType().Name,
                    Environment.NewLine, 
                    batch.StackTrace
                );
            else
                return String.Format(
                    "Failed to issue batch of type '{0}'. Set Batch.CaptureStackTraces to true to enable stack trace captures.",
                    batch.GetType().Name
                );
        }
    }

    public static class BatchExtensions {
        public static void IssueAndWrapExceptions (this Batch batch, DeviceManager manager) {
            try {
                batch.Issue(manager);
            } catch (Exception exc) {
                throw new BatchIssueFailedException(batch, exc);
            }
        }
    }
}
