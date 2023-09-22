using System;
using System.Collections.Generic;
using System.Diagnostics;
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

    public interface IDynamicAtlas : IDisposable, IDynamicTexture {
        IntPtr Data { get; }
        bool TryReserve (int widthW, int heightW, out DynamicAtlasReservation result);
        void Invalidate (Rectangle rectangle);
        void Clear ();
        Bounds BoundsFromRectangle (in Rectangle rectangle);
    }

    public readonly struct DynamicAtlasReservation {
        public readonly IDynamicAtlas Atlas;
        public readonly int X, Y, Width, Height;

        internal DynamicAtlasReservation (IDynamicAtlas atlas, int x, int y, int w, int h) {
            Atlas = atlas;
            X = x;
            Y = y;
            Width = w;
            Height = h;
        }

        public unsafe void Upload<T> (T[] pixels)
            where T : unmanaged
        {
            fixed (T* pPixels = pixels)
                Upload(pPixels, pixels.Length);
        }

        public unsafe void Upload<T> (T *data, int elementCount)
            where T : unmanaged
        {
            var elementSize = Marshal.SizeOf<T>();
            if (!(Atlas is DynamicAtlas<T> atlas))
                throw new ArgumentException($"Expected DynamicAtlas<{typeof(T)}> but was {Atlas.GetType()}");

            var atlasPixels = atlas.Data;
            var atlasSize = atlas.PixelBuffer.Size;
            var strideBytes = Width * elementSize;
            var height = Math.Min(Height, elementCount / Width);
            for (var y = 0; y < height; y++) {
                var dest = ((Y + y) * Atlas.Width) + X;
                var src = y * Width;
                Buffer.MemoryCopy(data + src, atlasPixels + dest, atlasSize - (dest * elementSize), strideBytes);
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

    public unsafe sealed class DynamicAtlas<T> : IDynamicAtlas
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

            public void Execute (ThreadGroup group) {
                Atlas.GenerateMips(Region);
            }
        }

        public bool IsDisposed { get; private set; }

        public readonly RenderCoordinator Coordinator;
        public int BytesPerPixel { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }
        public SurfaceFormat Format { get; private set; }
        public Texture2D Texture { get; private set; }
        public T* Data => (T*)PixelBuffer.Data;
        IntPtr IDynamicAtlas.Data => (IntPtr)PixelBuffer.Data;
        public bool IsDirty => (DirtyMipsRegion.Width + DirtyUploadRegion.Width + DirtyMipsRegion.Height + DirtyUploadRegion.Height) > 0;
        public int Spacing { get; private set; }
        public T? ClearValue;

        private int X, Y, RowHeight;
        private object Lock = new object();
        private Action _BeforeIssue, _BeforePrepare;
        private bool _NeedClear, _IsQueued, _NeedRequeue;
        internal NativeAllocation PixelBuffer;
        private NativeAllocation MipBuffer;
        private int MipLevelCount;
        private Rectangle DirtyMipsRegion, DirtyUploadRegion;
        private MipGeneratorFn GenerateMip;

        public DynamicAtlas (
            RenderCoordinator coordinator, int width, int height, SurfaceFormat format, 
            int spacing = 2, MipGeneratorFn mipGenerator = null, object tag = null
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

            _NeedClear = true;
            PixelBuffer = coordinator.AtlasAllocator.Allocate<T>(width * height);
            if (mipGenerator != null) {
                var totalMipSize = 4;
                var currentMipSizePixels = (width / 2) * (height / 2);
                while (currentMipSizePixels > 1) {
                    totalMipSize += (currentMipSizePixels + 1);
                    currentMipSizePixels /= 2;
                }
                MipBuffer = Coordinator.AtlasAllocator.Allocate<T>(totalMipSize);
            }
            
            if (DebugColors) {
                var p = (Color*)(void*)Data;
                if (p != null) {
                    var w = 4096 * 4;
                    for (int i = 0, l = PixelBuffer.Size / 4; i < l; i++)
                        p[i] = new Color(i % 255, (Id * 64) % 255, (Id * 192) % 255, 255);
                }
            }

            MipLevelCount = Texture.LevelCount;

            Invalidate();
        }

        public MipGeneratorFn MipGenerator {
            get => GenerateMip;
            set {
                if (value == GenerateMip)
                    return;
                if (value == null)
                    throw new NullReferenceException();
                GenerateMip = value;
                Invalidate();
            }
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
                    Tag = $"Atlas<{typeof(T).Name}> {Tag ?? GetHashCode().ToString("X8")}"
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

                if ((workItem.Region.Width <= 0) || (workItem.Region.Height <= 0))
                    return;

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

            var dr = DirtyUploadRegion;
            PixelBuffer.AddReference();
            try {
                DirtyUploadRegion = default;
                UploadRect((IntPtr)PixelBuffer.Data, Width, Height, 0, dr);
                UploadMipsLocked(PixelBuffer, dr);
            } catch (ObjectDisposedException ode) {
                if (isRetrying)
                    throw;

                Texture = null;
                EnsureValidResource();
                DirtyUploadRegion = dr;
                FlushLocked(true);
            } finally {
                PixelBuffer.ReleaseReference();
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

        private unsafe void UploadMipsLocked (NativeAllocation src, Rectangle region) {
            if (MipLevelCount <= 1)
                return;

            var srcWidth = Width;
            var srcHeight = Height;

            try {
                var pSrcBuffer = src.Data;
                var pSrc = pSrcBuffer;
                MipBuffer.AddReference();
                var pDest = (byte*)MipBuffer.Data;
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
                MipBuffer.ReleaseReference();
            }
        }

        private unsafe void GenerateMips (Rectangle region) {
            lock (Lock) {
                if (IsDisposed)
                    return;

                PixelBuffer.AddReference();
                try {
                    GenerateMipsLocked(PixelBuffer, region);
                } finally {
                    PixelBuffer.ReleaseReference();
                }
            }
        }

        // FIXME: Deduplicate?
        private unsafe void GenerateMipsLocked (NativeAllocation src, Rectangle region) {
            if (MipLevelCount <= 1) {
                DirtyUploadRegion = Rectangle.Union(DirtyUploadRegion, region);
                return;
            }

            var srcWidth = Width;
            var srcHeight = Height;
            var started = Stopwatch.GetTimestamp();
            
            try {
                var pSrcBuffer = src.Data;
                var pSrc = (byte*)pSrcBuffer;
                MipBuffer.AddReference();
                var pDest = (byte*)MipBuffer.Data;
                int x1 = region.Left, y1 = region.Top, x2 = region.Width, y2 = region.Height;
                var srcRect = new Rectangle(x1, y1, x2, y2);
                var destRect = new Rectangle(x1 / 2, y1 / 2, (x2 + 1) / 2, (y2 + 1) / 2);

                for (var i = 1; i < MipLevelCount; i++) {
                    int destWidth = srcWidth / 2, 
                        destHeight = srcHeight / 2,
                        srcStrideBytes = srcWidth * BytesPerPixel, 
                        destStrideBytes = destWidth * BytesPerPixel;

                    GenerateMip(
                        pSrc, srcWidth, srcHeight, srcStrideBytes,
                        pDest, destWidth, destHeight, destStrideBytes
                    );

                    pSrc = pDest;
                    pDest += destHeight * destStrideBytes;

                    srcWidth = destWidth;
                    srcHeight = destHeight;
                    srcRect = destRect;
                }
            } finally {
                DirtyUploadRegion = Rectangle.Union(DirtyUploadRegion, region);
                MipBuffer.ReleaseReference();
                var elapsedMs = (Stopwatch.GetTimestamp() - started) / (double)(Stopwatch.Frequency / 1000);
                if (elapsedMs >= 3)
                    Debug.WriteLine($"Generating mips took {elapsedMs}ms for {region.Width}x{region.Height} region");
            }
        }

        public bool TryReserve (int width, int height, out DynamicAtlasReservation result) {
            lock (Lock) {
                bool needWrap = (X + width) >= (Width - 1);

                if (
                    ((Y + height) >= Height) ||
                    (
                        needWrap && ((Y + RowHeight + height) >= Height)
                    )
                ) {
                    result = default(DynamicAtlasReservation);
                    return false;
                }

                if (needWrap) {
                    Y += (RowHeight + Spacing);
                    X = Spacing;
                    RowHeight = 0;
                }

                result = new DynamicAtlasReservation(this, X, Y, width, height);
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

            if (!ClearValue.HasValue || ClearValue.Value.Equals(default(T))) {
                MemoryUtil.Memset((byte*)Data, 0, PixelBuffer.Size);
            } else {
                var ptr = Data;
                var cvalue = ClearValue.Value;
                for (int i = 0, c = Width * Height; i < c; i++)
                    ptr[i] = cvalue;
            }
        }

        void IDynamicAtlas.Clear () => Clear();

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
                MipBuffer?.ReleaseReference();
                PixelBuffer?.ReleaseReference();
                Texture = null;
            }
        }

        public static implicit operator AbstractTextureReference (DynamicAtlas<T> atlas) {
            return new AbstractTextureReference(atlas);
        }

        public override string ToString () => $"DynamicAtlas<{typeof(T).Name}> #{GetHashCode():X8}";
    }
}
