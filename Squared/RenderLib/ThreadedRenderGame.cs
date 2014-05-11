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
                if (RenderCoordinator != null)
                    RenderCoordinator.EnableThreading = value;

                _UseThreadedDraw = value;
            }
        }

        public FrameTiming PreviousFrameTiming {
            get;
            private set;
        }

        private FrameTiming NextFrameTiming;

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

        protected override void Initialize () {
            RenderManager = new RenderManager(GraphicsDevice, Thread.CurrentThread);
            RenderCoordinator = new RenderCoordinator(
                RenderManager, base.BeginDraw, base.EndDraw
            );
            RenderCoordinator.EnableThreading = _UseThreadedDraw;

            base.Initialize();
        }

        public abstract void Draw(GameTime gameTime, Frame frame);

        protected override bool BeginDraw() {
            RenderCoordinator.WorkStopwatch.Restart();

            try {
                return RenderCoordinator.BeginDraw();
            } finally {
                RenderCoordinator.WorkStopwatch.Stop();
                NextFrameTiming.BeginDraw = RenderCoordinator.WorkStopwatch.Elapsed;
            }
        }

        sealed protected override void Draw(GameTime gameTime) {
            RenderCoordinator.WorkStopwatch.Restart();

            try {
                Draw(gameTime, RenderCoordinator.Frame);
            } finally {
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
        }
    }

    public struct FrameTiming {
        public TimeSpan Wait, BeginDraw, Draw, EndDraw;
    }
}
