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
using Squared.Threading;

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

        public readonly ThreadGroup ThreadGroup;

        public FrameTiming PreviousFrameTiming {
            get;
            private set;
        }

        protected bool HasEverLoadedContent { get; private set; }
        protected bool IsUnloadingContent { get; private set; }
        protected bool IsLoadingContent { get; private set; }
        public bool IsContentLoaded { get; private set; }

        private readonly ConcurrentQueue<Action<GameTime>> BeforeDrawQueue = new ConcurrentQueue<Action<GameTime>>();

        public event Action BeginDrawFailed;

        public MultithreadedGame()
            : base() {

            var threadCount = Math.Min(Math.Max(2, Environment.ProcessorCount), 8);
            ThreadGroup = new ThreadGroup(threadCount, true, comThreadingModel: ApartmentState.MTA, name: "MultithreadedGame") {
                MainThreadStepLengthLimitMs = 4
            };

#if !FNA
            if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA) {
                throw new InvalidOperationException(
                    "An STA apartment is required. See comments for more information."
                );
                // Okay, so.
                // COM interop in .NET is a nightmare and doesn't work correctly in the presence of STA apartments and threads.
                // Because XNA Song shells out to Windows Media Player, and Windows Media Player is total garbage,
                //  playing Songs in an MTA apartment tends to pretty reliably hang your game forever.
                // For now, UniformBinding bypasses COM wrappers, so things seem to work! But good luck. RIP.
            }
#endif
        }

        private void InternalDispose () {
            if (RenderCoordinator != null)
                RenderCoordinator.Dispose();

            if (ThreadGroup != null)
                ThreadGroup.Dispose();
        }

        protected override void Dispose (bool disposing) {
            InternalDispose();

            base.Dispose(disposing);
        }

        protected override void EndRun() {
            InternalDispose();   

            base.EndRun();
        }

        protected void OnFormMoved (object sender, EventArgs e) {
            RenderCoordinator.NotifyWindowIsMoving();
        }

        protected void OnFormClosing (object sender, CancelEventArgs e) {
            InternalDispose();   
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
            gfClosingEvent?.AddEventHandler(gf, (CancelEventHandler)OnFormClosing);

            var gfMovedEvent = gf.GetType().GetEvent("LocationChanged");
            gfMovedEvent?.AddEventHandler(gf, (EventHandler)OnFormMoved);

            return true;
        }

        protected override void Initialize () {
            var gds = Services.GetService(typeof(IGraphicsDeviceService)) as IGraphicsDeviceService;
            if (gds != null) {
                RenderCoordinator = new RenderCoordinator(gds, Thread.CurrentThread, ThreadGroup, base.BeginDraw, base.EndDraw);
                RenderManager = RenderCoordinator.Manager;
            } else {
                RenderManager = new RenderManager(GraphicsDevice, Thread.CurrentThread, ThreadGroup);
                RenderCoordinator = new RenderCoordinator(
                    RenderManager, base.BeginDraw, base.EndDraw
                );
            }

            // FIXME: Preloading shaders crashes when done from a worker thread
            RenderCoordinator.DoThreadedIssue = false;
            RenderCoordinator.DoThreadedPrepare = true;

            RenderCoordinator.DeviceReset += (s, e) => OnDeviceReset();

            gds.DeviceResetting += Gds_DeviceResetting;

            SetupCloseHook();

            base.Initialize();
        }

        private void Gds_DeviceResetting (object sender, EventArgs e) {
            if (!RenderCoordinator.WaitForActiveDraws())
                ;
        }

        public abstract void Draw (GameTime gameTime, Frame frame);

        protected override bool BeginDraw() {
            ThreadGroup.TryStepMainThreadUntilDrained();

            var settling = RenderCoordinator.IsWaitingForDeviceToSettle;

            RenderCoordinator.StartWorkPhase(RenderCoordinator.WorkPhases.BeginDraw);
            try {
                var ok = IsContentLoaded && !settling && RenderCoordinator.BeginDraw();
                if (!ok) {
                    if (BeginDrawFailed != null)
                        BeginDrawFailed();
                    else if (!settling)
                        Console.Error.WriteLine("BeginDraw failed");
                }
                return ok;
            } finally {
                RenderCoordinator.NextFrameTiming.BeginDraw = RenderCoordinator.EndWorkPhase(RenderCoordinator.WorkPhases.BeginDraw);
            }
        }

        protected abstract void OnLoadContent (bool isReloading);
        protected virtual void OnUnloadContent () {
        }

        sealed protected override void LoadContent () {
            if (IsLoadingContent)
                return;
            RenderCoordinator.WaitForActiveDraws();

            IsLoadingContent = true;
            try {
                base.LoadContent();
                OnLoadContent(HasEverLoadedContent);
                HasEverLoadedContent = true;
                IsContentLoaded = true;
            } finally {
                IsLoadingContent = false;
            }
        }

        sealed protected override void UnloadContent () {
            if (IsUnloadingContent)
                return;
            RenderCoordinator.WaitForActiveDraws();

            IsUnloadingContent = true;
            try {
                OnUnloadContent();
                base.UnloadContent();
            } finally {
                IsContentLoaded = false;
                IsUnloadingContent = false;
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

        /// <summary>
        /// Queues an operation to occur immediately before Present, after all drawing
        ///  commands have been issued. This is an ideal time to perform tasks like
        ///  texture read-back.
        /// </summary>
        public void BeforePresent (Action action) {
            RenderCoordinator.BeforePresent(action);
        }

        sealed protected override void Draw (GameTime gameTime) {
            var priorIndex = Batch.LifetimeCount;
            RenderCoordinator.NextFrameTiming.PriorPrimitiveCount = NativeBatch.LifetimePrimitiveCount;
            RenderCoordinator.NextFrameTiming.PriorCommandCount = NativeBatch.LifetimeCommandCount;

            // ????
            RenderCoordinator.StartWorkPhase(RenderCoordinator.WorkPhases.Wait);
            RenderCoordinator.WaitForActiveDraws();
            RenderCoordinator.NextFrameTiming.Wait += RenderCoordinator.EndWorkPhase(RenderCoordinator.WorkPhases.Wait);

            try {
                OnBeforeDraw(gameTime);
                var frame = RenderCoordinator.BeginFrame(true);
                Squared.Threading.Profiling.Superluminal.BeginEventFormat("Build Frame", "SRFrame #{0}", frame.Index, color: 0x1010CF);
                RenderCoordinator.StartWorkPhase(RenderCoordinator.WorkPhases.BuildFrame);
                Draw(gameTime, frame);
            } finally {
                Squared.Threading.Profiling.Superluminal.EndEvent();
                RenderCoordinator.SynchronousDrawsEnabled = true;
                RenderCoordinator.NextFrameTiming.BuildFrame = RenderCoordinator.EndWorkPhase(RenderCoordinator.WorkPhases.BuildFrame);
                RenderCoordinator.NextFrameTiming.BatchCount = (int)(Batch.LifetimeCount - priorIndex);
            }
        }

        protected override void EndDraw() {
            try {
                RenderCoordinator.EndDraw();
            } catch (Exception exc) {
                Console.WriteLine("Caught {0} in EndDraw", exc);
                throw;
            } finally {
                var lpc = NativeBatch.LifetimePrimitiveCount;
                var ppc = RenderCoordinator.NextFrameTiming.PriorPrimitiveCount;
                var lcc = NativeBatch.LifetimeCommandCount;
                var pcc = RenderCoordinator.NextFrameTiming.PriorCommandCount;

                RenderCoordinator.NextFrameTiming.PrimitiveCount = (int)(lpc - ppc);
                RenderCoordinator.NextFrameTiming.CommandCount = (int)(lcc - pcc);
                PreviousFrameTiming = RenderCoordinator.NextFrameTiming;
                RenderCoordinator.NextFrameTiming = default;
            }

            ThreadGroup.TryStepMainThreadUntilDrained();
        }

        protected virtual void OnDeviceReset () {
        }
    }

    /// <summary>
    /// Records timing and count information for the most recent frame.
    /// Phases occur in this order:
    /// Wait: Waiting for the render queue to become empty.
    /// BeginDraw: Initializing the device for rendering.
    /// Draw: Running Game.Draw to build the frame.
    /// Prepare: Prepare the frame and its batches for rendering.
    /// EndDraw: Multiple steps:
    ///     Run BeforeIssue handlers
    ///     Issue draw calls to the hardware
    ///     Run BeforePresent handlers
    ///     Present the final frame to the screen
    ///     Run AfterPresent handlers
    /// </summary>
    public struct FrameTiming {
        public TimeSpan Wait, BeginDraw, BuildFrame, BeforePrepare, Prepare, BeforeIssue, Issue, BeforePresent, Present, AfterPresent;
        public TimeSpan Handlers => BeforePrepare + BeforeIssue + BeforePresent + AfterPresent;
        public TimeSpan EndDraw => BeforeIssue + Issue + BeforePresent + Present + AfterPresent;
        public int BatchCount, CommandCount, PrimitiveCount;

        internal long PriorPrimitiveCount, PriorCommandCount;
    }
}
