﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Graphics.PackedVector;
using Squared.Util;

namespace Squared.Render {
    public static class ColorSpace {
        static ColorSpace () {
            for (int i = 0; i < 256; i++) {
                double fv = i / 255.0;
                if (fv < 0.04045)
                    sRGBByteToLinearTable[i] = fv / 12.92;
                else
                    sRGBByteToLinearTable[i] = Math.Pow((fv + 0.055) / 1.055, 2.4);

                sRGBByteToLinearFloatTable[i] = (float)sRGBByteToLinearTable[i];
                LinearByteTosRGBTable[i] = LinearTosRGB(i / 255.0);

                sRGBByteToLinearByteTable[i] = (byte)Arithmetic.Clamp(sRGBByteToLinearTable[i] * 255, 0f, 255f);
                LinearByteTosRGBByteTable[i] = (byte)(LinearTosRGB(i / 255.0) * 255);

                InverseAlphaTable[i] = (i > 0) ? 1.0 / i : 0;
            }
        }

        public static readonly double[] sRGBByteToLinearTable = new double[256];
        public static readonly float[] sRGBByteToLinearFloatTable = new float[256];
        public static readonly byte[] sRGBByteToLinearByteTable = new byte[256];

        public static readonly double[] LinearByteTosRGBTable = new double[256];
        public static readonly byte[] LinearByteTosRGBByteTable = new byte[256];

        public static readonly double[] InverseAlphaTable = new double[256];

        public static double sRGBToLinear (double sRGB) {
            var low = sRGB / 12.92;
            var high = Math.Pow((sRGB + 0.055) / 1.055, 2.4);
            if (sRGB >= 0.04045)
                return high;
            else
                return low;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double LinearTosRGB (double linear) {
            var low = linear * 12.92;
            var high = 1.055 * Math.Pow(linear, 1.0 / 2.4) - 0.055;
            if (linear <= 0.0031308)
                return low;
            else
                return high;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte AveragesRGB (byte a, byte b, byte c, byte d) {
            // FIXME: Do this using the byte tables to be faster?
            double sum = sRGBByteToLinearTable[a] + sRGBByteToLinearTable[b] + sRGBByteToLinearTable[c] + sRGBByteToLinearTable[d];
            return (byte)(LinearTosRGB(sum / 4) * 255);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Color? ConvertColor (Color? color, ColorConversionMode mode) {
            if (!color.HasValue)
                return null;
            return ConvertColor(color.Value, mode);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Color ConvertColor (Color color, ColorConversionMode mode) {
            ConvertColor(ref color, mode);
            return color;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ConvertColor (ref Color color, ColorConversionMode mode) {
            if (mode == ColorConversionMode.None)
                return;

            if (color.A == 0)
                return;

            byte r = color.R, g = color.G, b = color.B;
            if (color.A < 255) {
                double amult = InverseAlphaTable[color.A], ad = color.A / 255.0,
                    rd = (r * amult), gd = (g * amult), bd = (b * amult);
                if (mode == ColorConversionMode.LinearToSRGB) {
                    rd = LinearTosRGB(rd);
                    gd = LinearTosRGB(gd);
                    bd = LinearTosRGB(bd);
                } else {
                    rd = sRGBToLinear(rd);
                    gd = sRGBToLinear(gd);
                    bd = sRGBToLinear(bd);
                }
                color = new Color(
                    (float)(rd * ad),
                    (float)(gd * ad),
                    (float)(bd * ad),
                    (float)ad
                );
            } else {
                var table = (mode == ColorConversionMode.LinearToSRGB)
                    ? LinearByteTosRGBByteTable
                    : sRGBByteToLinearByteTable;
                color = new Color(
                    table[color.R], table[color.G],
                    table[color.B], table[color.A]
                );
            }
        }

        public static void OkLabToOkLCh (float a, float b, out double C, out double h) {
            C = Math.Sqrt((a * a) + (b * b));
            h = Math.Atan2(b, a) * 180 / Math.PI;
            if (!Arithmetic.IsFinite(C))
                C = 0;
            if (!Arithmetic.IsFinite(h))
                h = 0;
            else if (h < 0)
                h += 360;
        }

        public static void OkLChToOkLab (double C, double h, out float a, out float b) {
            h *= Math.PI / 180;
            a = (float)(C * Math.Cos(h));
            b = (float)(C * Math.Sin(h));
        }
    }

    public sealed class ColorLUT : IDisposable {
        public readonly Texture2D Texture;
        public readonly bool OwnsTexture;
        public readonly int Resolution;
        public readonly int RowCount;

        public bool IsDisposed { get; private set; }

        public ColorLUT (
            Texture2D texture, bool ownsTexture, int rowCount = 1
        ) {
            Texture = texture;
            var sliceHeight = Texture.Height / rowCount;
            var sliceWidth = sliceHeight;
            var totalWidth = (sliceWidth * sliceWidth);
            if (Texture.Width != totalWidth)
                throw new ArgumentException("Texture should be N NxN slices laid out horizontally");
            Resolution = sliceWidth;
            RowCount = rowCount;
            OwnsTexture = ownsTexture;
        }

        public static implicit operator Texture2D (ColorLUT lut) {
            return lut?.Texture;
        }

        public static ColorLUT CreateIdentity (
            RenderCoordinator coordinator,
            LUTPrecision precision = LUTPrecision.UInt8, 
            LUTResolution resolution = LUTResolution.Low, 
            bool renderable = false,
            Matrix? colorMatrix = null
        ) {
            var surfaceFormat = precision == LUTPrecision.UInt8
                ? SurfaceFormat.Color
                : (precision == LUTPrecision.UInt16
                    ? SurfaceFormat.Rgba64
                    : SurfaceFormat.Vector4);

            var width = (int)resolution * (int)resolution;
            var height = (int)resolution;

            Texture2D tex;
            {
                if (renderable)
                    tex = new RenderTarget2D(coordinator.Device, width, height, false, surfaceFormat, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
                else
                    tex = new Texture2D(coordinator.Device, width, height, false, surfaceFormat);
                tex.Tag = $"ColorLUT";
                coordinator.RegisterAutoAllocatedTextureResource(tex);
            }

            var _matrix = colorMatrix.HasValue ? colorMatrix.Value : default(Matrix);
            var result = new ColorLUT(tex, true);
            switch (precision) {
                case LUTPrecision.UInt8:
                    result.SetToIdentity8(coordinator, colorMatrix.HasValue, ref _matrix);
                    break;
                case LUTPrecision.UInt16:
                    result.SetToIdentity16(coordinator, colorMatrix.HasValue, ref _matrix);
                    break;
                case LUTPrecision.Float32:
                    result.SetToIdentityF(coordinator, colorMatrix.HasValue, ref _matrix);
                    break;
                default:
                    throw new ArgumentOutOfRangeException("precision");
            }
            return result;
        }

        private static void Calculate (int x, int y, int z, float invResMinus1, bool useMatrix, ref Matrix colorMatrix, out Vector3 result) {
            var temp = new Vector3(x * invResMinus1, y * invResMinus1, z * invResMinus1);
            if (useMatrix)
                Vector3.Transform(ref temp, ref colorMatrix, out result);
            else
                result = temp;
        }

        private unsafe void SetToIdentityInner (
            int y, int z,
            Vector3* scratch, bool useMatrix, ref Matrix colorMatrix
        ) {
            float invResMinus1 = 1.0f / (Resolution - 1);
            for (int x = 0; x < Resolution; x += 1)
                Calculate(x, y, z, invResMinus1, useMatrix, ref colorMatrix, out scratch[x]);
        }

        public unsafe void SetToIdentity8 (RenderCoordinator coordinator, bool useMatrix, ref Matrix colorMatrix) {
            var scratch = stackalloc Vector3[Resolution];
            var stride = Resolution * Resolution;
            var buf = new Color[stride * Resolution];

            for (int z = 0; z < Resolution; z += 1) {
                int xOffset = z * Resolution;
                for (int y = 0; y < Resolution; y += 1) {
                    int rowOffset = xOffset + (y * stride);
                    SetToIdentityInner(y, z, scratch, useMatrix, ref colorMatrix);

                    for (int x = 0; x < Resolution; x += 1) {
                        var v = scratch[x];
                        buf[rowOffset + x] = new Color(v.X, v.Y, v.Z, 1.0f);
                    }
                } 
            }

            Texture.SetData(buf);
        }

        public unsafe void SetToIdentity16 (RenderCoordinator coordinator, bool useMatrix, ref Matrix colorMatrix) {
            var scratch = stackalloc Vector3[Resolution];
            var stride = Resolution * Resolution;
            var buf = new Rgba64[stride * Resolution];

            for (int z = 0; z < Resolution; z += 1) {
                int xOffset = z * Resolution;
                for (int y = 0; y < Resolution; y += 1) {
                    int rowOffset = xOffset + (y * stride);
                    SetToIdentityInner(y, z, scratch, useMatrix, ref colorMatrix);

                    for (int x = 0; x < Resolution; x += 1) {
                        var v = scratch[x];
                        buf[rowOffset + x] = new Rgba64(v.X, v.Y, v.Z, 1.0f);
                    }
                } 
            }

            Texture.SetData(buf);
        }

        public unsafe void SetToIdentityF (RenderCoordinator coordinator, bool useMatrix, ref Matrix colorMatrix) {
            var scratch = stackalloc Vector3[Resolution];
            var stride = Resolution * Resolution;
            var buf = new Vector4[stride * Resolution];

            for (int z = 0; z < Resolution; z += 1) {
                int xOffset = z * Resolution;
                for (int y = 0; y < Resolution; y += 1) {
                    int rowOffset = xOffset + (y * stride);
                    SetToIdentityInner(y, z, scratch, useMatrix, ref colorMatrix);

                    for (int x = 0; x < Resolution; x += 1)
                        buf[rowOffset + x] = new Vector4(scratch[x], 1.0f);
                } 
            }

            Texture.SetData(buf);
        }

        public void Dispose () {
            if (IsDisposed)
                return;

            IsDisposed = true;
            if (OwnsTexture)
                Texture.Dispose();
        }
    }

    public enum LUTPrecision : int {
        UInt8 = 8,
        UInt16 = 16,
        Float32 = 32
    }

    public enum LUTResolution : int {
        Low = 16,
        Medium = 24,
        High = 32
    }

    public enum ColorConversionMode : int {
        None,
        LinearToSRGB,
        SRGBToLinear,
    }
}
