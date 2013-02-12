using System;
using System.Collections.Generic;
using System.Linq;
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
        public object CreateResourceLock = new object();
        /// <summary>
        /// You must acquire this lock before rendering or resetting the device.
        /// </summary>
        public object UseResourceLock = new object();
        /// <summary>
        /// This lock is held during frame preparation.
        /// </summary>
        public object PrepareLock = new object();

        private bool _Running = true;
        private Frame _FrameBeingPrepared = null;
        private Frame _FrameBeingDrawn = null;

        private readonly Func<bool> _SyncBeginDraw;
        private readonly Action _SyncEndDraw;

        private WorkerThread _DrawThread;

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
        public RenderCoordinator (RenderManager manager, Func<bool> synchronousBeginDraw, Action synchronousEndDraw) {
            Manager = manager;

            _SyncBeginDraw = synchronousBeginDraw;
            _SyncEndDraw = synchronousEndDraw;

            CoreInitialize();
        }

        /// <summary>
        /// Constructs a render coordinator. A render manager and synchronous draw methods are automatically provided for you.
        /// </summary>
        /// <param name="deviceService"></param>
        public RenderCoordinator (IGraphicsDeviceService deviceService) {
            Manager = new RenderManager(deviceService.GraphicsDevice);

            _SyncBeginDraw = DefaultBeginDraw;
            _SyncEndDraw = DefaultEndDraw;

            CoreInitialize();
        }

        private void CoreInitialize () {
#if !PSM
            _DrawThread = new WorkerThread(ThreadedDraw);
#endif

            Device.DeviceResetting += OnDeviceResetting;
            Device.DeviceReset += OnDeviceReset;
        }

        protected bool DefaultBeginDraw () {
            if (Device.GraphicsDeviceStatus == GraphicsDeviceStatus.Normal)
                return true;

            return false;
        }

        protected void DefaultEndDraw () {
            var viewport = Device.Viewport;
            Device.Present(
                new Rectangle(0, 0, viewport.Width, viewport.Height),
                new Rectangle(0, 0, viewport.Width, viewport.Height),
                IntPtr.Zero
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

        public void WaitForActiveDraw () {
            if (_DrawDepth > 0)
                if (EnableThreading)
                    _DrawThread.WaitForPendingWork();
        }

        public bool BeginDraw () {
            if (Interlocked.Increment(ref _DrawDepth) > 1)
                if (DoThreadedIssue)
                    _DrawThread.WaitForPendingWork();

            if (_Running) {
                _FrameBeingPrepared = Manager.CreateFrame();

                return true;
            } else {
                return false;
            }
        }

        protected void PrepareFrame (Frame frame) {
            Manager.ResetBufferGenerators();

            lock (PrepareLock)
                frame.Prepare(DoThreadedPrepare);
        }

        /// <summary>
        /// Finishes preparing the current Frame and readies it to be sent to the graphics device for rendering.
        /// </summary>
        protected void PrepareNextFrame () {
            var newFrame = Interlocked.Exchange(ref _FrameBeingPrepared, null);

            PrepareFrame(newFrame);

            if (EnableThreading)
                _DrawThread.WaitForPendingWork();

            var oldFrame = Interlocked.Exchange(ref _FrameBeingDrawn, newFrame);

            if (oldFrame != null)
                oldFrame.Dispose();
        }
        
        protected bool DoThreadedPrepare {
            get {
#if PSM
                return false;
#else
                return EnableThreading;
#endif
            }
        }
        
        protected bool DoThreadedIssue { 
            get {
#if PSM
                return false;
#else
                return EnableThreading;
#endif
            }
        }

        public void EndDraw () {
            PrepareNextFrame();
            
            if (_Running) {
                lock (UseResourceLock)
                    if (!_SyncBeginDraw())
                        return;
                
                if (DoThreadedIssue) {
                    _DrawThread.RequestWork();
                } else {
                    ThreadedDraw(_DrawThread);
                }

                if (_DeviceLost) {
                    if (DoThreadedIssue)
                        _DrawThread.WaitForPendingWork();

                    _DeviceLost = IsDeviceLost;
                }
            } else {
                Interlocked.Decrement(ref _DrawDepth);
            }
        }

        protected void RenderFrame (Frame frame) {
            lock (UseResourceLock)
                if (frame != null) {
                    using (frame) {
                        _DeviceLost |= IsDeviceLost;

                        if (!_DeviceLost)
                            frame.Draw();
                    }
                }

            _DeviceLost |= IsDeviceLost;
        }

        protected void RenderFrameToDraw () {
            var frameToDraw = Interlocked.Exchange(ref _FrameBeingDrawn, null);

            RenderFrame(frameToDraw);
        }

        protected void ThreadedDraw (WorkerThread thread) {
            try {
                RenderFrameToDraw();

                _SyncEndDraw();

                _DeviceLost |= IsDeviceLost;
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
                ClearBatch.AddNew(frame, int.MinValue, materials.Clear, clearColor: Color.Transparent);

                drawBehavior(frame);

                PrepareFrame(frame);

                var oldRenderTargets = Device.GetRenderTargets();
                var oldViewport = Device.Viewport;
                try {
                    Device.SetRenderTarget(renderTarget);
                    Device.Viewport = new Viewport(0, 0, renderTarget.Width, renderTarget.Height);
                    materials.PushViewTransform(ViewTransform.CreateOrthographic(Device.Viewport));

                    RenderFrame(frame);
                } finally {
                    Device.SetRenderTargets(oldRenderTargets);
                    materials.PopViewTransform();
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
            _Running = false;
   
            if (_DrawThread != null) {
                if (EnableThreading)
                    _DrawThread.WaitForPendingWork();

                _DrawThread.Dispose();
                _DrawThread = null;
            }
        }
    }
    
#if PSM
    class DeviceLostException : Exception {
    }
#endif
}
