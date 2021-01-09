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
        public int Width, Height, ChannelCount;
        public bool IsDisposed { get; private set; }
        public void* Data { get; private set; }
        public bool IsFloatingPoint { get; private set; }

        private static FileStream OpenStream (string path) {
            return File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        }

        public Image (string path, bool premultiply = true, bool asFloatingPoint = false)
            : this (OpenStream(path), true, premultiply, asFloatingPoint) {
        }

        public Image (Stream stream, bool ownsStream, bool premultiply = true, bool asFloatingPoint = false) {
            var length = stream.Length - stream.Position;

            if (!stream.CanSeek)
                throw new ArgumentException("Stream must be seekable");
            if (!stream.CanRead)
                throw new ArgumentException("Stream must be readable");

            byte[] buffer;
            int readOffset;
            var ms = stream as MemoryStream;
            if (ms != null) {
                buffer = ms.GetBuffer();
                readOffset = (int)ms.Position;
            } else {
                buffer = new byte[length];
                readOffset = 0;
                stream.Read(buffer, 0, (int)length);
            }

            InitializeFromBuffer(buffer, readOffset, (int)length, premultiply, asFloatingPoint);

            if (ownsStream)
                stream.Dispose();
        }

        public Image (ArraySegment<byte> buffer, bool premultiply = true, bool asFloatingPoint = false) {
            InitializeFromBuffer(buffer.Array, buffer.Offset, buffer.Count, premultiply, asFloatingPoint);
        }

        private void InitializeFromBuffer (
            byte[] buffer, int offset, int length, 
            bool premultiply = true, bool asFloatingPoint = false
        ) {
            IsFloatingPoint = asFloatingPoint;

            // FIXME: Don't request RGBA?
            fixed (byte * pBuffer = buffer) {
                if (asFloatingPoint)
                    Data = Native.API.stbi_loadf_from_memory(pBuffer + offset, length, out Width, out Height, out ChannelCount, 4);
                else
                    Data = Native.API.stbi_load_from_memory(pBuffer + offset, length, out Width, out Height, out ChannelCount, 4);
            }

            if (Data == null) {
                var reason = STB.Native.API.stbi_failure_reason();
                var message = "Failed to load image";
                if (reason != null)
                    message += ": " + Encoding.UTF8.GetString(reason, 128);
                throw new Exception(message);
            }

            if (asFloatingPoint)
                ConvertFPData(premultiply);
            else
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
                if (IsFloatingPoint)
                    return SurfaceFormat.Vector4;
                else
                    return SurfaceFormat.Color;
            }
        }

        public Texture2D CreateTexture (RenderCoordinator coordinator, bool generateMips = false, bool padToPowerOfTwo = false) {
            if (IsDisposed)
                throw new ObjectDisposedException("Image is disposed");
            // FIXME: Channel count

            int width = padToPowerOfTwo ? Arithmetic.NextPowerOfTwo(Width) : Width;
            var height = padToPowerOfTwo ? Arithmetic.NextPowerOfTwo(Height) : Height;

            Texture2D result;
            lock (coordinator.CreateResourceLock)
                result = new Texture2D(coordinator.Device, width, height, generateMips, Format);

            // FIXME: FP mips
            if (generateMips && !IsFloatingPoint)
                UploadWithMips(coordinator, result);
            else
                UploadDirect(coordinator, result);

            return result;
        }

        internal int SizeofPixel {
            get {
                int components;
                return STB.ImageWrite.GetBytesPerPixelAndComponents(Format, out components);
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
