using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Squared.Render.Mips {
    public unsafe delegate void MipGeneratorFn (
        void* src, int srcWidth, int srcHeight, int srcStrideBytes, void* dest, int destWidth, int destHeight, int destStrideBytes
    );

    [Flags]
    public enum MipFormat : int {
        // Premultiplied RGBA, 4 bytes per pixel (XNA Color)
        pRGBA = 0,
        // Non-premultiplied RGBA, 4 bytes per pixel
        RGBA  = 1,
        // Grayscale, 1 byte per pixel
        Gray1 = 2,
        // 4 byte RGBA where RGB = A (essentially, premultiplied)
        pGray4 = 3,
        // Grayscale float
        Single = 4, 
        // 16 byte RGBA where each channel is a float
        Vector4 = 5,
        // 16 byte premultiplied RGBA where each channel is a float
        pVector4 = 6,
        // Grayscale float using Minimum instead of Average value
        SingleMin = 7,
        // Grayscale float combining Minimum and Average values (good for text SDFs)
        SinglePseudoMin = 8,
        // Grayscale float using Minimum instead of Average value
        SingleMax = 9,

        // If set, the RGB channels are sRGB. Not valid for Gray1.
        sRGB = 0x10,
    }

    public static class MipGenerator {
        public sealed class WithGammaRamp {
            public readonly GammaRamp Ramp;
            private byte[] GammaTable, InvGammaTable;
            private readonly MipGeneratorFn PAGray;

            public WithGammaRamp (double gamma)
                : this (new GammaRamp(gamma)) {
            }

            public unsafe WithGammaRamp (GammaRamp ramp) {
                Ramp = ramp;
                GammaTable = Ramp.GammaTable;
                InvGammaTable = Ramp.InvGammaTable;
                PAGray = _PAGray;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public byte Average (byte a, byte b, byte c, byte d) {
                var sum = InvGammaTable[a] + InvGammaTable[b] + InvGammaTable[c] + InvGammaTable[d];
                return GammaTable[sum >> 2];
            }

            public unsafe MipGeneratorFn Get (MipFormat format) {
                switch (format) {
                    case MipFormat.pGray4:
                        return PAGray;
                    default:
                        throw new ArgumentOutOfRangeException("format");
                }
            }

            private unsafe void _PAGray (
                void* src, int srcWidth, int srcHeight, int srcStrideBytes, void* dest, int destWidth, int destHeight, int destStrideBytes
            ) {
                if ((destWidth < srcWidth / 2) || (destHeight < srcHeight / 2))
                    throw new ArgumentOutOfRangeException();

                byte* pSrc = (byte*)src, pDest = (byte*)dest;
            
                unchecked {
                    for (var y = 0; y < destHeight; y++) {
                        byte* srcRow = pSrc + ((y * 2) * srcStrideBytes);
                        byte* destRow = pDest + (y * destStrideBytes);

                        for (var x = 0; x < destWidth; x++) {
                            var a = srcRow + ((x * 2) * 4);
                            var b = a + 4;
                            var c = a + srcStrideBytes;
                            var d = b + srcStrideBytes;

                            var result = destRow + (x * 4);
                            var gray = Average(a[3], b[3], c[3], d[3]);
                            result[0] = result[1] = result[2] = result[3] = gray;
                        }
                    }
                }
            }
        }

        private static readonly MipGeneratorFn[] Cache;

        static MipGenerator () {
            Cache = new MipGeneratorFn[1 + (int)(MipFormat.sRGB | MipFormat.SingleMax)];
            for (int i = 0; i < Cache.Length; i++)
                Cache[i] = _Get((MipFormat)i);
        }

        public static void Set (MipFormat format, MipGeneratorFn implementation) {
            Cache[(int)format] = implementation;
        }

        private static unsafe MipGeneratorFn _Get (MipFormat format) {
            switch (format) {
                case MipFormat.RGBA:
                case MipFormat.pRGBA:
                    return _Color;
                // FIXME: Is this right? I think maybe it needs to be different
                case MipFormat.RGBA | MipFormat.sRGB:
                case MipFormat.pRGBA | MipFormat.sRGB:
                    return _sRGBColor;
                case MipFormat.Gray1:
                    return _Gray;
                case MipFormat.pGray4:
                    return _PAGray;
                case MipFormat.pGray4 | MipFormat.sRGB:
                    return _sRGBPAGray;
                case MipFormat.Single:
                    return _Single;
                case MipFormat.SingleMin:
                    return _SingleMin;
                case MipFormat.SinglePseudoMin:
                    return _SinglePseudoMin;
                case MipFormat.SingleMax:
                    return _SingleMax;
                default:
                    return null;
            }
        }

        public static MipGeneratorFn Get (MipFormat format) {
            var index = (int)format;
            if ((index < 0) || (index >= Cache.Length))
                throw new ArgumentOutOfRangeException("format");
            var result = Cache[index];
            if (result == null)
                throw new ArgumentOutOfRangeException("format");
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte Average (byte a, byte b, byte c, byte d) {
            var sum = a + b + c + d;
            return (byte)(sum >> 2);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Average (float a, float b, float c, float d) {
            var sum = a + b + c + d;
            return sum / 4f;
        }

        private static unsafe void _Color (
            void* src, int srcWidth, int srcHeight, int srcStrideBytes, void* dest, int destWidth, int destHeight, int destStrideBytes
        ) {
            if ((destWidth < srcWidth / 2) || (destHeight < srcHeight / 2))
                throw new ArgumentOutOfRangeException();

            byte* pSrc = (byte*)src, pDest = (byte*)dest;
            
            unchecked {
                for (var y = 0; y < destHeight; y++) {
                    byte* srcRow = pSrc + ((y * 2) * srcStrideBytes);
                    byte* destRow = pDest + (y * destStrideBytes);

                    for (var x = 0; x < destWidth; x++) {
                        var a = srcRow + ((x * 2) * 4);
                        var b = a + 4;
                        var c = a + srcStrideBytes;
                        var d = b + srcStrideBytes;

                        var result = destRow + (x * 4);
                        result[0] = Average(a[0], b[0], c[0], d[0]);
                        result[1] = Average(a[1], b[1], c[1], d[1]);
                        result[2] = Average(a[2], b[2], c[2], d[2]);
                        result[3] = Average(a[3], b[3], c[3], d[3]);
                    }
                }
            }
        }

        private static unsafe void _sRGBColor (
            void* src, int srcWidth, int srcHeight, int srcStrideBytes, void* dest, int destWidth, int destHeight, int destStrideBytes
        ) {
            if ((destWidth < srcWidth / 2) || (destHeight < srcHeight / 2))
                throw new ArgumentOutOfRangeException();

            byte* pSrc = (byte*)src, pDest = (byte*)dest;
            
            unchecked {
                for (var y = 0; y < destHeight; y++) {
                    byte* srcRow = pSrc + ((y * 2) * srcStrideBytes);
                    byte* destRow = pDest + (y * destStrideBytes);

                    for (var x = 0; x < destWidth; x++) {
                        var a = srcRow + ((x * 2) * 4);
                        var b = a + 4;
                        var c = a + srcStrideBytes;
                        var d = b + srcStrideBytes;

                        var result = destRow + (x * 4);
                        result[0] = ColorSpace.AveragesRGB(a[0], b[0], c[0], d[0]);
                        result[1] = ColorSpace.AveragesRGB(a[1], b[1], c[1], d[1]);
                        result[2] = ColorSpace.AveragesRGB(a[2], b[2], c[2], d[2]);
                        // The alpha channel is always linear
                        result[3] = Average(a[3], b[3], c[3], d[3]);
                    }
                }
            }
        }

        private static unsafe void _sRGBPAGray (
            void* src, int srcWidth, int srcHeight, int srcStrideBytes, void* dest, int destWidth, int destHeight, int destStrideBytes
        ) {
            if ((destWidth < srcWidth / 2) || (destHeight < srcHeight / 2))
                throw new ArgumentOutOfRangeException();

            byte* pSrc = (byte*)src, pDest = (byte*)dest;
            
            unchecked {
                for (var y = 0; y < destHeight; y++) {
                    byte* srcRow = pSrc + ((y * 2) * srcStrideBytes);
                    byte* destRow = pDest + (y * destStrideBytes);

                    for (var x = 0; x < destWidth; x++) {
                        var a = srcRow + ((x * 2) * 4);
                        var b = a + 4;
                        var c = a + srcStrideBytes;
                        var d = b + srcStrideBytes;

                        var result = destRow + (x * 4);
                        // Average the alpha channel because it is linear
                        var alphaAverage = Average(a[3], b[3], c[3], d[3]);
                        var gray = ColorSpace.LinearByteTosRGBByteTable[alphaAverage];
                        result[0] = result[1] = result[2] = gray;
                        result[3] = alphaAverage;
                    }
                }
            }
        }

        private static unsafe void _Gray (
            void* src, int srcWidth, int srcHeight, int srcStrideBytes, void* dest, int destWidth, int destHeight, int destStrideBytes
        ) {
            if ((destWidth < srcWidth / 2) || (destHeight < srcHeight / 2))
                throw new ArgumentOutOfRangeException();

            byte* pSrc = (byte*)src, pDest = (byte*)dest;
            
            unchecked {
                for (var y = 0; y < destHeight; y++) {
                    byte* srcRow = pSrc + ((y * 2) * srcStrideBytes);
                    byte* destRow = pDest + (y * destStrideBytes);

                    for (var x = 0; x < destWidth; x++) {
                        var a = srcRow + (x * 2);
                        var c = a + srcStrideBytes;

                        var result = destRow + x;
                        var gray = Average(a[0], a[1], c[0], c[1]);
                        *result = gray;
                    }
                }
            }
        }

        private static unsafe void _Single (
            void* src, int srcWidth, int srcHeight, int srcStrideBytes, void* dest, int destWidth, int destHeight, int destStrideBytes
        ) {
            if ((destWidth < srcWidth / 2) || (destHeight < srcHeight / 2))
                throw new ArgumentOutOfRangeException();

            byte* pSrc = (byte*)src, pDest = (byte*)dest;
            
            unchecked {
                for (var y = 0; y < destHeight; y++) {
                    float* srcRow = (float*)(pSrc + ((y * 2) * srcStrideBytes)),
                        srcRowNext = (float*)((byte*)srcRow + srcStrideBytes),
                        destRow = (float*)(pDest + (y * destStrideBytes));

                    for (var x = 0; x < destWidth; x++) {
                        var a = srcRow + (x * 2);
                        var c = srcRowNext + (x * 2);

                        var result = destRow + x;
                        var gray = Average(a[0], a[1], c[0], c[1]);
                        *result = gray;
                    }
                }
            }
        }

        private static unsafe void _SingleMin (
            void* src, int srcWidth, int srcHeight, int srcStrideBytes, void* dest, int destWidth, int destHeight, int destStrideBytes
        ) {
            if ((destWidth < srcWidth / 2) || (destHeight < srcHeight / 2))
                throw new ArgumentOutOfRangeException();

            byte* pSrc = (byte*)src, pDest = (byte*)dest;
            
            unchecked {
                for (var y = 0; y < destHeight; y++) {
                    float* srcRow = (float*)(pSrc + ((y * 2) * srcStrideBytes)),
                        srcRowNext = (float*)((byte*)srcRow + srcStrideBytes),
                        destRow = (float*)(pDest + (y * destStrideBytes));

                    for (var x = 0; x < destWidth; x++) {
                        var a = srcRow + (x * 2);
                        var c = srcRowNext + (x * 2);

                        var result = destRow + x;
                        var gray = Math.Min(Math.Min(a[0], a[1]), Math.Min(c[0], c[1]));
                        *result = gray;
                    }
                }
            }
        }

        private static unsafe void _SinglePseudoMin (
            void* src, int srcWidth, int srcHeight, int srcStrideBytes, void* dest, int destWidth, int destHeight, int destStrideBytes
        ) {
            if ((destWidth < srcWidth / 2) || (destHeight < srcHeight / 2))
                throw new ArgumentOutOfRangeException();

            byte* pSrc = (byte*)src, pDest = (byte*)dest;
            
            unchecked {
                for (var y = 0; y < destHeight; y++) {
                    float* srcRow = (float*)(pSrc + ((y * 2) * srcStrideBytes)),
                        srcRowNext = (float*)((byte*)srcRow + srcStrideBytes),
                        destRow = (float*)(pDest + (y * destStrideBytes));

                    for (var x = 0; x < destWidth; x++) {
                        var a = srcRow + (x * 2);
                        var c = srcRowNext + (x * 2);

                        var result = destRow + x;
                        var gray = Math.Min(Math.Min(a[0], a[1]), Math.Min(c[0], c[1])) +
                            Average(a[0], a[1], c[0], c[1]);
                        *result = gray / 2f;
                    }
                }
            }
        }

        private static unsafe void _SingleMax (
            void* src, int srcWidth, int srcHeight, int srcStrideBytes, void* dest, int destWidth, int destHeight, int destStrideBytes
        ) {
            if ((destWidth < srcWidth / 2) || (destHeight < srcHeight / 2))
                throw new ArgumentOutOfRangeException();

            byte* pSrc = (byte*)src, pDest = (byte*)dest;
            
            unchecked {
                for (var y = 0; y < destHeight; y++) {
                    float* srcRow = (float*)(pSrc + ((y * 2) * srcStrideBytes)),
                        srcRowNext = (float*)((byte*)srcRow + srcStrideBytes),
                        destRow = (float*)(pDest + (y * destStrideBytes));

                    for (var x = 0; x < destWidth; x++) {
                        var a = srcRow + (x * 2);
                        var c = srcRowNext + (x * 2);

                        var result = destRow + x;
                        var gray = Math.Max(Math.Max(a[0], a[1]), Math.Max(c[0], c[1]));
                        *result = gray;
                    }
                }
            }
        }

        private static unsafe void _PAGray (
            void* src, int srcWidth, int srcHeight, int srcStrideBytes, void* dest, int destWidth, int destHeight, int destStrideBytes
        ) {
            if ((destWidth < srcWidth / 2) || (destHeight < srcHeight / 2))
                throw new ArgumentOutOfRangeException();

            byte* pSrc = (byte*)src, pDest = (byte*)dest;
            
            unchecked {
                for (var y = 0; y < destHeight; y++) {
                    byte* srcRow = pSrc + ((y * 2) * srcStrideBytes);
                    byte* destRow = pDest + (y * destStrideBytes);

                    for (var x = 0; x < destWidth; x++) {
                        var a = srcRow + ((x * 2) * 4);
                        var b = a + 4;
                        var c = a + srcStrideBytes;
                        var d = b + srcStrideBytes;

                        var result = destRow + (x * 4);
                        // Average the alpha channel because it is linear
                        var gray = Average(a[3], b[3], c[3], d[3]);
                        result[0] = result[1] = result[2] = result[3] = gray;
                    }
                }
            }
        }
    }
}
