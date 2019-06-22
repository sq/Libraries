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

        public static bool IsRenderTargetValid (RenderTarget2D rt) {
            return (rt != null) && !rt.IsDisposed && !rt.IsContentLost;
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
            }

            OnDispose();
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
        public struct WriteTarget : IDisposable {
            public bool IsReleased { get; private set; }
            public readonly RenderTargetRing Ring;
            public readonly RenderTarget2D Target;

            internal WriteTarget (RenderTargetRing ring, RenderTarget2D target) {
                IsReleased = false;
                Ring = ring;
                Target = target;
            }

            public void Dispose () {
                lock (Ring.Lock) {
                    if (IsReleased)
                        return;
                    if (Ring.CurrentWriteTarget == Target)
                        Ring.CurrentWriteTarget = null;
                    else
                        ;
                    Ring.RasterizedTarget = Target;
                    IsReleased = true;
                }
            }
        }

        private RenderTarget2D CurrentWriteTarget = null, RasterizedTarget = null;
        private List<RenderTarget2D> AvailableTargets = new List<RenderTarget2D>();
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
        }

        protected override void OnDispose () {
            
        }
    }
}
