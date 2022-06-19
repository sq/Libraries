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
                MipFormat.Vector4 | MipFormat.sRGB,
                MipFormat.pVector4,
                MipFormat.pVector4 | MipFormat.sRGB,
            };

            foreach (var format in formats)
                MipGenerator.Set(format, Get(format));
        }

        public static unsafe MipGeneratorFn Get (
            MipFormat format, 
            stbir_filter filter_horizontal = stbir_filter.DEFAULT,
            stbir_filter filter_vertical = stbir_filter.DEFAULT,
            stbir_edge edge_horizontal = stbir_edge.CLAMP,
            stbir_edge edge_vertical = stbir_edge.CLAMP
        ) {
            var masked = (format & ~MipFormat.sRGB);
            var datatype = (masked == MipFormat.Vector4) || 
                (masked == MipFormat.pVector4) ||
                (masked == MipFormat.Single)
                ? stbir_datatype.FLOAT
                : stbir_datatype.UINT8;
            var colorspace = (format & MipFormat.sRGB) == MipFormat.sRGB
                ? stbir_colorspace.SRGB
                : stbir_colorspace.LINEAR;
            var channels = masked == MipFormat.Gray1 ? 1 : 4;
            var flags = (masked == MipFormat.pRGBA) || 
                (masked == MipFormat.pGray4) ||
                (masked == MipFormat.pVector4)
                ? stbir_flags.ALPHA_PREMULTIPLIED
                : default;
            var alpha_channel = (masked == MipFormat.Gray1) ? -1 : 3;

            unsafe void Implementation (
                void* src, int srcWidth, int srcHeight, int srcStrideBytes, 
                void* dest, int destWidth, int destHeight, int destStrideBytes
            ) {
                var result = API.stbir_resize(
                    src, srcWidth, srcHeight, srcStrideBytes,
                    dest, destWidth, destHeight, destStrideBytes,
                    datatype, channels, alpha_channel, (int)flags,
                    edge_horizontal, edge_vertical,
                    filter_horizontal, filter_vertical, 
                    colorspace, null
                );
                if (result != 1)
                    throw new Exception("An error occurred in stb_image_resize");
            }

            return Implementation;
        }
    }
}
