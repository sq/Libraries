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
            var pData = (uint*)Data;
            var pBytes = (byte*)Data;
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
            var pBytes = (byte*)Data;
            var pEnd = pBytes + (Width * Height * 4);
            for (; pBytes < pEnd; pBytes += 4) {
                var r = pBytes[0];
                pBytes[0] = pBytes[2];
                pBytes[2] = r;
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
