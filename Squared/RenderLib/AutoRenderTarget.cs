using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Graphics;

namespace Squared.Render {
    public class AutoRenderTarget : IDisposable, IDynamicTexture {
        public bool IsDisposed { get; private set; }
        public readonly RenderCoordinator Coordinator;

        public int Width { get; private set; }
        public int Height { get; private set; }
        public readonly bool MipMap;
        public readonly SurfaceFormat PreferredFormat;
        public readonly DepthFormat PreferredDepthFormat;
        public readonly int PreferredMultiSampleCount;

        private object Lock = new object();
        private RenderTarget2D CurrentInstance;

        public AutoRenderTarget (
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

            GetOrCreateInstance(true);
        }

        private bool IsInstanceValid {
            get {
                return (CurrentInstance != null) && !CurrentInstance.IsDisposed && !CurrentInstance.IsContentLost;
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

                lock (Coordinator.CreateResourceLock)
                    CurrentInstance = new RenderTarget2D(
                        Coordinator.Device, Width, Height, MipMap,
                        PreferredFormat, PreferredDepthFormat,
                        PreferredMultiSampleCount, RenderTargetUsage.PreserveContents
                    );

                return CurrentInstance;
            }
        }

        public RenderTarget2D Get () {
            return GetOrCreateInstance(false);
        }

        public static explicit operator RenderTarget2D (AutoRenderTarget art) {
            return art.Get();
        }

        public void Dispose () {
            lock (Lock) {
                if (IsDisposed)
                    return;

                IsDisposed = true;
            }

            if (IsInstanceValid)
                Coordinator.DisposeResource(CurrentInstance);
        }
    }
}
