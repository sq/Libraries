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
using System.Runtime.CompilerServices;

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

    public sealed class DeviceManager : IDisposable {
        public static class ActiveMaterial {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static void Set (DeviceManager deviceManager, Material material) {
                if (deviceManager.CurrentMaterial != material) {
                    Set_Slow(deviceManager, material);
                } else {
                    material.Flush();
                }
            }

            private static void Set_Slow (DeviceManager deviceManager, Material material) {
                if (deviceManager.CurrentMaterial != null)
                    deviceManager.CurrentMaterial.End(deviceManager);

                deviceManager.CurrentMaterial = material;

                if (material != null)
                    material.Begin(deviceManager);
            }
        }

        private readonly Stack<BlendState>            BlendStateStack = new Stack<BlendState>();
        private readonly Stack<DepthStencilState>     DepthStencilStateStack = new Stack<DepthStencilState>();
        private readonly Stack<RasterizerState>       RasterizerStateStack = new Stack<RasterizerState>();
        private readonly Stack<RenderTargetBinding[]> RenderTargetStack = new Stack<RenderTargetBinding[]>();
        private readonly Stack<Viewport>              ViewportStack = new Stack<Viewport>();

        public readonly GraphicsDevice Device;
        public Material CurrentMaterial { get; private set; }
        public int FrameIndex { get; internal set; }

        private static volatile int NextDeviceId;
        public int DeviceId { get; private set; }

        private bool _IsDisposed;

        public bool IsDisposed {
            get {
                if (!_IsDisposed && Device.IsDisposed)
                    _IsDisposed = true;
                return _IsDisposed;
            }
        }

        public void Dispose () {
            if (IsDisposed)
                return;

            _IsDisposed = true;
            // FIXME: Dispose device? Probably not
        }

        public DeviceManager (GraphicsDevice device) {
            Device = device;
            DeviceId = ++NextDeviceId;
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
            // GOD
            RenderManager.ResetDeviceState(Device);
        }

        public DefaultMaterialSetEffectParameters CurrentParameters {
            get {
                return CurrentMaterial.Parameters;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ApplyMaterial (Material material) {
            ActiveMaterial.Set(this, material);
        }

        public void Begin (bool changeRenderTargets) {
            if (CurrentMaterial != null)
                CurrentMaterial = null;

            RenderManager.ResetDeviceState(Device);
            if (changeRenderTargets)
                Device.SetRenderTargets();
            Device.SetVertexBuffer(null);
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
        public          DeviceManager  DeviceManager { get; private set; }
        public readonly Thread         MainThread;

        // FIXME: ????
        private volatile int _AllowCreatingNewGenerators = 1;

        private int _FrameCount = 0;
        private object _FrameLock = new object();
        private Frame _Frame;
        private bool _FrameInUse = false;

        internal long TotalVertexBytes, TotalIndexBytes;

        private readonly List<IBufferGenerator> _AllBufferGenerators = new List<IBufferGenerator>();
        private ThreadLocal<Dictionary<Type, IBufferGenerator>> _BufferGenerators;

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

        private readonly HashSet<IDrainable> RequiredDrainListPools = new HashSet<IDrainable>();
        private static readonly object ListPoolLock = new object();

        public event EventHandler<DeviceManager> DeviceChanged;

        public RenderManager (GraphicsDevice device, Thread mainThread, ThreadGroup threadGroup) {
            if (mainThread == null)
                throw new ArgumentNullException("mainThread");

            MainThread = mainThread;
            DeviceManager = new DeviceManager(device);
            ThreadGroup = threadGroup;
            PrepareManager = new PrepareManager(ThreadGroup);

            CreateNewBufferGenerators();

            _Frame = null;
        }

        internal void CreateNewBufferGenerators () {
            lock (_AllBufferGenerators)
            foreach (var generator in _AllBufferGenerators) {
                try {
                    generator.Dispose();
                } catch (Exception) {
                }
            }

            _BufferGenerators = new ThreadLocal<Dictionary<Type, IBufferGenerator>>(
                MakeThreadBufferGeneratorTable
            );
        }

        internal void ChangeDevice (GraphicsDevice device) {
            if (DeviceManager != null)
                DeviceManager.Dispose();
            DeviceManager = new DeviceManager(device);
            CreateNewBufferGenerators();
            if (DeviceChanged != null)
                DeviceChanged(this, DeviceManager);
        }

        private Dictionary<Type, IBufferGenerator> MakeThreadBufferGeneratorTable () {
            var result = new Dictionary<Type, IBufferGenerator>(new ReferenceComparer<Type>());
            return result;
        }

        private static readonly BlendState ResetBlendState = new BlendState {
            AlphaDestinationBlend = Blend.Zero,
            ColorDestinationBlend = Blend.Zero,
            Name = "ResetBlendState"
        };

        private static readonly SamplerState ResetSamplerState = new SamplerState {
            Filter = TextureFilter.Point,
            AddressU = TextureAddressMode.Clamp,
            AddressV = TextureAddressMode.Clamp,
            AddressW = TextureAddressMode.Clamp,
            Name = "ResetSamplerState"
        };

        /// <summary>
        /// A device reset can leave a device in an intermediate state that will cause drawing operations to fail later.
        /// We address this by resetting pieces of device state to known-good values at the beginning of every frame,
        ///  and you can call this method to do so manually at any time.
        /// </summary>
        public static void ResetDeviceState (GraphicsDevice device) {
            const int numStages = 16;
            const int numVertexStages = 4;

            for (int i = 0; i < numStages; i++) {
                device.Textures[i] = null;
                device.SamplerStates[i] = ResetSamplerState;
            }

            for (int i = 0; i < numVertexStages; i++) {
                device.VertexTextures[i] = null;
                device.VertexSamplerStates[i] = ResetSamplerState;
            }

            device.BlendState = ResetBlendState;
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
            var context = new Batch.PrepareContext(PrepareManager, false, frame.BatchesToRelease);
            context.PrepareMany(frame.Batches);

            PrepareManager.AssertEmpty();
        }

        internal void ParallelPrepareBatches (Frame frame) {
            var context = new Batch.PrepareContext(PrepareManager, true, frame.BatchesToRelease);
            context.PrepareMany(frame.Batches);

            PrepareManager.Wait();
            PrepareManager.AssertEmpty();
        }

        internal int PickFrameIndex () {
            return Interlocked.Increment(ref _FrameCount);
        }

        internal void ReleaseFrame (Frame frame) {
            lock (_FrameLock) {
                if (_Frame != frame)
                    throw new InvalidOperationException();

                _FrameInUse = false;
            }
        }

        public Frame CreateFrame () {
            lock (_FrameLock) {
                if (_FrameInUse)
                    throw new InvalidOperationException("A frame is already in use");

                _FrameInUse = true;
                CollectAllocators();
                _Frame = new Frame();
                _Frame.Initialize(this, PickFrameIndex());
                return _Frame;
            }
        }

        public void CollectAllocators () {
            lock (_ArrayAllocators)
                foreach (var allocator in _ArrayAllocators.Values)
                    allocator.Step();

            // Ensure that all the list pools used by the previous frame
            //  have finished their async clears. This ensures that we don't
            //  end up leaking lots of lists
            lock (ListPoolLock)
            foreach (var lp in RequiredDrainListPools)
                lp.WaitForWorkItems();
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

        internal void AddDrainRequiredListPool (IDrainable listPool) {
            lock (ListPoolLock)
                RequiredDrainListPools.Add(listPool);
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

    public sealed class BatchComparer : IRefComparer<Batch>, IComparer<Batch> {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Compare (ref Batch x, ref Batch y) {
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Compare (Batch x, Batch y) {
            return Compare(ref x, ref y);
        }
    }

    public class PrepareManager {
        public struct Task : IWorkItem {
            public IBatch Batch;
            public Batch.PrepareContext Context;

            public Task (IBatch batch, Batch.PrepareContext context) {
                Batch = batch;
                Context = context;
            }

            public void Execute () {
                Context.Validate(Batch, false);

                if (!Batch.State.IsCombined)
                    Batch.Prepare(Context);

                Batch.State.IsPrepareQueued = false;
            }
        };

        public  readonly ThreadGroup     Group;
        private readonly WorkQueue<Task> Queue;

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
        
        public void PrepareMany<T> (DenseList<T> batches, Batch.PrepareContext context)
            where T : IBatch 
        {
            int totalAdded = 0;

            const int blockSize = 128;
            using (var buffer = BufferPool<Task>.Allocate(blockSize)) {
                var task = new Task(null, context);
                int j = 0, c = batches.Count;

                T batch;
                for (int i = 0; i < c; i++) {
                    batches.GetItem(i, out batch);
                    if (batch == null)
                        continue;

                    ValidateBatch(batch, true);
                    task.Batch = batch;

                    if (context.Async) {
                        buffer.Data[j++] = task;
                        totalAdded += 1;

                        if (j == (blockSize - 1)) {
                            Queue.EnqueueMany(new ArraySegment<Task>(buffer.Data, 0, j));
                            j = 0;
                        }
                    } else {
                        task.Execute();
                    }
                }

                if (context.Async && (j > 0))
                    Queue.EnqueueMany(new ArraySegment<Task>(buffer.Data, 0, j));
            }

            if (context.Async)
                Group.NotifyQueuesChanged();
        }

        internal void ValidateBatch (IBatch batch, bool enqueuing) {
            var state = batch.State;

            lock (state) {
                if (!state.IsInitialized)
                    throw new Exception("Uninitialized batch");
                /*
                else if (state.IsCombined)
                    throw new Exception("Batch combined");
                */
                else if (state.IsPrepared)
                    throw new Exception("Batch already prepared");
                else if (state.IsIssued)
                    throw new Exception("Batch already issued");

                if (enqueuing) {
                    if (state.IsPrepareQueued)
                        throw new Exception("Batch already queued for prepare");

                    state.IsPrepareQueued = true;
                }
            }
        }

        public void Prepare (IBatch batch, Batch.PrepareContext context) {
            if (batch == null)
                return;

            ValidateBatch(batch, true);
            var task = new Task(batch, context);

            if (context.Async) {
                Queue.Enqueue(task);
                // FIXME: Is this too often?
                Group.NotifyQueuesChanged();
            } else {
                task.Execute();
            }
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
            if (manager.IsDisposed)
                return;

#if DEBUG
            if (Debugger.IsAttached) {
                batch.Issue(manager);
            } else {
#endif

            var state = batch.State;
            if (!state.IsInitialized)
                throw new BatchIssueFailedException(batch, new Exception("Batch not initialized"));
            else if (state.IsCombined)
                // HACK
                return;
            else if (state.IsPrepareQueued)
                throw new BatchIssueFailedException(batch, new Exception("Batch in prepare queue"));
            else if (!state.IsPrepared)
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
