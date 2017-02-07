using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Squared.Render {
    public class DynamicAtlas<T> : IDisposable 
        where T : struct 
    {
        public struct Reservation {
            public readonly DynamicAtlas<T> Atlas;
            public readonly int X, Y, Width, Height;

            internal Reservation (DynamicAtlas<T> atlas, int x, int y, int w, int h) {
                Atlas = atlas;
                X = x;
                Y = y;
                Width = w;
                Height = h;
            }

            public void Upload (T[] pixels) {
                for (var y = 0; y < Height; y++) {
                    var dest = ((Y + y) * Atlas.Width) + X;
                    var src = y * Width;
                    Array.Copy(pixels, src, Atlas.Pixels, dest, Width);
                }

                Atlas.IsDirty = true;
            }

            public Rectangle Rectangle {
                get {
                    return new Rectangle(X, Y, Width, Height);
                }
            }

            public Texture2D Texture {
                get {
                    if (Atlas != null)
                        return Atlas.Texture;
                    else
                        return null;
                }
            }
        }

        public readonly RenderCoordinator Coordinator;
        public readonly int Width, Height;
        public Texture2D Texture { get; private set; }
        public T[] Pixels { get; private set; }
        public bool IsDirty { get; private set; }

        private Texture2D _Texture;
        private int Spacing = 2;
        private int X = 1, Y = 1, RowHeight = 0;
        private object Lock = new object();
        private Action _BeforePrepare;

        public DynamicAtlas (RenderCoordinator coordinator, int width, int height, SurfaceFormat format) {
            Coordinator = coordinator;
            Width = width;
            Height = height;
            _BeforePrepare = Flush;

            lock (Coordinator.CreateResourceLock)
                Texture = new Texture2D(coordinator.Device, width, height, false, format);

            coordinator.DeviceReset += Coordinator_DeviceReset;

            Pixels = new T[width * height];
            Invalidate();
        }

        private void Coordinator_DeviceReset (object sender, EventArgs e) {
            Invalidate();
        }

        public void Invalidate () {
            lock (Lock) {
                if (IsDirty)
                    return;

                Coordinator.BeforePrepare(_BeforePrepare);
                IsDirty = true;
            }
        }

        public void Flush () {
            lock (Lock) {
                if (Texture == null)
                    return;

                IsDirty = false;

                lock (Coordinator.UseResourceLock)
                    Texture.SetData(Pixels);
            }
        }

        public bool TryReserve (int width, int height, out Reservation result) {
            bool needWrap = (X + width) >= (Width - 1);

            if (
                ((Y + height) >= Height) ||
                (
                    needWrap && ((Y + RowHeight + height) >= Height)
                )
            ) {
                result = default(Reservation);
                return false;
            }

            if (needWrap) {
                Y += (RowHeight + Spacing);
                X = 1;
                RowHeight = 0;
            }

            result = new Reservation(this, X, Y, width, height);
            X += width + Spacing;
            RowHeight = Math.Max(RowHeight, height);
            Invalidate();

            return true;
        }

        public void Clear () {
            Array.Clear(Pixels, 0, Pixels.Length);
            X = Y = 1;
            RowHeight = 0;
            IsDirty = true;
        }

        public void Dispose () {
            lock (Lock) {
                Coordinator.DeviceReset -= Coordinator_DeviceReset;
                Coordinator.DisposeResource(Texture);
                Texture = null;
                Pixels = null;
            }
        }
    }
}
