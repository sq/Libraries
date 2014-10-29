using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using System.Threading;
using Microsoft.Xna.Framework.Graphics;
using Squared.Render.Internal;
using System.Reflection;
using System.ComponentModel;
using System.Collections.Concurrent;

namespace Squared.Render {
    public abstract class MultithreadedGame : Microsoft.Xna.Framework.Game {
        public RenderCoordinator RenderCoordinator {
            get;
            protected set;
        }

        public RenderManager RenderManager {
            get;
            protected set;
        }

        private bool _UseThreadedDraw;
        protected bool UseThreadedDraw {
            get {
                return _UseThreadedDraw;
            }
            set {
                _UseThreadedDraw = value;
            }
        }

        public FrameTiming PreviousFrameTiming {
            get;
            private set;
        }

        private FrameTiming NextFrameTiming;
        private readonly ConcurrentQueue<Action<GameTime>> BeforeDrawQueue = new ConcurrentQueue<Action<GameTime>>();

        public MultithreadedGame()
            : base() {

#if SDL2
            // Again, I say: NOPE. -flibit
            UseThreadedDraw = false;
#else
            UseThreadedDraw = true;
            if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
                throw new InvalidOperationException("XNA only correctly supports single-thread apartments.");
#endif
        }

        protected override void Dispose (bool disposing) {
            if (RenderCoordinator != null)
                RenderCoordinator.Dispose();

            base.Dispose(disposing);
        }

        protected override void EndRun() {
            if (RenderCoordinator != null)
                RenderCoordinator.Dispose();

            base.EndRun();
        }

        protected void OnFormClosing (object sender, CancelEventArgs e) {
            if (RenderCoordinator != null)
                RenderCoordinator.Dispose();            
        }

        // HACK: Hook the form Closing event so we can tear down our rendering state before our associated Win32
        //  window is destroyed. This helps prevent a crash when the main thread destroys a window while a paint is active.
        protected bool SetupCloseHook () {
            var gw = Window;
            
            var gfField = gw.GetType().GetField("mainForm", BindingFlags.NonPublic | BindingFlags.Instance);
            if (gfField == null)
                return false;

            var gf = gfField.GetValue(gw);
            if (gf == null)
                return false;

            var gfClosingEvent = gf.GetType().GetEvent("Closing");
            if (gfClosingEvent == null)
                return false;

            gfClosingEvent.AddEventHandler(gf, (CancelEventHandler)OnFormClosing);
            return true;
        }

        protected override void Initialize () {
            RenderManager = new RenderManager(GraphicsDevice, Thread.CurrentThread);
            RenderCoordinator = new RenderCoordinator(
                RenderManager, base.BeginDraw, base.EndDraw
            );
            RenderCoordinator.EnableThreading = _UseThreadedDraw;
            RenderCoordinator.DeviceReset += (s, e) => OnDeviceReset();

            SetupCloseHook();

            base.Initialize();
        }

        public abstract void Draw(GameTime gameTime, Frame frame);

        protected override bool BeginDraw() {
            RenderCoordinator.WorkStopwatch.Restart();

            try {
                var failed = RenderCoordinator.BeginDraw();
                return failed;
            } finally {
                RenderCoordinator.WorkStopwatch.Stop();
                NextFrameTiming.BeginDraw = RenderCoordinator.WorkStopwatch.Elapsed;
            }
        }

        protected virtual void OnBeforeDraw (GameTime gameTime) {
            Action<GameTime> action;

            while (BeforeDrawQueue.Count > 0) {
                if (!BeforeDrawQueue.TryDequeue(out action))
                    continue;

                action(gameTime);
            }
        }

        /// <summary>
        /// Queues an operation to occur immediately before Game.Draw, after the 
        ///  previous frame has finished. You can do SynchronousDrawToRenderTarget here.
        /// </summary>
        public void BeforeDraw (Action<GameTime> action) {
            BeforeDrawQueue.Enqueue(action);
        }

        sealed protected override void Draw(GameTime gameTime) {
            RenderCoordinator.WorkStopwatch.Restart();

            try {
                OnBeforeDraw(gameTime);
                RenderCoordinator.SynchronousDrawsEnabled = false;
                Draw(gameTime, RenderCoordinator.Frame);
            } finally {
                RenderCoordinator.SynchronousDrawsEnabled = true;
                RenderCoordinator.WorkStopwatch.Stop();
                NextFrameTiming.Draw = RenderCoordinator.WorkStopwatch.Elapsed;
            }
        }

        protected override void EndDraw() {
            RenderCoordinator.WorkStopwatch.Restart();

            try {
                RenderCoordinator.EndDraw();
            } finally {
                RenderCoordinator.WorkStopwatch.Stop();

                NextFrameTiming.EndDraw = RenderCoordinator.WorkStopwatch.Elapsed;
                NextFrameTiming.Wait = RenderCoordinator.WaitStopwatch.Elapsed;
                PreviousFrameTiming = NextFrameTiming;

                RenderCoordinator.WaitStopwatch.Reset();
            }

            RenderCoordinator.EnableThreading = _UseThreadedDraw;
        }

        protected virtual void OnDeviceReset () {
        }
    }

    public struct FrameTiming {
        public TimeSpan Wait, BeginDraw, Draw, EndDraw;
    }
}
