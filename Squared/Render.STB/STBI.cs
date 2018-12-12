using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Util;

namespace Squared.Render.STB {
    public unsafe class Image : IDisposable {
        public readonly int Width, Height, ChannelCount;
        public bool IsDisposed { get; private set; }
        public void* Data { get; private set; }

        public Image (string path, bool premultiply = true)
            : this (File.OpenRead(path), true, premultiply) {
        }

        public Image (FileStream stream, bool ownsStream, bool premultiply = true) {
            BufferPool<byte>.Buffer scratch = BufferPool<byte>.Allocate(10240);

            var callbacks = new Native.STBI_IO_Callbacks {
                eof = (user) => {
                    return stream.Position >= stream.Length ? 1 : 0;
                },
                read = (user, buf, count) => {
                    if (scratch.Data.Length < count) {
                        scratch.Dispose();
                        scratch = BufferPool<byte>.Allocate(count);
                    }

                    var bytesRead = stream.Read(scratch.Data, 0, count);
                    fixed (byte* pScratch = scratch.Data)
                        Buffer.MemoryCopy(pScratch, buf, count, bytesRead);
                    return bytesRead;
                },
                skip = (user, count) => {
                    stream.Seek(count, SeekOrigin.Current);
                }
            };

            // FIXME: Don't request RGBA?
            Data = Native.API.stbi_load_from_callbacks(ref callbacks, null, out Width, out Height, out ChannelCount, 4);

            scratch.Dispose();
            if (ownsStream)
                stream.Dispose();

            if (Data == null) {
                var reason = STB.Native.API.stbi_failure_reason();
                var message = "Failed to load image";
                if (reason != null)
                    message += ": " + Encoding.UTF8.GetString(reason, 128);
                throw new Exception(message);
            }

            ConvertData(premultiply);
        }

        private unsafe void ConvertData (bool premultiply) {
            if (premultiply)
                PremultiplyAndChannelSwapData();
            else
                ChannelSwapData();
        }

        private unsafe void PremultiplyAndChannelSwapData () {
            var pData = (byte*)Data;
            for (int i = 0, count = Width * Height; i < count; i++, pData+=4) {
                var a = pData[3];
                var r = (byte)(pData[0] * a / 255);
                var g = (byte)(pData[1] * a / 255);
                var b = (byte)(pData[2] * a / 255);
                pData[0] = b;
                pData[1] = g;
                pData[2] = r;
            }
        }

        private unsafe void ChannelSwapData () {
            var pData = (byte*)Data;
            for (int i = 0, count = Width * Height; i < count; i++, pData+=4) {
                var temp = pData[0];
                pData[0] = pData[2];
                pData[2] = temp;
            }
        }

        public Texture2D CreateTexture (RenderCoordinator coordinator, bool generateMips = false) {
            if (IsDisposed)
                throw new ObjectDisposedException("Image is disposed");
            // FIXME: Channel count
            var pPreviousLevelData = Data;
            var pLevelData = Data;
            int levelWidth = Width, levelHeight = Height;
            int previousLevelWidth = Width, previousLevelHeight = Height;
            Texture2D result;
            lock (coordinator.CreateResourceLock)
                result = new Texture2D(coordinator.Device, Width, Height, generateMips, SurfaceFormat.Color);

            using (var scratch = BufferPool<Color>.Allocate(Width * Height))
            fixed (Color* pScratch = scratch.Data)
            for (uint level = 0; (levelWidth >= 1) && (levelHeight >= 1); level++) {
                if (level > 0) {
                    if (generateMips)
                        pLevelData = pScratch;
                    else
                        break;

                    MipGenerator.Color(pPreviousLevelData, previousLevelWidth, previousLevelHeight, pLevelData, levelWidth, levelHeight);
                }

                lock (coordinator.UseResourceLock)
                    Evil.TextureUtils.SetDataFast(result, level, pLevelData, levelWidth, levelHeight, (uint)levelWidth * 4);
                /*
                var pSurface = Evil.TextureUtils.GetSurfaceLevel(result, level);
                try {
                    Evil.TextureUtils.SetData(result, pSurface, pLevelData, levelWidth, levelHeight, (uint)levelWidth * 4, Evil.D3DFORMAT.A8B8G8R8);
                } finally {
                    Marshal.Release(new IntPtr(pSurface));
                }
                */

                previousLevelWidth = levelWidth;
                previousLevelHeight = levelHeight;
                var newWidth = levelWidth / 2;
                var newHeight = levelHeight / 2;
                levelWidth = newWidth;
                levelHeight = newHeight;
                var temp = pPreviousLevelData;
                if (temp == pLevelData) {
                    pLevelData = pScratch;
                } else {
                    pPreviousLevelData = pLevelData;
                    pLevelData = pPreviousLevelData;
                }
            }
            return result;
        }

        public void Dispose () {
            IsDisposed = true;
            if (Data != null) {
                Native.API.stbi_image_free(Data);
                Data = null;
            }
        }
    }

    public enum ImagePrecision {
        Default = 0,
        Byte = 8,
        UInt16 = 16
    }

    public enum ImageChannels {
        Default = 0,
        Grey = 1,
        GreyAlpha = 2,
        RGB = 3,
        RGBA = 4
    }
}
