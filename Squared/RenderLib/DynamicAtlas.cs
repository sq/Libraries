using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Squared.Render {
    public delegate void MipGenerator<T> (T[] src, int srcWidth, int srcHeight, T[] dest) where T : struct;

    public static class MipGenerator {
        private static readonly double[] ToLinearTable = new
            double[256];

        static MipGenerator () {
            for (int i = 0; i < 256; i++) {
                double fv = i / 255.0;
                if (fv < 0.04045)
                    ToLinearTable[i] = fv / 12.92;
                else
                    ToLinearTable[i] = Math.Pow((fv + 0.055) / 1.055, 2.4);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double ToLinear (byte v) {
            return ToLinearTable[v];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte FromLinear (double v) {
            double scaled;
	        if (v <= 0.0031308)
		        scaled = 12.92 * v;
	        else
                scaled = (Math.Pow(v, 1.0 / 2.4) - 0.055) * 1.055;

            return (byte)(scaled * 255.0);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte Average_sRGB (byte a, byte b, byte c, byte d) {
            double sum = ToLinear(a) + ToLinear(b) + ToLinear(c) + ToLinear(d);
            return FromLinear(sum / 4);
        }

        public static unsafe void Color (Color[] src, int srcWidth, int srcHeight, Color[] dest) {
            var destWidth = srcWidth / 2;
            var destHeight = srcHeight / 2;

            fixed (Color* pSrcColor = src, pDestColor = dest) {
                byte* pSrc = (byte*)pSrcColor, pDest = (byte*)pDestColor;
                var srcRowSize = srcWidth * 4;
            
                for (var y = 0; y < destHeight; y++) {
                    byte* srcRow = pSrc + ((y * 2) * srcRowSize);
                    byte* destRow = pDest + (y * destWidth * 4);

                    for (var x = 0; x < destWidth; x++) {
                        var a = srcRow + ((x * 2) * 4);
                        var b = a + 4;
                        var c = a + srcRowSize;
                        var d = b + srcRowSize;

                        var result = destRow + (x * 4);
                        result[0] = (byte)((a[0] + b[0] + c[0] + d[0]) / 4);
                        result[1] = (byte)((a[1] + b[1] + c[1] + d[1]) / 4);
                        result[2] = (byte)((a[2] + b[2] + c[2] + d[2]) / 4);
                        result[3] = (byte)((a[3] + b[3] + c[3] + d[3]) / 4);
                    }
                }
            }
        }

        public static unsafe void sRGBColor (Color[] src, int srcWidth, int srcHeight, Color[] dest) {
            var destWidth = srcWidth / 2;
            var destHeight = srcHeight / 2;

            fixed (Color* pSrcColor = src, pDestColor = dest) {
                byte* pSrc = (byte*)pSrcColor, pDest = (byte*)pDestColor;
                var srcRowSize = srcWidth * 4;
            
                for (var y = 0; y < destHeight; y++) {
                    byte* srcRow = pSrc + ((y * 2) * srcRowSize);
                    byte* destRow = pDest + (y * destWidth * 4);

                    for (var x = 0; x < destWidth; x++) {
                        var a = srcRow + ((x * 2) * 4);
                        var b = a + 4;
                        var c = a + srcRowSize;
                        var d = b + srcRowSize;

                        var result = destRow + (x * 4);
                        result[0] = Average_sRGB(a[0], b[0], c[0], d[0]);
                        result[1] = Average_sRGB(a[1], b[1], c[1], d[1]);
                        result[2] = Average_sRGB(a[2], b[2], c[2], d[2]);
                        result[3] = Average_sRGB(a[3], b[3], c[3], d[3]);
                    }
                }
            }
        }
    }

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
        public int Spacing { get; private set; }

        private Texture2D _Texture;
        private int X, Y, RowHeight;
        private object Lock = new object();
        private Action _BeforePrepare;
        private T[] MipBuffer;
        private int MipLevelCount;
        private readonly MipGenerator<T> GenerateMip;

        public DynamicAtlas (
            RenderCoordinator coordinator, int width, int height, SurfaceFormat format, 
            int spacing = 2, MipGenerator<T> mipGenerator = null
        ) {
            Coordinator = coordinator;
            Width = width;
            Height = height;
            Spacing = spacing;
            X = Y = Spacing;
            RowHeight = 0;
            _BeforePrepare = Flush;

            lock (Coordinator.CreateResourceLock)
                Texture = new Texture2D(coordinator.Device, width, height, mipGenerator != null, format);

            Pixels = new T[width * height];
            if (mipGenerator != null)
                MipBuffer = new T[(width / 2) * (height / 2)];

            GenerateMip = mipGenerator;
            MipLevelCount = Texture.LevelCount;

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

                GenerateMips();
            }
        }

        private void GenerateMips () {
            if (MipLevelCount <= 1)
                return;

            var srcBuffer = Pixels;
            var srcWidth = Width;
            var srcHeight = Height;

            for (var i = 1; i < MipLevelCount; i++) {
                var destWidth = srcWidth / 2;
                var destHeight = srcHeight / 2;

                GenerateMip(srcBuffer, srcWidth, srcHeight, MipBuffer);
                lock (Coordinator.UseResourceLock)
                    Texture.SetData(i, null, MipBuffer, 0, destWidth * destHeight);

                srcBuffer = MipBuffer;
                srcWidth = destWidth;
                srcHeight = destHeight;
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
                X = Spacing;
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
            X = Y = Spacing;
            RowHeight = 0;
            Invalidate();
        }

        public void Dispose () {
            lock (Lock) {
                Coordinator.DisposeResource(Texture);
                Texture = null;
                Pixels = null;
            }
        }
    }
}
