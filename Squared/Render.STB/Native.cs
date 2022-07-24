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

    [Flags]
    public enum stbir_flags : int {
        /// <summary>
        /// Set this flag if your texture has premultiplied alpha. Otherwise, stbir will
        /// use alpha-weighted resampling (effectively premultiplying, resampling,
        /// then unpremultiplying).
        /// </summary>
        ALPHA_PREMULTIPLIED = (1 << 0),
        /// <summary>
        /// The specified alpha channel should be handled as gamma-corrected value even
        /// when doing sRGB operations.
        /// </summary>
        ALPHA_USES_COLORSPACE = (1 << 1),
    }
    
    public enum stbir_colorspace : int {
        LINEAR,
        SRGB,
    }

    public enum stbir_edge : int {
        CLAMP   = 1,
        REFLECT = 2,
        WRAP    = 3,
        ZERO    = 4,
    }
    
    public enum stbir_filter : int {
        DEFAULT      = 0,  // use same filter type that easy-to-use API chooses
        BOX          = 1,  // A trapezoid w/1-pixel wide ramps, same result as box for integer scale ratios
        TRIANGLE     = 2,  // On upsampling, produces same results as bilinear texture filtering
        CUBICBSPLINE = 3,  // The cubic b-spline (aka Mitchell-Netrevalli with B=1,C=0), gaussian-esque
        CATMULLROM   = 4,  // An interpolating cubic spline
        MITCHELL     = 5,  // Mitchell-Netrevalli filter with B=1/3, C=1/3
    }

    public enum stbir_datatype : int {
        UINT8 ,
        UINT16,
        UINT32,
        FLOAT ,
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
        public static unsafe extern int stbir_resize(         
            void *input_pixels , int input_w , int input_h , int input_stride_in_bytes,
            void *output_pixels, int output_w, int output_h, int output_stride_in_bytes,
            stbir_datatype datatype,
            int num_channels, int alpha_channel, int flags,
            stbir_edge edge_mode_horizontal, stbir_edge edge_mode_vertical,
            stbir_filter filter_horizontal,  stbir_filter filter_vertical,
            stbir_colorspace space, void *alloc_context
        );

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, SetLastError = false)]
        public static unsafe extern int stbir_resize_subpixel(
            void *input_pixels , int input_w , int input_h , int input_stride_in_bytes,
            void *output_pixels, int output_w, int output_h, int output_stride_in_bytes,
            stbir_datatype datatype,
            int num_channels, int alpha_channel, int flags,
            stbir_edge edge_mode_horizontal, stbir_edge edge_mode_vertical,
            stbir_filter filter_horizontal,  stbir_filter filter_vertical,
            stbir_colorspace space, void *alloc_context,
            float x_scale, float y_scale,
            float x_offset, float y_offset
        );

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, SetLastError = false)]
        public static unsafe extern int stbir_resize_region(  
            void *input_pixels , int input_w , int input_h , int input_stride_in_bytes,
            void *output_pixels, int output_w, int output_h, int output_stride_in_bytes,
            stbir_datatype datatype,
            int num_channels, int alpha_channel, int flags,
            stbir_edge edge_mode_horizontal, stbir_edge edge_mode_vertical,
            stbir_filter filter_horizontal,  stbir_filter filter_vertical,
            stbir_colorspace space, void *alloc_context,
            float s0, float t0, float s1, float t1
        );
    }
}