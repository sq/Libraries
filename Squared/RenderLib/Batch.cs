using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Util;

namespace Squared.Render {
    public abstract class Batch : IDisposable {
        public static class IdForType<T> where T : Batch {
            public static readonly int Id;

            static IdForType () {
                lock (Types.IdForType) {
                    if (!Types.IdForType.TryGetValue(typeof(T), out var id))
                        id = Types.AssignIdLocked(typeof(T));

                    Id = id;
                }
            }
        }

        public static class Types {
            internal static int NextTypeId;
            internal static Dictionary<Type, int> IdForType = new Dictionary<Type, int>(new ReferenceComparer<Type>());
            private static readonly List<Type> _All = new List<Type>();
            public static Type[] All;

            static Types () {
                var tBatch = typeof(Batch);
                foreach (var type in tBatch.Assembly.GetTypes()) {
                    if (!tBatch.IsAssignableFrom(type))
                        continue;
                    if (type.IsAbstract)
                        continue;
                    if (type.IsGenericTypeDefinition)
                        continue;

                    IdForType[type] = NextTypeId++;
                    _All.Add(type);
                }
                All = _All.ToArray();
            }

            internal static int AssignIdLocked (Type type) {
                var id = NextTypeId++;
                IdForType[type] = id;
                _All.Add(type);
                Volatile.Write(ref All, _All.ToArray());
                return id;
            }
        }

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
            internal void Validate (Batch batch, bool enqueuing) {
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
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

        public static bool CaptureStackTraces = false;
        public static bool ThrowOnBatchLeak = false;

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
        internal bool Released;
        internal IBatchPool Pool;

        internal UnorderedList<Batch> BatchesCombinedIntoThisOne = null;

        internal volatile Threading.IFuture SuspendFuture = null;

        private static long LifetimeBatchCount = 0;
        private static long NextInstanceId = 0;

        protected PrepareState State;

        public readonly int TypeId;

        protected Batch () {
            var thisType = GetType();
            TypeId = Types.IdForType[thisType];

            State = new PrepareState();

            InstanceId = Interlocked.Increment(ref NextInstanceId);
        }

#if DEBUG
        ~Batch () {
            if (Released)
                return;

            var msg = "{GetType().Name} was not released. Allocated at {StackTrace}";
            Debug.WriteLine(msg);
            if (ThrowOnBatchLeak)
                throw new Exception(msg);
        }
#endif

        /// <summary>
        /// Debugging property that renders the nesting hierarchy that leads to this batch
        /// </summary>
        public string Hierarchy {
            get {
                var sb = new StringBuilder();
                var b = this;
                while (b != null) {
                    if (sb.Length > 0)
                        sb.Append(" <- ");
                    sb.Append(b.ToString());
                    b = b.Container as Batch;
                }
                return sb.ToString();
            }
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

        internal bool AreParametersEqual (ref MaterialParameterValues rhs) {
            return MaterialParameters.Equals(ref rhs);
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
            Layer = layer;
            Material = material;
            MaterialParameters.Clear();

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

            Interlocked.Increment(ref LifetimeBatchCount);
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

        public void Dispose () {
            // Does nothing
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
        protected virtual void MarkAsIssued (DeviceManager manager) {
            WaitForSuspend();

            State.IsIssued = true;
            lock (manager.ReleaseQueue)
                manager.ReleaseQueue.Add(this);
        }

        // This is where you send commands to the video card to render your batch.
        public virtual void Issue (DeviceManager manager) {
            WaitForSuspend();

            State.IsIssued = true;
            manager.ReleaseQueue.Add(this);
        }

        protected virtual void OnReleaseResources () {
        }

        public void ReleaseResources () {
            if (SuspendFuture != null)
                SuspendFuture = null;

            if (Released)
                throw new ObjectDisposedException("Batch");

            if (State.IsPrepareQueued)
                throw new Exception("Batch currently queued for prepare");
            else if (!State.IsInitialized)
                throw new Exception("Batch uninitialized");
            Released = true;

            State.IsPrepared = false;
            State.IsInitialized = false;
            Container = null;
            Material = null;

            OnReleaseResources();

            StackTrace = null;
            Thread.MemoryBarrier();

            Pool?.Release(this);
        }

        /// <summary>
        /// Notifies the render manager that it should release the batch once the current frame is done drawing.
        /// You may opt to avoid calling this method in order to reuse a batch across multiple frames.
        /// </summary>
        public void Resume () {
            if (SuspendFuture != null)
                SuspendFuture.SetResult(Threading.NoneType.None, null);
            else
                throw new InvalidOperationException("Not suspended");
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
                return Volatile.Read(ref LifetimeBatchCount);
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

        protected virtual string FormatName () => Name;

        public override string ToString () {
            if (Name != null)
                return string.Format("{0} '{5}' #{1} {2} layer={4} material={3}", GetType().Name, InstanceId, StateString, Material, Layer, FormatName());
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
            320, 16, 64, BatchCapacityLimit, 10240
        );

        protected DenseList<T> _DrawCalls = new DenseList<T>();
        private static bool _CanFastClearDrawCalls = false;

        public ListBatch ()
            : base () 
        {
            _DrawCalls.ListPool = _ListPool;
        }

        public T FirstDrawCall {
            get {
                return _DrawCalls.FirstOrDefault();
            }
        }

        public T LastDrawCall {
            get {
                return _DrawCalls.LastOrDefault();
            }
        }

        protected void Initialize (
            IBatchContainer container, int layer, Material material,
            bool addToContainer, int? capacity = null
        ) {
            if (_CanFastClearDrawCalls)
                _DrawCalls.UnsafeFastClear();
            else
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
            _CanFastClearDrawCalls = enableFastClear;
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

        protected void Add (ref T item) {
            _DrawCalls.Add(ref item);
        }

        protected override void OnReleaseResources() {
            _DrawCalls.Dispose();
            base.OnReleaseResources();
        }

        public override string ToString () {
            if (Name != null)
                return string.Format("{0} x{6} '{5}' #{1} {2} layer={4} material={3}", GetType().Name, InstanceId, StateString, Material, Layer, FormatName(), Count);
            else
                return string.Format("{0} x{5} #{1} {2} layer={4} material={3}", GetType().Name, InstanceId, StateString, Material, Layer, Count);
        }
    }

    public sealed class ClearBatch : Batch {
        public Color? ClearColor;
        public Vector4? ClearValue;
        public float? ClearZ;
        public int? ClearStencil;

        public void Initialize (
            IBatchContainer container, int layer, Material material, 
            Color? clearColor, float? clearZ, int? clearStencil, Vector4? clearValue
        ) {
            base.Initialize(container, layer, material, true);
            ClearColor = clearColor;
            ClearValue = clearValue;
            ClearZ = clearZ;
            ClearStencil = clearStencil;
            if (ClearColor.HasValue && clearValue.HasValue)
                throw new ArgumentException("Cannot specify both clear color and clear value");

            if (!(clearColor.HasValue || clearValue.HasValue || clearZ.HasValue || clearStencil.HasValue))
                throw new ArgumentException("You must specify at least one of clearColor, clearZ and clearStencil.");
        }

        protected override void Prepare (PrepareManager manager) {
        }

        public override void Issue (DeviceManager manager) {
            base.Issue(manager);
            manager.ApplyMaterial(Material, ref MaterialParameters);

            var clearOptions = default(ClearOptions);

            if (ClearColor.HasValue || ClearValue.HasValue)
                clearOptions |= ClearOptions.Target;
            if (ClearZ.HasValue)
                clearOptions |= ClearOptions.DepthBuffer;
            if (ClearStencil.HasValue)
                clearOptions |= ClearOptions.Stencil;

            if (ClearValue.HasValue)
                manager.Device.Clear(
                    clearOptions,
                    ClearValue.GetValueOrDefault(new Vector4(0, 0, 0, 1)), ClearZ.GetValueOrDefault(0), ClearStencil.GetValueOrDefault(0)
                );
            else
                manager.Device.Clear(
                    clearOptions,
                    ClearColor.GetValueOrDefault(Color.Black), ClearZ.GetValueOrDefault(0), ClearStencil.GetValueOrDefault(0)
                );
        }

        public static void AddNew (IBatchContainer container, int layer, Material material, Color? clearColor = null, float? clearZ = null, int? clearStencil = null, Vector4? clearValue = null) {
            if (container == null)
                throw new ArgumentNullException("container");

            var result = container.RenderManager.AllocateBatch<ClearBatch>();
            result.Initialize(container, layer, material, clearColor, clearZ, clearStencil, clearValue);
            result.CaptureStack(0);
            result.Dispose();
        }
    }

    public sealed class ModifyViewTransformBatch : Batch {
        public DefaultMaterialSet Materials;
        public ViewTransformModifier Modifier;
        public object UserData;

        public void Initialize (IBatchContainer container, int layer, Material material, DefaultMaterialSet materialSet, ViewTransformModifier modifier, object userData) {
            base.Initialize(container, layer, material, true);
            Materials = materialSet;
            Modifier = modifier;
            UserData = userData;
        }

        protected override void Prepare (PrepareManager manager) {
        }

        public override void Issue (DeviceManager manager) {
            base.Issue(manager);

            var vt = Materials.ViewTransform;
            Modifier(ref vt, UserData);
            Materials.SetViewTransform(ref vt);
        }

        public static void AddNew (IBatchContainer container, int layer, DefaultMaterialSet materialSet, ViewTransformModifier modifier, object userData = null) {
            if (container == null)
                throw new ArgumentNullException("container");

            var result = container.RenderManager.AllocateBatch<ModifyViewTransformBatch>();
            result.Initialize(container, layer, null, materialSet, modifier, userData);
            result.CaptureStack(0);
            result.Dispose();
        }
    }

    public sealed class SetScissorBatch : Batch {
        public Rectangle? Scissor;
        public bool Intersect;

        public void Initialize (IBatchContainer container, int layer, Material material, Rectangle? scissor, bool intersect) {
            base.Initialize(container, layer, material, true);
            Scissor = scissor;
            Intersect = intersect;
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
                var intersected = Rectangle.Intersect(
                    Intersect ? manager.Device.ScissorRectangle : viewportRect, 
                    Scissor.Value
                );
                manager.Device.ScissorRectangle = intersected;
            }
        }

        public static void AddNew (IBatchContainer container, int layer, Material material, Rectangle? scissor, bool intersect) {
            if (container == null)
                throw new ArgumentNullException("container");

            var result = container.RenderManager.AllocateBatch<SetScissorBatch>();
            result.Initialize(container, layer, material, scissor, intersect);
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
            items.UnsafeFastClear();
        }

        public virtual void Setup (TSelf self, ref DenseList<TSubBatch> items, int count) {
            items.UnsafeFastClear();
            items.ListPool = _SubListPool;
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
