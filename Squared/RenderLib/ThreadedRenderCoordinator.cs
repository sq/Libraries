using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Render.Internal;
using Squared.Threading;

namespace Squared.Render {
    public class RenderCoordinator : IDisposable {
        public struct SafepointToken : IDisposable {
            public readonly RenderCoordinator Parent;

            internal SafepointToken (RenderCoordinator rc) {
                Parent = rc;
            }

            public void Dispose () {
                Parent.ExitSafepoint();
            }
        }

        struct DrawTask : IWorkItem {
            public readonly Action<Frame> Callback;
            public readonly Frame Frame;

            public DrawTask (Action<Frame> callback, Frame frame) {
                Callback = callback;
                Frame = frame;
            }

            public void Execute () {
                lock (Frame)
                    Callback(Frame);
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

        // Held during paint
        private readonly object DrawLock = new object();

        private bool _Running = true;
#if SDL2 // Disable threading -flibit
        // 8 months later and I continue to say: NOPE. -flibit
        private bool _ActualEnableThreading = false;
#else
        private bool _ActualEnableThreading = true;
#endif
        private object _FrameLock = new object();
        private Frame  _FrameBeingPrepared = null;

        internal bool SynchronousDrawsEnabled = true;

        private readonly Func<bool> _SyncBeginDraw;
        private readonly Action _SyncEndDraw;
        private readonly List<IDisposable> _PendingDisposes = new List<IDisposable>();
        private readonly ManualResetEventSlim _SynchronousDrawFinishedSignal = new ManualResetEventSlim(true);

        public readonly Stopwatch
            WorkStopwatch = new Stopwatch(),
            WaitStopwatch = new Stopwatch(),
            BeforePrepareStopwatch = new Stopwatch(),
            BeforePresentStopwatch = new Stopwatch();

        // Used to detect re-entrant painting (usually means that an
        //  exception was thrown on the render thread)
        private int _SynchronousDrawIsActive = 0;
        // Sometimes a new paint can be issued while we're blocked on
        //  a wait handle, because waits pump messages. We need to
        //  detect this and ensure another draw does not begin.
        private int _InsideDrawOperation = 0;

        // Lost devices can cause things to go horribly wrong if we're 
        //  using multithreaded rendering
        private bool _DeviceLost = false;

        private readonly ConcurrentQueue<Action> BeforePrepareQueue = new ConcurrentQueue<Action>();
        private readonly ConcurrentQueue<Action> BeforePresentQueue = new ConcurrentQueue<Action>();
        private readonly ConcurrentQueue<Action> AfterPresentQueue = new ConcurrentQueue<Action>();

        public readonly ThreadGroup ThreadGroup;
        private readonly WorkQueue<DrawTask> DrawQueue;

        public event EventHandler DeviceReset;

        public bool IsDisposed { get; private set; }

        /// <summary>
        /// Constructs a render coordinator.
        /// </summary>
        /// <param name="manager">The render manager responsible for creating frames and dispatching them to the graphics device.</param>
        /// <param name="synchronousBeginDraw">The function responsible for synchronously beginning a rendering operation. This will be invoked on the rendering thread.</param>
        /// <param name="synchronousEndDraw">The function responsible for synchronously ending a rendering operation and presenting it to the screen. This will be invoked on the rendering thread.</param>
        public RenderCoordinator (
            RenderManager manager, 
            Func<bool> synchronousBeginDraw, Action synchronousEndDraw
        ) {
            Manager = manager;
            ThreadGroup = manager.ThreadGroup;
            UseResourceLock = manager.UseResourceLock;
            CreateResourceLock = manager.CreateResourceLock;

            _SyncBeginDraw = synchronousBeginDraw;
            _SyncEndDraw = synchronousEndDraw;

            DrawQueue = ThreadGroup.GetQueueForType<DrawTask>();

            CoreInitialize();
        }

        /// <summary>
        /// Constructs a render coordinator. A render manager and synchronous draw methods are automatically provided for you.
        /// </summary>
        /// <param name="deviceService"></param>
        public RenderCoordinator (IGraphicsDeviceService deviceService, Thread mainThread, ThreadGroup threadGroup) {
            ThreadGroup = threadGroup;
            Manager = new RenderManager(deviceService.GraphicsDevice, mainThread, ThreadGroup);

            _SyncBeginDraw = DefaultBeginDraw;
            _SyncEndDraw = DefaultEndDraw;

            DrawQueue = ThreadGroup.GetQueueForType<DrawTask>();

            CoreInitialize();
        }

        /// <summary>
        /// Queues an operation to occur immediately before prepare operations begin.
        /// </summary>
        public void BeforePrepare (Action action) {
            BeforePrepareQueue.Enqueue(action);
        }

        /// <summary>
        /// Queues an operation to occur immediately before Present, after all drawing
        ///  commands have been issued.
        /// </summary>
        public void BeforePresent (Action action) {
            BeforePresentQueue.Enqueue(action);
        }

        /// <summary>
        /// Queues an operation to occur immediately after Present.
        /// </summary>
        public void AfterPresent (Action action) {
            AfterPresentQueue.Enqueue(action);
        }

        private void CoreInitialize () {
            Device.DeviceResetting += OnDeviceResetting;
            Device.DeviceReset += OnDeviceReset;
        }

        protected bool DefaultBeginDraw () {
            if (IsDisposed)
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

            var viewport = Device.Viewport;
            Device.Present(
#if !SDL2 // Ignore verbose Present() overload -flibit
                new Rectangle(0, 0, viewport.Width, viewport.Height),
                new Rectangle(0, 0, viewport.Width, viewport.Height),
                IntPtr.Zero
#endif
            );
        }

        // We must acquire both locks before resetting the device to avoid letting the reset happen during a paint or content load operation.
        protected void OnDeviceResetting (object sender, EventArgs args) {
            Monitor.Enter(CreateResourceLock);
            Monitor.Enter(UseResourceLock);
            Monitor.Enter(DrawLock);
        }

        protected void OnDeviceReset (object sender, EventArgs args) {
            Monitor.Exit(DrawLock);
            Monitor.Exit(UseResourceLock);
            Monitor.Exit(CreateResourceLock);

            if (DeviceReset != null)
                DeviceReset(this, EventArgs.Empty);
        }

        public void NotifyDeviceChanged () {
            if (DeviceReset != null)
                DeviceReset(this, EventArgs.Empty);
        }

        protected void ExitSafepoint () {
            Monitor.Exit(DrawLock);
        }

        public SafepointToken Safepoint () {
            WaitForActiveDraw();
            Monitor.Enter(DrawLock);

            return new SafepointToken(this);
        }

        private void WaitForPendingWork () {
            if (IsDisposed)
                return;

            var working = WorkStopwatch.IsRunning;
            if (working)
                WorkStopwatch.Stop();

            WaitStopwatch.Start();
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
            WaitStopwatch.Stop();

            if (working)
                WorkStopwatch.Start();
        }

        private bool WaitForActiveSynchronousDraw () {
            if (IsDisposed)
                return false;

            _SynchronousDrawFinishedSignal.Wait();
            return true;
        }

        private bool WaitForActiveDraws () {
            return WaitForActiveDraw() &&
                WaitForActiveSynchronousDraw();
        }

        public bool WaitForActiveDraw () {
            if (_ActualEnableThreading) {
                DrawQueue.WaitUntilDrained();
            } else
                return false;

            return true;
        }

        public Frame BeginFrame () {
            return _FrameBeingPrepared = Manager.CreateFrame();
        }

        public bool BeginDraw () {
            if (IsDisposed)
                return false;
            if (_InsideDrawOperation > 0)
                return false;

            WaitForActiveSynchronousDraw();
            WaitForActiveDraw();

            Interlocked.Increment(ref _InsideDrawOperation);
            try {
                _ActualEnableThreading = EnableThreading;

                bool result;
                if (_Running) {
                    if (DoThreadedIssue)
                        result = true;
                    else
                        result = _SyncBeginDraw();
                } else {
                    result = false;
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

            try {
                RunBeforePrepareHandlers();
                Manager.ResetBufferGenerators(frame.Index);
                frame.Prepare(DoThreadedPrepare && threaded);
            } finally {
                if (DoThreadedPrepare)
                    Monitor.Exit(PrepareLock);
            }
        }

        /// <summary>
        /// Finishes preparing the current Frame and readies it to be sent to the graphics device for rendering.
        /// </summary>
        protected void PrepareNextFrame (Frame newFrame, bool threaded) {
            if (newFrame != null)
                PrepareFrame(newFrame, threaded);
        }
        
        protected bool DoThreadedPrepare {
            get {
                // FIXME: With the old BufferGenerator, this caused random bitmapbatch corruption
                return _ActualEnableThreading;
            }
        }
        
        protected bool DoThreadedIssue { 
            get {
                return _ActualEnableThreading;
            }
        }

        public void EndDraw () {
            if (IsDisposed)
                return;

            Interlocked.Increment(ref _InsideDrawOperation);
            try {
                Frame newFrame;
                lock (_FrameLock)
                    newFrame = Interlocked.Exchange(ref _FrameBeingPrepared, null);

                PrepareNextFrame(newFrame, true);
            
                if (_Running) {
                    if (DoThreadedIssue) {
                        lock (UseResourceLock)
                        if (!_SyncBeginDraw())
                            return;

                        DrawQueue.Enqueue(new DrawTask(ThreadedDraw, newFrame));
                        ThreadGroup.NotifyQueuesChanged();
                    } else {
                        ThreadedDraw(newFrame);
                    }

                    if (_DeviceLost) {
                        WaitForActiveDraw();

                        _DeviceLost = IsDeviceLost;
                    }
                }
            } finally {
                Interlocked.Decrement(ref _InsideDrawOperation);
            }
        }

        private void RenderFrame (Frame frame, bool acquireLock) {
            if (acquireLock)
                Monitor.Enter(UseResourceLock);

            try {
                // In D3D builds, this checks to see whether PIX is attached right now
                //  so that if it's not, we don't waste cpu time/gc pressure on trace messages
                Tracing.RenderTrace.BeforeFrame();

                if (frame != null) {
                    _DeviceLost |= IsDeviceLost;

                    if (!_DeviceLost)
                        frame.Draw();
                }
            } finally {
                if (acquireLock)
                    Monitor.Exit(UseResourceLock);
            }

            _DeviceLost |= IsDeviceLost;
        }

        protected void RunBeforePrepareHandlers () {
            BeforePrepareStopwatch.Start();

            while (BeforePrepareQueue.Count > 0) {
                Action beforePrepare;
                if (!BeforePrepareQueue.TryDequeue(out beforePrepare))
                    continue;

                beforePrepare();
            }

            BeforePrepareStopwatch.Stop();
        }

        protected void RunBeforePresentHandlers () {
            BeforePresentStopwatch.Start();

            while (BeforePresentQueue.Count > 0) {
                Action beforePresent;
                if (!BeforePresentQueue.TryDequeue(out beforePresent))
                    continue;

                beforePresent();
            }

            BeforePresentStopwatch.Stop();
        }

        protected void RunAfterPresentHandlers () {
            while (AfterPresentQueue.Count > 0) {
                Action afterPresent;
                if (!AfterPresentQueue.TryDequeue(out afterPresent))
                    continue;

                afterPresent();
            }
        }

        protected void RenderFrameToDraw (Frame frameToDraw, bool endDraw) {
            try {
                if (frameToDraw != null) {                    
                    Manager.FlushBufferGenerators(frameToDraw.Index);    
                    RenderFrame(frameToDraw, true);
                }

                if (endDraw) {
                    RunBeforePresentHandlers();
                    _SyncEndDraw();
                }

                FlushPendingDisposes();

                if (endDraw) {
                    RunAfterPresentHandlers();
                }
            } finally {
                if (frameToDraw != null)
                    frameToDraw.Dispose();
            }
        }

        protected void ThreadedDraw (Frame frame) {
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

        /// <summary>
        /// Synchronously renders a complete frame to the specified render target.
        /// Automatically sets up the device's viewport and the view transform of your materials and restores them afterwards.
        /// </summary>
        public bool SynchronousDrawToRenderTarget (RenderTarget2D renderTarget, DefaultMaterialSet materials, Action<Frame> drawBehavior) {
            if (renderTarget.IsDisposed)
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
                using (var frame = Manager.CreateFrame()) {
                    materials.PushViewTransform(ViewTransform.CreateOrthographic(renderTarget.Width, renderTarget.Height));

                    ClearBatch.AddNew(frame, int.MinValue, materials.Clear, clearColor: Color.Transparent);

                    drawBehavior(frame);

                    PrepareNextFrame(frame, false);

                    var oldRenderTargets = Device.GetRenderTargets();
                    var oldViewport = Device.Viewport;
                    try {
                        Device.SetRenderTarget(renderTarget);
                        RenderManager.ResetDeviceState(Device);
                        Device.Viewport = new Viewport(0, 0, renderTarget.Width, renderTarget.Height);

                        RenderFrameToDraw(frame, false);
                    } finally {
                        Device.SetRenderTargets(oldRenderTargets);
                        materials.PopViewTransform();
                        Device.Viewport = oldViewport;
                    }
                }

                return true;
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

        public void Dispose () {
            if (!_Running) {
                FlushPendingDisposes();
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

                FlushPendingDisposes();
            } catch (ObjectDisposedException) {
            } catch (DeviceLostException) {
            } catch (DeviceNotResetException) {
            }
        }

        // TODO: Move this
        internal static void FlushDisposeList (List<IDisposable> list) {
            IDisposable[] pds = null;

            lock (list) {
                if (list.Count == 0)
                    return;

                // Prevents a deadlock from recursion
                pds = list.ToArray();
                list.Clear();
            }

            foreach (var pd in pds) {
                try {
                    pd.Dispose();
                } catch (ObjectDisposedException) {
                }
            }
        }

        private void FlushPendingDisposes () {
            FlushDisposeList(_PendingDisposes);

            Manager.FlushPendingDisposes();
        }

        public void DisposeResource (IDisposable resource) {
            if (resource == null)
                return;

            if (IsDisposed) {
                resource.Dispose();
                return;
            }

            lock (_PendingDisposes)
                _PendingDisposes.Add(resource);
        }
    }
}
