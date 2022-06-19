using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Squared.Render.Mips {
    public unsafe delegate void MipGenerator<T> (
        void* src, int srcWidth, int srcHeight, int srcStrideBytes, void* dest, int destWidth, int destHeight, int destStrideBytes
    ) where T : unmanaged;

    public static class MipGenerator {
        public sealed class WithGammaRamp {
            public readonly GammaRamp Ramp;
            private byte[] GammaTable, InvGammaTable;

            public WithGammaRamp (double gamma)
                : this (new GammaRamp(gamma)) {
            }

            public WithGammaRamp (GammaRamp ramp) {
                Ramp = ramp;
                GammaTable = Ramp.GammaTable;
                InvGammaTable = Ramp.InvGammaTable;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public byte Average (byte a, byte b, byte c, byte d) {
                var sum = InvGammaTable[a] + InvGammaTable[b] + InvGammaTable[c] + InvGammaTable[d];
                return GammaTable[sum >> 2];
            }

            public unsafe void PAGray (
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte Average (byte a, byte b, byte c, byte d) {
            var sum = a + b + c + d;
            return (byte)(sum >> 2);
        }

        public static unsafe void Color (
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

        public static unsafe void sRGBColor (
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

        public static unsafe void sRGBPAGray (
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

        public static unsafe void PAGray (
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
