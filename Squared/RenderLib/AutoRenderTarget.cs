using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Graphics;
using Squared.Render.Tracing;

namespace Squared.Render {
    public abstract class AutoRenderTargetBase : ITraceCapturingDisposable {
        /// <summary>
        /// If set, stack traces will be captured when an instance is disposed.
        /// </summary>
        public static bool CaptureStackTraces = false;

        public StackTrace DisposedStackTrace;

        public bool IsDisposed { get; private set; }
        public readonly RenderCoordinator Coordinator;

        public int Width { get; protected set; }
        public int Height { get; protected set; }
        public bool MipMap { get; protected set; }
        public SurfaceFormat PreferredFormat { get; protected set; }
        public DepthFormat PreferredDepthFormat { get; protected set; }
        public int PreferredMultiSampleCount { get; protected set; }

        protected object Lock = new object();

        public AutoRenderTargetBase (
            RenderCoordinator coordinator,
            int width, int height,
            bool mipMap = false,
            SurfaceFormat preferredFormat = SurfaceFormat.Color,
            DepthFormat preferredDepthFormat = DepthFormat.None,
            int preferredMultiSampleCount = 1
        ) {
            Coordinator = coordinator;

            Width = width;
            Height = height;
            MipMap = mipMap;
            PreferredFormat = preferredFormat;
            PreferredDepthFormat = preferredDepthFormat;
            PreferredMultiSampleCount = preferredMultiSampleCount;
        }

        void ITraceCapturingDisposable.AutoCaptureTraceback () {
            if (DisposedStackTrace != null)
                return;

            if (CaptureStackTraces)
                DisposedStackTrace = new StackTrace(1, true);
        }

        public static bool IsRenderTargetValid (Texture rt) {
            var rt2d = rt as RenderTarget2D;
            if (rt2d?.IsContentLost == true)
                return false;
            return (rt != null) && !rt.IsDisposed;
        }

        protected RenderTarget2D CreateInstance () {
            lock (Coordinator.CreateResourceLock)
                return new RenderTarget2D(
                    Coordinator.Device, Width, Height, MipMap,
                    PreferredFormat, PreferredDepthFormat,
                    PreferredMultiSampleCount, RenderTargetUsage.PreserveContents
                );
        }

        protected abstract void OnDispose ();
    
        public void Dispose () {
            lock (Lock) {
                if (IsDisposed)
                    return;

                IsDisposed = true;

                ((ITraceCapturingDisposable)this).AutoCaptureTraceback();

                OnDispose();
            }
        }
    }

    public class AutoRenderTarget : AutoRenderTargetBase, IDynamicTexture {
        private RenderTarget2D CurrentInstance;
        public string Name { get; private set; }

        public AutoRenderTarget (
            RenderCoordinator coordinator,
            int width, int height,
            bool mipMap = false,
            SurfaceFormat preferredFormat = SurfaceFormat.Color,
            DepthFormat preferredDepthFormat = DepthFormat.None,
            int preferredMultiSampleCount = 1
        ) : base(
            coordinator, width, height,
            mipMap, preferredFormat, preferredDepthFormat, 
            preferredMultiSampleCount
        ) {
            if (coordinator == null)
                throw new ArgumentNullException(nameof(coordinator));

            GetOrCreateInstance(true);
        }

        public void SetName (string name) {
            Name = name;
        }

        private bool IsInstanceValid {
            get {
                return IsRenderTargetValid(CurrentInstance);
            }
        }

        SurfaceFormat IDynamicTexture.Format {
            get {
                return CurrentInstance?.Format ?? PreferredFormat;
            }
        }

        Texture2D IDynamicTexture.Texture {
            get {
                return Get();
            }
        }

        public void Resize (int width, int height) {
            lock (Lock) {
                if (IsDisposed)
                    throw new ObjectDisposedException("AutoRenderTarget");

                if ((Width == width) && (Height == height))
                    return;

                Width = width;
                Height = height;
            }

            GetOrCreateInstance(true);
        }

        private RenderTarget2D GetOrCreateInstance (bool forceCreate) {
            lock (Lock) {
                if (IsDisposed)
                    throw new ObjectDisposedException("AutoRenderTarget");

                if (IsInstanceValid) {
                    if (!forceCreate) {
                        return CurrentInstance;
                    } else {
                        lock (Coordinator.UseResourceLock)
                            CurrentInstance.Dispose();
                        CurrentInstance = null;
                    }
                }

                CurrentInstance = CreateInstance();
                CurrentInstance.Name = Name;
                CurrentInstance.SetName(Name);

                return CurrentInstance;
            }
        }

        public RenderTarget2D Get () {
            return GetOrCreateInstance(false);
        }

        public static explicit operator RenderTarget2D (AutoRenderTarget art) {
            return art.Get();
        }

        protected override void OnDispose () {
            if (IsInstanceValid)
                Coordinator.DisposeResource(CurrentInstance);
        }
    }

    public class RenderTargetRing : AutoRenderTargetBase {
        public struct WriteTarget {
            public readonly RenderTargetRing Ring;
            public readonly RenderTarget2D Target, Previous;

            internal WriteTarget (RenderTargetRing ring, RenderTarget2D target, RenderTarget2D previous) {
                Ring = ring;
                Target = target;
                Previous = previous;
            }
        }

        private Queue<RenderTarget2D> AvailableTargets = new Queue<RenderTarget2D>();
        public int TotalCreated { get; private set; }
        public readonly int Capacity;

        public RenderTargetRing (
            RenderCoordinator coordinator, int capacity, int width, int height, 
            bool mipMap = false, SurfaceFormat preferredFormat = SurfaceFormat.Color, 
            DepthFormat preferredDepthFormat = DepthFormat.None, int preferredMultiSampleCount = 1
        ) : base(
            coordinator, width, height, mipMap, preferredFormat, 
            preferredDepthFormat, preferredMultiSampleCount
        ) {
            Capacity = Math.Max(1, capacity);
            for (int i = 0; i < Capacity; i++)
                AvailableTargets.Enqueue(CreateInstance());
        }

        public WriteTarget AcquireWriteTarget () {
            lock (Lock) {
                if (LastWriteTarget != null)
                    AvailableTargets.Enqueue(LastWriteTarget);
                LastWriteTarget = CurrentWriteTarget;

                if (TotalCreated < Capacity) {
                    TotalCreated++;
                    CurrentWriteTarget = CreateInstance();
                    return new WriteTarget(this, CurrentWriteTarget, LastWriteTarget);
                }

                while (AvailableTargets.Count > 0) {
                    var rt = AvailableTargets.Dequeue();
                    if (!IsRenderTargetValid(rt)) {
                        Coordinator.DisposeResource(rt);
                        TotalCreated--;
                        continue;
                    }

                    CurrentWriteTarget = rt;
                    return new WriteTarget(this, rt, LastWriteTarget);
                }

                if (TotalCreated < Capacity) {
                    TotalCreated++;
                    CurrentWriteTarget = CreateInstance();
                    return new WriteTarget(this, CurrentWriteTarget, LastWriteTarget);
                }

                throw new Exception("Failed to create or reuse render target");
            }
        }

        public RenderTarget2D CurrentWriteTarget { get; private set; }
        public RenderTarget2D LastWriteTarget { get; private set; }

        protected override void OnDispose () {
            Coordinator.DisposeResource(CurrentWriteTarget);
            Coordinator.DisposeResource(LastWriteTarget);
            foreach (var rt in AvailableTargets)
                Coordinator.DisposeResource(rt);
            AvailableTargets.Clear();
        }
    }
}
