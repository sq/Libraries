using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Squared.Render.STB.Native {
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate int ReadCallback (void* userData, byte* data, int size);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate void SkipCallback (void* userData, int count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate int EOFCallback (void* userData);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate void WriteCallback (void* userData, byte* data, int size);

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct STBI_IO_Callbacks {
        [MarshalAs(UnmanagedType.FunctionPtr)]
        public ReadCallback read;
        [MarshalAs(UnmanagedType.FunctionPtr)]
        public SkipCallback skip;
        [MarshalAs(UnmanagedType.FunctionPtr)]
        public EOFCallback eof;
    }
    
    // stbir_pixel_layout specifies:
    //   number of channels
    //   order of channels
    //   whether color is premultiplied by alpha
    // for back compatibility, you can cast the old channel count to an stbir_pixel_layout
    public enum stbir_pixel_layout : int {
        BGR      = 0,               // 3-chan, with order specified (for channel flipping)
        _1CHANNEL = 1,              
        _2CHANNEL = 2,
        RGB      = 3,               // 3-chan, with order specified (for channel flipping) 
        RGBA     = 4,               // alpha formats, alpha is NOT premultiplied into color channels

        _4CHANNEL = 5,
        BGRA = 6,
        ARGB = 7,
        ABGR = 8,
        RA   = 9,
        AR   = 10,

        RGBA_PM = 11,               // alpha formats, alpha is premultiplied into color channels
        BGRA_PM = 12,
        ARGB_PM = 13,
        ABGR_PM = 14,
        RA_PM   = 15,
        AR_PM   = 16,
    }

    public enum stbir_edge : int {
        CLAMP   = 0,
        REFLECT = 1,
        WRAP    = 2,  // this edge mode is slower and uses more memory
        ZERO    = 3,
    }
    
    public enum stbir_filter : int {
        DEFAULT      = 0,  // use same filter type that easy-to-use API chooses
        BOX          = 1,  // A trapezoid w/1-pixel wide ramps, same result as box for integer scale ratios
        TRIANGLE     = 2,  // On upsampling, produces same results as bilinear texture filtering
        CUBICBSPLINE = 3,  // The cubic b-spline (aka Mitchell-Netrevalli with B=1,C=0), gaussian-esque
        CATMULLROM   = 4,  // An interpolating cubic spline
        MITCHELL     = 5,  // Mitchell-Netrevalli filter with B=1/3, C=1/3
        POINT_SAMPLE = 6,  // Simple point sampling
        OTHER        = 7,  // User callback specified
    }

    public enum stbir_datatype : int {
        UINT8            = 0,
        UINT8_SRGB       = 1,
        UINT8_SRGB_ALPHA = 2,  // alpha channel, when present, should also be SRGB (this is very unusual)
        UINT16           = 3,
        FLOAT            = 4,
        HALF_FLOAT       = 5
    }

    [System.Security.SuppressUnmanagedCodeSecurity]
    public static unsafe partial class API {
        static API () {
            try {
                var loader = new Util.EmbeddedDLLLoader(Assembly.GetExecutingAssembly());
                loader.Load(DllName + ".dll");
            } catch (Exception exc) {
                Console.Error.WriteLine("Failed to load {0}: {1}", DllName, exc.Message);
            }
        }

        const string DllName = "stb_image";

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, SetLastError = false)]
        public static extern void stbi_image_free (void* ptr);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, SetLastError = false)]
        public static extern byte* stbi_load_from_memory (byte* buffer, int len, out int x, out int y, out int channels, int desired_channels);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, SetLastError = false)]
        public static extern byte* stbi_load_from_callbacks (ref STBI_IO_Callbacks clbk, void *user, out int x, out int y, out int channels, int desired_channels);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, SetLastError = false)]
        public static extern ushort* stbi_load_16_from_memory (byte* buffer, int len, out int x, out int y, out int channels, int desired_channels);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, SetLastError = false)]
        public static extern ushort* stbi_load_16_from_callbacks (ref STBI_IO_Callbacks clbk, void *user, out int x, out int y, out int channels, int desired_channels);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, SetLastError = false)]
        public static extern float* stbi_loadf_from_memory (byte* buffer, int len, out int x, out int y, out int channels, int desired_channels);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, SetLastError = false)]
        public static extern float* stbi_loadf_from_callbacks (ref STBI_IO_Callbacks clbk, void *user, out int x, out int y, out int channels, int desired_channels);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, SetLastError = false)]
        public static extern int stbi_info_from_memory (byte* buffer, int len, out int x, out int y, out int comp);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, SetLastError = false)]
        public static extern int stbi_info_from_callbacks (ref STBI_IO_Callbacks clbk, void *user, out int x, out int y, out int comp);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, SetLastError = false)]
        public static extern int stbi_is_16_bit_from_memory (byte* buffer, int len);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, SetLastError = false)]
        public static extern int stbi_is_16_bit_from_callbacks (ref STBI_IO_Callbacks clbk, void *user);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, SetLastError = false)]
        public static extern byte* stbi_failure_reason ();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, SetLastError = false)]
        public static extern int stbi_write_png_to_func (WriteCallback callback, void *user, int w, int h, int comp, byte* data, int strideInBytes);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, SetLastError = false)]
        public static extern int stbi_write_bmp_to_func (WriteCallback callback, void *user, int w, int h, int comp, byte* data);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, SetLastError = false)]
        public static extern int stbi_write_tga_to_func (WriteCallback callback, void *user, int w, int h, int comp, byte* data);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, SetLastError = false)]
        public static extern int stbi_write_jpg_to_func (WriteCallback callback, void *user, int w, int h, int comp, byte* data, int quality);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, SetLastError = false)]
        public static extern int stbi_write_hdr_to_func (WriteCallback callback, void *user, int w, int h, int comp, float* data);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, SetLastError = false)]
        public static extern int get_stbi_write_png_compression_level ();
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, SetLastError = false)]
        public static extern void set_stbi_write_png_compression_level (int level);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, SetLastError = false)]
        public static unsafe extern void * stbir_resize(         
            void *input_pixels, 
            int input_w, int input_h, int input_stride_in_bytes,
            void *output_pixels, 
            int output_w, int output_h, int output_stride_in_bytes,
            stbir_pixel_layout pixel_layout, stbir_datatype data_type,
            stbir_edge edge, stbir_filter filter
        );
    }
}