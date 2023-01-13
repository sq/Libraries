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

        public static byte[] GetTextureData (Texture2D tex) {
            int numComponents;
            var bytesPerPixel = Evil.TextureUtils.GetBytesPerPixelAndComponents(tex.Format, out numComponents);
            var count = tex.Width * tex.Height * bytesPerPixel;
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
            fixed (byte* pBuffer = buffer)
                WriteImage(pBuffer, buffer.Length, width, height, sourceFormat, stream, format, jpegQuality);
        }

        public static unsafe void WriteImage (
            byte * data, int dataLength, int width, int height, 
            SurfaceFormat sourceFormat, Stream stream, 
            ImageWriteFormat format = ImageWriteFormat.PNG, int jpegQuality = 75
        ) {
            int numComponents;
            var bytesPerPixel = Evil.TextureUtils.GetBytesPerPixelAndComponents(sourceFormat, out numComponents);

            if (dataLength < (bytesPerPixel * width * height))
                throw new ArgumentException("buffer");

            using (var scratch = BufferPool<byte>.Allocate(1024 * 64))
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
                        if ((bytesPerPixel / numComponents) != 4)
                            throw new NotImplementedException("Non-fp32");
                        Native.API.stbi_write_hdr_to_func(callback, _pScratch, width, height, numComponents, (float*)(void*)data);
                        break;
                    case ImageWriteFormat.PNG:
                        if ((bytesPerPixel / numComponents) != 1)
                            throw new NotImplementedException("Non-8bpp");
                        Native.API.stbi_write_png_to_func(callback, _pScratch, width, height, numComponents, data, width * bytesPerPixel);
                        break;
                    case ImageWriteFormat.BMP:
                        if ((bytesPerPixel / numComponents) != 1)
                            throw new NotImplementedException("Non-8bpp");
                        Native.API.stbi_write_bmp_to_func(callback, _pScratch, width, height, numComponents, data);
                        break;
                    case ImageWriteFormat.TGA:
                        if ((bytesPerPixel / numComponents) != 1)
                            throw new NotImplementedException("Non-8bpp");
                        Native.API.stbi_write_tga_to_func(callback, _pScratch, width, height, numComponents, data);
                        break;
                    case ImageWriteFormat.JPEG:
                        if ((bytesPerPixel / numComponents) != 1)
                            throw new NotImplementedException("Non-8bpp");
                        Native.API.stbi_write_jpg_to_func(callback, _pScratch, width, height, numComponents, data, jpegQuality);
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
