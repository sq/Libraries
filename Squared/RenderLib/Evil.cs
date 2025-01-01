using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
// using SDL2;
using SDL3;

namespace Squared.Render.Evil {
    public enum FNA3D_SysRendererTypeEXT
    {
	    OpenGL,
	    Vulkan,
	    D3D11,
	    Metal,
        SDL_GPU
    };

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct FNA3D_SysRendererEXT {
        [StructLayout(LayoutKind.Sequential)]
        public unsafe struct U_SDL {
            /// <summary>
            /// SDL_GPUDevice *
            /// </summary>
            public IntPtr device;

            public IntPtr reserved1, reserved2, reserved3, reserved4, reserved5, reserved6;
        }

        [StructLayout(LayoutKind.Explicit)]
        public unsafe struct U {
            [FieldOffset(0)]
            public U_SDL sdl;
            [FieldOffset(0)]
            public fixed byte padding[1024];
        }

        public UInt32 version;
        public FNA3D_SysRendererTypeEXT rendererType;
        public U renderer;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct FNA3D_SysTextureEXT {
        [StructLayout(LayoutKind.Sequential)]
        public unsafe struct U_SDL {
            /// <summary>
            /// SDL_GPUTexture *
            /// </summary>
            public IntPtr texture;
            /// <summary>
            /// SDL_GPUTextureCreateInfo *
            /// </summary>
            public IntPtr createInfo;
        }

        [StructLayout(LayoutKind.Explicit)]
        public unsafe struct U {
            [FieldOffset(0)]
            public U_SDL sdl;
            [FieldOffset(0)]
            public fixed byte padding[1024];
        }

        public UInt32 version;
        public FNA3D_SysRendererTypeEXT rendererType;
        public U texture;
    }

    public class SysTexture2D : Texture2D {
        protected static unsafe IntPtr CreateHandle (GraphicsDevice device, IntPtr texture, SDL.SDL_GPUTextureCreateInfo createInfo) {
            var hDevice = DeviceUtils.GetFNA3DDevice(device);
            var st = new FNA3D_SysTextureEXT {
                version = 0,
                rendererType = FNA3D_SysRendererTypeEXT.SDL_GPU,
                texture = {
                    sdl = {
                        texture = texture,
                        createInfo = (IntPtr)(&createInfo),
                    },
                },
            };
            return DeviceUtils.FNA3D_CreateSysTextureEXT(hDevice, ref st);
        }

        public static SurfaceFormat GetMatchingSizeFormat (SDL.SDL_GPUTextureFormat format) {
            var sizeInPixels = SDL.SDL_GPUTextureFormatTexelBlockSize(format);
            // FIXME: Compressed formats
            return sizeInPixels switch {
                16 => SurfaceFormat.Vector4,
                8 => SurfaceFormat.Rgba64,
                4 => SurfaceFormat.Color,
                2 => SurfaceFormat.HalfSingle,
                1 => SurfaceFormat.Alpha8,
                _ => throw new ArgumentOutOfRangeException(nameof(format)),
            };
        }

        public unsafe SysTexture2D (GraphicsDevice device, IntPtr texture, SDL.SDL_GPUTextureCreateInfo createInfo) 
            : base (
                  device, 
                  (int)createInfo.width, (int)createInfo.height, (int)createInfo.num_levels, 
                  GetMatchingSizeFormat(createInfo.format), CreateHandle(device, texture, createInfo)
            )
        {
        }

        public unsafe SysTexture2D (GraphicsDevice device, SDL.SDL_GPUTextureCreateInfo createInfo)
            : this (
                  device, CreateTextureFromInfo(device, ref createInfo), createInfo
            )
        {
        }

        private static IntPtr CreateTextureFromInfo (GraphicsDevice device, ref SDL.SDL_GPUTextureCreateInfo createInfo) {
            var sd = DeviceUtils.GetSDLDevice(device).device;
            var result = SDL.SDL_CreateGPUTexture(sd, ref createInfo);
            if (result == IntPtr.Zero)
                throw new Exception("Failed to create SDL_GPU texture: " + SDL.SDL_GetError());
            return result;
        }
    }

    public static class EffectUtils {
        public static Effect EffectFromFxcOutput (GraphicsDevice device, Stream stream, string name = null) {
            if (device == null)
                throw new NullReferenceException("device");

            using (var ms = new MemoryStream()) {
                stream.CopyTo(ms);
                return EffectFromFxcOutput(device, ms.GetBuffer(), name);
            }
        }

        public static unsafe Effect EffectFromFxcOutput (GraphicsDevice device, byte[] bytes, string name = null) {
            if (device == null)
                throw new NullReferenceException("device");

            var result = new Effect(device, bytes);
            result.Name = name;
            return result;
        }
    }

    public static class TextureUtils {
        public static readonly SurfaceFormat ColorSrgbEXT;
        public static readonly SurfaceFormat? Bc7EXT, Bc7SrgbEXT, Dxt5SrgbEXT;

        static TextureUtils () {
            if (!Enum.TryParse<SurfaceFormat>("ColorSrgbEXT", out ColorSrgbEXT))
                ColorSrgbEXT = SurfaceFormat.Color;
            if (Enum.TryParse<SurfaceFormat>("Bc7EXT", out SurfaceFormat temp))
                Bc7EXT = temp;
            if (Enum.TryParse<SurfaceFormat>("Bc7SrgbEXT", out temp))
                Bc7SrgbEXT = temp;
            if (Enum.TryParse<SurfaceFormat>("Dxt5SrgbEXT", out temp))
                Dxt5SrgbEXT = temp;
        }

        public static int GetBytesPerPixelAndComponents (SurfaceFormat format, out int numComponents) {
            // HACK
            if (format == ColorSrgbEXT)
                format = SurfaceFormat.Color;
            else if ((format == Bc7EXT) || (format == Bc7SrgbEXT) || (format == Dxt5SrgbEXT))
                format = SurfaceFormat.Dxt5;

            switch (format) {
                case SurfaceFormat.Alpha8:
                case SurfaceFormat.ByteEXT:
                    numComponents = 1;
                    return 1;
                case SurfaceFormat.Color:
                    numComponents = 4;
                    return 4;
                case SurfaceFormat.Rgba64:
                    numComponents = 4;
                    return 8;
                case SurfaceFormat.HalfSingle:
                case SurfaceFormat.UShortEXT:
                    numComponents = 1;
                    return 2;
                case SurfaceFormat.Single:
                    numComponents = 1;
                    return 4;
                case SurfaceFormat.HalfVector2:
                    numComponents = 2;
                    return 4;
                case SurfaceFormat.Vector2:
                    numComponents = 2;
                    return 8;
                case SurfaceFormat.HalfVector4:
                    numComponents = 4;
                    return 8;
                case SurfaceFormat.Vector4:
                    numComponents = 4;
                    return 16;
                case SurfaceFormat.Rg32:
                    numComponents = 2;
                    return 4;
                case SurfaceFormat.Bgr565:
                    numComponents = 3;
                    return 2;
                case SurfaceFormat.Dxt1:
                    // FIXME: These are technically less than 1 byte per pixel
                    numComponents = 3;
                    return 1;
                case SurfaceFormat.Dxt3:
                    // FIXME: These are technically less than 1 byte per pixel
                    numComponents = 4;
                    return 1;
                case SurfaceFormat.Dxt5:
                    // HACK: 16 pixel groups -> 128 bits (16 bytes) of output
                    numComponents = 4;
                    return 1;
                default:
                    throw new ArgumentException("Surface format " + format + " not implemented");
            }
        }

        const int VALUE_CHANNEL_RGB = 0;
        const int VALUE_CHANNEL_R = 1;
        const int VALUE_CHANNEL_A = 2;
        const int VALUE_CHANNEL_RG = 3;

        const int ALPHA_CHANNEL_A = 0;
        const int ALPHA_CONSTANT_ONE = 1;
        const int ALPHA_CHANNEL_G = 2;

        const int ALPHA_MODE_NORMAL = 0;
        const int ALPHA_MODE_BC = 1;
        const int ALPHA_MODE_BC7 = 2;

        /// <returns>(x: valueSource, y: alphaSource, z: needsPremultiply, w: alphaMode)</returns>
        public static Vector4 GetTraits (SurfaceFormat format) {
            if (
                (format == Bc7EXT) ||
                (format == Bc7SrgbEXT)
            )
                return new Vector4(VALUE_CHANNEL_RGB, ALPHA_CHANNEL_A, 0, ALPHA_MODE_BC7);

            switch (format) {
                // FIXME: Is the value channel R in fna? I think it is
                case SurfaceFormat.Alpha8:
                    return new Vector4(VALUE_CHANNEL_A, ALPHA_CHANNEL_A, 0, ALPHA_MODE_NORMAL);
                case SurfaceFormat.ByteEXT:
                case SurfaceFormat.UShortEXT:
                    return new Vector4(VALUE_CHANNEL_R, ALPHA_CONSTANT_ONE, 0, ALPHA_MODE_NORMAL);
                case SurfaceFormat.Single:
                case SurfaceFormat.HalfSingle:
                    return new Vector4(VALUE_CHANNEL_R, ALPHA_CONSTANT_ONE, 0, ALPHA_MODE_NORMAL);
                case SurfaceFormat.Rg32:
                case SurfaceFormat.Vector2:
                case SurfaceFormat.HalfVector2:
                case SurfaceFormat.NormalizedByte2:
                    return new Vector4(VALUE_CHANNEL_RG, ALPHA_CONSTANT_ONE, 0, ALPHA_MODE_NORMAL);
                case SurfaceFormat.Bgr565:
                    return new Vector4(VALUE_CHANNEL_RGB, ALPHA_CONSTANT_ONE, 0, ALPHA_MODE_NORMAL);
                case SurfaceFormat.Dxt1:
                    return new Vector4(VALUE_CHANNEL_RGB, ALPHA_CONSTANT_ONE, 0, ALPHA_MODE_BC);
                case SurfaceFormat.Dxt3:
                case SurfaceFormat.Dxt5:
                case SurfaceFormat.Dxt5SrgbEXT:
                    return new Vector4(VALUE_CHANNEL_RGB, ALPHA_CHANNEL_A, 0, ALPHA_MODE_BC);

                // case SurfaceFormat.HalfVector4:
                // case SurfaceFormat.HdrBlendable:
                // case SurfaceFormat.NormalizedByte4:
                // case SurfaceFormat.Rgba1010102:
                // case SurfaceFormat.Rgba64:
                // case SurfaceFormat.Vector4:
                // case SurfaceFormat.Bgra4444:
                // case SurfaceFormat.Bgra5551:
                // case SurfaceFormat.Color:
                // case SurfaceFormat.ColorBgraExt:
                // case SurfaceFormat.ColorSrgb:
                default:
                    return new Vector4(VALUE_CHANNEL_RGB, ALPHA_CHANNEL_A, 0, ALPHA_MODE_NORMAL);
            }
        }

        public static bool IsBlockTextureFormat (SurfaceFormat format) {
            if ((format == Bc7EXT) || (format == Bc7SrgbEXT) || (format == Dxt5SrgbEXT))
                return true;

            switch (format) {
                case SurfaceFormat.Dxt1:
                case SurfaceFormat.Dxt3:
                case SurfaceFormat.Dxt5:
                    return true;
                default:
                    return false;
            }
        }

        public static unsafe void SetDataFast (
            this Texture2D texture, uint level, void* pData,
            int width, int height, uint pitch
        ) {
            int temp;
            int bytesPerPixel = GetBytesPerPixelAndComponents(texture.Format, out temp);
            var maxSize = (texture.Width * texture.Height * bytesPerPixel);
            var actualSize = (int)(pitch * height);
            if (actualSize > maxSize)
                throw new ArgumentOutOfRangeException("pitch");
            texture.SetDataPointerEXT((int)level, new Rectangle(0, 0, width, height), new IntPtr(pData), actualSize);
        }

        public static unsafe void SetDataFast (
            this Texture2D texture, uint level, void* pData,
            Rectangle rect, uint pitch
        ) {
            int temp;
            int bytesPerPixel = GetBytesPerPixelAndComponents(texture.Format, out temp);
            var maxSize = (texture.Width * texture.Height * bytesPerPixel);
            var actualSize = (int)(pitch * rect.Height);
            if (actualSize > maxSize)
                throw new ArgumentOutOfRangeException("pitch");
            texture.SetDataPointerEXT((int)level, rect, new IntPtr(pData), actualSize);
        }

        public static void GetDataFast<T> (
            this Texture2D texture, T[] data
        ) where T : unmanaged {
            texture.GetData(data);
        }

        public static void GetDataFast<T> (
            this Texture2D texture, int level, Rectangle? rect, 
            T[] data, int startIndex, int elementCount
        ) where T : unmanaged {
            texture.GetData(level, rect, data, startIndex, elementCount);
        }

        private static bool? _IsHDREnabled;

        public static bool IsHDREnabled () {
            if (_IsHDREnabled.HasValue)
                return _IsHDREnabled.Value;

            try {
                var result = SDL.SDL_GetHintBoolean("FNA3D_ENABLE_HDR_COLORSPACE", default) 
                    != default;
                _IsHDREnabled = result;
                return result;
            } catch {
                _IsHDREnabled = false;
                return false;
            }
        }

        public static bool FormatIsLinearSpace (DeviceManager manager, SurfaceFormat format) {
            switch (format) {
                case SurfaceFormat.Color:
                    return false;

                case SurfaceFormat.Dxt1:
                case SurfaceFormat.Dxt5:
                case SurfaceFormat.Bc7EXT:
                    return false;

                case SurfaceFormat.Rgba1010102:
                    // FIXME: This isn't actually linear space either.
                    // What format it is depends on whether HDR is active but it's never strictly linear space,
                    //  it's either sRGB or PQ
                    return false;

                case SurfaceFormat.HdrBlendable:
                    switch (manager?.GraphicsBackendName ?? "Unknown") {
                        // FIXME: DXGI will automatically enable HDR for a F16 swapchain, even if you haven't explicitly
                        //  requested an HDR colorspace.
                        case "D3D11":
                            return true;
                        case "Vulkan":
                            return IsHDREnabled();
                        case "SDL_GPU":
                            // FIXME
                            return IsHDREnabled();
                        default:
                            return false;
                    }

                case SurfaceFormat.HalfSingle:
                case SurfaceFormat.Single:
                case SurfaceFormat.HalfVector2:
                case SurfaceFormat.Vector2:
                case SurfaceFormat.HalfVector4:
                case SurfaceFormat.Vector4:
                    return true;

                default:
                    if (format == ColorSrgbEXT)
                        return true;
                    else if (format == Dxt5SrgbEXT)
                        return true;
                    else if (format == Bc7SrgbEXT)
                        return true;

                    return false;
            }
        }
    }

    public static class DeviceUtils {
        [DllImport("FNA3D", CallingConvention = CallingConvention.Cdecl)]
		public static extern void FNA3D_GetSysRendererEXT(
			IntPtr device,
			out FNA3D_SysRendererEXT sysrenderer
		);

        [DllImport("FNA3D", CallingConvention = CallingConvention.Cdecl)]
		public static extern IntPtr FNA3D_CreateSysTextureEXT(
			IntPtr device,
			ref FNA3D_SysTextureEXT systexture
		);

        internal static IntPtr GetFNA3DDevice (GraphicsDevice device) {
            // FIXME: Optimize this
            var f = device.GetType().GetField("GLDevice", BindingFlags.Instance | BindingFlags.NonPublic);
            return (IntPtr)f.GetValue(device);
        }

        public static FNA3D_SysRendererEXT.U_SDL GetSDLDevice (GraphicsDevice device) {
            var fdev = GetFNA3DDevice(device);
            FNA3D_GetSysRendererEXT(fdev, out var sr);
            if (sr.rendererType != FNA3D_SysRendererTypeEXT.SDL_GPU)
                throw new Exception("Incorrect renderer type");
            return sr.renderer.sdl;
        }
    }
}
