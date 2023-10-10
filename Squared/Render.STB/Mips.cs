using System;
using Squared.Render.Mips;
using Squared.Render.STB.Native;

namespace Squared.Render {
    public static class STBMipGenerator {
        /// <summary>
        /// Replaces the built in Squared.Render mip generators with stb_image_resize
        /// </summary>
        public static void InstallGlobally () {
            var formats = new[] {
                MipFormat.Gray1,
                MipFormat.pRGBA, MipFormat.RGBA, MipFormat.pGray4,
                MipFormat.pRGBA | MipFormat.sRGB,
                MipFormat.RGBA | MipFormat.sRGB,
                MipFormat.pGray4 | MipFormat.sRGB,
                MipFormat.Vector4,
                MipFormat.pVector4,
                MipFormat.HalfSingle,
                MipFormat.HalfVector4,
            };

            foreach (var format in formats)
                MipGenerator.Set(format, Get(format));
        }

        public static unsafe MipGeneratorFn Get (
            MipFormat format, 
            stbir_filter filter = stbir_filter.DEFAULT,
            stbir_edge edge = stbir_edge.CLAMP
        ) {
            stbir_pixel_layout pixelLayout;
            stbir_datatype dataType;
            var masked = (format & ~MipFormat.sRGB);
            var sRGB = (format & MipFormat.sRGB) == MipFormat.sRGB;

            switch (masked) {
                case MipFormat.pRGBA:
                    pixelLayout = stbir_pixel_layout.RGBA_PM;
                    dataType = sRGB ? stbir_datatype.UINT8_SRGB : stbir_datatype.UINT8;
                    break;
                case MipFormat.pGray4:
                    pixelLayout = stbir_pixel_layout._4CHANNEL;
                    dataType = sRGB ? stbir_datatype.UINT8_SRGB : stbir_datatype.UINT8;
                    break;
                case MipFormat.RGBA:
                    pixelLayout = stbir_pixel_layout.RGBA;
                    dataType = sRGB ? stbir_datatype.UINT8_SRGB : stbir_datatype.UINT8;
                    break;
                case MipFormat.Gray1:
                    pixelLayout = stbir_pixel_layout._1CHANNEL;
                    dataType = sRGB ? stbir_datatype.UINT8_SRGB : stbir_datatype.UINT8;
                    break;
                case MipFormat.Single:
                    pixelLayout = stbir_pixel_layout._1CHANNEL;
                    dataType = stbir_datatype.FLOAT;
                    break;
                case MipFormat.HalfSingle:
                    pixelLayout = stbir_pixel_layout._1CHANNEL;
                    dataType = stbir_datatype.HALF_FLOAT;
                    break;
                case MipFormat.Vector4:
                    pixelLayout = stbir_pixel_layout.RGBA;
                    dataType = stbir_datatype.FLOAT;
                    break;
                case MipFormat.pVector4:
                    pixelLayout = stbir_pixel_layout.RGBA_PM;
                    dataType = stbir_datatype.FLOAT;
                    break;
                case MipFormat.HalfVector4:
                    pixelLayout = stbir_pixel_layout.RGBA;
                    dataType = stbir_datatype.HALF_FLOAT;
                    break;
                case MipFormat.pHalfVector4:
                    pixelLayout = stbir_pixel_layout.RGBA_PM;
                    dataType = stbir_datatype.HALF_FLOAT;
                    break;

                default:
                case MipFormat.SingleMin:
                case MipFormat.SinglePseudoMin:
                case MipFormat.SingleMax:
                    return null;
            }

            unsafe void Implementation (
                void* src, int srcWidth, int srcHeight, int srcStrideBytes, 
                void* dest, int destWidth, int destHeight, int destStrideBytes
            ) {
                var result = API.stbir_resize(
                    src, srcWidth, srcHeight, srcStrideBytes,
                    dest, destWidth, destHeight, destStrideBytes,
                    pixelLayout, dataType, edge, filter
                );
                if (result == default)
                    throw new Exception("An error occurred in stb_image_resize");
            }

            return Implementation;
        }
    }
}
