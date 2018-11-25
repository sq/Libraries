using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Graphics.PackedVector;
using Squared.Util;

namespace Squared.Render {
    public class ColorLUT : IDisposable {
        public readonly Texture2D Texture;
        public readonly bool OwnsTexture;
        public readonly int Resolution;

        public bool IsDisposed { get; private set; }

        public ColorLUT (
            Texture2D texture, bool ownsTexture
        ) {
            Texture = texture;
            var sliceHeight = Texture.Height;
            var sliceWidth = sliceHeight;
            var totalWidth = (sliceWidth * sliceWidth);
            if (Texture.Width != totalWidth)
                throw new ArgumentException("Texture should be N NxN slices laid out horizontally");
            Resolution = sliceWidth;
            OwnsTexture = ownsTexture;
        }

        public static implicit operator Texture2D (ColorLUT lut) {
            return lut.Texture;
        }

        public static ColorLUT CreateIdentity (
            RenderCoordinator coordinator,
            LUTPrecision precision = LUTPrecision.UInt8, 
            LUTResolution resolution = LUTResolution.Low, 
            bool renderable = false
        ) {
            var surfaceFormat = precision == LUTPrecision.UInt8
                ? SurfaceFormat.Color
                : (precision == LUTPrecision.UInt16
                    ? SurfaceFormat.Rgba64
                    : SurfaceFormat.Vector4);

            var width = (int)resolution * (int)resolution;
            var height = (int)resolution;

            Texture2D tex;
            lock (coordinator.CreateResourceLock) {
                if (renderable)
                    tex = new RenderTarget2D(coordinator.Device, width, height, false, surfaceFormat, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
                else
                    tex = new Texture2D(coordinator.Device, width, height, false, surfaceFormat);
            }

            var result = new ColorLUT(tex, true);
            switch (precision) {
                case LUTPrecision.UInt8:
                    result.SetToIdentity8(coordinator);
                    break;
                case LUTPrecision.UInt16:
                    result.SetToIdentity16(coordinator);
                    break;
                case LUTPrecision.Float32:
                    result.SetToIdentityF(coordinator);
                    break;
                default:
                    throw new ArgumentOutOfRangeException("precision");
            }
            return result;
        }

        public void SetToIdentity8 (RenderCoordinator coordinator) {
            var stride = Resolution * Resolution;
            var pixelCount = stride * Resolution;
            var buf = new Color[pixelCount];
            var resMinus1 = Resolution - 1;
            var max = 255;
            for (int slice = 0; slice < Resolution; slice += 1) {
                int xOffset = slice * Resolution;
                for (int y = 0; y < Resolution; y += 1) {
                    int rowOffset = xOffset + (y * stride);
                    for (int x = 0; x < Resolution; x += 1) {
                        int r = (max * x) / resMinus1;
                        int g = (max * y) / resMinus1;
                        int b = (max * slice) / resMinus1;
                        buf[rowOffset + x] = new Color(
                            r, g, b, max
                        );
                    }
                } 
            }

            lock (coordinator.UseResourceLock)
                Texture.SetData(buf);
        }

        public void SetToIdentity16 (RenderCoordinator coordinator) {
            var stride = Resolution * Resolution;
            var pixelCount = stride * Resolution;
            var buf = new Rgba64[pixelCount];
            float resMinus1 = Resolution - 1;
            for (int slice = 0; slice < Resolution; slice += 1) {
                int xOffset = slice * Resolution;
                for (int y = 0; y < Resolution; y += 1) {
                    int rowOffset = xOffset + (y * stride);
                    for (int x = 0; x < Resolution; x += 1) {
                        float r = x / resMinus1;
                        float g = y / resMinus1;
                        float b = slice / resMinus1;
                        buf[rowOffset + x] = new Rgba64(
                            r, g, b, 1
                        );
                    }
                } 
            }

            lock (coordinator.UseResourceLock)
                Texture.SetData(buf);
        }

        public void SetToIdentityF (RenderCoordinator coordinator) {
            var stride = Resolution * Resolution;
            var pixelCount = stride * Resolution;
            var buf = new Vector4[pixelCount];
            float resMinus1 = Resolution - 1;
            for (int slice = 0; slice < Resolution; slice += 1) {
                int xOffset = slice * Resolution;
                for (int y = 0; y < Resolution; y += 1) {
                    int rowOffset = xOffset + (y * stride);
                    for (int x = 0; x < Resolution; x += 1) {
                        float r = x / resMinus1;
                        float g = y / resMinus1;
                        float b = slice / resMinus1;
                        buf[rowOffset + x] = new Vector4(
                            r, g, b, 1
                        );
                    }
                } 
            }

            lock (coordinator.UseResourceLock)
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
}
