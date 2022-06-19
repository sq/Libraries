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
using Squared.Render.Mips;
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

    public sealed class DynamicAtlas<T> : IDisposable, IDynamicTexture
        where T : unmanaged
    {
        public readonly object Tag = null;
        public bool DebugColors = false;
        private static int NextId = 0;
        private int Id;

        private struct GenerateMipsWorkItem : IWorkItem {
            public static WorkItemConfiguration Configuration =>
                new WorkItemConfiguration {
                    ConcurrencyPadding = 2,
                    DefaultStepCount = 2
                };

            public DynamicAtlas<T> Atlas;
            public Rectangle Region;

            public void Execute () {
                Atlas.GenerateMips(Region);
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

                Atlas.Invalidate(new Rectangle(X, Y, Width, Height));
            }

            public void Invalidate () {
                Atlas.Invalidate(new Rectangle(X, Y, Width, Height));
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
        public bool IsDirty => (DirtyMipsRegion.Width + DirtyUploadRegion.Width + DirtyMipsRegion.Height + DirtyUploadRegion.Height) > 0;
        public int Spacing { get; private set; }

        private int X, Y, RowHeight;
        private object Lock = new object();
        private Action _BeforeIssue, _BeforePrepare;
        private bool _NeedClear, _IsQueued, _NeedRequeue;
        private T[] MipBuffer;
        private int MipLevelCount;
        private readonly MipGenerator<T> GenerateMip;
        private Rectangle DirtyMipsRegion,DirtyUploadRegion;

        public DynamicAtlas (
            RenderCoordinator coordinator, int width, int height, SurfaceFormat format, 
            int spacing = 2, MipGenerator<T> mipGenerator = null, object tag = null
        ) {
            Id = NextId++;
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
            Tag = tag;

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
            
            if (DebugColors) {
                var p = Pixels as Color[];
                if (p != null) {
                    var w = 4096 * 4;
                    for (int i = 0, l = p.Length; i < l; i++)
                        p[i] = (Color)new Color(i % 255, (Id * 64) % 255, (Id * 192) % 255, 255);
                }
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

            lock (Coordinator.CreateResourceLock) {
                Texture = new Texture2D(Coordinator.Device, Width, Height, GenerateMip != null, Format) {
                    Tag = $"DynamicAtlas<{typeof(T).Name}> {Tag ?? GetHashCode().ToString("X8")}"
                };
                Coordinator.AutoAllocatedTextureResources.Add(Texture);
            }
        }

        public void Invalidate (Rectangle rect) {
            lock (Lock) {
                if (_IsQueued)
                    _NeedRequeue = true;

                var myRect = new Rectangle(0, 0, Width, Height);
                if (DirtyMipsRegion.Width > 0)
                    DirtyMipsRegion = Rectangle.Intersect(Rectangle.Union(DirtyMipsRegion, rect), myRect);
                else
                    DirtyMipsRegion = Rectangle.Intersect(rect, myRect);
                if (DirtyUploadRegion.Width > 0)
                    DirtyUploadRegion = Rectangle.Intersect(Rectangle.Union(DirtyUploadRegion, rect), myRect);
                else
                    DirtyUploadRegion = Rectangle.Intersect(rect, myRect);

                QueueHandlers();
            }
        }

        public void Invalidate () {
            lock (Lock) {
                if (_IsQueued)
                    _NeedRequeue = true;

                DirtyMipsRegion = DirtyUploadRegion = new Rectangle(0, 0, Width, Height);
                QueueHandlers();
            }
        }

        private void QueueHandlers () {
            if (!IsDisposed && !_IsQueued) {
                _IsQueued = true;
                Coordinator.BeforePrepare(_BeforePrepare);
                // This is queued by the BeforePrepare handler
                // Coordinator.BeforeIssue(_BeforeIssue);
            }
        }

        private void QueueGenerateMips () {
            lock (Lock) {
                _IsQueued = false;

                var workItem = new GenerateMipsWorkItem {
                    Atlas = this,
                    Region = DirtyMipsRegion
                };
                DirtyMipsRegion = default;
                Coordinator.ThreadGroup.Enqueue(workItem);

                Coordinator.BeforeIssue(_BeforeIssue);
            }
        }

        public void Flush () {
            Coordinator.ThreadGroup.GetQueueForType<GenerateMipsWorkItem>().WaitUntilDrained();

            lock (Lock) {
                if (IsDisposed)
                    return;

                FlushLocked(false);

                // HACK: If invalidation happens in the middle of us performing updates, some updates
                //  might never get propagated into our mips
                if (_NeedRequeue) {
                    _NeedRequeue = false;
                    QueueHandlers();
                }
            }
        }

        private void FlushLocked (bool isRetrying) {
            EnsureValidResource();

            if (Texture == null)
                return;

            var hSrc = GCHandle.Alloc(Pixels, GCHandleType.Pinned);
            var dr = DirtyUploadRegion;
            try {
                DirtyUploadRegion = default;
                UploadRect(hSrc.AddrOfPinnedObject(), Width, Height, 0, dr);
                UploadMipsLocked(hSrc, dr);
            } catch (ObjectDisposedException ode) {
                if (isRetrying)
                    throw;

                Texture = null;
                EnsureValidResource();
                DirtyUploadRegion = dr;
                FlushLocked(true);
            } finally {
                hSrc.Free();
            }
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

        private unsafe void GenerateMips (Rectangle region) {
            lock (Lock) {
                if (IsDisposed)
                    return;

                var hSrc = GCHandle.Alloc(Pixels, GCHandleType.Pinned);
                try {
                    GenerateMipsLocked(hSrc, region);
                } finally {
                    hSrc.Free();
                }
            }
        }

        // FIXME: Deduplicate?
        private unsafe void GenerateMipsLocked (GCHandle hSrc, Rectangle region) {
            if (MipLevelCount <= 1) {
                DirtyUploadRegion = Rectangle.Union(DirtyUploadRegion, region);
                return;
            }

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
                    var srcStrideBytes = srcWidth * BytesPerPixel;
                    var destStrideBytes = destWidth * BytesPerPixel;

                    GenerateMip(
                        pSrc, srcWidth, srcHeight, srcStrideBytes,
                        pDest, destWidth, destHeight, destStrideBytes
                    );

                    pSrc = pDest;
                    pDest += destHeight * destStrideBytes;

                    srcWidth = destWidth;
                    srcHeight = destHeight;
                }
            } finally {
                DirtyUploadRegion = Rectangle.Union(DirtyUploadRegion, region);
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

        public Bounds BoundsFromRectangle (in Rectangle rectangle) {
            return GameExtensionMethods.BoundsFromRectangle(Width, Height, in rectangle);
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

        public override string ToString () => $"DynamicAtlas<{typeof(T).Name}> #{GetHashCode():X8}";
    }
}
