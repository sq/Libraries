using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Threading;
using Squared.Util;

namespace Squared.Render.STB {
    public unsafe class Image : IDisposable {
        private volatile int _RefCount;
        public int RefCount => _RefCount;

        public int Width, Height, OriginalChannelCount, ChannelCount;
        public bool IsDisposed { get; private set; }
        private volatile void* _Data;
        public void* Data => _Data;
        public bool IsFloatingPoint { get; private set; }
        public bool Is16Bit { get; private set; }

        public int DataLength => Width * Height * ChannelCount;

        private byte[][] MipChain = null;

        private static FileStream OpenStream (string path) {
            return File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        }

        public Image (string path, bool premultiply = true, bool asFloatingPoint = false, bool enable16Bit = false)
            : this (OpenStream(path), true, premultiply, asFloatingPoint) {
        }

        public Image (Stream stream, bool ownsStream, bool premultiply = true, bool asFloatingPoint = false, bool enable16Bit = false, bool generateMips = false) {
            var length = stream.Length - stream.Position;

            _RefCount = 1;

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
            } else if (stream is FileStream fs) {
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
                generateMips: generateMips
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

        public unsafe Image (ArraySegment<byte> buffer, bool premultiply = true, bool asFloatingPoint = false, bool generateMips = false) {
            fixed (byte* pBuffer = buffer.Array) {
                InitializeFromPointer(
                    pBuffer, buffer.Offset, buffer.Count,
                    premultiply: premultiply, 
                    asFloatingPoint: asFloatingPoint,
                    enable16Bit: false,
                    generateMips: generateMips
                );
            }
        }

        private void InitializeFromPointer (
            byte* pBuffer, int offset, int length, 
            bool premultiply = true, bool asFloatingPoint = false, 
            bool enable16Bit = false, bool generateMips = false
        ) {
            IsFloatingPoint = asFloatingPoint;
            const int desiredChannelCount = 4;

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

            int components;
            SizeofPixel = Evil.TextureUtils.GetBytesPerPixelAndComponents(Format, out components);

            if (asFloatingPoint)
                ConvertFPData(premultiply);
            else if (Is16Bit)
                ConvertData16(premultiply);
            else
                ConvertData(premultiply);

            if (generateMips)
                GenerateMips();
        }

        private unsafe void ConvertFPData (bool premultiply) {
            if (premultiply)
                PremultiplyFPData();
        }

        private unsafe void ConvertData16 (bool premultiply) {
            if (premultiply)
                throw new NotImplementedException();
        }

        private unsafe void ConvertData (bool premultiply) {
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

        private unsafe void PremultiplyData () {
            if (IsDisposed)
                throw new ObjectDisposedException("Image");
            if (ChannelCount != 4)
                throw new InvalidOperationException("Image is not rgba");
            var pData = (uint*)_Data;
            var pBytes = (byte*)pData;
            var pEnd = pData + (Width * Height);
            for (; pData < pEnd; pData++, pBytes+=4) {
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

        private unsafe void PremultiplyAndChannelSwapData () {
            if (IsDisposed)
                throw new ObjectDisposedException("Image");
            if (ChannelCount != 4)
                throw new InvalidOperationException("Image is not rgba");
            var pData = (uint*)_Data;
            var pBytes = (byte*)pData;
            var pEnd = pData + (Width * Height);
            for (; pData < pEnd; pData++, pBytes+=4) {
                var value = *pData;
                var a = (value & 0xFF000000) >> 24;
                var r = (value & 0xFF);
                var g = (value & 0xFF00) >> 8;
                var b = (value & 0xFF0000) >> 16;
                pBytes[0] = (byte)(b * a / 255);
                pBytes[1] = (byte)(g * a / 255);
                pBytes[2] = (byte)(r * a / 255);
            }
        }

        private unsafe void ChannelSwapData () {
            if (IsDisposed)
                throw new ObjectDisposedException("Image");
            var pBytes = (byte*)_Data;
            var pEnd = pBytes + DataLength;
            for (; pBytes < pEnd; pBytes += ChannelCount) {
                var r = pBytes[0];
                pBytes[0] = pBytes[2];
                pBytes[2] = r;
            }
        }

        public SurfaceFormat Format {
            get {
                if (IsFloatingPoint)
                    return SurfaceFormat.Vector4;
                else if (Is16Bit)
                    return SurfaceFormat.Rgba64;
                else if (ChannelCount == 4)
                    return SurfaceFormat.Color;
                else if (ChannelCount == 1)
                    return SurfaceFormat.Alpha8;
                else
                    throw new NotImplementedException($"{ChannelCount} channel(s)");
            }
        }

        public Texture2D CreateTexture (RenderCoordinator coordinator, bool padToPowerOfTwo = false) {
            if (IsDisposed)
                throw new ObjectDisposedException("Image");
            // FIXME: Channel count

            int width = padToPowerOfTwo ? Arithmetic.NextPowerOfTwo(Width) : Width;
            var height = padToPowerOfTwo ? Arithmetic.NextPowerOfTwo(Height) : Height;

            Texture2D result;
            lock (coordinator.CreateResourceLock) {
                result = new Texture2D(coordinator.Device, width, height, MipChain != null, Format) {
                    Tag = "STB.Image"
                };
                coordinator.AutoAllocatedTextureResources.Add(result);
            }

            // FIXME: FP mips, 16bit mips
            if ((MipChain != null) && !IsFloatingPoint && !Is16Bit)
                UploadWithMips(coordinator, result, false, true);
            else
                UploadDirect(coordinator, result, false, true);

            return result;
        }

        public Future<Texture2D> CreateTextureAsync (RenderCoordinator coordinator, bool mainThread, bool padToPowerOfTwo) {
            if (IsDisposed)
                throw new ObjectDisposedException("Image");
            // FIXME: Channel count

            int width = padToPowerOfTwo ? Arithmetic.NextPowerOfTwo(Width) : Width;
            var height = padToPowerOfTwo ? Arithmetic.NextPowerOfTwo(Height) : Height;

            Texture2D tex;
            lock (coordinator.CreateResourceLock) {
                tex = new Texture2D(coordinator.Device, width, height, MipChain != null, Format) {
                    Tag = "STB.Image"
                };
                coordinator.AutoAllocatedTextureResources.Add(tex);
            }

            // FIXME: FP mips, 16bit mips
            Future<Texture2D> result;
            if ((MipChain != null) && !IsFloatingPoint && !Is16Bit)
                result = UploadWithMips(coordinator, tex, true, mainThread);
            else
                result = UploadDirect(coordinator, tex, true, mainThread);

            return result;
        }

        public int SizeofPixel { get; private set; }

        private Stopwatch UploadTimer = new Stopwatch();

        private Future<Texture2D> UploadDirect (RenderCoordinator coordinator, Texture2D result, bool async, bool mainThread) {
            if (IsDisposed)
                throw new ObjectDisposedException("Image");
            // FIXME: async?
            UploadTimer.Restart();
            lock (coordinator.UseResourceLock)
                Evil.TextureUtils.SetDataFast(result, 0, Data, new Rectangle(0, 0, Width, Height), (uint)(Width * SizeofPixel));
            if (UploadTimer.Elapsed.TotalMilliseconds > 1)
                Debug.Print($"Uploading non-mipped texture took {UploadTimer.Elapsed.TotalMilliseconds}ms");
            return new Future<Texture2D>(result);
        }

        private unsafe void GenerateMips () {
            void* pPreviousLevelData = null, pLevelData = Data;
            int levelWidth = Width, levelHeight = Height;
            int previousLevelWidth = Width, previousLevelHeight = Height;
            // FIXME
            MipChain = new byte[64][];
            var pins = new List<GCHandle>();

            for (uint level = 0; (levelWidth >= 1) && (levelHeight >= 1); level++) {
                if (IsDisposed)
                    throw new ObjectDisposedException("Image");

                if (level > 0) {
                    var levelBuf = new byte[levelWidth * levelHeight * SizeofPixel];
                    MipChain[level - 1] = levelBuf;
                    var pin = GCHandle.Alloc(levelBuf, GCHandleType.Pinned);
                    pins.Add(pin);
                    pLevelData = (void*)pin.AddrOfPinnedObject();
                    // FIXME: Do this one step at a time in a list of work items
                    MipGenerator.Color(pPreviousLevelData, previousLevelWidth, previousLevelHeight, pLevelData, levelWidth, levelHeight);
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

        private unsafe Future<Texture2D> UploadWithMips (RenderCoordinator coordinator, Texture2D result, bool async, bool mainThread) {
            var pPreviousLevelData = Data;
            var pLevelData = Data;
            int levelWidth = Width, levelHeight = Height;
            int previousLevelWidth = Width, previousLevelHeight = Height;

            if (MipChain == null)
                throw new Exception("Mip chain not generated or already uploaded");

            UploadTimer.Restart();

            var f = new Future<Texture2D>();
            int itemsPending = 1;
            var onItemComplete = (OnWorkItemComplete<UploadMipWorkItem>)((ref UploadMipWorkItem wi) => {
                if (Interlocked.Decrement(ref itemsPending) != 0)
                    return;
                f.SetResult(result, null);
            });

            var queue = coordinator.ThreadGroup.GetQueueForType<UploadMipWorkItem>(mainThread);
            for (uint level = 0; (levelWidth >= 1) && (levelHeight >= 1); level++) {
                if (IsDisposed)
                    throw new ObjectDisposedException("Image");

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
                    Interlocked.Increment(ref itemsPending);
                    queue.Enqueue(workItem, onItemComplete);
                } else
                    workItem.Execute();

                previousLevelWidth = levelWidth;
                previousLevelHeight = levelHeight;
                var newWidth = levelWidth / 2;
                var newHeight = levelHeight / 2;
                levelWidth = newWidth;
                levelHeight = newHeight;
            }

            Interlocked.Decrement(ref itemsPending);
            if (!async)
                f.SetResult(result, null);

            if (UploadTimer.Elapsed.TotalMilliseconds > 2)
                Debug.Print($"Uploading mipped texture took {UploadTimer.Elapsed.TotalMilliseconds}ms");

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
            }
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

            public void Execute () {
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
