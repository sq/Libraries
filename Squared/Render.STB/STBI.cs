using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Render.Mips;
using Squared.Render.Resources;
using Squared.Threading;
using Squared.Util;

namespace Squared.Render.STB {
    public unsafe sealed class Image : IDisposable {
        // FIXME: Causes crashes
        public const bool EnableMmap = true;

        public readonly string Name;
        private volatile int _RefCount;
        public int RefCount => _RefCount;

        public int Width, Height, OriginalChannelCount, ChannelCount;
        public bool IsDisposed { get; private set; }
        private volatile void* _Data;
        public void* Data => _Data;
        public bool IsPremultiplied { get; set; }
        public bool IsFloatingPoint { get; private set; }
        public bool Is16Bit { get; private set; }

        public int DataLength => Width * Height * ChannelCount;

        private byte[][] MipChain = null;

        private static FileStream OpenStream (string path) {
            return File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        }

        public static unsafe void GetInfoFromFile (FileStream stream, string path, out int width, out int height, out int channels) {
            bool ownsStream = stream == null;
            if (ownsStream)
                stream = File.Open(path, FileMode.Open);
            using (var mappedFile = MemoryMappedFile.CreateFromFile(stream, null, 0, MemoryMappedFileAccess.Read, HandleInheritability.None, true))
            using (var view = mappedFile.CreateViewAccessor(0, stream.Length, MemoryMappedFileAccess.Read)) {
                byte* ptr = null;
                view.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
                Native.API.stbi_info_from_memory(ptr, (int)view.Capacity, out width, out height, out channels);
                view.SafeMemoryMappedViewHandle.ReleasePointer();
            }
            if (ownsStream)
                stream.Dispose();
        }

        public Image (string path, bool premultiply = true, bool asFloatingPoint = false, bool enable16Bit = false)
            : this (OpenStream(path), true, premultiply, asFloatingPoint) {
            Name = path;
        }

        public Image (
            Stream stream, bool ownsStream, bool premultiply = true, 
            bool asFloatingPoint = false, bool enable16Bit = false, bool generateMips = false,
            bool sRGB = false, bool enableGrayscale = false
        ) {
            var length = stream.Length - stream.Position;

            _RefCount = 1;
            Name = (stream as FileStream)?.Name;
            if (Name == null)
                FileStreamProvider.TryGetStreamPath(stream, out Name);

            if (!stream.CanSeek)
                throw new ArgumentException("Stream must be seekable");
            if (!stream.CanRead)
                throw new ArgumentException("Stream must be readable");

            MemoryMappedFile mappedFile = null;
            MemoryMappedViewAccessor mappedView = null;
            GCHandle hData = default;
            byte* pData = null;
            int readOffset;
            if (stream is MemoryStream ms) {
                var buffer = ms.GetBuffer();
                hData = GCHandle.Alloc(buffer, GCHandleType.Pinned);
                pData = (byte*)hData.AddrOfPinnedObject();
                readOffset = (int)ms.Position;
            } else if (EnableMmap && (stream is FileStream fs)) {
                mappedFile = MemoryMappedFile.CreateFromFile(fs, null, 0, MemoryMappedFileAccess.Read, HandleInheritability.None, false);
                mappedView = mappedFile.CreateViewAccessor(0, fs.Length, MemoryMappedFileAccess.Read);
                mappedView.SafeMemoryMappedViewHandle.AcquirePointer(ref pData);
                readOffset = 0;
            } else {
                var buffer = new byte[length];
                readOffset = 0;
                stream.Read(buffer, 0, (int)length);
                hData = GCHandle.Alloc(buffer, GCHandleType.Pinned);
                pData = (byte*)hData.AddrOfPinnedObject();
            }

            InitializeFromPointer(
                pData, readOffset, (int)length, 
                premultiply: premultiply, 
                asFloatingPoint: asFloatingPoint, 
                enable16Bit: enable16Bit,
                generateMips: generateMips,
                sRGB: sRGB,
                enableGrayscale: enableGrayscale
            );

            if (ownsStream)
                stream.Dispose();
            if (hData.IsAllocated)
                hData.Free();
            if (mappedFile != null) {
                mappedView.Dispose();
                mappedFile.Dispose();
            }
        }

        public unsafe Image (ArraySegment<byte> buffer, bool premultiply = true, bool asFloatingPoint = false, bool generateMips = false, bool sRGB = false, bool enableGrayscale = false) {
            fixed (byte* pBuffer = buffer.Array) {
                InitializeFromPointer(
                    pBuffer, buffer.Offset, buffer.Count,
                    premultiply: premultiply, 
                    asFloatingPoint: asFloatingPoint,
                    enable16Bit: false,
                    generateMips: generateMips,
                    sRGB: sRGB,
                    enableGrayscale: enableGrayscale
                );
            }
        }

        private void InitializeFromPointer (
            byte* pBuffer, int offset, int length, 
            bool premultiply = true, bool asFloatingPoint = false, 
            bool enable16Bit = false, bool generateMips = false,
            bool sRGB = false, bool enableGrayscale = false
        ) {
            IsFloatingPoint = asFloatingPoint;
            Native.API.stbi_info_from_memory(pBuffer + offset, length, out int width, out int height, out int components);
            int desiredChannelCount = !enableGrayscale || (components > 1) ? 4 : 1;

            // FIXME: Don't request RGBA?
            Is16Bit = enable16Bit && Native.API.stbi_is_16_bit_from_memory(pBuffer + offset, length) != 0;

            if (asFloatingPoint)
                _Data = Native.API.stbi_loadf_from_memory(pBuffer + offset, length, out Width, out Height, out OriginalChannelCount, desiredChannelCount);
            else if (Is16Bit)
                _Data = Native.API.stbi_load_16_from_memory(pBuffer + offset, length, out Width, out Height, out OriginalChannelCount, desiredChannelCount);
            else
                _Data = Native.API.stbi_load_from_memory(pBuffer + offset, length, out Width, out Height, out OriginalChannelCount, desiredChannelCount);

            ChannelCount = desiredChannelCount;

            if (_Data == null) {
                var reason = STB.Native.API.stbi_failure_reason();
                var message = "Failed to load image";
                if (reason != null)
                    message += ": " + Encoding.UTF8.GetString(reason, 128);
                throw new Exception(message);
            }

            SizeofPixel = Evil.TextureUtils.GetBytesPerPixelAndComponents(GetFormat(sRGB, desiredChannelCount), out components);

            if (asFloatingPoint)
                ConvertFPData(premultiply);
            else if (Is16Bit)
                ConvertData16(premultiply);
            else
                ConvertData(premultiply);

            IsPremultiplied = premultiply;

            if (generateMips)
                GenerateMips(sRGB);
        }

        private unsafe void ConvertFPData (bool premultiply) {
            if (ChannelCount < 3)
                return;
            if (premultiply)
                PremultiplyFPData();
        }

        private unsafe void ConvertData16 (bool premultiply) {
            if (ChannelCount < 3)
                return;
            if (premultiply)
                PremultiplyData16();
        }

        private unsafe void ConvertData (bool premultiply) {
            if (ChannelCount < 3)
                return;
#if FNA
            if (premultiply)
                PremultiplyData();
#else
            if (premultiply)
                PremultiplyAndChannelSwapData();
            else
                ChannelSwapData();
#endif
        }

        private unsafe void PremultiplyFPData () {
            if (IsDisposed)
                throw new ObjectDisposedException("Image");
            var pData = (float*)_Data;
            var pEnd = pData + DataLength;
            for (; pData < pEnd; pData+=ChannelCount) {
                var a = pData[3];
                var temp = pData[0];
                pData[0] *= a;
                pData[1] *= a;
                pData[2] *= a;
            }
        }

        private unsafe void PremultiplyData16 () {
            if (IsDisposed)
                throw new ObjectDisposedException("Image");
            if (ChannelCount != 4)
                throw new InvalidOperationException("Image is not rgba");
            var pData = (ushort*)_Data;
            var pEnd = pData + (Width * Height * 4);
            for (; pData < pEnd; pData += 4) {
                ushort r = pData[0], g = pData[1], b = pData[2], a = pData[3];
                pData[0] = (ushort)(r * a / ushort.MaxValue);
                pData[1] = (ushort)(g * a / ushort.MaxValue);
                pData[2] = (ushort)(b * a / ushort.MaxValue);
            }
        }

        private unsafe void PremultiplyData () {
            if (IsDisposed)
                throw new ObjectDisposedException("Image");
            if (ChannelCount != 4)
                throw new InvalidOperationException("Image is not rgba");
            var pData = (uint*)_Data;
            var pBytes = (byte*)pData;
            var pEnd = pData + (Width * Height);
            for (; pData < pEnd; pData++, pBytes+=SizeofPixel) {
                var value = *pData;
                var a = (value & 0xFF000000) >> 24;
                var r = (value & 0xFF);
                var g = (value & 0xFF00) >> 8;
                var b = (value & 0xFF0000) >> 16;
                pBytes[0] = (byte)(r * a / 255);
                pBytes[1] = (byte)(g * a / 255);
                pBytes[2] = (byte)(b * a / 255);
            }
        }

        public SurfaceFormat GetFormat (bool sRGB, int channelCount) {
            switch (channelCount) {
                case 1:
                    if (IsFloatingPoint)
                        return SurfaceFormat.Single;
                    else if (Is16Bit)
                        throw new ArgumentOutOfRangeException(nameof(channelCount));
                    else
                        return SurfaceFormat.Alpha8;
                case 2:
                    if (IsFloatingPoint)
                        return SurfaceFormat.Vector2;
                    else if (Is16Bit)
                        return SurfaceFormat.Rg32;
                    else
                        throw new ArgumentOutOfRangeException(nameof(channelCount));
                case 4:
                    if (IsFloatingPoint)
                        return SurfaceFormat.Vector4;
                    else if (Is16Bit)
                        return SurfaceFormat.Rgba64;
                    else
                        return sRGB ? Evil.TextureUtils.ColorSrgbEXT : SurfaceFormat.Color;
                default:
                    throw new ArgumentOutOfRangeException(nameof(channelCount));
            }
        }

        public Texture2D CreateTexture (RenderCoordinator coordinator, bool padToPowerOfTwo = false, bool sRGB = false, string name = null) {
            if (IsDisposed)
                throw new ObjectDisposedException("Image");
            // FIXME: Channel count

            int width = padToPowerOfTwo ? Arithmetic.NextPowerOfTwo(Width) : Width;
            var height = padToPowerOfTwo ? Arithmetic.NextPowerOfTwo(Height) : Height;

            Texture2D result;
            lock (coordinator.CreateResourceLock) {
                result = new Texture2D(coordinator.Device, width, height, MipChain != null, GetFormat(sRGB, ChannelCount)) {
                    Tag = "STB.Image",
                    Name = name,
                };
                coordinator.RegisterAutoAllocatedTextureResource(result);
            }

            if (MipChain != null)
                UploadWithMips(coordinator, result, false);
            else
                UploadDirect(coordinator, result, false);

            return result;
        }

        public Future<Texture2D> CreateTextureAsync (RenderCoordinator coordinator, bool mainThread, bool padToPowerOfTwo, bool sRGB = false, string name = null) {
            if (IsDisposed)
                throw new ObjectDisposedException("Image");
            // FIXME: Channel count

            int width = padToPowerOfTwo ? Arithmetic.NextPowerOfTwo(Width) : Width;
            var height = padToPowerOfTwo ? Arithmetic.NextPowerOfTwo(Height) : Height;

            Texture2D tex;
            lock (coordinator.CreateResourceLock) {
                tex = new Texture2D(coordinator.Device, width, height, MipChain != null, GetFormat(sRGB, ChannelCount)) {
                    Tag = "STB.Image",
                    Name = name,
                };
                coordinator.RegisterAutoAllocatedTextureResource(tex);
            }

            Future<Texture2D> result;
            if (MipChain != null)
                result = UploadWithMips(coordinator, tex, true);
            else
                result = UploadDirect(coordinator, tex, true);

            return result;
        }

        public int SizeofPixel { get; private set; }

        private Stopwatch UploadTimer = new Stopwatch();

        private Future<Texture2D> UploadDirect (RenderCoordinator coordinator, Texture2D result, bool async) {
            if (IsDisposed)
                throw new ObjectDisposedException("Image");
            // FIXME: async?
            // FIXME: Make sure this happens before the next issue
            UploadTimer.Restart();
            lock (coordinator.UseResourceLock)
                Evil.TextureUtils.SetDataFast(result, 0, Data, new Rectangle(0, 0, Width, Height), (uint)(Width * SizeofPixel));
            if (UploadTimer.Elapsed.TotalMilliseconds > 1)
                Debug.Print($"Uploading non-mipped texture took {UploadTimer.Elapsed.TotalMilliseconds}ms");
            return new Future<Texture2D>(result);
        }

        private unsafe void GenerateMips (bool sRGB) {
            void* pPreviousLevelData = null, pLevelData = Data;
            int levelWidth = Width, levelHeight = Height;
            int previousLevelWidth = Width, previousLevelHeight = Height;
            // FIXME
            var pins = new List<GCHandle>();

            MipFormat format = (MipFormat)(-1);
            if (IsFloatingPoint) {
                if (ChannelCount == 4) {
                    format = IsPremultiplied ? MipFormat.pVector4 : MipFormat.Vector4;
                } else if (ChannelCount == 1) {
                    format = MipFormat.Single;
                }
            } else if (!Is16Bit) {
                if (ChannelCount == 4) {
                    format = IsPremultiplied ? MipFormat.pRGBA : MipFormat.RGBA;
                } else {
                    format = MipFormat.Gray1;
                }
            }

            if (format == (MipFormat)(-1))
                throw new ArgumentOutOfRangeException("Mip generation is not supported for this image format");
            if (sRGB)
                format |= MipFormat.sRGB;

            var mipGenerator = STBMipGenerator.Get(format);
            if (mipGenerator == null)
                return;

            MipChain = new byte[64][];

            for (uint level = 0; (levelWidth >= 1) && (levelHeight >= 1); level++) {
                if (IsDisposed)
                    throw new ObjectDisposedException("Image");

                if (level > 0) {
                    // FIXME: Use NativeAllocator
                    var levelBuf = new byte[levelWidth * levelHeight * SizeofPixel];
                    MipChain[level - 1] = levelBuf;
                    var pin = GCHandle.Alloc(levelBuf, GCHandleType.Pinned);
                    pins.Add(pin);
                    pLevelData = (void*)pin.AddrOfPinnedObject();
                    // FIXME: Do this one step at a time in a list of work items
                    var previousStrideBytes = previousLevelWidth * SizeofPixel;
                    var levelStrideBytes = levelWidth * SizeofPixel;
                    mipGenerator(
                        pPreviousLevelData, previousLevelWidth, previousLevelHeight, previousStrideBytes,
                        pLevelData, levelWidth, levelHeight, levelStrideBytes
                    );
                }

                previousLevelWidth = levelWidth;
                previousLevelHeight = levelHeight;
                var newWidth = levelWidth / 2;
                var newHeight = levelHeight / 2;
                levelWidth = newWidth;
                levelHeight = newHeight;
                pPreviousLevelData = pLevelData;
            }

            foreach (var pin in pins)
                pin.Free();
        }

        private unsafe Future<Texture2D> UploadWithMips (RenderCoordinator coordinator, Texture2D result, bool async) {
            var pPreviousLevelData = Data;
            var pLevelData = Data;
            int levelWidth = Width, levelHeight = Height;
            int previousLevelWidth = Width, previousLevelHeight = Height;

            if (MipChain == null)
                throw new Exception("Mip chain not generated or already uploaded");

            UploadTimer.Restart();

            var f = new Future<Texture2D>();
            try {
                int itemsPending = 0;
                bool doneQueueing = false;

                var mainThread = (coordinator.GraphicsBackendName == "OpenGL");
                var queue = coordinator.ThreadGroup.GetQueueForType<UploadMipWorkItem>(mainThread);
                for (uint level = 0; (levelWidth >= 1) && (levelHeight >= 1); level++) {
                    if (IsDisposed)
                        throw new ObjectDisposedException("Image");
                    Interlocked.Increment(ref itemsPending);

                    byte[] mip;
                    uint mipPitch;
                    if (level > 0) {
                        mip = MipChain[level - 1];
                        mipPitch = (uint)(levelWidth * SizeofPixel);
                    } else {
                        mip = null;
                        mipPitch = (uint)(Width * SizeofPixel);
                    }

                    var workItem = new UploadMipWorkItem {
                        Coordinator = coordinator,
                        Image = this,
                        Mip = mip,
                        MipPitch = mipPitch,
                        Texture = result,
                        Level = level,
                        LevelWidth = levelWidth,
                        LevelHeight = levelHeight
                    };
                    if (async) {
                        // FIXME
                        queue.Enqueue(workItem, OnItemComplete);
                    } else
                        workItem.Execute(coordinator.ThreadGroup);

                    previousLevelWidth = levelWidth;
                    previousLevelHeight = levelHeight;
                    var newWidth = levelWidth / 2;
                    var newHeight = levelHeight / 2;
                    levelWidth = newWidth;
                    levelHeight = newHeight;
                }

                doneQueueing = true;
                if (!async || (Volatile.Read(ref itemsPending) <= 0)) {
                    // HACK
                    var temp = default(UploadMipWorkItem);
                    OnItemComplete(ref temp);
                }

                if (!async && (UploadTimer.Elapsed.TotalMilliseconds > 2))
                    Debug.Print($"Uploading mipped texture took {UploadTimer.Elapsed.TotalMilliseconds}ms");

                void OnItemComplete (ref UploadMipWorkItem wi) {
                    if (Interlocked.Decrement(ref itemsPending) > 0)
                        return;
                    if (doneQueueing && !f.Completed)
                        f.SetResult(result, null);
                }
            } catch (Exception exc) {
                f.SetResult2(null, ExceptionDispatchInfo.Capture(exc));
            }

            // FIXME: Ensure this is finished before the next issue
            return f;
        }

        public void AddRef () {
            if (IsDisposed)
                throw new ObjectDisposedException("Image");
            if (Interlocked.Increment(ref _RefCount) <= 1)
                throw new ObjectDisposedException("Image");
        }

        public void Dispose () {
            if (IsDisposed)
                return;

            var newRefCount = Interlocked.Decrement(ref _RefCount);

            if (RefCount <= 0) {
                IsDisposed = true;
                var data = Data;
                _Data = null;
                MipChain = null;
                if (data != null)
                    Native.API.stbi_image_free(data);

                GC.SuppressFinalize(this);
            }
        }

        ~Image () {
            var data = Data;
            _Data = null;
            if (data != null)
                Native.API.stbi_image_free(data);
        }

        private unsafe struct UploadMipWorkItem : IWorkItem {
            public static WorkItemConfiguration Configuration =>
                new WorkItemConfiguration {
                    MaxConcurrency = 1,
                    DefaultStepCount = 4
                };

            internal uint MipPitch;
            internal Image Image;
            internal Texture2D Texture;
            internal uint Level;
            internal int LevelHeight;
            internal RenderCoordinator Coordinator;

            public object Mip { get; set; }
            public int LevelWidth { get; internal set; }

            public void Execute (ThreadGroup group) {
                var pin = default(GCHandle);
                void* pData;
                if (Mip != null) {
                    pin = GCHandle.Alloc(Mip, GCHandleType.Pinned);
                    pData = (void*)pin.AddrOfPinnedObject();
                } else {
                    pData = Image.Data;
                    if (pData == null)
                        throw new Exception("Image has no data");
                }

                lock (Coordinator.UseResourceLock)
                    Evil.TextureUtils.SetDataFast(Texture, Level, pData, new Rectangle(0, 0, LevelWidth, LevelHeight), MipPitch);

                if (pin.IsAllocated)
                    pin.Free();
            }
        }
    }
}
