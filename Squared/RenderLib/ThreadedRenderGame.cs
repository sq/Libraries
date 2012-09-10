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
            return RenderCoordinator.BeginDraw();
        }

        sealed protected override void Draw(GameTime gameTime) {
            Draw(gameTime, RenderCoordinator.Frame);
        }

        protected override void EndDraw() {
            RenderCoordinator.EndDraw();
        }
    }
}
