using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using System.Threading;
using Microsoft.Xna.Framework.Graphics;
using Squared.Render.Internal;
using System.Reflection;

namespace Squared.Render {
    public abstract class MultithreadedGame : Microsoft.Xna.Framework.Game {
        // Set this to false to disable multithreaded rendering
        public bool UseThreadedDraw = true;
        // Lock this when applying changes to the device or loading content
        // (NOT when drawing)
        public object CreateResourceLock = new object();
        // Lock this when drawing or resetting the device
        // (NOT when loading content)
        public object UseResourceLock = new object();

        private bool _Running = true;
        private Frame _FrameBeingPrepared = null;
        private Frame _FrameBeingDrawn = null;

        private WorkerThread _DrawThread;

        // Used to detect re-entrant painting (usually means that an
        //  exception was thrown on the render thread)
        private int _DrawDepth = 0;

        // Lost devices can cause things to go horribly wrong if we're 
        //  using multithreaded rendering
        private bool _DeviceLost = false;

        public RenderManager RenderManager {
            get;
            protected set;
        }

        public MultithreadedGame()
            : base() {

            _DrawThread = new WorkerThread(ThreadedDraw, 3);

#if XBOX
            Thread.CurrentThread.SetProcessorAffinity(1);
#endif
        }

        protected override void Dispose (bool disposing) {
            _Running = false;
            if (UseThreadedDraw)
                _DrawThread.WaitForPendingWork();

            base.Dispose(disposing);
        }

        protected override void Initialize () {
            base.Initialize();

            RenderManager = new RenderManager(GraphicsDevice);
        }

        public abstract void Draw(GameTime gameTime, Frame frame);

        protected override bool BeginDraw() {
            if (Interlocked.Increment(ref _DrawDepth) > 1)
                if (UseThreadedDraw)
                    _DrawThread.WaitForPendingWork();

            if (_Running) {
                _FrameBeingPrepared = RenderManager.CreateFrame();

                return true;
            } else {
                return false;
            }
        }

        sealed protected override void Draw(GameTime gameTime) {
            if (_Running) 
                this.Draw(gameTime, _FrameBeingPrepared);
        }

        protected void PrepareNextFrame() {
            var newFrame = Interlocked.Exchange(ref _FrameBeingPrepared, null);

            newFrame.Prepare(UseThreadedDraw);

            if (UseThreadedDraw)
                _DrawThread.WaitForPendingWork();

            var oldFrame = Interlocked.Exchange(ref _FrameBeingDrawn, newFrame);

            if (oldFrame != null)
                oldFrame.Dispose();
        }

        protected override void EndDraw() {
            PrepareNextFrame();

            if (_Running) {
                lock (UseResourceLock)
                    if (!base.BeginDraw())
                        return;

                if (UseThreadedDraw) {
                    _DrawThread.RequestWork();
                } else {
                    ThreadedDraw(_DrawThread);
                }

                if (_DeviceLost && IsActive) {
                    if (UseThreadedDraw)
                        _DrawThread.WaitForPendingWork();

                    _DeviceLost = CheckGraphicsDeviceLost();
                }
            } else {
                Interlocked.Decrement(ref _DrawDepth);
            }
        }

        protected void RenderFrameToDraw() {
            var frameToDraw = Interlocked.Exchange(ref _FrameBeingDrawn, null);

            lock (UseResourceLock)
                if (frameToDraw != null) {
                    using (frameToDraw) {
                        _DeviceLost |= CheckGraphicsDeviceLost();

                        if (!_DeviceLost)
                            frameToDraw.Draw();
                    }
                }

            _DeviceLost |= CheckGraphicsDeviceLost();
        }

        protected void ThreadedDraw(WorkerThread thread) {
            try {
                RenderFrameToDraw();

                base.EndDraw();

                _DeviceLost |= CheckGraphicsDeviceLost();
            } catch (DeviceLostException) {
                _DeviceLost = true;
            } finally {
                Interlocked.Decrement(ref _DrawDepth);
            }
        }

        bool CheckGraphicsDeviceLost() {
            var device = GraphicsDevice;
            if (device == null)
                return false;

            bool result = false;

            result = (bool)(device.GraphicsDeviceStatus != GraphicsDeviceStatus.Normal);

            return result;
        }
    }
}
