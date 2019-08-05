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
        public bool IsFloatingPoint { get; private set; }
        public bool IsPaletted { get; private set; }
        public UInt32[] Palette { get; private set; }

        private static FileStream OpenStream (string path) {
            return File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        }

        public Image (string path, bool premultiply = true, bool asFloatingPoint = false, UInt32[] palette = null)
            : this (OpenStream(path), true, premultiply, asFloatingPoint, palette) {
        }

        public Image (Stream stream, bool ownsStream, bool premultiply = true, bool asFloatingPoint = false, UInt32[] palette = null) {
            BufferPool<byte>.Buffer scratch = BufferPool<byte>.Allocate(10240);
            var length = stream.Length;

            if (!stream.CanSeek)
                throw new ArgumentException("Stream must be seekable");
            if (!stream.CanRead)
                throw new ArgumentException("Stream must be readable");

            var callbacks = new Native.STBI_IO_Callbacks {
                eof = (user) => {
                    return stream.Position >= length ? 1 : 0;
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

            IsFloatingPoint = asFloatingPoint;

            // FIXME: Don't request RGBA?
            if (palette != null) {
                if (asFloatingPoint)
                    throw new ArgumentException("Cannot load paletted image as floating point");
                else if (premultiply)
                    throw new ArgumentException("FIXME: Cannot premultiply paletted image");
                ChannelCount = 1;
                fixed (UInt32 * pPalette = palette)
                    Data = Native.API.stbi_load_from_callbacks_with_palette(ref callbacks, null, out Width, out Height, pPalette, palette.Length);
                Palette = palette;
                IsPaletted = true;
            } else if (asFloatingPoint)
                Data = Native.API.stbi_loadf_from_callbacks(ref callbacks, null, out Width, out Height, out ChannelCount, 4);
            else
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

            if (asFloatingPoint)
                ConvertFPData(premultiply);
            else if (palette == null)
                ConvertData(premultiply);
        }

        private unsafe void ConvertFPData (bool premultiply) {
            if (premultiply)
                PremultiplyFPData();
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
            var pData = (float*)Data;
            var pEnd = pData + (Width * Height * ChannelCount);
            for (; pData < pEnd; pData+=4) {
                var a = pData[3];
                var temp = pData[0];
                pData[0] *= a;
                pData[1] *= a;
                pData[2] *= a;
            }
        }

        private unsafe void PremultiplyData () {
            var pData = (uint*)Data;
            var pBytes = (byte*)Data;
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
            var pEnd = pBytes + (Width * Height * ChannelCount);
            for (; pBytes < pEnd; pBytes += 4) {
                var r = pBytes[0];
                pBytes[0] = pBytes[2];
                pBytes[2] = r;
            }
        }

        public SurfaceFormat Format {
            get {
                if (IsPaletted)
                    return SurfaceFormat.Alpha8;
                else if (IsFloatingPoint)
                    return SurfaceFormat.Vector4;
                else
                    return SurfaceFormat.Color;
            }
        }

        public Texture2D CreateTexture (RenderCoordinator coordinator, bool generateMips = false) {
            if (IsDisposed)
                throw new ObjectDisposedException("Image is disposed");
            // FIXME: Channel count
            Texture2D result;
            lock (coordinator.CreateResourceLock)
                result = new Texture2D(coordinator.Device, Width, Height, generateMips, Format);

            // FIXME: FP mips
            if (generateMips && !IsFloatingPoint)
                UploadWithMips(coordinator, result);
            else
                UploadDirect(coordinator, result);

            return result;
        }

        internal int SizeofPixel {
            get {
                return 4 * (IsFloatingPoint ? 4 : 1);
            }
        }

        private void UploadDirect (RenderCoordinator coordinator, Texture2D result) {
            lock (coordinator.UseResourceLock)
                Evil.TextureUtils.SetDataFast(result, 0, Data, Width, Height, (uint)(Width * SizeofPixel));
        }

        private void UploadWithMips (RenderCoordinator coordinator, Texture2D result) {
            var pPreviousLevelData = Data;
            var pLevelData = Data;
            int levelWidth = Width, levelHeight = Height;
            int previousLevelWidth = Width, previousLevelHeight = Height;

            using (var scratch = BufferPool<Color>.Allocate(Width * Height))
            fixed (Color* pScratch = scratch.Data)
            for (uint level = 0; (levelWidth >= 1) && (levelHeight >= 1); level++) {
                if (level > 0) {
                    pLevelData = pScratch;

                    MipGenerator.Color(pPreviousLevelData, previousLevelWidth, previousLevelHeight, pLevelData, levelWidth, levelHeight);
                }

                lock (coordinator.UseResourceLock)
                    Evil.TextureUtils.SetDataFast(result, level, pLevelData, levelWidth, levelHeight, (uint)(levelWidth * SizeofPixel));

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
