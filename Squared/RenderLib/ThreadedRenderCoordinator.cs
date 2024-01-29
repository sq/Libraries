using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Render.Internal;
using Squared.Threading;
using Squared.Util;

namespace Squared.Render {
    public interface ITraceCapturingDisposable : IDisposable {
        void AutoCaptureTraceback ();
    }

    public static class RenderCoordinatorExtensions {
        public static void ApplyChangesAfterPresent (this GraphicsDeviceManager gdm, RenderCoordinator rc) {
            // HACK: Wait until rendering has finished, then reset the device on the main thread
            var sc = SynchronizationContext.Current;
            rc.AfterPresent(() => {
                if (rc.EnableThreading && sc != null)
                    sc.Post((_) => gdm.ApplyChanges(), null);
                else
                    gdm.ApplyChanges();
            });
        }
    }

    public delegate void PendingDrawHandler (IBatchContainer container, DefaultMaterialSet materials, object userData);

    public sealed class RenderCoordinator : IDisposable {
        private static ThreadLocal<bool> IsDrawingOnThisThread = new ThreadLocal<bool>(false);

        struct CompletedPendingDraw {
            public object UserData;
            public ExceptionDispatchInfo Exception;
            public IFuture OnComplete;
            public RenderTarget2D RenderTarget;
        }

        sealed class PendingDraw {
            public RenderTarget2D RenderTarget;
            public DefaultMaterialSet Materials;
            public object UserData;
            public PendingDrawHandler Handler;
            public IFuture OnComplete;
            public ViewTransform? ViewTransform;
        }

        struct DrawTask : IWorkItem {
            public static WorkItemConfiguration Configuration =>
                new WorkItemConfiguration {
                    Priority = 1
                };

            public readonly Action<Frame> Callback;
            public readonly Frame Frame;

            public DrawTask (Action<Frame> callback, Frame frame) {
                Callback = callback;
                Frame = frame;
            }

            public void Execute (ThreadGroup group) {
                try {
                    IsDrawingOnThisThread.Value = true;
                    Monitor.Enter(Frame);
                    Callback(Frame);
                } finally {
                    IsDrawingOnThisThread.Value = false;
                    Monitor.Exit(Frame);
                }
            }
        }

        public readonly RenderManager Manager;

        /// <summary>
        /// If set to false, threads will not be used for rendering.
        /// </summary>
        public bool EnableThreading = true;
        /// <summary>
        /// You must acquire this lock before applying changes to the device, creating objects, or loading content.
        /// </summary>
        public readonly object CreateResourceLock;
        /// <summary>
        /// You must acquire this lock before rendering or resetting the device.
        /// </summary>
        public readonly object UseResourceLock;
        /// <summary>
        /// This lock is held during frame preparation.
        /// </summary>
        public readonly object PrepareLock = new object();
        /// <summary>
        /// Specifies an amount of artificial lag (in milliseconds) to add to GPU rendering operations
        /// </summary>
        public int IssueLag = 0;

        public readonly NativeAllocator AtlasAllocator = new NativeAllocator { Name = "Squared.Render.AtlasAllocator" };

        // Held during paint
        private readonly object DrawLock = new object();

        private bool _Running = true;
        private bool _ActualEnableThreading = true;
        private bool _EnableThreadedPrepare = true;
        private bool _EnableThreadedIssue = false;
        private object _FrameLock = new object();
        private Frame  _FrameBeingPrepared = null;

        private readonly IGraphicsDeviceService DeviceService;

        internal bool SynchronousDrawsEnabled = true;
        internal FrameTiming NextFrameTiming;

        private volatile bool IsResetting;
        private volatile bool FirstFrameSinceReset;

        public event EventHandler<string> OnLogMessage;

        private readonly Func<GameWindow> GetWindow;
        private readonly Func<bool> _SyncBeginDraw;
        private readonly Action _SyncEndDraw;
        private readonly Action<Frame> _ThreadedDraw;
        private readonly EventHandler<EventArgs> _OnAutoAllocatedTextureDisposed;
        private readonly DisposalQueue PendingDisposes = new DisposalQueue();
        private readonly ManualResetEvent _SynchronousDrawFinishedSignal = new ManualResetEvent(true);

        internal enum WorkPhases : int {
            NONE,
            Wait,
            BuildFrame,
            BeforePrepare,
            Prepare,
            BeginDraw,
            BeforeIssue,
            Issue,
            BeforePresent,
            Present,
            AfterPresent,
            SyncEndDraw,
            COUNT,
        }

        private readonly Stopwatch[] Stopwatches = new Stopwatch[(int)WorkPhases.COUNT];

        // Used to detect re-entrant painting (usually means that an
        //  exception was thrown on the render thread)
        private int _SynchronousDrawIsActive = 0;
        // Sometimes a new paint can be issued while we're blocked on
        //  a wait handle, because waits pump messages. We need to
        //  detect this and ensure another draw does not begin.
        private int _InsideDrawOperation = 0;

        // Lost devices can cause things to go horribly wrong if we're 
        //  using multithreaded rendering
        private bool _DeviceLost = false, _DeviceIsDisposed = false;

        private readonly UnorderedList<PendingDraw> PendingDrawQueue = new UnorderedList<PendingDraw>();
        private readonly UnorderedList<CompletedPendingDraw> CompletedPendingDrawQueue = new UnorderedList<CompletedPendingDraw>();
        private readonly Queue<Action> BeforePrepareQueue = new Queue<Action>();
        private readonly Queue<Action> BeforeIssueQueue = new Queue<Action>();
        private readonly Queue<Action> BeforePresentQueue = new Queue<Action>();
        private readonly Queue<Action> AfterPresentQueue = new Queue<Action>();

        private readonly ManualResetEvent PresentBegunSignal = new ManualResetEvent(false),
            PresentEndedSignal = new ManualResetEvent(false);
        private long PresentBegunWhen = 0, PresentEndedWhen = 0;

        public readonly ThreadGroup ThreadGroup;
        private readonly WorkQueue<DrawTask> DrawQueue;

        public event EventHandler DeviceReset, DeviceChanged;

        public bool IsDisposed { get; private set; }
        public bool IsFNA => Manager.DeviceManager.IsFNA;

        private long TimeOfLastResetOrDeviceChange = 0;

        private int IsDisposingResources = 0;

        public string GraphicsBackendName => Manager.DeviceManager.GraphicsBackendName;

        private readonly HashSet<Texture2D> AutoAllocatedTextureResources = 
            new HashSet<Texture2D>(ReferenceComparer<Texture2D>.Instance);

        internal void LogPrint (string text) {
            if (OnLogMessage != null)
                OnLogMessage(this, text);
        }

        internal void LogPrint<T1> (string format, T1 arg1) {
            if (OnLogMessage != null)
                OnLogMessage(this, string.Format(format, arg1));
        }

        internal void LogPrint<T1, T2> (string format, T1 arg1, T2 arg2) {
            if (OnLogMessage != null)
                OnLogMessage(this, string.Format(format, arg1, arg2));
        }

        internal void LogPrint<T1, T2, T3> (string format, T1 arg1, T2 arg2, T3 arg3) {
            if (OnLogMessage != null)
                OnLogMessage(this, string.Format(format, arg1, arg2, arg3));
        }

        internal void StartWorkPhase (WorkPhases phase) {
            var sw = Stopwatches[(int)phase];
            sw.Restart();
        }

        internal bool SuspendWorkPhase (WorkPhases phase) {
            var sw = Stopwatches[(int)phase];
            if (!sw.IsRunning)
                return false;
            sw.Stop();
            return true;
        }

        internal void ResumeWorkPhase (WorkPhases phase) {
            var sw = Stopwatches[(int)phase];
            sw.Start();
        }

        internal TimeSpan EndWorkPhase (WorkPhases phase) {
            var sw = Stopwatches[(int)phase];
            if (!sw.IsRunning)
                return default;

            sw.Stop();

#if DEBUG
            /*
            for (int i = 0; i < Stopwatches.Length; i++) {
                if (Stopwatches[i].IsRunning)
                    throw new Exception($"Stopwatch {(WorkPhases)i} was still running at the end of phase {phase}");
            }
            */
#endif

            return sw.Elapsed;
        }

        /// <summary>
        /// Constructs a render coordinator.
        /// </summary>
        /// <param name="manager">The render manager responsible for creating frames and dispatching them to the graphics device.</param>
        /// <param name="synchronousBeginDraw">The function responsible for synchronously beginning a rendering operation. This will be invoked on the rendering thread.</param>
        /// <param name="synchronousEndDraw">The function responsible for synchronously ending a rendering operation and presenting it to the screen. This will be invoked on the rendering thread.</param>
        public RenderCoordinator (
            RenderManager manager, Func<GameWindow> getWindow,
            Func<bool> synchronousBeginDraw, Action synchronousEndDraw
        ) {
            Manager = manager;
            ThreadGroup = manager.ThreadGroup;
            UseResourceLock = manager.UseResourceLock;
            CreateResourceLock = manager.CreateResourceLock;

            GetWindow = getWindow;
            _SyncBeginDraw = synchronousBeginDraw;
            _SyncEndDraw = synchronousEndDraw;
            _ThreadedDraw = ThreadedDraw;
            _OnAutoAllocatedTextureDisposed = OnAutoAllocatedTextureDisposed;

            DrawQueue = ThreadGroup.GetQueueForType<DrawTask>();

            RegisterForDeviceEvents();
            for (int i = 0; i < Stopwatches.Length; i++)
                Stopwatches[i] = new Stopwatch();
        }

        /// <summary>
        /// Constructs a render coordinator. A render manager and synchronous draw methods are automatically provided for you.
        /// </summary>
        /// <param name="deviceService"></param>
        public RenderCoordinator (
            IGraphicsDeviceService deviceService, Thread mainThread, ThreadGroup threadGroup,
            Func<GameWindow> getWindow, Func<bool> synchronousBeginDraw = null, Action synchronousEndDraw = null
        ) {
            DeviceService = deviceService;
            ThreadGroup = threadGroup;
            Manager = new RenderManager(deviceService.GraphicsDevice, mainThread, ThreadGroup);
            UseResourceLock = Manager.UseResourceLock;
            CreateResourceLock = Manager.CreateResourceLock;

            GetWindow = getWindow;
            _SyncBeginDraw = synchronousBeginDraw ?? DefaultBeginDraw;
            _SyncEndDraw = synchronousEndDraw ?? DefaultEndDraw;
            _ThreadedDraw = ThreadedDraw;
            _OnAutoAllocatedTextureDisposed = OnAutoAllocatedTextureDisposed;

            DrawQueue = ThreadGroup.GetQueueForType<DrawTask>();

            RegisterForDeviceEvents();

            deviceService.DeviceCreated += DeviceService_DeviceCreated;
            for (int i = 0; i < Stopwatches.Length; i++)
                Stopwatches[i] = new Stopwatch();
        }

        private void OnAutoAllocatedTextureDisposed (object sender, EventArgs e) {
            if (sender is Texture2D tex)
                lock (AutoAllocatedTextureResources)
                    AutoAllocatedTextureResources.Remove(tex);
        }

        public void RegisterAutoAllocatedTextureResource (Texture2D texture) {
            lock (AutoAllocatedTextureResources)
                AutoAllocatedTextureResources.Add(texture);

            texture.Disposing += _OnAutoAllocatedTextureDisposed;
        }

        public void ForEachAutoAllocatedTextureResource (Action<Texture2D> callback) {
            lock (AutoAllocatedTextureResources)
                foreach (var tex in AutoAllocatedTextureResources)
                    if (tex?.IsDisposed == false)
                        callback(tex);
        }

        public void ForEachAutoAllocatedTextureResource (Action<Texture2D, object> callback, object userData) {
            lock (AutoAllocatedTextureResources)
                foreach (var tex in AutoAllocatedTextureResources)
                    if (tex?.IsDisposed == false)
                        callback(tex, userData);
        }

        private void SetGraphicsDevice (GraphicsDevice device) {
            TimeOfLastResetOrDeviceChange = Time.Ticks;
            if (Manager.DeviceManager.Device != device)
                Manager.ChangeDevice(device);

            RegisterForDeviceEvents();

            if (DeviceChanged != null)
                DeviceChanged(this, EventArgs.Empty);
        }

        private void DeviceService_DeviceCreated (object sender, EventArgs e) {
            SetGraphicsDevice(DeviceService.GraphicsDevice);
        }

        /// <summary>
        /// Queues an operation to occur on the main thread immediately before prepare operations begin.
        /// </summary>
        public void BeforePrepare (Action action) {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            lock (BeforePrepareQueue)
                BeforePrepareQueue.Enqueue(action);
        }

        /// <summary>
        /// Queues an operation to occur on the render thread immediately before drawing operations begin.
        /// </summary>
        public void BeforeIssue (Action action) {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            lock (BeforeIssueQueue)
                BeforeIssueQueue.Enqueue(action);
        }

        /// <summary>
        /// Queues an operation to occur on the render thread immediately before Present, after all drawing
        ///  commands have been issued.
        /// </summary>
        public void BeforePresent (Action action) {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            lock (BeforePresentQueue)
                BeforePresentQueue.Enqueue(action);
        }

        /// <summary>
        /// Queues an operation to occur immediately after Present.
        /// </summary>
        public void AfterPresent (Action action) {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            lock (AfterPresentQueue)
                AfterPresentQueue.Enqueue(action);
        }

        private void RegisterForDeviceEvents () {
            Device.DeviceResetting += OnDeviceResetting;
            Device.DeviceReset += OnDeviceReset;
            Device.DeviceLost += OnDeviceLost;
            Device.Disposing += OnDeviceDisposing;
        }

        protected bool DefaultBeginDraw () {
            if (IsDisposed)
                return false;

            if (IsWaitingForDeviceToSettle)
                return false;

            if (Device.GraphicsDeviceStatus == GraphicsDeviceStatus.Normal) {
                RenderManager.ResetDeviceState(Device);
                return true;
            } else if (!_Running)
                return false;

            return false;
        }

        protected void DefaultEndDraw () {
            if (IsDisposed)
                return;

            if (IsWaitingForDeviceToSettle)
                return;

            var viewport = Device.Viewport;
            Device.Present(
#if !SDL2 || FNA // Ignore verbose Present() overload -flibit
                new Rectangle(0, 0, viewport.Width, viewport.Height),
                new Rectangle(0, 0, viewport.Width, viewport.Height),
                IntPtr.Zero
#endif
            );
        }

        protected void OnDeviceDisposing (object sender, EventArgs args) {
            _DeviceIsDisposed = true;
            _DeviceLost = true;
        }

        protected void OnDeviceLost (object sender, EventArgs args) {
            TimeOfLastResetOrDeviceChange = Time.Ticks;
            FirstFrameSinceReset = true;
            _DeviceLost = true;
            Manager.OnDeviceResetOrLost();
        }

        // We must acquire both locks before resetting the device to avoid letting the reset happen during a paint or content load operation.
        protected void OnDeviceResetting (object sender, EventArgs args) {

            TimeOfLastResetOrDeviceChange = Time.Ticks;
            FirstFrameSinceReset = true;

            if (!IsResetting) {
                IsResetting = true;

                Monitor.Enter(DrawLock);
                Monitor.Enter(UseResourceLock);
                Monitor.Enter(CreateResourceLock);
            }

            UniformBinding.HandleDeviceReset();
        }

        protected void EndReset () {
            if (Device == null) {
            }

            if (Device.IsDisposed) {
                _DeviceIsDisposed = true;
                return;
            }

            if (IsResetting) {
                Monitor.Exit(CreateResourceLock);
                Monitor.Exit(UseResourceLock);
                Monitor.Exit(DrawLock);

                IsResetting = false;
                FirstFrameSinceReset = true;
            }
        }

        protected void OnDeviceReset (object sender, EventArgs args) {
            TimeOfLastResetOrDeviceChange = Time.Ticks;

            if (IsResetting)
                EndReset();

            if (DeviceReset != null)
                DeviceReset(this, EventArgs.Empty);

            Manager.OnDeviceResetOrLost();
        }

        internal void NotifyWindowIsMoving () {
            // Suspend rendering operations until we have stopped moving for a bit
            TimeOfLastResetOrDeviceChange = Time.Ticks;
        }
                
        private void WaitForPendingWork () {
            if (IsDisposed)
                return;

            StartWorkPhase(WorkPhases.Wait);
            try {
                DrawQueue.WaitUntilDrained();
            } catch (DeviceLostException) {
                _DeviceLost = true;
            } catch (ObjectDisposedException) {
                if (Device.IsDisposed)
                    _Running = false;
                else
                    throw;
            }
            NextFrameTiming.Wait += EndWorkPhase(WorkPhases.Wait);
        }

        private bool WaitForActiveSynchronousDraw () {
            if (IsDisposed)
                return false;

            _SynchronousDrawFinishedSignal.WaitOne();
            return true;
        }

        public bool WaitForActiveDraws () {
            Threading.Profiling.Superluminal.BeginEvent("RenderCoordinator.WaitForActiveDraws");
            try {
                return WaitForActiveDraw() &&
                    WaitForActiveSynchronousDraw();
            } finally {
                Threading.Profiling.Superluminal.EndEvent();
            }
        }

        internal bool WaitForActiveDraw () {
            if (_ActualEnableThreading) {
                if (IsDrawingOnThisThread.Value)
                    throw new Exception("This thread is currently performing a draw");

                DrawQueue.WaitUntilDrained();
            } else
                return false;

            return true;
        }

        internal Frame BeginFrame (bool flushPendingDraws) {
            if (flushPendingDraws)
                RunPendingDraws();
            SynchronousDrawsEnabled = false;
            var frame = _FrameBeingPrepared = Manager.CreateFrame(this);
            return _FrameBeingPrepared;
        }

        private void PendingDrawSetupHandler (DeviceManager dm, object _pd) {
            var pd = (PendingDraw)_pd;

            if (!AutoRenderTarget.IsRenderTargetValid(pd.RenderTarget))
                throw new ObjectDisposedException("Render target for pending draw was disposed between prepare and issue");

            var vt = pd.ViewTransform ??
                ViewTransform.CreateOrthographic(pd.RenderTarget.Width, pd.RenderTarget.Height);
            pd.Materials.PushViewTransform(ref vt);
            dm.Device.Clear(Color.Transparent);
        }

        private void PendingDrawTeardownHandler (DeviceManager dm, object _pd) {
            var pd = (PendingDraw)_pd;
            pd.Materials.PopViewTransform();
        }

        private void RunPendingDraws () {
            lock (PendingDrawQueue)
                if (PendingDrawQueue.Count == 0)
                    return;

            int i = 0;
            BatchGroup bg = null;

            while (true) {
                PendingDraw pd;
                lock (PendingDrawQueue) {
                    if (PendingDrawQueue.Count == 0)
                        return;
                    pd = PendingDrawQueue.DangerousGetItem(0);
                    PendingDrawQueue.DangerousRemoveAt(0);
                }

                ExceptionDispatchInfo excInfo = null;
                try {
                    if (!AutoRenderTarget.IsRenderTargetValid(pd.RenderTarget))
                        throw new Exception("Render target for pending draw was disposed between queue and prepare");

                    if (!DoSynchronousDrawToRenderTarget(pd.RenderTarget, pd.Materials, pd.Handler, pd.UserData, ref pd.ViewTransform, "Pending Draw"))
                        throw new Exception("Unknown failure performing pending draw");
                } catch (Exception exc) {
                    excInfo = ExceptionDispatchInfo.Capture(exc);
                }
                    // throw new Exception("Unexpected error performing pending draw");

                if (pd.OnComplete != null) {
                    lock (CompletedPendingDrawQueue)
                        CompletedPendingDrawQueue.Add(new CompletedPendingDraw {
                            UserData = pd.UserData,
                            Exception = excInfo,
                            OnComplete = pd.OnComplete,
                            RenderTarget = pd.RenderTarget
                        });
                } else if (excInfo != null) {
                } else {
                }
            }
        }

        private void NotifyPendingDrawCompletions () {
            lock (CompletedPendingDrawQueue) {
                foreach (var cpd in CompletedPendingDrawQueue) {
                    if (cpd.Exception != null) {
                        cpd.OnComplete.SetResult2(null, cpd.Exception);
                        continue;
                    }

                    if (
                        (cpd.OnComplete.ResultType == cpd.UserData.GetType()) ||
                        (cpd.OnComplete.ResultType == typeof(object))
                    )
                        cpd.OnComplete.SetResult(cpd.UserData, null);
                    else if (
                        (cpd.OnComplete.ResultType == typeof(RenderTarget2D)) ||
                        (cpd.OnComplete.ResultType == typeof(Texture2D))
                    )
                        cpd.OnComplete.SetResult(cpd.RenderTarget, null);
                    else
                        cpd.OnComplete.SetResult(null, new Exception("No way to generate completion result for type " + cpd.OnComplete.ResultType));
                }
                CompletedPendingDrawQueue.Clear();
            }
        }

        public bool IsWaitingForDeviceToSettle {
            get {
                var now = Time.Ticks;
                var timeSinceReset = (now - TimeOfLastResetOrDeviceChange);
                var threshold = Time.TicksFromMsecs(500);
                if (timeSinceReset < threshold)
                    return true;

                return false;
            }
        }

        private bool ShouldSuppressResetRelatedDrawErrors {
            get {
                if (FirstFrameSinceReset || _DeviceIsDisposed || _DeviceLost)
                    return true;

                if (IsWaitingForDeviceToSettle)
                    return true;

                // HACK
                return false;
            }
        }

        public bool BeginDraw () {
            var ffsr = FirstFrameSinceReset;

            if (IsResetting || _DeviceIsDisposed) {
                EndReset();
                if (_DeviceIsDisposed) {
                    _DeviceIsDisposed = Device.IsDisposed;
                    if (!_DeviceIsDisposed)
                        _DeviceLost = false;
                }
                return false;
            }
            if (IsDisposed)
                return false;
            if (_InsideDrawOperation > 0)
                return false;

            if (IsWaitingForDeviceToSettle)
                return false;

            try {
                var suspended = SuspendWorkPhase(WorkPhases.BeginDraw);
                WaitForActiveSynchronousDraw();
                WaitForActiveDraw();
                if (suspended)
                    ResumeWorkPhase(WorkPhases.BeginDraw);
            } catch (Exception exc) {
                if (ShouldSuppressResetRelatedDrawErrors) {
                    if (
                        (exc is ObjectDisposedException) || 
                        (exc is InvalidOperationException) || 
                        (exc is NullReferenceException)
                    )
                        return false;
                }

                throw;
            }

            Interlocked.Increment(ref _InsideDrawOperation);
            try {
                _ActualEnableThreading = EnableThreading;

                // HACK: Fix problem where if the game is minimized, explorer.exe cannot dispatch a restore event successfully
                //  because we tend to be blocked waiting on a signal when windows expects us to be pumping messages instead.
                var gw = GetWindow();
                if (gw != null) {
                    var wflags = (SDL2.SDL.SDL_WindowFlags)SDL2.SDL.SDL_GetWindowFlags(gw.Handle);
                    if (
                        ((wflags & SDL2.SDL.SDL_WindowFlags.SDL_WINDOW_MINIMIZED) != default) ||
                        ((wflags & SDL2.SDL.SDL_WindowFlags.SDL_WINDOW_HIDDEN) != default)
                    )
                        _ActualEnableThreading = false;
                }

                PresentBegunSignal.Reset();

                bool result;
                if (_Running) {
                    if (DoThreadedIssue)
                        result = true;
                    else
                        result = _SyncBeginDraw();
                } else {
                    result = false;
                }

                if (ffsr && FirstFrameSinceReset && result) {
                    FirstFrameSinceReset = false;
                    UniformBinding.CollectGarbage();
                }

                return result;
            } finally {
                Interlocked.Decrement(ref _InsideDrawOperation);
            }
        }

        protected void CheckMainThread (bool allowThreading) {
            if (allowThreading)
                return;

            if (Thread.CurrentThread != Manager.MainThread)
                throw new ThreadStateException("Function running off main thread in single threaded mode");
        }

        protected void PrepareFrame (Frame frame, bool threaded) {
            if (DoThreadedPrepare)
                Monitor.Enter(PrepareLock);

            CheckMainThread(DoThreadedPrepare && threaded);

            Squared.Threading.Profiling.Superluminal.BeginEventFormat("Prepare Frame", "SRFrame #{0}", frame.Index, color: 0x10CF10);
            try {
                // TODO: Perform this asynchronously at the end of the previous draw
                Manager.ResetBufferGenerators(frame.Index);
                frame.Prepare(DoThreadedPrepare && threaded);
            } finally {
                if (DoThreadedPrepare)
                    Monitor.Exit(PrepareLock);
                Squared.Threading.Profiling.Superluminal.EndEvent();
            }
        }

        /// <summary>
        /// Finishes preparing the current Frame and readies it to be sent to the graphics device for rendering.
        /// </summary>
        protected void PrepareNextFrame (Frame newFrame, bool threaded) {
            PresentBegunSignal.Reset();

            if (newFrame != null)
                PrepareFrame(newFrame, threaded);
        }
        
        public bool DoThreadedPrepare {
            get {
                return _ActualEnableThreading && _EnableThreadedPrepare;
            }
            set {
                _EnableThreadedPrepare = value;
            }
        }
        
        public bool DoThreadedIssue { 
            get {
                return _ActualEnableThreading && _EnableThreadedIssue && (GraphicsBackendName != "OpenGL");
            }
            set {
                _EnableThreadedIssue = value;
            }
        }

        public void EndDraw () {
            if (IsDisposed)
                return;

            if (IsWaitingForDeviceToSettle)
                return;

            Interlocked.Increment(ref _InsideDrawOperation);
            try {
                StartWorkPhase(WorkPhases.SyncEndDraw);

                Frame newFrame;
                lock (_FrameLock)
                    newFrame = Interlocked.Exchange(ref _FrameBeingPrepared, null);

                StartWorkPhase(WorkPhases.BeforePrepare);
                RunBeforePrepareHandlers();
                NextFrameTiming.BeforePrepare = EndWorkPhase(WorkPhases.BeforePrepare);

                StartWorkPhase(WorkPhases.Prepare);
                PrepareNextFrame(newFrame, true);
                NextFrameTiming.Prepare = EndWorkPhase(WorkPhases.Prepare);
            
                if (_Running) {
                    if (DoThreadedIssue) {
                        lock (UseResourceLock)
                        if (!_SyncBeginDraw())
                            return;

                        DrawQueue.Enqueue(new DrawTask(_ThreadedDraw, newFrame));
                    } else {
                        ThreadedDraw(newFrame);
                    }

                    if (_DeviceLost) {
                        WaitForActiveDraw();

                        _DeviceLost = IsDeviceLost;
                    }
                }
            } finally {
                NextFrameTiming.SyncEndDraw = EndWorkPhase(WorkPhases.SyncEndDraw);
                Interlocked.Decrement(ref _InsideDrawOperation);
            }
        }

        private void RenderFrame (Frame frame, bool acquireLock) {
            try {
                // In D3D builds, this checks to see whether PIX is attached right now
                //  so that if it's not, we don't waste cpu time/gc pressure on trace messages
                Tracing.RenderTrace.BeforeFrame();

                StartWorkPhase(WorkPhases.BeforeIssue);
                RunBeforeIssueHandlers();
                NextFrameTiming.BeforeIssue = EndWorkPhase(WorkPhases.BeforeIssue);

                if (acquireLock)
                    Monitor.Enter(UseResourceLock);

                if (frame != null) {
                    _DeviceLost |= IsDeviceLost;

                    if (!_DeviceLost) {
                        StartWorkPhase(WorkPhases.Issue);
                        frame.Draw();
                    }
                }
            } finally {
                NextFrameTiming.Issue = EndWorkPhase(WorkPhases.Issue);
                if (acquireLock)
                    Monitor.Exit(UseResourceLock);
            }

            _DeviceLost |= IsDeviceLost;
        }

        private void DrainHandlerQueue (Queue<Action> queue) {
            while (true) {
                Action handler;

                lock (queue) {
                    if (queue.Count <= 0)
                        return;

                    handler = queue.Dequeue();
                }

                handler();
            }
        }

        private void RunBeforePrepareHandlers () {
            DrainHandlerQueue(BeforePrepareQueue);
        }

        private void RunBeforeIssueHandlers () {
            DrainHandlerQueue(BeforeIssueQueue);
        }

        private void RunBeforePresentHandlers () {
            DrainHandlerQueue(BeforePresentQueue);
        }

        private void RunAfterPresentHandlers () {
            NotifyPendingDrawCompletions();

            DrainHandlerQueue(AfterPresentQueue);
        }

        public bool TryWaitForPresentToStart (int millisecondsTimeout, out bool didPresentEnd, float delayMs = 1) {
            didPresentEnd = false;

            var now = Time.Ticks;
            var waitEnd = (long)(now + (millisecondsTimeout + delayMs) * Time.MillisecondInTicks);
            if (!PresentBegunSignal.WaitOne(millisecondsTimeout)) {
                return false;
            }

            var offset = Time.MillisecondInTicks * delayMs;
            var expected = PresentBegunWhen + offset;

            now = Time.Ticks;
            if (now >= waitEnd)
                return false;
            if (now < expected)
                Thread.SpinWait(50);
            now = Time.Ticks;

            while (now < expected) {
                if (now >= waitEnd)
                    return false;
                Thread.Yield();
                Thread.SpinWait(10);
                now = Time.Ticks;
            }

            didPresentEnd = PresentEndedSignal.WaitOne(0);
            return true;
        }

        private void SetPresentBegun () {
            var now = Time.Ticks;
            PresentBegunWhen = now;
            PresentEndedSignal.Reset();
            PresentBegunSignal.Set();
        }

        private void SetPresentEnded () {
            var now = Time.Ticks;
            PresentEndedWhen = now;
            PresentEndedSignal.Set();
        }

        protected void RenderFrameToDraw (Frame frameToDraw, bool endDraw) {
            Squared.Threading.Profiling.Superluminal.BeginEventFormat("Issue Frame", "SRFrame #{0}", frameToDraw.Index, color: 0x10CFCF);
            try {
                if (IssueLag > 0)
                    Thread.Sleep(IssueLag);

                PresentBegunSignal.Reset();

                if (frameToDraw != null) {
                    Manager.PrepareManager.AssertEmpty();
                    Manager.FlushBufferGenerators(frameToDraw.Index);
                    RenderFrame(frameToDraw, true);
                }

                if (endDraw) {
                    StartWorkPhase(WorkPhases.BeforePresent);
                    RunBeforePresentHandlers();
                    NextFrameTiming.BeforePresent = EndWorkPhase(WorkPhases.BeforePresent);

                    SetPresentBegun();
                    _SyncEndDraw();
                    SetPresentEnded();

                    StartWorkPhase(WorkPhases.AfterPresent);
                    RunAfterPresentHandlers();
                    NextFrameTiming.AfterPresent = EndWorkPhase(WorkPhases.AfterPresent);
                }
            } finally {
                if (frameToDraw != null)
                    frameToDraw.Dispose();
                Squared.Threading.Profiling.Superluminal.EndEvent();
            }
        }

        protected void ThreadedDraw (Frame frame) {
            var list = PendingDisposes.FreezeCurrentList();
            var rmList = Manager.PendingDisposes.FreezeCurrentList();

            try {
                if (!_Running)
                    return;

                CheckMainThread(DoThreadedIssue);

                lock (DrawLock)
                    RenderFrameToDraw(frame, true);

                _DeviceLost |= IsDeviceLost;
            } catch (InvalidOperationException ioe) {
                // XNA generates this on exit and we can't do anything about it
                if (ioe.Message == "An unexpected error has occurred.") {
                    ;
                } else if (ioe is ObjectDisposedException) {
                    if (Device.IsDisposed)
                        _Running = false;
                    else
                        throw;
                } else {
                    throw;
                }
            } catch (DeviceLostException) {
                _DeviceLost = true;
            } finally {
                lock (UseResourceLock)
                lock (CreateResourceLock) {
                    PendingDisposes.DisposeListContents(list);
                    Manager.PendingDisposes.DisposeListContents(rmList);
                }
            }
        }

        protected bool IsDeviceLost {
            get {
                var device = Device;
                if (device == null)
                    return false;

                return device.GraphicsDeviceStatus != GraphicsDeviceStatus.Normal;
            }
        }

        private bool ValidateDrawToRenderTarget (RenderTarget2D renderTarget, DefaultMaterialSet materials) {
            if (renderTarget == null)
                throw new ArgumentNullException("renderTarget");
            if (renderTarget.IsDisposed)
                return false;
            if (materials == null)
                throw new ArgumentNullException("materials");

            return true;
        }

        /// <summary>
        /// Queues a draw operation to the specified render target.
        /// The draw operation will occur at the start of the next frame before the frame itself has been rendered.
        /// </summary>
        /// <param name="onComplete">A Future instance that will have userData stored into it when rendering is complete.</param>
        public bool QueueDrawToRenderTarget (
            RenderTarget2D renderTarget, DefaultMaterialSet materials, 
            PendingDrawHandler handler, object userData = null, IFuture onComplete = null,
            ViewTransform? viewTransform = null
        ) {
            if (!ValidateDrawToRenderTarget(renderTarget, materials))
                return false;

            lock (PendingDrawQueue)
                PendingDrawQueue.Add(new PendingDraw {
                    RenderTarget = renderTarget,
                    Handler = handler,
                    Materials = materials,
                    UserData = userData,
                    OnComplete = onComplete,
                    ViewTransform = viewTransform
                });
            return true;
        }

        private bool DoSynchronousDrawToRenderTarget (
            RenderTarget2D renderTarget, 
            DefaultMaterialSet materials,
            Delegate drawBehavior, object userData,
            ref ViewTransform? viewTransform,
            string description
        ) {
            var oldLazyState = materials.LazyViewTransformChanges;
            try {
                materials.LazyViewTransformChanges = false;
                materials.ApplyViewTransform(materials.ViewTransform, true);
                using (var frame = Manager.CreateFrame(this)) {
                    frame.ChangeRenderTargets = false;
                    frame.Label = description;
                    if (viewTransform.HasValue)
                        materials.PushViewTransform(ref viewTransform);
                    else
                        materials.PushViewTransform(ViewTransform.CreateOrthographic(renderTarget.Width, renderTarget.Height));

                    try {
                        // FIXME: Should queued draws run here? They are probably meant to run in the next real frame

                        var singleBehavior = drawBehavior as Action<Frame>;
                        var multiBehavior = drawBehavior as PendingDrawHandler;
                        if (singleBehavior != null)
                            singleBehavior(frame);
                        else if (multiBehavior != null)
                            multiBehavior(frame, materials, userData);
                        else
                            throw new ArgumentException("Draw behavior was not of a compatible type");

                        RunBeforePrepareHandlers();
                        PrepareNextFrame(frame, false);

                        Manager.DeviceManager.SetRenderTarget(renderTarget);
                        RenderManager.ResetDeviceState(Device);
                        Device.Clear(Color.Transparent);

                        RenderFrameToDraw(frame, false);
                        // We don't have to push/pop anymore because the stacks are cleared at the end of a frame.

                        return true;
                    } finally {
                        materials.PopViewTransform();
                    }
                }
            } finally {
                materials.LazyViewTransformChanges = oldLazyState;
            }
        }

        /// <summary>
        /// Synchronously renders a complete frame to the specified render target.
        /// Automatically sets up the device's viewport and the view transform of your materials and restores them afterwards.
        /// </summary>
        public bool SynchronousDrawToRenderTarget (
            RenderTarget2D renderTarget, 
            DefaultMaterialSet materials, 
            Action<Frame> drawBehavior
        ) {
            return SynchronousDrawToRenderTargetSetup(renderTarget, materials, drawBehavior, null);
        }

        /// <summary>
        /// Synchronously renders a complete frame to the specified render target.
        /// Automatically sets up the device's viewport and the view transform of your materials and restores them afterwards.
        /// </summary>
        public bool SynchronousDrawToRenderTarget (
            RenderTarget2D renderTarget, 
            DefaultMaterialSet materials, 
            PendingDrawHandler drawBehavior,
            object userData = null
        ) {
            return SynchronousDrawToRenderTargetSetup(renderTarget, materials, drawBehavior, userData);
        }

        private bool SynchronousDrawToRenderTargetSetup (
            RenderTarget2D renderTarget, 
            DefaultMaterialSet materials, 
            Delegate drawBehavior, object userData
        ) {
            if (!ValidateDrawToRenderTarget(renderTarget, materials))
                return false;

            if (!SynchronousDrawsEnabled)
                throw new InvalidOperationException("Synchronous draws not available inside of Game.Draw");

            WaitForActiveDraw();

            var oldDrawIsActive = Interlocked.Exchange(ref _SynchronousDrawIsActive, 1);
            if (oldDrawIsActive != 0)
                throw new InvalidOperationException("A synchronous draw is already in progress");

            _SynchronousDrawFinishedSignal.Reset();

            WaitForActiveDraw();

            try {
                ViewTransform? vt = null;
                return DoSynchronousDrawToRenderTarget(renderTarget, materials, drawBehavior, userData, ref vt, "Synchronous Draw");
            } finally {
                _SynchronousDrawFinishedSignal.Set();
                Interlocked.Exchange(ref _SynchronousDrawIsActive, 0);
            }
        }

        public GraphicsDevice Device {
            get {
                return Manager.DeviceManager.Device;
            }
        }

        public Frame Frame {
            get {
                if (_Running) {
                    var f = _FrameBeingPrepared;
                    if (f != null)
                        return f;
                    else
                        throw new InvalidOperationException("Not preparing a frame");
                } else
                    throw new InvalidOperationException("Not running");
            }
        }

        public bool TryGetPreparingFrame (out Frame frame) {
            frame = null;

            if (!_Running)
                return false;

            frame = _FrameBeingPrepared;
            return (frame != null);
        }

        public void Dispose () {
            if (!_Running) {
                Manager.PendingDisposes.DisposeListContents(Manager.PendingDisposes.FreezeCurrentList());
                PendingDisposes.DisposeListContents(PendingDisposes.FreezeCurrentList());
                return;
            }

            if (IsDisposed)
                return;

            // HACK
            Manager.PrepareManager.Group.Dispose();

            _Running = false;
            IsDisposed = true;

            try {
                WaitForActiveDraws();

                Manager.PendingDisposes.DisposeListContents(Manager.PendingDisposes.FreezeCurrentList());
                PendingDisposes.DisposeListContents(PendingDisposes.FreezeCurrentList());
            } catch (ObjectDisposedException) {
            } catch (DeviceLostException) {
            } catch (DeviceNotResetException) {
            }
        }

        /// <summary>
        /// Queues a resource to be disposed after the next draw and then erases its storage.
        /// </summary>
        public void DisposeResource<T> (ref T resource) where T : IDisposable {
            DisposeResource(resource);
            resource = default(T);
        }

        /// <summary>
        /// Queues a resource to be disposed after the next draw.
        /// </summary>
        public void DisposeResource (IDisposable resource) {
            if (resource == null)
                return;

            if (resource is Texture2D tex)
                lock (CreateResourceLock)
                    AutoAllocatedTextureResources.Remove(tex);

            var tcd = resource as ITraceCapturingDisposable;
            tcd?.AutoCaptureTraceback();

            if (IsDisposed) {
                resource.Dispose();
                return;
            }

            PendingDisposes.Enqueue(resource);
        }
    }
}
