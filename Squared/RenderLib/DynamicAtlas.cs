using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Game;
using Squared.Threading;
using Squared.Util;

namespace Squared.Render {
    public interface IDynamicTexture {
        bool IsDisposed { get; }
        int Width { get; }
        int Height { get; }
        SurfaceFormat Format { get; }
        Texture2D Texture { get; }
    }

    public unsafe delegate void MipGenerator<T> (void* src, int srcWidth, int srcHeight, void* dest, int destWidth, int destHeight) where T : struct;

    public static class MipGenerator {
        public class WithGammaRamp {
            public readonly GammaRamp Ramp;
            private byte[] GammaTable, InvGammaTable;

            public WithGammaRamp (double gamma)
                : this (new GammaRamp(gamma)) {
            }

            public WithGammaRamp (GammaRamp ramp) {
                Ramp = ramp;
                GammaTable = Ramp.GammaTable;
                InvGammaTable = Ramp.InvGammaTable;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public byte Average (byte a, byte b, byte c, byte d) {
                var sum = InvGammaTable[a] + InvGammaTable[b] + InvGammaTable[c] + InvGammaTable[d];
                return GammaTable[sum / 4];
            }

            public unsafe void PAGray (void* src, int srcWidth, int srcHeight, void* dest, int destWidth, int destHeight) {
                if ((destWidth < srcWidth / 2) || (destHeight < srcHeight / 2))
                    throw new ArgumentOutOfRangeException();

                byte* pSrc = (byte*)src, pDest = (byte*)dest;
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
                        var gray = Average(a[3], b[3], c[3], d[3]);
                        result[0] = result[1] = result[2] = result[3] = gray;
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte Average (byte a, byte b, byte c, byte d) {
            var sum = a + b + c + d;
            return (byte)(sum / 4);
        }

        public static unsafe void Color (void* src, int srcWidth, int srcHeight, void* dest, int destWidth, int destHeight) {
            if ((destWidth < srcWidth / 2) || (destHeight < srcHeight / 2))
                throw new ArgumentOutOfRangeException();

            byte* pSrc = (byte*)src, pDest = (byte*)dest;
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
                    result[0] = Average(a[0], b[0], c[0], d[0]);
                    result[1] = Average(a[1], b[1], c[1], d[1]);
                    result[2] = Average(a[2], b[2], c[2], d[2]);
                    result[3] = Average(a[3], b[3], c[3], d[3]);
                }
            }
        }

        public static unsafe void sRGBColor (void* src, int srcWidth, int srcHeight, void* dest, int destWidth, int destHeight) {
            if ((destWidth < srcWidth / 2) || (destHeight < srcHeight / 2))
                throw new ArgumentOutOfRangeException();

            byte* pSrc = (byte*)src, pDest = (byte*)dest;
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
                    result[0] = ColorSpace.AveragesRGB(a[0], b[0], c[0], d[0]);
                    result[1] = ColorSpace.AveragesRGB(a[1], b[1], c[1], d[1]);
                    result[2] = ColorSpace.AveragesRGB(a[2], b[2], c[2], d[2]);
                    // The alpha channel is always linear
                    result[3] = Average(a[3], b[3], c[3], d[3]);
                }
            }
        }

        public static unsafe void sRGBPAGray (void* src, int srcWidth, int srcHeight, void* dest, int destWidth, int destHeight) {
            if ((destWidth < srcWidth / 2) || (destHeight < srcHeight / 2))
                throw new ArgumentOutOfRangeException();

            byte* pSrc = (byte*)src, pDest = (byte*)dest;
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
                    // Average the alpha channel because it is linear
                    var alphaAverage = Average(a[3], b[3], c[3], d[3]);
                    var gray = ColorSpace.LinearByteTosRGBByteTable[alphaAverage];
                    result[0] = result[1] = result[2] = gray;
                    result[3] = alphaAverage;
                }
            }
        }

        public static unsafe void PAGray (void* src, int srcWidth, int srcHeight, void* dest, int destWidth, int destHeight) {
            if ((destWidth < srcWidth / 2) || (destHeight < srcHeight / 2))
                throw new ArgumentOutOfRangeException();

            byte* pSrc = (byte*)src, pDest = (byte*)dest;
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
                    // Average the alpha channel because it is linear
                    var gray = Average(a[3], b[3], c[3], d[3]);
                    result[0] = result[1] = result[2] = result[3] = gray;
                }
            }
        }
    }

    public class DynamicAtlas<T> : IDisposable, IDynamicTexture
        where T : struct 
    {
        private struct GenerateMipsWorkItem : IWorkItem {
            public DynamicAtlas<T> Atlas;

            public void Execute () {
                Atlas.GenerateMips();
            }
        }

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
        }

        public bool IsDisposed { get; private set; }

        public readonly RenderCoordinator Coordinator;
        public int BytesPerPixel { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }
        public SurfaceFormat Format { get; private set; }
        public Texture2D Texture { get; private set; }
        public T[] Pixels { get; private set; }
        public bool IsDirty { get; private set; }
        public int Spacing { get; private set; }

        private int X, Y, RowHeight;
        private object Lock = new object();
        private Action _BeforeIssue, _BeforePrepare;
        private bool _NeedClear;
        private T[] MipBuffer;
        private int MipLevelCount;
        private readonly MipGenerator<T> GenerateMip;
        private Rectangle DirtyRegion;

        public DynamicAtlas (
            RenderCoordinator coordinator, int width, int height, SurfaceFormat format, 
            int spacing = 2, MipGenerator<T> mipGenerator = null
        ) {
            Coordinator = coordinator;
            Width = width;
            Height = height;
            Format = format;
            int temp;
            BytesPerPixel = Evil.TextureUtils.GetBytesPerPixelAndComponents(Format, out temp);
            Spacing = spacing;
            X = Y = Spacing;
            RowHeight = 0;
            _BeforeIssue = Flush;
            _BeforePrepare = QueueGenerateMips;

            GenerateMip = mipGenerator;

            EnsureValidResource();

            Pixels = new T[width * height];
            if (mipGenerator != null) {
                var totalMipSize = 4;
                var currentMipSizePixels = (width / 2) * (height / 2);
                while (currentMipSizePixels > 1) {
                    totalMipSize += (currentMipSizePixels + 1);
                    currentMipSizePixels /= 2;
                }
                MipBuffer = new T[totalMipSize];
            }

            MipLevelCount = Texture.LevelCount;

            Invalidate();
        }

        private void EnsureValidResource () {
            if (IsDisposed)
                return;

            if (Texture != null) {
                if (Texture.IsDisposed)
                    Texture = null;
            }

            if (Texture != null)
                return;

            lock (Coordinator.CreateResourceLock)
                Texture = new Texture2D(Coordinator.Device, Width, Height, GenerateMip != null, Format);
        }

        public void Invalidate (Rectangle rect) {
            lock (Lock) {
                if (IsDirty) {
                    DirtyRegion = Rectangle.Intersect(
                        Rectangle.Union(DirtyRegion, rect),
                        new Rectangle(0, 0, Width, Height)
                    );
                    return;
                }

                IsDirty = true;
                DirtyRegion = rect;
                if (!IsDisposed) {
                    Coordinator.BeforePrepare(_BeforePrepare);
                    Coordinator.BeforeIssue(_BeforeIssue);
                }
            }
        }

        public void Invalidate () {
            lock (Lock) {
                DirtyRegion = new Rectangle(0, 0, Width, Height);

                if (IsDirty)
                    return;

                if (!IsDisposed) {
                    Coordinator.BeforePrepare(_BeforePrepare);
                    Coordinator.BeforeIssue(_BeforeIssue);
                }
                IsDirty = true;
            }
        }

        private void QueueGenerateMips () {
            var workItem = new GenerateMipsWorkItem {
                Atlas = this
            };
            Coordinator.ThreadGroup.Enqueue(workItem);
        }

        public void Flush () {
            Coordinator.ThreadGroup.GetQueueForType<GenerateMipsWorkItem>().WaitUntilDrained();
            lock (Lock) {
                if (IsDisposed)
                    return;

                FlushLocked(false);
            }
        }

        private void FlushLocked (bool isRetrying) {
            EnsureValidResource();

            if (Texture == null)
                return;

            var hSrc = GCHandle.Alloc(Pixels, GCHandleType.Pinned);
            try {
                UploadRect(hSrc.AddrOfPinnedObject(), Width, Height, 0, DirtyRegion);
                UploadMipsLocked(hSrc, DirtyRegion);
                IsDirty = false;
            } catch (ObjectDisposedException ode) {
                if (isRetrying)
                    throw;

                Texture = null;
                EnsureValidResource();
                IsDirty = true;
                FlushLocked(true);
            } finally {
                hSrc.Free();
            }

            DirtyRegion = default(Rectangle);
        }

        private unsafe void UploadRect (IntPtr src, int srcWidth, int srcHeight, int destLevel, Rectangle rect) {
            var sizeBytes = srcWidth * srcHeight * BytesPerPixel;
            var rowPitch = (srcWidth * BytesPerPixel);
            var y = Math.Max(0, rect.Top);
            var h = Math.Min(Height, rect.Height);

            // TODO: Partial uploads on x axis as well
            var pSrc = (src + (y * rowPitch)).ToPointer();

            lock (Coordinator.UseResourceLock)
                Evil.TextureUtils.SetDataFast(
                    Texture, (uint)destLevel, pSrc, 
                    new Rectangle(0, y, srcWidth, h), (uint)rowPitch
                );
        }

        private unsafe void UploadMipsLocked (GCHandle hSrc, Rectangle region) {
            if (MipLevelCount <= 1)
                return;

            var srcBuffer = Pixels;
            var srcWidth = Width;
            var srcHeight = Height;

            var hMips = GCHandle.Alloc(MipBuffer, GCHandleType.Pinned);
            
            try {
                var pSrcBuffer = hSrc.AddrOfPinnedObject().ToPointer();
                var pSrc = pSrcBuffer;
                var pDest = (byte*)(hMips.AddrOfPinnedObject().ToPointer());
                var mipRect = region;

                // FIXME: Only re-generate mips for the portion of the atlas that changed
                for (var i = 1; i < MipLevelCount; i++) {
                    var destWidth = srcWidth / 2;
                    var destHeight = srcHeight / 2;
                    mipRect = new Rectangle(
                        (int)Math.Floor(mipRect.Left / 2f),
                        (int)Math.Floor(mipRect.Top / 2f),
                        (int)Math.Ceiling(mipRect.Width / 2f),
                        (int)Math.Ceiling(mipRect.Height / 2f)
                    );

                    UploadRect((IntPtr)pDest, destWidth, destHeight, i, mipRect); 

                    pSrc = pDest;
                    pDest += (destWidth * destHeight * BytesPerPixel);

                    srcWidth = destWidth;
                    srcHeight = destHeight;
                }
            } finally {
                hMips.Free();
            }
        }

        private unsafe void GenerateMips () {
            lock (Lock) {
                if (IsDisposed)
                    return;

                var hSrc = GCHandle.Alloc(Pixels, GCHandleType.Pinned);
                var region = DirtyRegion;
                try {
                    GenerateMipsLocked(hSrc, region);
                } finally {
                    hSrc.Free();
                }
            }
        }

        // FIXME: Deduplicate?
        private unsafe void GenerateMipsLocked (GCHandle hSrc, Rectangle region) {
            if (MipLevelCount <= 1)
                return;

            var srcBuffer = Pixels;
            var srcWidth = Width;
            var srcHeight = Height;

            var hMips = GCHandle.Alloc(MipBuffer, GCHandleType.Pinned);
            
            try {
                var pSrcBuffer = hSrc.AddrOfPinnedObject().ToPointer();
                var pSrc = pSrcBuffer;
                var pDest = (byte*)(hMips.AddrOfPinnedObject().ToPointer());
                var mipRect = region;

                // FIXME: Only re-generate mips for the portion of the atlas that changed
                for (var i = 1; i < MipLevelCount; i++) {
                    var destWidth = srcWidth / 2;
                    var destHeight = srcHeight / 2;
                    mipRect = new Rectangle(
                        (int)Math.Floor(mipRect.Left / 2f),
                        (int)Math.Floor(mipRect.Top / 2f),
                        (int)Math.Ceiling(mipRect.Width / 2f),
                        (int)Math.Ceiling(mipRect.Height / 2f)
                    );

                    GenerateMip(pSrc, srcWidth, srcHeight, pDest, destWidth, destHeight);

                    pSrc = pDest;
                    pDest += (destWidth * destHeight * BytesPerPixel);

                    srcWidth = destWidth;
                    srcHeight = destHeight;
                }
            } finally {
                hMips.Free();
            }
        }

        public bool TryReserve (int width, int height, out Reservation result) {
            lock (Lock) {
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
            }

            AutoClear();
            Invalidate(new Rectangle(X, Y, width, height));

            return true;
        }

        private void AutoClear () {
            lock (Lock) {
                if (!_NeedClear)
                    return;

                _NeedClear = false;
            }

            Array.Clear(Pixels, 0, Pixels.Length);
        }

        public void Clear (bool eraseOldPixels = true) {
            lock (Lock) {
                if (!_NeedClear)
                    _NeedClear = eraseOldPixels;
                X = Y = Spacing;
                RowHeight = 0;
            }

            Invalidate();
        }

        public Bounds BoundsFromRectangle (Rectangle rectangle) {
            return GameExtensionMethods.BoundsFromRectangle(Width, Height, ref rectangle);
        }

        public Bounds BoundsFromRectangle (ref Rectangle rectangle) {
            return GameExtensionMethods.BoundsFromRectangle(Width, Height, ref rectangle);
        }

        public void Dispose () {
            lock (Lock) {
                if (IsDisposed)
                    return;
                IsDisposed = true;

                Coordinator.DisposeResource(Texture);
                Texture = null;
                Pixels = null;
            }
        }

        public static implicit operator AbstractTextureReference (DynamicAtlas<T> atlas) {
            return new AbstractTextureReference(atlas);
        }
    }
}
