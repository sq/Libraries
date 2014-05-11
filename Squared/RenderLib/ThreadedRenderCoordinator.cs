using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Render.Internal;

namespace Squared.Render {
    public class RenderCoordinator : IDisposable {
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

        private bool _Running = true;
        private bool _ActualEnableThreading = true;
        private Frame _FrameBeingPrepared = null;
        private Frame _FrameBeingDrawn = null;

        private readonly Func<bool> _SyncBeginDraw;
        private readonly Action _SyncEndDraw;
        private readonly List<IDisposable> _PendingDisposes = new List<IDisposable>();

        private WorkerThread _DrawThread;
        public readonly Stopwatch
            WorkStopwatch = new Stopwatch(),
            WaitStopwatch = new Stopwatch();

        // Used to detect re-entrant painting (usually means that an
        //  exception was thrown on the render thread)
        private int _DrawDepth = 0;

        // Lost devices can cause things to go horribly wrong if we're 
        //  using multithreaded rendering
        private bool _DeviceLost = false;

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
            UseResourceLock = manager.UseResourceLock;
            CreateResourceLock = manager.CreateResourceLock;

            _SyncBeginDraw = synchronousBeginDraw;
            _SyncEndDraw = synchronousEndDraw;

            CoreInitialize();
        }

        /// <summary>
        /// Constructs a render coordinator. A render manager and synchronous draw methods are automatically provided for you.
        /// </summary>
        /// <param name="deviceService"></param>
        public RenderCoordinator (IGraphicsDeviceService deviceService, Thread mainThread) {
            Manager = new RenderManager(deviceService.GraphicsDevice, mainThread);

            _SyncBeginDraw = DefaultBeginDraw;
            _SyncEndDraw = DefaultEndDraw;

            CoreInitialize();
        }

        public ThreadPriority ThreadPriority {
            get {
                return _DrawThread.Thread.Priority;
            }
            set {
                _DrawThread.Thread.Priority = value;
            }
        }

        private void CoreInitialize () {
            _DrawThread = new WorkerThread(ThreadedDraw);

            Device.DeviceResetting += OnDeviceResetting;
            Device.DeviceReset += OnDeviceReset;
        }

        protected bool DefaultBeginDraw () {
            if (Device.GraphicsDeviceStatus == GraphicsDeviceStatus.Normal)
                return true;
            else if (!_Running)
                return false;

            return false;
        }

        protected void DefaultEndDraw () {
            var viewport = Device.Viewport;
            Device.Present(
#if !SDL2
                // TODO: Check if we _really_ have to implement this for MG-SDL2 -flibit
                new Rectangle(0, 0, viewport.Width, viewport.Height),
                new Rectangle(0, 0, viewport.Width, viewport.Height),
                IntPtr.Zero
#endif
            );
        }

        // We must acquire both locks before resetting the device to avoid letting the reset happen during a paint or content load operation.
        protected void OnDeviceResetting (object sender, EventArgs args) {
            Monitor.Enter(UseResourceLock);
            Monitor.Enter(CreateResourceLock);
        }

        protected void OnDeviceReset (object sender, EventArgs args) {
            Monitor.Exit(UseResourceLock);
            Monitor.Exit(CreateResourceLock);
        }

        private void WaitForPendingWork () {
            var working = WorkStopwatch.IsRunning;
            if (working)
                WorkStopwatch.Stop();

            WaitStopwatch.Start();
            try {
                _DrawThread.WaitForPendingWork();
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

        public void WaitForActiveDraw () {
            if (_DrawDepth > 0)
                if (_ActualEnableThreading)
                    WaitForPendingWork();
        }

        public bool BeginDraw () {
            if (Interlocked.Increment(ref _DrawDepth) > 1)
                if (_ActualEnableThreading)
                    WaitForPendingWork();

            _ActualEnableThreading = EnableThreading;

            if (_Running) {
                _FrameBeingPrepared = Manager.CreateFrame();

                if (DoThreadedIssue)
                    return true;
                else
                    return _SyncBeginDraw();
            } else {
                return false;
            }
        }

        protected void CheckMainThread (bool allowThreading) {
            if (allowThreading)
                return;

            if (Thread.CurrentThread != Manager.MainThread)
                throw new ThreadStateException("Function running off main thread in single threaded mode");
        }

        protected void PrepareFrame (Frame frame) {
            if (DoThreadedPrepare)
                Monitor.Enter(PrepareLock);

            CheckMainThread(DoThreadedPrepare);

            try {
                Manager.ResetBufferGenerators();
                frame.Prepare(DoThreadedPrepare);
            } finally {
                if (DoThreadedPrepare)
                    Monitor.Exit(PrepareLock);
            }
        }

        /// <summary>
        /// Finishes preparing the current Frame and readies it to be sent to the graphics device for rendering.
        /// </summary>
        protected void PrepareNextFrame () {
            var newFrame = Interlocked.Exchange(ref _FrameBeingPrepared, null);

            if (newFrame != null)
                PrepareFrame(newFrame);

            WaitForActiveDraw();

            var oldFrame = Interlocked.Exchange(ref _FrameBeingDrawn, newFrame);

            if (oldFrame != null)
                oldFrame.Dispose();
        }
        
        protected bool DoThreadedPrepare {
            get {
                return _ActualEnableThreading;
            }
        }
        
        protected bool DoThreadedIssue { 
            get {
#if PSM
                return false;
#else
                return _ActualEnableThreading;
#endif
            }
        }

        public void EndDraw () {
            PrepareNextFrame();
            
            if (_Running) {
                if (DoThreadedIssue) {
                    lock (UseResourceLock)
                        if (!_SyncBeginDraw())
                            return;

                    _DrawThread.RequestWork();
                } else {
                    ThreadedDraw(_DrawThread);
                }

                if (_DeviceLost) {
                    WaitForActiveDraw();

                    _DeviceLost = IsDeviceLost;
                }
            } else {
                Interlocked.Decrement(ref _DrawDepth);
            }
        }

        protected void RenderFrame (Frame frame, bool acquireLock) {
            if (acquireLock)
                Monitor.Enter(UseResourceLock);

            try {
                // In D3D builds, this checks to see whether PIX is attached right now
                //  so that if it's not, we don't waste cpu time/gc pressure on trace messages
                Tracing.RenderTrace.BeforeFrame();

                if (frame != null) {
                    using (frame) {
                        _DeviceLost |= IsDeviceLost;

                        if (!_DeviceLost)
                            frame.Draw();
                    }
                }
            } finally {
                if (acquireLock)
                    Monitor.Exit(UseResourceLock);
            }

            _DeviceLost |= IsDeviceLost;
        }

        protected void RenderFrameToDraw () {
            var frameToDraw = Interlocked.Exchange(ref _FrameBeingDrawn, null);

            if (frameToDraw != null)
                RenderFrame(frameToDraw, true);
        }

        protected void ThreadedDraw (WorkerThread thread) {
            if (!_Running)
                return;

            CheckMainThread(DoThreadedIssue);

            try {
                RenderFrameToDraw();

                _SyncEndDraw();

                FlushPendingDisposes();

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
                Interlocked.Decrement(ref _DrawDepth);
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
        public void SynchronousDrawToRenderTarget (RenderTarget2D renderTarget, DefaultMaterialSet materials, Action<Frame> drawBehavior) {
            using (var frame = Manager.CreateFrame()) {
                lock (UseResourceLock) {
                    materials.PushViewTransform(ViewTransform.CreateOrthographic(renderTarget.Width, renderTarget.Height));

                    ClearBatch.AddNew(frame, int.MinValue, materials.Clear, clearColor: Color.Transparent);

                    drawBehavior(frame);

                    PrepareFrame(frame);

                    var oldRenderTargets = Device.GetRenderTargets();
                    var oldViewport = Device.Viewport;
                    try {
                        Device.SetRenderTarget(renderTarget);
                        Device.Viewport = new Viewport(0, 0, renderTarget.Width, renderTarget.Height);

                        RenderFrame(frame, false);
                    } finally {
                        Device.SetRenderTargets(oldRenderTargets);
                        materials.PopViewTransform();
                        Device.Viewport = oldViewport;
                    }
                }
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

            _Running = false;

            try {
                WaitForActiveDraw();

                if (_DrawThread != null) {
                    _DrawThread.Dispose();
                    _DrawThread = null;
                }

                FlushPendingDisposes();
            } catch (ObjectDisposedException) {
            } catch (DeviceLostException) {
#if !SDL2
            } catch (DeviceNotResetException) {
#endif
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

            lock (_PendingDisposes)
                _PendingDisposes.Add(resource);
        }
    }
    
#if PSM
    class DeviceLostException : Exception {
    }
#endif
}
