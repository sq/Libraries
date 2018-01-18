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
using System.Collections;

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
        bool IsReleased { get; }
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
            if (newRenderTarget != null) {
                Device.Viewport = new Viewport(0, 0, newRenderTarget.Width, newRenderTarget.Height);
            } else {
                Device.Viewport = new Viewport(0, 0, Device.PresentationParameters.BackBufferWidth, Device.PresentationParameters.BackBufferHeight);
            }
        }

        public void PushRenderTargets (RenderTargetBinding[] newRenderTargets) {
            PushStates();

            var first = (RenderTarget2D)newRenderTargets[0].RenderTarget;

            RenderTargetStack.Push(Device.GetRenderTargets());
            ViewportStack.Push(Device.Viewport);
            Device.SetRenderTargets(newRenderTargets);
            RenderManager.ResetDeviceState(Device);
            Device.Viewport = new Viewport(0, 0, first.Width, first.Height);
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
        public struct MemoryStatistics {
            public long ManagedVertexBytes, ManagedIndexBytes;
            public long UnmanagedVertexBytes, UnmanagedIndexBytes;
        }

        public readonly PrepareManager PrepareManager;
        public readonly ThreadGroup    ThreadGroup;
        public readonly DeviceManager  DeviceManager;
        public readonly Thread         MainThread;

        // FIXME: ????
        private volatile int _AllowCreatingNewGenerators = 1;

        private int _FrameCount = 0;
        private readonly Frame _Frame;
        private bool _FrameInUse = false;

        internal long TotalVertexBytes, TotalIndexBytes;

        private readonly List<IBufferGenerator> _AllBufferGenerators = new List<IBufferGenerator>();
        private readonly ThreadLocal<Dictionary<Type, IBufferGenerator>> _BufferGenerators;

        private readonly Dictionary<Type, int> _PreferredPoolCapacities =
            new Dictionary<Type, int>(new ReferenceComparer<Type>());
        private readonly Dictionary<Type, IArrayPoolAllocator> _ArrayAllocators = 
            new Dictionary<Type, IArrayPoolAllocator>(new ReferenceComparer<Type>());
        private readonly Dictionary<Type, IBatchPool> _BatchAllocators =
            new Dictionary<Type, IBatchPool>(new ReferenceComparer<Type>());
        private readonly List<IDisposable> _PendingDisposes = new List<IDisposable>();

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
            ThreadGroup = threadGroup;
            PrepareManager = new PrepareManager(ThreadGroup);

            _BufferGenerators = new ThreadLocal<Dictionary<Type, IBufferGenerator>>(
                MakeThreadBufferGeneratorTable
            );

            _Frame = new Frame();
        }

        private Dictionary<Type, IBufferGenerator> MakeThreadBufferGeneratorTable () {
            var result = new Dictionary<Type, IBufferGenerator>(new ReferenceComparer<Type>());
            return result;
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

        public MemoryStatistics GetMemoryStatistics () {
            var result = new MemoryStatistics {
                UnmanagedVertexBytes = TotalVertexBytes,
                UnmanagedIndexBytes = TotalIndexBytes,
                ManagedVertexBytes = 0,
                ManagedIndexBytes = 0
            };

            lock (_AllBufferGenerators)
            foreach (var generator in _AllBufferGenerators) {
                result.ManagedVertexBytes += generator.ManagedVertexBytes;
                result.ManagedIndexBytes += generator.ManagedIndexBytes;
            }

            return result;
        }

        internal void SynchronousPrepareBatches (Frame frame) {
            PrepareManager.PrepareMany(frame.Batches, false);

            PrepareManager.AssertEmpty();
        }

        internal void ParallelPrepareBatches (Frame frame) {
            PrepareManager.PrepareMany(frame.Batches, true);

            PrepareManager.Wait();
            PrepareManager.AssertEmpty();
        }

        internal int PickFrameIndex () {
            return Interlocked.Increment(ref _FrameCount);
        }

        internal void ReleaseFrame (Frame frame) {
            if (_Frame != frame)
                throw new InvalidOperationException();

            lock (_Frame)
                _FrameInUse = false;
        }

        public Frame CreateFrame () {
            lock (_Frame) {
                if (_FrameInUse)
                    throw new InvalidOperationException("A frame is already in use");

                _FrameInUse = true;
                CollectAllocators();
                _Frame.Initialize(this, PickFrameIndex());
                return _Frame;
            }
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
            var bg = _BufferGenerators.Value;
            if (bg.TryGetValue(t, out result))
                return (T)result;

            if (_AllowCreatingNewGenerators != 1)
                throw new InvalidOperationException("Cannot create a buffer generator after the flush operation has occurred");

            result = (IBufferGenerator)Activator.CreateInstance(
                t, this
            );
            bg.Add(t, result);

            lock (_AllBufferGenerators)
                _AllBufferGenerators.Add(result);

            return (T)result;
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

        internal void ResetBufferGenerators (int frameIndex) {
            _AllowCreatingNewGenerators = 1;

            lock (_AllBufferGenerators)
            foreach (var generator in _AllBufferGenerators)
                generator.Reset(frameIndex);
        }

        internal void FlushBufferGenerators (int frameIndex) {
            PrepareManager.Wait();

            _AllowCreatingNewGenerators = 0;

            lock (_AllBufferGenerators)
            foreach (var bg in _AllBufferGenerators)
                bg.Flush(frameIndex);
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
                    return 1;
            } else if (y == null) {
                return -1;
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
        private enum States : int {
            Initialized = 0,
            Preparing = 1,
            Prepared = 2,
            Drawing = 3,
            Drawn = 4,
            Disposed = 5
        }

        private static readonly object PrepareLock = new object();

        private readonly object BatchLock = new object();

        public static BatchComparer BatchComparer = new BatchComparer();

        private static ListPool<Batch> _ListPool = new ListPool<Batch>(
            16, 256, 4096
        );

        public RenderManager RenderManager;
        public int Index;
        public long InitialBatchCount;

        internal DenseList<Batch> Batches = new DenseList<Batch>();

        volatile int State = (int)States.Disposed;

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
            Batches.ListPool = _ListPool;
            Batches.Clear();
            RenderManager = renderManager;
            Index = index;
            InitialBatchCount = Batch.LifetimeCount;
            State = (int)States.Initialized;
        }

        public void Add (Batch batch) {
            if (State != (int)States.Initialized)
                throw new InvalidOperationException();

            if (batch == null)
                throw new ArgumentNullException("batch");

            lock (BatchLock) {
                /*
#if DEBUG && PARANOID
                if (Batches.Contains(batch))
                    throw new InvalidOperationException("Batch already added to this frame");
#endif
                */

                Batches.Add(batch);
                batch.Container = this;
            }
        }

        public void Prepare (bool parallel) {
            if (Interlocked.Exchange(ref State, (int)States.Preparing) != (int)States.Initialized)
                throw new InvalidOperationException("Frame was not in initialized state when prepare operation began ");

            if (false) {
                var totalBatches = Batch.LifetimeCount - InitialBatchCount;
                Console.WriteLine("Frame contains {0} batches", totalBatches);
            }

            var numRemoved = BatchCombiner.CombineBatches(ref Batches);

            // Batch combining may have left holes so we need to sort again to sift the holes
            //  to the back
            Batches.Sort(BatchComparer);
            Batches.RemoveTail(numRemoved);

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

            if (Interlocked.Exchange(ref State, (int)States.Prepared) != (int)States.Preparing)
                throw new InvalidOperationException("Frame was not in preparing state when prepare operation completed");
        }

        public void Draw () {
            if (Interlocked.Exchange(ref State, (int)States.Drawing) != (int)States.Prepared)
                throw new InvalidOperationException();

            if (Tracing.RenderTrace.EnableTracing)
                Tracing.RenderTrace.ImmediateMarker("Frame {0:0000} : Begin Draw", Index);

            var dm = RenderManager.DeviceManager;
            var device = dm.Device;

            int c = Batches.Count;
            var _batches = Batches.GetBuffer(false);
            for (int i = 0; i < c; i++) {
                var batch = _batches[i];
                if (batch != null)
                    batch.IssueAndWrapExceptions(dm);
            }

            dm.Finish();

            if (Tracing.RenderTrace.EnableTracing)
                Tracing.RenderTrace.ImmediateMarker("Frame {0:0000} : End Draw", Index);

            if (Interlocked.Exchange(ref State, (int)States.Drawn) != (int)States.Drawing)
                throw new InvalidOperationException();
        }

        public void Dispose () {
            if (State == (int)States.Disposed)
                return;

            var _batches = Batches.GetBuffer(false);
            for (int i = 0; i < Batches.Count; i++) {
                var batch = _batches[i];
                if (batch != null)
                    batch.ReleaseResources();
            }

            Batches.Dispose();

            RenderManager.ReleaseFrame(this);

            State = (int)States.Disposed;
        }

        public bool IsReleased {
            get {
                return State == (int)States.Disposed;
            }
        }

        public override int GetHashCode() {
            return Index;
        }
    }

    public class PrepareManager {
        public struct Task : IWorkItem {
            public readonly PrepareManager Manager;
            public readonly bool IsAsync;

            public IBatch Batch;

            public Task (PrepareManager manager, IBatch batch, bool isAsync) {
                if (manager == null)
                    throw new ArgumentNullException("manager");

                Manager = manager;
                Batch = batch;
                IsAsync = isAsync;
            }

            public void Execute () {
                Execute(IsAsync);
            }

            public void Execute (bool async) {
                Batch.Prepare(Manager, async);
            }
        };

        public  readonly ThreadGroup     Group;
        private readonly WorkQueue<Task> Queue;

        static readonly ThreadLocal<bool> ForceSync = new ThreadLocal<bool>(() => false);

        public PrepareManager (ThreadGroup threadGroup) {
            Group = threadGroup;
            Queue = threadGroup.GetQueueForType<Task>();
        }

        public void AssertEmpty () {
            Queue.AssertEmpty();
        }

        public void Wait () {
            Group.NotifyQueuesChanged(true);            
            Queue.WaitUntilDrained();
        }
        
        public void PrepareMany<T> (DenseList<T> batches, bool async)
            where T : IBatch 
        {
            if (ForceSync.Value)
                async = false;

            int totalAdded = 0;

            const int blockSize = 256;
            using (var buffer = BufferPool<Task>.Allocate(blockSize)) {
                var task = new Task(this, null, async);
                int j = 0, c = batches.Count;

                for (j = 0; j < c; j++) {
                    var batch = batches[j];
                    if (batch == null)
                        continue;

                    task.Batch = batch;

                    if (async) {
                        buffer.Data[j] = task;
                        totalAdded += 1;

                        if (j == (blockSize - 1)) {
                            Queue.EnqueueMany(new ArraySegment<Task>(buffer.Data, 0, j));
                            j = 0;
                        }
                    } else {
                        task.Execute();
                    }
                }

                if (async && (j > 0)) {
                    Queue.EnqueueMany(new ArraySegment<Task>(buffer.Data, 0, j));
                }
            }

            if (async)
                Group.NotifyQueuesChanged();
        }

        public void PrepareAsync (IBatch batch) {
            if (batch == null)
                return;

            if (ForceSync.Value) {
                PrepareSync(batch);
                return;
            }

            Queue.Enqueue(new Task(this, batch, true));
            // FIXME: Is this too often?
            Group.NotifyQueuesChanged();
        }

        public void PrepareSync (IBatch batch) {
            if (batch == null)
                return;

            var wasSync = ForceSync.Value;
            try {
                ForceSync.Value = true;
                batch.Prepare(this, false);
            } finally {
                ForceSync.Value = wasSync;
            }
        }
    }

    public interface IBatch : IDisposable {
        bool IsPrepared { get; }
        void Prepare (PrepareManager manager, bool async);
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

        public bool IsPrepared { get; protected set; }

        internal UnorderedList<Batch> BatchesCombinedIntoThisOne = null;

        protected static long _BatchCount = 0;

        protected Batch () {
            var thisType = GetType();
            lock (TypeIds) {
                if (!TypeIds.TryGetValue(thisType, out TypeId))
                    TypeIds.Add(thisType, TypeId = TypeIds.Count);
            }
        }

        protected void Initialize (IBatchContainer container, int layer, Material material, bool addToContainer) {
            if ((material != null) && (material.IsDisposed))
                throw new ObjectDisposedException("material");

            StackTrace = null;
            if (BatchesCombinedIntoThisOne != null)
                BatchesCombinedIntoThisOne.Clear();
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
                StackTrace = new StackTrace(2 + extraFramesToSkip, false);
        }

        // This is where you should do any computation necessary to prepare a batch for rendering.
        // Examples: State sorting, computing vertices.
        public virtual void Prepare (PrepareManager manager, bool async) {
            Prepare(manager);
            IsPrepared = true;
        }

        protected virtual void Prepare (PrepareManager manager) {
            IsPrepared = true;
        }

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
            IsPrepared = false;

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
            return string.Format("{0} #{1} {2} material={3}", GetType().Name, Index, IsPrepared ? "prepared" : "", Material);
        }
    }

    public struct DenseList<T> : IDisposable, IEnumerable<T> {
        public struct Enumerator : IEnumerator<T> {
            private Buffer Buffer;
            private int Index;
            private int Count;

            internal Enumerator (Buffer buffer) {
                Buffer = buffer;
                Index = -1;
                Count = buffer.Count;
            }

            public T Current {
                get {
                    return Buffer.Data[Index];
                }
            }

            object IEnumerator.Current {
                get {
                    return Buffer.Data[Index];
                }
            }

            public void Dispose () {
                Buffer.Dispose();
                Index = -1;
            }

            public bool MoveNext () {
                if (Index < Count) {
                    Index++;
                    return Index < Count;
                }
                return false;
            }

            public void Reset () {
                Index = -1;
            }
        }

        public struct Buffer : IDisposable {
            internal BufferPool<T>.Buffer BufferPoolAllocation;
            internal bool IsTemporary;

            public int Count;
            public T[] Data;

            public T this[int index] {
                get {
                    return Data[index];
                }
                set {
                    Data[index] = value;
                }
            }            

            public void Dispose () {
                if (IsTemporary)
                    BufferPoolAllocation.Dispose();

                Data = null;
            }
        }

        public ListPool<T> ListPool;

        private T Item1, Item2, Item3, Item4;
        private UnorderedList<T> Calls;

        private int _Count;

        public void Clear () {
            _Count = 0;
            Item1 = Item2 = Item3 = Item4 = default(T);
            if (Calls != null)
                Calls.Clear();
        }

        public void CreateList (int? capacity = null) {
            if (Calls != null)
                return;

            if (ListPool != null)
                Calls = ListPool.Allocate(capacity);
            else if (capacity.HasValue)
                Calls = new UnorderedList<T>(capacity.Value);
            else
                Calls = new UnorderedList<T>(); 

            if (_Count > 0)
                Calls.Add(ref Item1);
            if (_Count > 1)
                Calls.Add(ref Item2);
            if (_Count > 2)
                Calls.Add(ref Item3);
            if (_Count > 3)
                Calls.Add(ref Item4);
            Item1 = Item2 = Item3 = Item4 = default(T);
            _Count = 0;
        }

        public int Count {
            get {
                if (Calls != null)
                    return Calls.Count;
                else
                    return _Count;
            }
        }

        public void Add (T item) {
            Add(ref item);
        }

        public T this [int index] {
            get {
                if (Calls != null)
                    return Calls.GetBuffer()[index];

                switch (index) {
                    case 0:
                        return Item1;
                    case 1:
                        return Item2;
                    case 2:
                        return Item3;
                    case 3:
                        return Item4;
                    default:
                        throw new Exception();
                }
            }
        }

        public void Add (ref T item) {
            if ((Calls != null) || (_Count >= 4)) {
                CreateList();
                Calls.Add(ref item);
                return;
            }

            var i = _Count;
            _Count += 1;
            switch (i) {
                case 0:
                    Item1 = item;
                    return;
                case 1:
                    Item2 = item;
                    return;
                case 2:
                    Item3 = item;
                    return;
                case 3:
                    Item4 = item;
                    return;
                default:
                    throw new Exception();
            }
        }

        public ArraySegment<T> ReserveSpace (int count) {
            // FIXME: Slow
            CreateList();
            return Calls.ReserveSpace(count);
        }

        public void RemoveRange (int index, int count) {
            // FIXME: Slow
            CreateList();
            Calls.RemoveRange(index, count);
        }

        public void RemoveTail (int count) {
            if (count == 0)
                return;
            if (count > Count)
                throw new ArgumentException("count");

            if (Calls == null) {
                _Count -= count;
                return;
            }

            Calls.RemoveRange(Calls.Count - count, count);
        }

        public void OverwriteWith (T[] data) {
            var count = data.Length;

            if (count > 4) {
                CreateList(count);
                Calls.Clear();
                Calls.AddRange(data);
            } else {
                _Count = count;
                if (data.Length > 0)
                    Item1 = data[0];
                if (data.Length > 1)
                    Item2 = data[1];
                if (data.Length > 2)
                    Item3 = data[2];
                if (data.Length > 3)
                    Item4 = data[3];
            }
        }

        public Buffer GetBuffer (bool writable) {
            if (writable)
                CreateList();

            if (Calls != null)
                return new Buffer {
                    IsTemporary = false,
                    Data = Calls.GetBuffer(),
                    Count = Calls.Count
                };
            else {
                var alloc = BufferPool<T>.Allocate(4);
                var buf = alloc.Data;
                buf[0] = Item1;
                buf[1] = Item2;
                buf[2] = Item3;
                buf[3] = Item4;
                return new Buffer {
                    IsTemporary = true,
                    Data = buf,
                    BufferPoolAllocation = alloc,
                    Count = _Count
                };
            }
        }

        public void Sort<TComparer> (TComparer comparer, int[] indices = null)
            where TComparer : IComparer<T>
        {
            if (Calls != null) {
                if (indices != null)
                    Calls.IndexedSort(comparer, indices);
                else
                    Calls.FastCLRSort(comparer);

                return;
            }

            if (_Count <= 1)
                return;

            T a, b;
            if (comparer.Compare(Item1, Item2) <= 0) {
                a = Item1; b = Item2;
            } else {
                a = Item2; b = Item1;
            }

            if (_Count == 2) {
                Item1 = a;
                Item2 = b;
                return;
            } else if (_Count == 3) {
                if (comparer.Compare(b, Item3) <= 0) {
                    Item1 = a;
                    Item2 = b;
                } else if (comparer.Compare(a, Item3) <= 0) {
                    Item1 = a;
                    Item2 = Item3;
                    Item3 = b;
                } else {
                    Item1 = Item3;
                    Item2 = a;
                    Item3 = b;
                }
            } else {
                T c, d;
                if (comparer.Compare(Item3, Item4) <= 0) {
                    c = Item3; d = Item4;
                } else {
                    c = Item4; d = Item3;
                }

                T m1;
                if (comparer.Compare(a, c) <= 0) {
                    Item1 = a;
                    m1 = c;
                } else {
                    Item1 = c;
                    m1 = a;
                }

                T m2;
                if (comparer.Compare(b, d) >= 0) {
                    Item4 = b;
                    m2 = d;
                } else {
                    Item4 = d;
                    m2 = b;
                }

                if (comparer.Compare(m1, m2) <= 0) {
                    Item2 = m1;
                    Item3 = m2;
                } else {
                    Item2 = m2;
                    Item3 = m1;
                }
            }
        }

        public void Dispose () {
            _Count = 0;
            Item1 = Item2 = Item3 = Item4 = default(T);
            if (ListPool != null)
                ListPool.Release(ref Calls);
            else
                Calls = null;
        }

        Enumerator GetEnumerator () {
            return new Enumerator(GetBuffer(false));
        }

        IEnumerator<T> IEnumerable<T>.GetEnumerator () {
            return new Enumerator(GetBuffer(false));
        }

        IEnumerator IEnumerable.GetEnumerator () {
            return new Enumerator(GetBuffer(false));
        }
    }

    public abstract class ListBatch<T> : Batch {
        public const int BatchCapacityLimit = 512;

        private static ListPool<T> _ListPool = new ListPool<T>(
            256, 4, 64, BatchCapacityLimit, 10240
        );

        protected DenseList<T> _DrawCalls = new DenseList<T>();

        new protected void Initialize (
            IBatchContainer container, int layer, Material material,
            bool addToContainer, int? capacity = null
        ) {
            _DrawCalls.ListPool = _ListPool;
            _DrawCalls.Clear();
            base.Initialize(container, layer, material, addToContainer);
        }

        protected void Add (T item) {
            _DrawCalls.Add(item);
        }

        protected void Add (ref T item) {
            _DrawCalls.Add(ref item);
        }

        protected override void OnReleaseResources() {
            _DrawCalls.Dispose();
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

        protected override void Prepare (PrepareManager manager) {
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

        protected override void Prepare (PrepareManager manager) {
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

        public OcclusionQuery OcclusionQuery;

        Action<DeviceManager, object> _Before, _After;
        private object _UserData;

        public override void Prepare (PrepareManager manager, bool async) {
            BatchCombiner.CombineBatches(ref _DrawCalls);

            _DrawCalls.Sort(Frame.BatchComparer);

            manager.PrepareMany(_DrawCalls, false);

            IsPrepared = true;
        }

        public override void Issue (DeviceManager manager) {
            if (OcclusionQuery != null)
                OcclusionQuery.Begin();
            if (_Before != null)
                _Before(manager, _UserData);

            try {
                using (var b = _DrawCalls.GetBuffer(false)) {
                    for (int i = 0; i < b.Count; i++)
                        if (b.Data[i] != null)
                            b.Data[i].IssueAndWrapExceptions(manager);
                }
            } finally {
                if (_After != null)
                    _After(manager, _UserData);
                if (OcclusionQuery != null)
                    OcclusionQuery.End();

                base.Issue(manager);
            }
        }

        private static readonly Action<DeviceManager, object> SetRenderTargetCallback = _SetRenderTargetCallback;

        private static void _SetRenderTargetCallback (DeviceManager dm, object userData) {
            var data = (SetRenderTargetData)userData;
            if (Tracing.RenderTrace.EnableTracing)
                Tracing.RenderTrace.ImmediateMarker("Set   RT {0}", Tracing.ObjectNames.ToObjectID(data.RenderTarget));

            dm.PushRenderTarget(data.RenderTarget);
            if (data.Before != null)
                data.Before(dm, data.UserData);
        }

        private static readonly Action<DeviceManager, object> RestoreRenderTargetCallback = _RestoreRenderTargetCallback;

        private static void _RestoreRenderTargetCallback (DeviceManager dm, object userData) {
            var data = (SetRenderTargetData)userData;
            if (Tracing.RenderTrace.EnableTracing)
                Tracing.RenderTrace.ImmediateMarker("Unset RT {0}", Tracing.ObjectNames.ToObjectID(data.RenderTarget));

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
            IsReleased = false;
            OcclusionQuery = null;
        }

        public RenderManager RenderManager {
            get;
            private set;
        }

        new public bool IsReleased { get; private set; }

        new public void Add (Batch batch) {
            if (batch == null)
                throw new ArgumentNullException("batch");
            if (batch.Container != null)
                throw new InvalidOperationException("This batch is already in another container.");

            batch.Container = this;
            base.Add(ref batch);
        }

        protected override void OnReleaseResources () {
            for (int i = 0, c = _DrawCalls.Count; i < c; i++) {
                var batch = _DrawCalls[i];
                if (batch != null)
                    batch.ReleaseResources();
            }

            _DrawCalls.Clear();
            IsReleased = true;
            OcclusionQuery = null;

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

            if (!batch.IsPrepared)
                throw new BatchIssueFailedException(batch, new Exception("Batch not prepared"));

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
