using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Util;

namespace Squared.Render {
    public interface IBatch : IDisposable {
        void Prepare (Batch.PrepareContext context);
        void Suspend ();

        bool AreParametersEqual (in MaterialParameterValues rhs);
    }

    public abstract class Batch : IBatch {
        public struct PrepareContext {
            private PrepareManager Manager;

            public bool Async;
            public DenseList<Batch> BatchesToRelease;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public PrepareContext (PrepareManager manager, bool async) {
                Manager = manager;
                Async = async;
                BatchesToRelease = new DenseList<Batch>();
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Prepare (Batch batch) {
                Manager.Prepare(batch, ref this);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal void InvokeBasePrepare (Batch b) {
                b.Prepare(Manager);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal void Validate (IBatch batch, bool enqueuing) {
                Manager.ValidateBatch(batch, enqueuing);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal void PrepareMany (ref DenseList<Batch> batches) {
                Manager.PrepareMany(ref batches, ref this);
            }
        }

        public struct PrepareState {
            public volatile PrepareStateFlags Flags;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Reset () {
                Flags = PrepareStateFlags.Initialized;
            }

            public bool IsInitialized {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => (Flags & PrepareStateFlags.Initialized) == PrepareStateFlags.Initialized;
                set {
                    if (value)
                        Flags |= PrepareStateFlags.Initialized;
                    else
                        Flags &= ~PrepareStateFlags.Initialized;
                }
            }

            public bool IsPrepareQueued {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => (Flags & PrepareStateFlags.PrepareQueued) == PrepareStateFlags.PrepareQueued;
                set {
                    if (value)
                        Flags |= PrepareStateFlags.PrepareQueued;
                    else
                        Flags &= ~PrepareStateFlags.PrepareQueued;
                }
            }

            public bool IsPrepared {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => (Flags & PrepareStateFlags.Prepared) == PrepareStateFlags.Prepared;
                set {
                    if (value)
                        Flags |= PrepareStateFlags.Prepared;
                    else
                        Flags &= ~PrepareStateFlags.Prepared;
                }
            }

            public bool IsIssued {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => (Flags & PrepareStateFlags.Issued) == PrepareStateFlags.Issued;
                set {
                    if (value)
                        Flags |= PrepareStateFlags.Issued;
                    else
                        Flags &= ~PrepareStateFlags.Issued;
                }
            }

            public bool IsCombined {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => (Flags & PrepareStateFlags.Combined) == PrepareStateFlags.Combined;
                set {
                    if (value)
                        Flags |= PrepareStateFlags.Combined;
                    else
                        Flags &= ~PrepareStateFlags.Combined;
                }
            }
        }

        [Flags]
        public enum PrepareStateFlags : uint {
            Initialized = 1,
            PrepareQueued = 2,
            Prepared = 4,
            Issued = 8,
            Combined = 16
        }

        private static Dictionary<Type, int> TypeIds = new Dictionary<Type, int>(new ReferenceComparer<Type>());

        public static bool CaptureStackTraces = false;

        public readonly int TypeId;

        /// <summary>
        /// Set a name for the batch to aid debugging;
        /// </summary>
        public string Name;

        public StackTrace StackTrace;
        public IBatchContainer Container;
        public int Layer;
        public Material Material;
        public MaterialParameterValues MaterialParameters;

        internal long InstanceId;
        internal bool ReleaseAfterDraw;
        internal bool Released;
        internal IBatchPool Pool;

        internal UnorderedList<Batch> BatchesCombinedIntoThisOne = null;

        internal volatile Threading.IFuture SuspendFuture = null;

        private static List<long[]> ThreadBatchCounters = new List<long[]>();
        private static ThreadLocal<long[]> ThreadBatchCounts = new ThreadLocal<long[]>(() => {
            var result = new long[1];
            lock (ThreadBatchCounters)
                ThreadBatchCounters.Add(result);
            return result;
        });
        private static long NextInstanceId = 0;

        protected PrepareState State;

        internal int TimesIssued = 0;

        protected Batch () {
            var thisType = GetType();
            lock (TypeIds) {
                if (!TypeIds.TryGetValue(thisType, out TypeId))
                    TypeIds.Add(thisType, TypeId = TypeIds.Count);
            }

            State = new PrepareState();

            InstanceId = Interlocked.Increment(ref NextInstanceId);
        }

        /// <summary>
        /// You can manually generate different values for this in order to automatically sort specific batches against each other
        /// For example, a depth pre-pass.
        /// </summary>
        public virtual int InternalSortOrdering {
            get {
                return 0;
            }
        }

        bool IBatch.AreParametersEqual (in MaterialParameterValues rhs) {
            return MaterialParameters.Equals(in rhs);
        }

        protected void Initialize (IBatchContainer container, int layer, Material material, bool addToContainer) {
            if (State.IsPrepareQueued)
                throw new Exception("Batch currently queued for prepare");

            if ((material != null) && (material.IsDisposed))
                throw new ObjectDisposedException("material");

            Name = null;
            StackTrace = null;
            if (BatchesCombinedIntoThisOne != null)
                BatchesCombinedIntoThisOne.Clear();
            Released = false;
            ReleaseAfterDraw = false;
            Layer = layer;
            Material = material;
            MaterialParameters.Clear();
            TimesIssued = 0;

            State.Reset();
            Thread.MemoryBarrier();

#if DEBUG
            if (material?.Effect != null) {
                if (container.RenderManager.DeviceManager.Device != material.Effect.GraphicsDevice)
                    throw new ArgumentException("This effect belongs to a different graphics device");
            }
#endif

            if (addToContainer)
                container.Add(this);

            var arr = ThreadBatchCounts.Value;
            arr[0]++;
        }

        /// <summary>
        /// Adds a previously-constructed batch to a new frame/container.
        /// Use this if you already created a batch in a previous frame and wish to use it again.
        /// </summary>
        public void Reuse (IBatchContainer newContainer, int? newLayer = null) {
            Thread.MemoryBarrier();
            if (Released)
                throw new ObjectDisposedException("batch");
            else if (State.IsCombined)
                throw new InvalidOperationException("Batch was combined into another batch");

            if (newLayer.HasValue)
                Layer = newLayer.Value;

            Thread.MemoryBarrier();
            if (!State.IsInitialized)
                throw new Exception("Not initialized");

            Thread.MemoryBarrier();
            if (State.IsPrepareQueued)
                throw new Exception("Batch currently queued for prepare");

            Thread.MemoryBarrier();
            State.IsPrepared = State.IsIssued = false;
            Thread.MemoryBarrier();

            newContainer.Add(this);
        }

        /// <summary>
        /// Suspends preparing of the batch until you call Dispose on it.
        /// This allows you to begin filling a batch with draw calls on another thread while the rest of the
        ///  render preparation pipeline continues.
        /// </summary>
        public void Suspend () {
            if (SuspendFuture != null)
                throw new InvalidOperationException("Batch already suspended");
            if (IsReleased)
                throw new InvalidOperationException("Batch already released");
            if (State.IsPrepared || State.IsIssued)
                throw new InvalidOperationException("Batch already prepared or issued");
            SuspendFuture = new Threading.SignalFuture();
        }

        public void CaptureStack (int extraFramesToSkip) {
            if (CaptureStackTraces)
                StackTrace = new StackTrace(1 + extraFramesToSkip, true);
        }

        protected void OnPrepareDone () {
            Thread.MemoryBarrier();
            State.IsPrepareQueued = false;
            State.IsPrepared = true;
            Thread.MemoryBarrier();
        }

        private void WaitForSuspend () {
            var sf = SuspendFuture;
            if (sf != null) {
                if (sf.Completed)
                    return;

                using (var mre = new ManualResetEventSlim(false)) {
                    sf.RegisterOnComplete(mre.Set);
                    if (!mre.Wait(1000))
                        throw new ThreadStateException("A batch remained suspended for too long");

                    var _ = sf.Result2;
                }

                SuspendFuture = null;
            }
        }

        // This is where you should do any computation necessary to prepare a batch for rendering.
        // Examples: State sorting, computing vertices.
        public virtual void Prepare (PrepareContext context) {
            WaitForSuspend();

            context.InvokeBasePrepare(this);
            OnPrepareDone();
        }

        protected virtual void Prepare (PrepareManager manager) {
            WaitForSuspend();

            OnPrepareDone();
        }

        /// <summary>
        /// Use this if you have to skip issuing the batch for some reason.
        /// </summary>
        protected virtual void MarkAsIssued () {
            WaitForSuspend();

            State.IsIssued = true;
        }

        // This is where you send commands to the video card to render your batch.
        public virtual void Issue (DeviceManager manager) {
            WaitForSuspend();

            State.IsIssued = true;
        }

        protected virtual void OnReleaseResources () {
            if (Released)
                return;

            Thread.MemoryBarrier();
            if (State.IsPrepareQueued)
                throw new Exception("Batch currently queued for prepare");
            else if (!State.IsInitialized)
                throw new Exception("Batch uninitialized");
            Thread.MemoryBarrier();

            State.IsPrepared = false;
            State.IsInitialized = false;
            Released = true;
            Thread.MemoryBarrier();

            Pool.Release(this);

            Container = null;
            Material = null;
        }

        public void ReleaseResources () {
            if (SuspendFuture != null)
                SuspendFuture = null;

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
            if (SuspendFuture != null)
                SuspendFuture.SetResult(Threading.NoneType.None, null);

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
            unchecked {
                return (int)InstanceId;
            }
        }

        public static long LifetimeCount {
            get {
                long result = 0;
                lock (ThreadBatchCounters)
                for (int i = 0, c = ThreadBatchCounters.Count; i < c; i++)
                    result += ThreadBatchCounters[i][0];
                return result;
            }
        }

        protected string StateString {
            get {
                if (State.IsIssued)
                    return "Issued";
                else if (State.IsPrepared)
                    return "Prepared";
                else if (State.IsPrepareQueued)
                    return "Prepare Queued";

                return "Invalid";
            }
        }

        public string StackString {
            get {
                var sb = new StringBuilder();
                sb.Append(this.ToString());
                var container = this.Container;
                while (container != null) {
                    sb.AppendLine();
                    sb.Append(container.ToString());
                    container = (container as Batch)?.Container;
                }
                return sb.ToString();
            }
        }

        public override string ToString () {
            if (Name != null)
                return string.Format("{0} '{5}' #{1} {2} layer={4} material={3}", GetType().Name, InstanceId, StateString, Material, Layer, Name);
            else
                return string.Format("{0} #{1} {2} layer={4} material={3}", GetType().Name, InstanceId, StateString, Material, Layer);
        }

        internal bool IsPrepareQueued => State.IsPrepareQueued;

        internal void SetCombined (bool newState) {
            State.IsCombined = newState;
        }

        internal void SetPrepareQueued (bool newState) {
            State.IsPrepareQueued = newState;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void GetState (out bool isInitialized, out bool isCombined, out bool isPrepareQueued, out bool isPrepared, out bool isIssued) {
            var flags = State.Flags;
            isInitialized = (flags & PrepareStateFlags.Initialized) == PrepareStateFlags.Initialized;
            isCombined = (flags & PrepareStateFlags.Combined) == PrepareStateFlags.Combined;
            isPrepareQueued = (flags & PrepareStateFlags.PrepareQueued) == PrepareStateFlags.PrepareQueued;
            isPrepared = (flags & PrepareStateFlags.Prepared) == PrepareStateFlags.Prepared;
            isIssued = (flags & PrepareStateFlags.Issued) == PrepareStateFlags.Issued;
        }
    }

    public interface IListBatch {
        int Count { get; }
    }

    public abstract class ListBatch<T> : Batch, IListBatch {
        public const int BatchCapacityLimit = 512;

        private static ListPool<T> _ListPool = new ListPool<T>(
            256, 4, 64, BatchCapacityLimit, 10240
        );

        protected DenseList<T> _DrawCalls = new DenseList<T>();

        public static void SetAllocator (UnorderedList<T>.Allocator allocator) {
            _ListPool.Allocator = allocator ?? UnorderedList<T>.Allocator.Default;
        }

        protected void Initialize (
            IBatchContainer container, int layer, Material material,
            bool addToContainer, int? capacity = null
        ) {
            _DrawCalls.ListPoolOrAllocator = _ListPool;
            _DrawCalls.Clear();
            base.Initialize(container, layer, material, addToContainer);
        }

        public static void AdjustPoolCapacities (
            int? itemSizeLimit, int? largeItemSizeLimit,
            int? smallPoolCapacity, int? largePoolCapacity
        ) {
            _ListPool.SmallPoolMaxItemSize = itemSizeLimit.GetValueOrDefault(_ListPool.SmallPoolMaxItemSize);
            _ListPool.LargePoolMaxItemSize = largeItemSizeLimit.GetValueOrDefault(_ListPool.LargePoolMaxItemSize);
            _ListPool.SmallPoolCapacity = smallPoolCapacity.GetValueOrDefault(_ListPool.SmallPoolCapacity);
            _ListPool.LargePoolCapacity = largePoolCapacity.GetValueOrDefault(_ListPool.LargePoolCapacity);
        }

        public static void ConfigureClearBehavior (bool enableFastClear) {
            _ListPool.FastClearEnabled = enableFastClear;
        }

        public int Count {
            get {
                return _DrawCalls.Count;
            }
        }

        public void EnsureCapacity (int capacity, bool lazy = false) {
            _DrawCalls.EnsureCapacity(capacity, lazy);
        }

        protected void Add (in T item) {
            _DrawCalls.Add(in item);
        }

        protected override void OnReleaseResources() {
            _DrawCalls.Dispose();
            base.OnReleaseResources();
        }
    }

    public sealed class ClearBatch : Batch {
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
            base.Issue(manager);
            manager.ApplyMaterial(Material, ref MaterialParameters);

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

        public static void AddNew (IBatchContainer container, int layer, Material material, Color? clearColor = null, float? clearZ = null, int? clearStencil = null) {
            if (container == null)
                throw new ArgumentNullException("container");

            var result = container.RenderManager.AllocateBatch<ClearBatch>();
            result.Initialize(container, layer, material, clearColor, clearZ, clearStencil);
            result.CaptureStack(0);
            result.Dispose();
        }
    }

    public sealed class SetScissorBatch : Batch {
        public Rectangle? Scissor;

        public void Initialize (IBatchContainer container, int layer, Material material, Rectangle? scissor) {
            base.Initialize(container, layer, material, true);
            Scissor = scissor;
        }

        protected override void Prepare (PrepareManager manager) {
        }

        public override void Issue (DeviceManager manager) {
            base.Issue(manager);

            var viewport = manager.Device.Viewport;
            var viewportRect = new Rectangle(viewport.X, viewport.Y, viewport.Width, viewport.Height);
            if (Scissor == null) {
                manager.Device.ScissorRectangle = viewportRect;
            } else {
                var intersected = Rectangle.Intersect(viewportRect, Scissor.Value);
                manager.Device.ScissorRectangle = intersected;
            }
        }

        public static void AddNew (IBatchContainer container, int layer, Material material, Rectangle? scissor) {
            if (container == null)
                throw new ArgumentNullException("container");

            var result = container.RenderManager.AllocateBatch<SetScissorBatch>();
            result.Initialize(container, layer, material, scissor);
            result.CaptureStack(0);
            result.Dispose();
        }
    }

    public sealed class SetViewportBatch : Batch {
        public Rectangle? Viewport;
        public DefaultMaterialSet MaterialSet;
        public bool UpdateViewTransform;

        public void Initialize (IBatchContainer container, int layer, Material material, Rectangle? viewport, DefaultMaterialSet materialSet) {
            base.Initialize(container, layer, material, true);
            Viewport = viewport;
            MaterialSet = materialSet;
        }

        protected override void Prepare (PrepareManager manager) {
        }

        public override void Issue (DeviceManager manager) {
            base.Issue(manager);

            Viewport newViewport;
            var rts = manager.Device.GetRenderTargets();
            if ((rts?.Length ?? 0) > 0) {
                var tex = (Texture2D)rts[0].RenderTarget;
                var deviceRect = new Rectangle(0, 0, tex.Width, tex.Height);
                if (Viewport.HasValue) {
                    var intersected = Rectangle.Intersect(deviceRect, Viewport.Value);
                    newViewport = new Viewport(intersected);
                } else {
                    newViewport = new Viewport(deviceRect);
                }
            } else {
                newViewport = new Viewport(Viewport ?? manager.Device.PresentationParameters.Bounds);
            }

            manager.SetViewport(newViewport);

            if (UpdateViewTransform)
                MaterialSet.SetViewTransform(ViewTransform.CreateOrthographic(newViewport));
        }

        public static void AddNew (IBatchContainer container, int layer, Material material, Rectangle? viewport, bool updateViewTransform = false, DefaultMaterialSet materialSet = null) {
            if (container == null)
                throw new ArgumentNullException("container");
            if (updateViewTransform && (materialSet == null))
                throw new ArgumentNullException("materialSet");

            var result = container.RenderManager.AllocateBatch<SetViewportBatch>();
            result.Initialize(container, layer, material, viewport, materialSet);
            result.UpdateViewTransform = updateViewTransform;
            result.CaptureStack(0);
            result.Dispose();
        }
    }

    public sealed class SetRenderTargetBatch : Batch {
        protected AutoRenderTarget AutoRenderTarget;
        protected RenderTarget2D RenderTarget;

        public void Initialize (IBatchContainer container, int layer, AutoRenderTarget autoRenderTarget) {
            base.Initialize(container, layer, null, true);
            AutoRenderTarget = autoRenderTarget;

            if (autoRenderTarget.IsDisposed)
                throw new ObjectDisposedException("renderTarget");
        }

        public void Initialize (IBatchContainer container, int layer, RenderTarget2D renderTarget) {
            base.Initialize(container, layer, null, true);
            RenderTarget = renderTarget;

            if (renderTarget.IsDisposed || renderTarget.IsContentLost)
                throw new ObjectDisposedException("renderTarget");
        }

        protected override void Prepare (PrepareManager manager) {
            if (AutoRenderTarget != null) {
                RenderTarget = AutoRenderTarget.Get();
            }
        }

        public override void Issue (DeviceManager manager) {
            base.Issue(manager);

            manager.SetRenderTarget(RenderTarget);
            RenderManager.ResetDeviceState(manager.Device);
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

    public abstract class SubBatchManager<TSelf, TDrawCall, TSubBatch>
        where TDrawCall : struct
        where TSubBatch : struct
    {
        public struct State {
            internal TDrawCall LastDrawCall;
            internal int StartOffset;
        }

        public static ListPool<TSubBatch> _SubListPool = new ListPool<TSubBatch>(
            256, 4, 32, 128, 512
        );

        public virtual void Clear (TSelf self, ref DenseList<TSubBatch> items) {
            items.Clear();
        }

        public virtual void Setup (TSelf self, ref DenseList<TSubBatch> items, int count) {
            items.Clear();
            items.ListPoolOrAllocator = _SubListPool;
            items.EnsureCapacity(count, true);
        }

        public virtual void Start (TSelf self, ref TDrawCall firstDc, out State currentState) {
            currentState = new State {
                LastDrawCall = firstDc,
                StartOffset = 0
            };
        }

        public virtual void Finish (TSelf self, ref State state, int count) {
            if (count <= state.StartOffset)
                return;
            CreateBatch(self, ref state.LastDrawCall, state.StartOffset, count - state.StartOffset);
        }

        protected abstract void CreateBatch (TSelf self, ref TDrawCall settings, int offset, int count);
        protected abstract bool KeyEquals (TSelf self, ref TDrawCall dc, ref TDrawCall last);

        public void Step (TSelf self, ref TDrawCall dc, ref State state, int i) {
            if (!KeyEquals(self, ref dc, ref state.LastDrawCall)) {
                CreateBatch(self, ref state.LastDrawCall, state.StartOffset, i - state.StartOffset);
                state.LastDrawCall = dc;
                state.StartOffset = i;
            }
        }
    }
}
