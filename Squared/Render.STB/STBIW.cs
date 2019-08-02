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
        public static byte[] GetTextureData (Texture2D tex, int numComponents) {
            if (tex.Format != SurfaceFormat.Color)
                throw new ArgumentException("Only SurfaceFormat.Color is implemented");
            if (numComponents != 4)
                throw new ArgumentOutOfRangeException("numComponents");

            var count = tex.Width * tex.Height * numComponents;
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
            int numComponents = 4;
            var buffer = GetTextureData(tex, numComponents);
            WriteImage(buffer, tex.Width, tex.Height, numComponents, stream, format);
        }

        public static unsafe void WriteImage (
            byte[] buffer, int width, int height, int numComponents, Stream stream, 
            ImageWriteFormat format = ImageWriteFormat.PNG, int jpegQuality = 75
        ) {
            var bytesPerPixel = buffer.Length / (width * height);

            using (var scratch = BufferPool<byte>.Allocate(65536))
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
                    case ImageWriteFormat.PNG:
                        Native.API.stbi_write_png_to_func(callback, _pScratch, width, height, numComponents, pBuffer, width * bytesPerPixel);
                        break;
                    case ImageWriteFormat.BMP:
                        Native.API.stbi_write_bmp_to_func(callback, _pScratch, width, height, numComponents, pBuffer);
                        break;
                    case ImageWriteFormat.TGA:
                        Native.API.stbi_write_tga_to_func(callback, _pScratch, width, height, numComponents, pBuffer);
                        break;
                    case ImageWriteFormat.JPEG:
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
        JPEG
    }
}
