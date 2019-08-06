using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Graphics;
using Squared.Util;

namespace Squared.Render.STB {
    public static class ImageWrite {
        public static int PNGCompressionLevel {
            get {
                return Native.API.get_stbi_write_png_compression_level();
            }
            set {
                if (value < 0)
                    throw new ArgumentOutOfRangeException("value");
                else if (value > 9)
                    throw new ArgumentOutOfRangeException("value");

                Native.API.set_stbi_write_png_compression_level(value);
            }
        }

        private static int GetBytesPerPixelAndComponents (SurfaceFormat format, out int numComponents) {
            switch (format) {
                case SurfaceFormat.Alpha8:
                    numComponents = 1;
                    return 1;
                case SurfaceFormat.Color:
                    numComponents = 4;
                    return 4;
                case SurfaceFormat.Rgba64:
                    numComponents = 4;
                    return 8;
                case SurfaceFormat.Vector4:
                    numComponents = 4;
                    return 16;
                default:
                    throw new ArgumentException("Surface format " + format + " not implemented");
            }
        }

        public static byte[] GetTextureData (Texture2D tex) {
            int numComponents;
            var bytesPerPixel = GetBytesPerPixelAndComponents(tex.Format, out numComponents);
            var count = tex.Width * tex.Height * bytesPerPixel * 2;
            var buffer = new byte[count];
            GetTextureData(tex, buffer);
            return buffer;
        }

        public static void GetTextureData (Texture2D tex, byte[] buffer) {
            tex.GetData(buffer);
        }

        public static void WriteImage (
            Texture2D tex, string filename, 
            ImageWriteFormat format = ImageWriteFormat.PNG, int jpegQuality = 75
        ) {
            using (var stream = File.Create(filename))
                WriteImage(tex, stream, format);
        }

        public static void WriteImage (
            Texture2D tex, Stream stream, 
            ImageWriteFormat format = ImageWriteFormat.PNG, int jpegQuality = 75
        ) {
            var buffer = GetTextureData(tex);
            WriteImage(buffer, tex.Width, tex.Height, tex.Format, stream, format);
        }

        public static unsafe void WriteImage (
            byte[] buffer, int width, int height, 
            SurfaceFormat sourceFormat, Stream stream, 
            ImageWriteFormat format = ImageWriteFormat.PNG, int jpegQuality = 75
        ) {
            int numComponents;
            var bytesPerPixel = GetBytesPerPixelAndComponents(sourceFormat, out numComponents);

            if (buffer.Length < (bytesPerPixel * width * height))
                throw new ArgumentException("buffer");

            using (var scratch = BufferPool<byte>.Allocate(1024 * 64))
            fixed (byte * pBuffer = buffer)
            fixed (byte * _pScratch = scratch.Data) {
                Native.WriteCallback callback = (pScratch, pData, count) => {
                    int offset = 0;
                    while (count > 0) {
                        var copySize = Math.Min(count, scratch.Data.Length);
                        Buffer.MemoryCopy(pData + offset, pScratch, copySize, copySize);
                        stream.Write(scratch.Data, 0, copySize);
                        count -= copySize;
                        offset += copySize;
                    }
                };

                switch (format) {
                    case ImageWriteFormat.HDR:
                        if (bytesPerPixel != 16)
                            throw new NotImplementedException("Non-vector4");
                        Native.API.stbi_write_hdr_to_func(callback, _pScratch, width, height, numComponents, (float*)(void*)pBuffer);
                        break;
                    case ImageWriteFormat.PNG:
                        if (bytesPerPixel != 4)
                            throw new NotImplementedException("Non-rgba32");
                        Native.API.stbi_write_png_to_func(callback, _pScratch, width, height, numComponents, pBuffer, width * bytesPerPixel);
                        break;
                    case ImageWriteFormat.BMP:
                        if (bytesPerPixel != 4)
                            throw new NotImplementedException("Non-rgba32");
                        Native.API.stbi_write_bmp_to_func(callback, _pScratch, width, height, numComponents, pBuffer);
                        break;
                    case ImageWriteFormat.TGA:
                        if (bytesPerPixel != 4)
                            throw new NotImplementedException("Non-rgba32");
                        Native.API.stbi_write_tga_to_func(callback, _pScratch, width, height, numComponents, pBuffer);
                        break;
                    case ImageWriteFormat.JPEG:
                        if (bytesPerPixel != 4)
                            throw new NotImplementedException("Non-rgba32");
                        Native.API.stbi_write_jpg_to_func(callback, _pScratch, width, height, numComponents, pBuffer, jpegQuality);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException("format");
                }
            }
        }
    }

    public enum ImageWriteFormat {
        PNG = 0,
        BMP,
        TGA,
        JPEG,
        /// <summary>
        /// Linear floating-point RGBA (Vector4)
        /// </summary>
        HDR
    }
}
