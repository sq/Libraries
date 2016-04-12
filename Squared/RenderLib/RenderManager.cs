#pragma warning disable 0420 // a reference to a volatile field will not be treated as volatile

// Uncomment this to make Frame.Add faster in debug builds
#define PARANOID

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
using Squared.Threading;

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
        bool IsDisposed { get; }
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

                    if (material != null) {
                        material.Begin(deviceManager);
                    }
                } else {
                    material.Flush();
                }
            }

            public void Dispose () {
            }
        }

        private readonly Stack<BlendState>            BlendStateStack = new Stack<BlendState>();
        private readonly Stack<DepthStencilState>     DepthStencilStateStack = new Stack<DepthStencilState>();
        private readonly Stack<RasterizerState>       RasterizerStateStack = new Stack<RasterizerState>();
        private readonly Stack<RenderTargetBinding[]> RenderTargetStack = new Stack<RenderTargetBinding[]>();
        private readonly Stack<Viewport>              ViewportStack = new Stack<Viewport>();

        public readonly GraphicsDevice Device;
        public Material CurrentMaterial { get; private set; }

        public DeviceManager (GraphicsDevice device) {
            Device = device;
        }

        public void PushStates () {
            BlendStateStack.Push(Device.BlendState);
            DepthStencilStateStack.Push(Device.DepthStencilState);
            RasterizerStateStack.Push(Device.RasterizerState);
        }

        public void PopStates () {
            Device.RasterizerState = RasterizerStateStack.Pop();
            Device.DepthStencilState = DepthStencilStateStack.Pop();
            Device.BlendState = BlendStateStack.Pop();
        }

        public void PushRenderTarget (RenderTarget2D newRenderTarget) {
            PushStates();

            RenderTargetStack.Push(Device.GetRenderTargets());
            ViewportStack.Push(Device.Viewport);
            Device.SetRenderTarget(newRenderTarget);
            RenderManager.ResetDeviceState(Device);
            Device.Viewport = new Viewport(0, 0, newRenderTarget.Width, newRenderTarget.Height);
        }

        public void PopRenderTarget () {
            PopStates();

            Device.SetRenderTargets(RenderTargetStack.Pop());
            Device.Viewport = ViewportStack.Pop();
        }

        public DefaultMaterialSetEffectParameters CurrentParameters {
            get {
                return CurrentMaterial.Parameters;
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

            RenderManager.ResetDeviceState(Device);

            Device.SetRenderTargets();
            Device.SetVertexBuffer(null);
        }
    }

    // Thread-safe
    public sealed class RenderManager {
        public readonly PrepareManager PrepareManager;
        public readonly ThreadGroup    ThreadGroup;
        public readonly DeviceManager  DeviceManager;
        public readonly Thread         MainThread;

        private int _AllowCreatingNewGenerators = 1;
        private int _FrameCount = 0;
        private readonly Dictionary<Type, int> _PreferredPoolCapacities =
            new Dictionary<Type, int>(new ReferenceComparer<Type>());
        private readonly Dictionary<Type, IArrayPoolAllocator> _ArrayAllocators = 
            new Dictionary<Type, IArrayPoolAllocator>(new ReferenceComparer<Type>());
        private readonly Dictionary<Type, IBatchPool> _BatchAllocators =
            new Dictionary<Type, IBatchPool>(new ReferenceComparer<Type>());
        private readonly Dictionary<Type, IBufferGenerator> _BufferGenerators =
            new Dictionary<Type, IBufferGenerator>(new ReferenceComparer<Type>());
        private readonly List<IDisposable> _PendingDisposes = new List<IDisposable>();
        private FramePool _FrameAllocator;

        private Action<IDisposable> _DisposeResource;

        /// <summary>
        /// You must acquire this lock before applying changes to the device, creating objects, or loading content.
        /// </summary>
        public readonly object CreateResourceLock = new object();
        /// <summary>
        /// You must acquire this lock before rendering or resetting the device.
        /// </summary>
        public readonly object UseResourceLock = new object();

        public RenderManager (GraphicsDevice device, Thread mainThread, ThreadGroup threadGroup) {
            if (mainThread == null)
                throw new ArgumentNullException("mainThread");

            MainThread = mainThread;
            DeviceManager = new DeviceManager(device);
            _FrameAllocator = new FramePool(this);
            ThreadGroup = threadGroup;
            PrepareManager = new PrepareManager(ThreadGroup);

            _DisposeResource = DisposeResource;
        }

        /// <summary>
        /// A device reset can leave a device in an intermediate state that will cause drawing operations to fail later.
        /// We address this by resetting pieces of device state to known-good values at the beginning of every frame,
        ///  and you can call this method to do so manually at any time.
        /// </summary>
        public static void ResetDeviceState (GraphicsDevice device) {
            const int numStages = 8;
            const int numVertexStages = 4;

            for (int i = 0; i < numStages; i++) {
                device.Textures[i] = null;
                device.SamplerStates[i] = SamplerState.PointClamp;
            }

            for (int i = 0; i < numVertexStages; i++) {
                device.VertexTextures[i] = null;
                device.VertexSamplerStates[i] = SamplerState.PointClamp;
            }

            device.BlendState = BlendState.Opaque;
            device.DepthStencilState = DepthStencilState.None;
            device.RasterizerState = RasterizerState.CullNone;
        }

        internal void SynchronousPrepareBatches (Frame frame) {
            PrepareManager.PrepareMany(frame.Batches, false);

            PrepareManager.Wait();
        }

        internal void ParallelPrepareBatches (Frame frame) {
            PrepareManager.PrepareMany(frame.Batches, true);

            PrepareManager.Wait();
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

        public bool TrySetPoolCapacity<T> (int newCapacity)
            where T : Batch {

            var t = typeof(T);
            IBatchPool p;
            lock (_BatchAllocators) {
                _PreferredPoolCapacities[t] = newCapacity;

                if (_BatchAllocators.TryGetValue(t, out p)) {
                    p.SetCapacity(newCapacity);
                    return true;
                }
            }

            return false;
        }

        public T GetBufferGenerator<T> ()
            where T : IBufferGenerator {
            var t = typeof(T);

            IBufferGenerator result = null;
            lock (_BufferGenerators) {
                if (_BufferGenerators.TryGetValue(t, out result))
                    return (T)result;

                if (_AllowCreatingNewGenerators != 1)
                    throw new InvalidOperationException("Cannot create a buffer generator after the flush operation has occurred");

                result = (IBufferGenerator)Activator.CreateInstance(
                    t, DeviceManager.Device, CreateResourceLock, _DisposeResource
                );
                _BufferGenerators.Add(t, result);

                return (T)result;
            }
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
                Func<IBatchPool, T> constructFn = (pool) =>
                    new T() {
                        Pool = pool
                    };

                allocator = new BatchPool<T>(constructFn);

                lock (_BatchAllocators) {
                    if (!_BatchAllocators.TryGetValue(t, out p))
                        _BatchAllocators[t] = allocator;
                    else
                        allocator = (BatchPool<T>)p;

                    int desiredCapacity;
                    if (_PreferredPoolCapacities.TryGetValue(t, out desiredCapacity))
                        allocator.SetCapacity(desiredCapacity);
                }
            } else {
                allocator = (BatchPool<T>)p;
            }

            return allocator.Allocate();
        }

        internal void ResetBufferGenerators () {
            _AllowCreatingNewGenerators = 1;

            lock (_BufferGenerators)
                foreach (var generator in _BufferGenerators.Values)
                    generator.Reset();
        }

        internal void FlushBufferGenerators () {
            _AllowCreatingNewGenerators = 0;

            lock (_BufferGenerators)
                foreach (var generator in _BufferGenerators.Values)
                    generator.Flush();
        }

        internal void FlushPendingDisposes () {
            RenderCoordinator.FlushDisposeList(_PendingDisposes);
        }

        public void DisposeResource (IDisposable resource) {
            if (resource == null)
                return;

            lock (_PendingDisposes)
                _PendingDisposes.Add(resource);
        }
    }

    public sealed class BatchComparer : IComparer<Batch> {
        public int Compare (Batch x, Batch y) {
            if (x == null) {
                if (y == null)
                    return 0;
                else
                    return -1;
            } else if (y == null) {
                return 1;
            }

            int result = x.Layer.CompareTo(y.Layer);
            if (result == 0) {
                int mx = 0, my = 0;

                if (x.Material != null)
                    mx = x.Material.MaterialID;
                if (y.Material != null)
                    my = y.Material.MaterialID;

                result = mx.CompareTo(my);
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

        private static readonly object PrepareLock = new object();

        public static BatchComparer BatchComparer = new BatchComparer();

        private static ListPool<Batch> _ListPool = new ListPool<Batch>(
            16, 0, 256, 4096
        );

        public RenderManager RenderManager;
        public int Index;
        public long InitialBatchCount;

        public UnorderedList<Batch> Batches;

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
            Batches = _ListPool.Allocate(null);
            RenderManager = renderManager;
            Index = index;
            InitialBatchCount = Batch.LifetimeCount;
            State = State_Initialized;
        }

        public void Add (Batch batch) {
            if (State != State_Initialized)
                throw new InvalidOperationException();

            lock (Batches) {
#if DEBUG && PARANOID
                if (Batches.Contains(batch))
                    throw new InvalidOperationException("Batch already added to this frame");
#endif

                Batches.Add(batch);
                batch.Container = this;
            }
        }

        public void Prepare (bool parallel) {
            if (Interlocked.Exchange(ref State, State_Preparing) != State_Initialized)
                throw new InvalidOperationException("Frame was not in initialized state when prepare operation began ");

            if (false) {
                var totalBatches = Batch.LifetimeCount - InitialBatchCount;
                Console.WriteLine("Frame contains {0} batches", totalBatches);
            }

            BatchCombiner.CombineBatches(Batches);

            Batches.Sort(BatchComparer);

            if (!Monitor.TryEnter(PrepareLock, 5000)) {
                throw new InvalidOperationException("Spent more than five seconds waiting for a previous prepare operation.");
            }

            try {
                if (parallel)
                    RenderManager.ParallelPrepareBatches(this);
                else
                    RenderManager.SynchronousPrepareBatches(this);
            } finally {
                Monitor.Exit(PrepareLock);
            }

            if (Interlocked.Exchange(ref State, State_Prepared) != State_Preparing)
                throw new InvalidOperationException("Frame was not in preparing state when prepare operation completed");
        }

        public void Draw () {
            if (Interlocked.Exchange(ref State, State_Drawing) != State_Prepared)
                throw new InvalidOperationException();

            if (Tracing.RenderTrace.EnableTracing)
                Tracing.RenderTrace.ImmediateMarker("Frame {0:0000} : Begin Draw", Index);

            var dm = RenderManager.DeviceManager;
            var device = dm.Device;

            int c = Batches.Count;
            var _batches = Batches.GetBuffer();
            for (int i = 0; i < c; i++) {
                var batch = _batches[i];
                if (batch != null)
                    batch.IssueAndWrapExceptions(dm);
            }

            dm.Finish();

            if (Tracing.RenderTrace.EnableTracing)
                Tracing.RenderTrace.ImmediateMarker("Frame {0:0000} : End Draw", Index);

            if (Interlocked.Exchange(ref State, State_Drawn) != State_Drawing)
                throw new InvalidOperationException();
        }

        public void Dispose () {
            if (State == State_Disposed)
                return;

            var _batches = Batches.GetBuffer();
            for (int i = 0; i < Batches.Count; i++) {
                var batch = _batches[i];
                if (batch != null)
                    batch.ReleaseResources();
            }

            _ListPool.Release(ref Batches);

            RenderManager.ReleaseFrame(this);

            State = State_Disposed;
        }

        public bool IsDisposed {
            get {
                return State == State_Disposed;
            }
        }

        public override int GetHashCode() {
            return Index;
        }
    }

    public class PrepareManager {
        public struct Task : IWorkItem {
            public readonly PrepareManager Manager;

            public IBatch Batch;

            public Task (PrepareManager manager, IBatch batch) {
                Manager = manager;
                Batch = batch;
            }

            public void Execute () {
                Batch.Prepare(Manager);
            }
        };

        public  readonly ThreadGroup     Group;
        private readonly WorkQueue<Task> Queue;

        static readonly ThreadLocal<bool> ForceSync = new ThreadLocal<bool>(() => false);

        public PrepareManager (ThreadGroup threadGroup) {
            Group = threadGroup;
            Queue = threadGroup.GetQueueForType<Task>();
        }

        public WorkQueue<Task>.Marker Mark () {
            return Queue.Mark();
        }

        public void Wait () {
            Group.NotifyQueuesChanged(true);
            Queue.WaitUntilDrained();
        }

        public void PrepareMany<T> (T batches, bool async)
            where T : IEnumerable<IBatch>
        {
            if (ForceSync.Value)
                async = false;

            const int blockSize = 256;
            using (var buffer = BufferPool<Task>.Allocate(blockSize)) {
                int j = 0;

                var task = new Task(this, null);
                foreach (var batch in batches) {
                    if (batch == null)
                        continue;

                    task.Batch = batch;
                    buffer.Data[j++] = task;

                    if (j == blockSize) {
                        Queue.EnqueueMany(new ArraySegment<Task>(buffer.Data, 0, j));
                        j = 0;
                    }
                }

                Queue.EnqueueMany(new ArraySegment<Task>(buffer.Data, 0, j));
            }

            Group.NotifyQueuesChanged();
        }

        public void PrepareAsync (IBatch batch) {
            if (batch == null)
                return;

            if (ForceSync.Value) {
                PrepareSync(batch);
                return;
            }

            Queue.Enqueue(new Task(this, batch));
            // FIXME: Is this too often?
            Group.NotifyQueuesChanged();
        }

        public void PrepareSync (IBatch batch) {
            if (batch == null)
                return;

            var wasSync = ForceSync.Value;
            try {
                ForceSync.Value = true;
                batch.Prepare(this);
            } finally {
                ForceSync.Value = wasSync;
            }
        }
    }

    public interface IBatch : IDisposable {
        void Prepare (PrepareManager manager);
    }

    public abstract class Batch : IBatch {
        private static Dictionary<Type, int> TypeIds = new Dictionary<Type, int>(new ReferenceComparer<Type>());

        public static bool CaptureStackTraces = false;

        public readonly int TypeId;

        public StackTrace StackTrace;
        public IBatchContainer Container;
        public int Layer;
        public Material Material;

        internal long Index;
        internal bool ReleaseAfterDraw;
        internal bool Released;
        internal IBatchPool Pool;
        internal bool IsCombined;

        protected static long _BatchCount = 0;

        protected Batch () {
            var thisType = GetType();
            if (!TypeIds.TryGetValue(thisType, out TypeId))
                TypeIds.Add(thisType, TypeId = TypeIds.Count);
        }

        protected void Initialize (IBatchContainer container, int layer, Material material, bool addToContainer) {
            if ((material != null) && (material.IsDisposed))
                throw new ObjectDisposedException("material");

            StackTrace = null;
            Released = false;
            ReleaseAfterDraw = false;
            Layer = layer;
            Material = material;
            IsCombined = false;

            Index = Interlocked.Increment(ref _BatchCount);

            if (addToContainer)
                container.Add(this);
        }

        /// <summary>
        /// Adds a previously-constructed batch to a new frame/container.
        /// Use this if you already created a batch in a previous frame and wish to use it again.
        /// </summary>
        public void Reuse (IBatchContainer newContainer, int? newLayer = null) {
            if (Released)
                throw new ObjectDisposedException("batch");
            else if (IsCombined)
                throw new InvalidOperationException("Batch was combined into another batch");

            if (newLayer.HasValue)
                Layer = newLayer.Value;

            newContainer.Add(this);
        }

        public void CaptureStack (int extraFramesToSkip) {
            if (CaptureStackTraces)
                StackTrace = new StackTrace(2 + extraFramesToSkip, true);
        }

        // This is where you should do any computation necessary to prepare a batch for rendering.
        // Examples: State sorting, computing vertices.
        public abstract void Prepare (PrepareManager manager);

        // This is where you send commands to the video card to render your batch.
        public virtual void Issue (DeviceManager manager) {
            Container = null;
        }

        protected virtual void OnReleaseResources () {
            if (Released)
                return;

            Released = true;
            Pool.Release(this);

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

        public override int GetHashCode() {
            return (int)Index;
        }

        public static long LifetimeCount {
            get {
                return _BatchCount;
            }
        }

        public override string ToString () {
            return string.Format("{0} #{1} material={2}", GetType().Name, Index, Material);
        }
    }

    public abstract class ListBatch<T> : Batch {
        public const int BatchCapacityLimit = 4096;

        protected UnorderedList<T> _DrawCalls;

        private static ListPool<T> _ListPool = new ListPool<T>(
            2048, 16, 128, BatchCapacityLimit
        );

        new protected void Initialize (
            IBatchContainer container, int layer, Material material,
            bool addToContainer, int? capacity = null
        ) {
            base.Initialize(container, layer, material, addToContainer);

            _DrawCalls = _ListPool.Allocate(capacity);
        }

        protected void Add (T item) {
            _DrawCalls.Add(item);
        }

        protected void Add (ref T item) {
            _DrawCalls.Add(ref item);
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
            base.Initialize(container, layer, material, true);
            ClearColor = clearColor;
            ClearZ = clearZ;
            ClearStencil = clearStencil;

            if (!(clearColor.HasValue || clearZ.HasValue || clearStencil.HasValue))
                throw new ArgumentException("You must specify at least one of clearColor, clearZ and clearStencil.");
        }

        public override void Prepare (PrepareManager manager) {
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

            base.Issue(manager);
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
            base.Initialize(container, layer, null, true);
            RenderTarget = renderTarget;
        }

        public override void Prepare (PrepareManager manager) {
        }

        public override void Issue (DeviceManager manager) {
            manager.Device.SetRenderTarget(RenderTarget);
            RenderManager.ResetDeviceState(manager.Device);

            base.Issue(manager);
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

    public class BatchGroup : ListBatch<Batch>, IBatchContainer {
        private class SetRenderTargetDataPool : BaseObjectPool<SetRenderTargetData> {
            protected override SetRenderTargetData AllocateNew () {
                return new SetRenderTargetData();
            }
        }

        private static readonly SetRenderTargetDataPool _Pool = new SetRenderTargetDataPool();

        private class SetRenderTargetData {
            public RenderTarget2D RenderTarget;
            public Action<DeviceManager, object> Before;
            public Action<DeviceManager, object> After;
            public object UserData;
        }

        Action<DeviceManager, object> _Before, _After;
        private object _UserData;

        public override void Prepare (PrepareManager manager) {
            BatchCombiner.CombineBatches(_DrawCalls);

            _DrawCalls.FastCLRSort(Frame.BatchComparer);

            manager.PrepareMany(_DrawCalls, true);
        }

        public override void Issue (DeviceManager manager) {
            if (_Before != null)
                _Before(manager, _UserData);

            try {
                foreach (var batch in _DrawCalls) {
                    if (batch != null)
                        batch.IssueAndWrapExceptions(manager);
                }
            } finally {
                if (_After != null)
                    _After(manager, _UserData);

                base.Issue(manager);
            }
        }

        private static readonly Action<DeviceManager, object> SetRenderTargetCallback = _SetRenderTargetCallback;

        private static void _SetRenderTargetCallback (DeviceManager dm, object userData) {
            var data = (SetRenderTargetData)userData;
            if (Tracing.RenderTrace.EnableTracing)
                Tracing.RenderTrace.ImmediateMarker("Set   Render Target {0}", data.RenderTarget.GetHashCode());

            dm.PushRenderTarget(data.RenderTarget);
            if (data.Before != null)
                data.Before(dm, data.UserData);
        }

        private static readonly Action<DeviceManager, object> RestoreRenderTargetCallback = _RestoreRenderTargetCallback;

        private static void _RestoreRenderTargetCallback (DeviceManager dm, object userData) {
            var data = (SetRenderTargetData)userData;
            if (Tracing.RenderTrace.EnableTracing)
                Tracing.RenderTrace.ImmediateMarker("Unset Render Target {0}", data.RenderTarget.GetHashCode());

            dm.PopRenderTarget();
            if (data.After != null)
                data.After(dm, data.UserData);

            _Pool.Release(data);
        }

        public static BatchGroup ForRenderTarget (IBatchContainer container, int layer, RenderTarget2D renderTarget, Action<DeviceManager, object> before = null, Action<DeviceManager, object> after = null, object userData = null) {
            if (container == null)
                throw new ArgumentNullException("container");

            var result = container.RenderManager.AllocateBatch<BatchGroup>();
            var data = _Pool.Allocate();

            data.RenderTarget = renderTarget;
            data.Before = before;
            data.After = after;
            data.UserData = userData;
            result.Initialize(container, layer, SetRenderTargetCallback, RestoreRenderTargetCallback, data);
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

        public void Initialize (IBatchContainer container, int layer, Action<DeviceManager, object> before, Action<DeviceManager, object> after, object userData, bool addToContainer = true) {
            base.Initialize(container, layer, null, addToContainer);

            RenderManager = container.RenderManager;
            _Before = before;
            _After = after;
            _UserData = userData;
        }

        public RenderManager RenderManager {
            get;
            private set;
        }

        public bool IsDisposed {
            get {
                return _DrawCalls == null;
            }
        }

        public void Add (Batch batch) {
            if (batch.Container != null)
                throw new InvalidOperationException("This batch is already in another container.");

            batch.Container = this;
            base.Add(ref batch);
        }

        protected override void OnReleaseResources () {
            foreach (var batch in _DrawCalls)
                if (batch != null)
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
#if DEBUG
            if (Debugger.IsAttached) {
                batch.Issue(manager);
            } else {
#endif

                try {
                    batch.Issue(manager);
                } catch (Exception exc) {                
                    throw new BatchIssueFailedException(batch, exc);
                }
#if DEBUG
            }
#endif
        }
    }
}
