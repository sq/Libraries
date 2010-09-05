#pragma warning disable 0420 // a reference to a volatile field will not be treated as volatile

using System;
using System.Collections.Generic;
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
                case PrimitiveType.PointList:
                    return vertexCount;
                case PrimitiveType.TriangleFan:
                case PrimitiveType.TriangleStrip:
                    return vertexCount - 2;
                case PrimitiveType.TriangleList:
                    return vertexCount / 3;
                default:
                    throw new ArgumentException();
            }
        }
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

        public void CommitChanges () {
            CurrentEffect.CommitChanges();
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
        }
    }

    // Thread-safe
    public sealed class RenderManager {
        class WorkerThreadInfo {
            public volatile int Start, Step;
            public volatile Frame Frame;
        }

        public readonly DeviceManager DeviceManager;

        private int _FrameCount = 0;
        private Dictionary<Type, IArrayPoolAllocator> _ArrayAllocators = 
            new Dictionary<Type, IArrayPoolAllocator>(new ReferenceComparer<Type>());
        private Dictionary<Type, IBatchPool> _BatchAllocators =
            new Dictionary<Type, IBatchPool>(new ReferenceComparer<Type>());
        private FramePool _FrameAllocator;

        private WorkerThread[] _WorkerThreads;

        public RenderManager (GraphicsDevice device) {
            DeviceManager = new DeviceManager(device);
            _FrameAllocator = new FramePool(this);

            _WorkerThreads = new WorkerThread[2];

            for (int i = 0; i < _WorkerThreads.Length; i++) {
                _WorkerThreads[i] = new WorkerThread(WorkerThreadFunc, 5 - i);
                _WorkerThreads[i].Tag = new WorkerThreadInfo { 
                    Start = i, 
                    Step = _WorkerThreads.Length, 
                    Frame = null 
                };
            }
        }

        private void WorkerThreadFunc (WorkerThread thread) {
            var info = thread.Tag as WorkerThreadInfo;
            Frame frame;
            int start, step;
            lock (info) {
                frame = info.Frame;
                start = info.Start;
                step = info.Step;
            }

            frame.PrepareSubset(start, step);

            lock (info) {
                info.Frame = null;
            }
        }

        internal void ParallelPrepare (Frame frame) {
            for (int i = 0; i < _WorkerThreads.Length; i++) {
                var wt = _WorkerThreads[i];

                var info = wt.Tag as WorkerThreadInfo;
                lock (info) {
                    if (info.Frame != null)
                        throw new InvalidOperationException();
                    info.Frame = frame;
                }

                wt.RequestWork();
            }

            for (int i = 0; i < _WorkerThreads.Length; i++) {
                var wt = _WorkerThreads[i];

                wt.WaitForPendingWork();

                var info = wt.Tag as WorkerThreadInfo;
                lock (info) {
                    if (info.Frame != null)
                        throw new InvalidOperationException();
                }
            }
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
            if (result == 0) {
                result = x.Index - y.Index;
            }

            return result;
        }
    }

    // Thread-safe
    public sealed class Frame : IDisposable {
        public static BatchComparer BatchComparer = new BatchComparer();

        private static ListPool<Batch> _ListPool = new ListPool<Batch>(
            8, 256, 4096
        );

        public static bool UseThreadedPrepare = true;

        public RenderManager RenderManager;
        public int Index;

        public List<Batch> Batches;

        volatile int _Prepared;

        public Frame () {
        }

        internal void Initialize (RenderManager renderManager, int index) {
            Batches = _ListPool.Allocate();
            RenderManager = renderManager;
            Index = index;
            _Prepared = 0;
        }

        public void Add (Batch batch) {
            if (_Prepared == 1)
                throw new InvalidOperationException();

            lock (Batches)
                Batches.Add(batch);
        }

        public void Prepare () {
            if (Interlocked.Exchange(ref _Prepared, 1) != 0)
                return;

            Batches.Sort(BatchComparer);

            if (UseThreadedPrepare)
                RenderManager.ParallelPrepare(this);
            else
                PrepareSubset(0, 1);

            if (Interlocked.Exchange(ref _Prepared, 2) != 1)
                throw new InvalidOperationException();
        }

        internal void PrepareSubset (int start, int step) {
            int c = Batches.Count;

            for (int i = start; i < c; i += step)
                Batches[i].Prepare();
        }

        public void Draw () {
            if (_Prepared != 2)
                throw new InvalidOperationException();

            var dm = RenderManager.DeviceManager;
            var device = dm.Device;

            int c = Batches.Count;
            for (int i = 0; i < c; i++)
                Batches[i].Issue(dm);

            dm.Finish();
        }

        public void Dispose () {
            for (int i = 0; i < Batches.Count; i++)
                Batches[i].ReleaseResources();

            _ListPool.Release(ref Batches);

            RenderManager.ReleaseFrame(this);

            _Prepared = 0;
        }
    }

    public abstract class Batch : IDisposable {
        internal Frame Frame;
        internal int Layer;
        internal Material Material;
        internal int Index;
        internal bool Released;
        internal IBatchPool Pool;

        protected static int _BatchCount = 0;

        public virtual void Initialize (Frame frame, int layer, Material material) {
            if ((material != null) && (material.IsDisposed))
                throw new ObjectDisposedException("material");

            Released = false;
            Frame = frame;
            Layer = layer;
            Material = material;

            Index = Interlocked.Increment(ref _BatchCount);
        }

        public abstract void Prepare ();
        public abstract void Issue (DeviceManager manager);
        public virtual void ReleaseResources () {
            if (Released)
                throw new ObjectDisposedException("Batch");

            Released = true;
            if (Pool != null)
                Pool.Release(this);
        }

        public void Dispose () {
            Frame.Add(this);
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

        public override void Initialize (Frame frame, int layer, Material material) {
            base.Initialize(frame, layer, material);

            _DrawCalls = _ListPool.Allocate();
        }

        public void Add (T item) {
            this.Add(ref item);
        }

        public virtual void Add (ref T item) {
            _DrawCalls.Add(item);
        }

        public override void ReleaseResources() {
            base.ReleaseResources();

            _ListPool.Release(ref _DrawCalls);
        }
    }

    public class ClearBatch : Batch {
        public Color ClearColor;

        public void Initialize (Frame frame, int layer, Color clearColor, Material material) {
            base.Initialize(frame, layer, material);
            ClearColor = clearColor;
        }

        public override void Prepare () {
        }

        public override void Issue (DeviceManager manager) {
            using (manager.ApplyMaterial(Material)) {
                var clearOptions = ClearOptions.Target;
                if (manager.Device.DepthStencilBuffer != null) {
                    clearOptions |= ClearOptions.DepthBuffer;
                    switch (manager.Device.DepthStencilBuffer.Format) {
                        case DepthFormat.Depth15Stencil1:
                        case DepthFormat.Depth24Stencil4:
                        case DepthFormat.Depth24Stencil8:
                        case DepthFormat.Depth24Stencil8Single:
                            clearOptions |= ClearOptions.Stencil;
                            break;
                    }
                }

                manager.Device.Clear(
                    clearOptions,
                    ClearColor, 0.0f, 0
                );
            }
        }

        public static void AddNew (Frame frame, int layer, Color clearColor, Material material) {
            if (frame == null)
                throw new ArgumentNullException("frame");

            var result = frame.RenderManager.AllocateBatch<ClearBatch>();
            result.Initialize(frame, layer, clearColor, material);
            result.Dispose();
        }
    }

    public class SetRenderTargetBatch : Batch {
        public int RenderTargetIndex;
        public RenderTarget2D RenderTarget;
        public DepthStencilBuffer DepthStencilBuffer;

        public void Initialize (Frame frame, int layer, int renderTargetIndex, RenderTarget2D renderTarget, DepthStencilBuffer depthStencilBuffer, Material material) {
            base.Initialize(frame, layer, material);
            RenderTargetIndex = renderTargetIndex;
            RenderTarget = renderTarget;
            DepthStencilBuffer = depthStencilBuffer;
        }

        public override void Prepare () {
        }

        public override void Issue (DeviceManager manager) {
            using (manager.ApplyMaterial(Material)) {
                manager.Device.SetRenderTarget(RenderTargetIndex, RenderTarget);
                manager.Device.DepthStencilBuffer = DepthStencilBuffer;
            }
        }

        public static void AddNew (Frame frame, int layer, int renderTargetIndex, RenderTarget2D renderTarget, DepthStencilBuffer depthStencilBuffer, Material material) {
            if (frame == null)
                throw new ArgumentNullException("frame");

            var result = frame.RenderManager.AllocateBatch<SetRenderTargetBatch>();
            result.Initialize(frame, layer, renderTargetIndex, renderTarget, depthStencilBuffer, material);
            result.Dispose();
        }
    }


    public class ResolveBackbufferBatch : Batch {
        public ResolveTexture2D ResolveTexture;

        public void Initialize (Frame frame, int layer, ResolveTexture2D resolveTexture, Material material) {
            base.Initialize(frame, layer, material);
            ResolveTexture = resolveTexture;
        }

        public override void Prepare () {
        }

        public override void Issue (DeviceManager manager) {
            using (manager.ApplyMaterial(Material))
                manager.Device.ResolveBackBuffer(ResolveTexture);
        }

        public static void AddNew (Frame frame, int layer, ResolveTexture2D resolveTexture, Material material) {
            if (frame == null)
                throw new ArgumentNullException("frame");

            var result = frame.RenderManager.AllocateBatch<ResolveBackbufferBatch>();
            result.Initialize(frame, layer, resolveTexture, material);
            result.Dispose();
        }
    }

    public interface IPoolAllocator {
        void Collect ();
    }

    /*
    // Thread-safe
    public class PoolAllocator<T> : IPoolAllocator
        where T : class, new() {

        public struct Allocation {
            public readonly T Object;

            internal Allocation (T obj) {
                Object = obj;
            }
        }

        public const int DefaultPoolCapacity = 128;
        public const int MaxPoolCapacity = 512;

        private UnorderedList<Allocation> LiveAllocations = new UnorderedList<Allocation>(DefaultPoolCapacity);
        private UnorderedList<T> Pool = new UnorderedList<T>(DefaultPoolCapacity);

        public PoolAllocator () {
        }

        public T Allocate () {
            T[] result;
            lock (Pool)
                Pool.TryPopFront(out result);

            if (result == null)
                result = new T();

            var a = new Allocation(poolId, result);
            lock (Pool)
                LiveAllocations.Add(a);
      
            return a;
        }

        public void Collect () {
            lock (Pool) {
                foreach (var a in LiveAllocations)
                    Pool.Add(a.Buffer);

                LiveAllocations.Clear();
            }
        }
    }
     */

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

        private int SelectPool (int capacity) {
            for (int power = 0; power < PowerCount; power++) {
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
}
