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

        private readonly Stopwatch Stopwatch = new Stopwatch();
        private FrameTiming NextFrameTiming;

        public MultithreadedGame()
            : base() {
#if XBOX
            Thread.CurrentThread.SetProcessorAffinity(1);
#endif
        }

        protected override void Dispose (bool disposing) {
            RenderCoordinator.Dispose();

            base.Dispose(disposing);
        }

        protected override void Initialize () {
            RenderManager = new RenderManager(GraphicsDevice);
            RenderCoordinator = new RenderCoordinator(
                RenderManager, base.BeginDraw, base.EndDraw
            );

            base.Initialize();
        }

        public abstract void Draw(GameTime gameTime, Frame frame);

        protected override bool BeginDraw() {
            Stopwatch.Reset();
            Stopwatch.Start();

            try {
                return RenderCoordinator.BeginDraw();
            } finally {
                Stopwatch.Stop();
                NextFrameTiming.BeginDraw = Stopwatch.Elapsed;
            }
        }

        sealed protected override void Draw(GameTime gameTime) {
            Stopwatch.Reset();
            Stopwatch.Start();

            try {
                Draw(gameTime, RenderCoordinator.Frame);
            } finally {
                Stopwatch.Stop();
                NextFrameTiming.Draw = Stopwatch.Elapsed;
            }
        }

        protected override void EndDraw() {
            Stopwatch.Reset();
            Stopwatch.Start();

            try {
                RenderCoordinator.EndDraw();
            } finally {
                Stopwatch.Stop();
                NextFrameTiming.EndDraw = Stopwatch.Elapsed;
                PreviousFrameTiming = NextFrameTiming;
            }
        }
    }

    public struct FrameTiming {
        public TimeSpan BeginDraw, Draw, EndDraw;
    }
}
