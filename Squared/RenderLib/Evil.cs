﻿using System;
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
#if WINDOWS
    [SuppressUnmanagedCodeSecurity]
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal unsafe delegate int GetDisplayModeDelegate (void* pDevice, int iSwapChain, out D3DDISPLAYMODE pMode);

    
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct D3DLOCKED_RECT {
        public int Pitch;
        public void* pBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3DDISPLAYMODE {
        public uint Width, Height, RefreshRate;
        public uint Format;
    }

    public static class COMUtils {
        /// <summary>
        /// Returns a function pointer from an interface's VTable.
        /// </summary>
        /// <param name="pInterface">The interface.</param>
        /// <param name="offsetInBytes">The offset into the VTable (in bytes).</param>
        /// <returns>The function pointer retrieved from the VTable.</returns>
        public static unsafe void* AccessVTable (void* pInterface, uint offsetInBytes) {
            void* pVTable = (*(void**)pInterface);
            return *((void**)((ulong)pVTable + offsetInBytes));
        }

        public static unsafe TDelegate GetMethodFromVTable<TDelegate> (void* pInterface, uint slotIndex) {
            var offsetInBytes = (uint)(slotIndex * sizeof(void*));
            var methodPtr = AccessVTable(pInterface, offsetInBytes);

            var pComFunction = AccessVTable(pInterface, offsetInBytes);
            var result = Marshal.GetDelegateForFunctionPointer<TDelegate>(new IntPtr(pComFunction));
            return result;
        }
    }

    public enum D3DXPARAMETER_CLASS : UInt32 { 
        SCALAR,
        VECTOR,
        MATRIX_ROWS,
        MATRIX_COLUMNS,
        OBJECT,
        STRUCT
    }

    public enum D3DXPARAMETER_TYPE : UInt32 { 
        VOID,
        BOOL,
        INT,
        FLOAT,
        STRING,
        TEXTURE,
        TEXTURE1D,
        TEXTURE2D,
        TEXTURE3D,
        TEXTURECUBE,
        SAMPLER,
        SAMPLER1D,
        SAMPLER2D,
        SAMPLER3D,
        SAMPLERCUBE,
        PIXELSHADER,
        VERTEXSHADER,
        PIXELFRAGMENT,
        VERTEXFRAGMENT,
        UNSUPPORTED
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct D3DXPARAMETER_DESC {
        public char*               Name;
        public char*               Semantic;
        public D3DXPARAMETER_CLASS Class;
        public D3DXPARAMETER_TYPE  Type;
        public uint                Rows;
        public uint                Columns;
        public uint                Elements;
        public uint                Annotations;
        public uint                StructMembers;
        public UInt32              Flags;
        public uint                SizeBytes;
    } 

    [Guid("F6CEB4B3-4E4C-40dd-B883-8D8DE5EA0CD5")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [SuppressUnmanagedCodeSecurity]
    [ComImport]
    public unsafe interface ID3DXEffect {
        // HACK: ENORMOUS HACK: Apparently ID3DXEffect::QueryInterface doesn't support ID3DXBaseEffect. So...

        //
        // ID3DXBaseEffect
        //

        void GetDesc ([Out] void* pDesc);
        void GetParameterDesc (void* hParameter, out D3DXPARAMETER_DESC pDesc);
        void GetTechniqueDesc (void* hTechnique, [Out] void* pDesc);
        void GetPassDesc (void* hPass, [Out] void* pDesc);
        void GetFunctionDesc (void* hShader, [Out] void* pDesc);

        [PreserveSig]
        void* GetParameter (void* hEnclosingParameter, uint index);
        [PreserveSig]
        void* GetParameterByName (
            void* hEnclosingParameter, 
            [MarshalAs(UnmanagedType.LPStr), In]
            string name
        );
        [PreserveSig]
        void* GetParameterBySemantic (
            void* hEnclosingParameter, 
            [MarshalAs(UnmanagedType.LPStr), In]
            string name
        );
        [PreserveSig]
        void* GetParameterElement (void* hEnclosingParameter, uint index);

        [PreserveSig]
        void* GetTechnique (uint index);
        [PreserveSig]
        void* GetTechniqueByName (
            [MarshalAs(UnmanagedType.LPStr), In]
            string name
        );

        [PreserveSig]
        void* GetPass (void* hTechnique, uint index);
        [PreserveSig]
        void* GetPassByName (
            void* hTechnique, 
            [MarshalAs(UnmanagedType.LPStr), In]
            string name
        );

        [PreserveSig]
        void* GetFunction (uint index);
        [PreserveSig]
        void* GetFunctionByName (
            [MarshalAs(UnmanagedType.LPStr), In]
            string name
        );

        [PreserveSig]
        void* GetAnnotation (void* hObject, uint index);
        [PreserveSig]
        void* GetAnnotationByName (
            void* hObject, 
            [MarshalAs(UnmanagedType.LPStr), In]
            string name
        );

        void SetValue (void* hParameter, void* pData, uint bytes);
        int GetValue (void* hParameter, void* pData, uint bytes);

        void SetBool (void* hParameter, int b);
        int GetBool (void* hParameter);

        void SetBoolArray (void* hParameter, int* pB, uint count);
        void GetBoolArray (void* hParameter, int* pB, uint count);

        void SetInt (void* hParameter, int i);
        int GetInt (void* hParameter);

        void SetIntArray (void* hParameter, int* pI, uint count);
        void GetIntArray (void* hParameter, int* pI, uint count);

        void SetFloat (void* hParameter, float f);
        float GetFloat (void* hParameter);

        void SetFloatArray (void* hParameter, float* pF, uint count);
        void GetFloatArray (void* hParameter, float* pF, uint count);

        void SetVector (void* hParameter, [In] ref Vector4 v);
        void GetVector (void* hParameter, out Vector4 v);

        void SetVectorArray (void* hParameter, Vector4* pV, uint count);
        void GetVectorArray (void* hParameter, Vector4* pV, uint count);

        void SetMatrix (void* hParameter, [In] Matrix* v);
        void GetMatrix (void* hParameter, out Matrix v);

        void SetMatrixArray (void* hParameter, Matrix* pV, uint count);
        void GetMatrixArray (void* hParameter, Matrix* pV, uint count);

        void SetMatrixPointerArray (void* hParameter, [In] ref Matrix* pV, uint count);
        void GetMatrixPointerArray (void* hParameter, out Matrix* pV, uint count);

        void SetMatrixTranspose (void* hParameter, [In] ref Matrix v);
        void GetMatrixTranspose (void* hParameter, out Matrix v);

        void SetMatrixTransposeArray (void* hParameter, Matrix* pV, uint count);
        void GetMatrixTransposeArray (void* hParameter, Matrix* pV, uint count);

        void SetMatrixTransposePointerArray (void* hParameter, [In] ref Matrix* pV, uint count);
        void GetMatrixTransposePointerArray (void* hParameter, out Matrix* pV, uint count);

        void SetString (
            void* hParameter,
            [MarshalAs(UnmanagedType.LPStr), In]
            string pString
        );

        void GetString (
            void* hParameter,
            [MarshalAs(UnmanagedType.LPStr), Out]
            StringBuilder ppString
        );

        void SetTexture (void* hParameter, void* pTexture);
        void* GetTexture (void* hParameter);

        void* GetPixelShader (void* hParameter);
        void* GetVertexShader (void* hParameter);

        void SetArrayRange (void* hParameter, uint uStart, uint uEnd);

        //
        // ID3DXEffect
        //

        void* GetPool ();

        void SetTechnique (void* hTechnique);
        [PreserveSig]
        void* GetCurrentTechnique ();

        void ValidateTechnique (void* hTechnique);
        void* FindNextValidTechnique (void* hTechnique);

        [PreserveSig]
        bool IsParameterUsed (void* hParameter, void* hTechnique);

        void Begin (uint* pPasses, UInt32 flags);
        void BeginPass (uint pass);
        void CommitChanges ();
        void EndPass ();
        void End ();

        void* GetDevice ();
        void OnLostDevice ();
        void OnResetDevice ();

        void SetStateManager (void* pManager);
        void* GetStateManager ();

        void BeginParameterBlock ();
        [PreserveSig]
        void* EndParameterBlock ();
        void ApplyParameterBlock (void* hBlock);
        void DeleteParameterBlock (void* hBlock);

        ID3DXEffect CloneEffect (void* pDevice);

        void SetRawValue (void* hParameter, [In] void* pData, uint byteOffset, uint countBytes);
    }
#endif

#if FNA
    public enum FNA3D_SysRendererTypeEXT
    {
	    OpenGL,
	    Vulkan,
	    D3D11,
	    Metal,
        SDL_GPU
    };

    public unsafe struct FNA3D_SysRendererEXT {
        public UInt32 Version;
        public FNA3D_SysRendererTypeEXT rendererType;
        public fixed byte padding[1024];
    }
#endif

    public static class EffectUtils {
#if WINDOWS
        [DllImport("d3dx9_41.dll")]
        [SuppressUnmanagedCodeSecurity]
        private static unsafe extern int D3DXCreateEffectEx (
            void*     pDevice,
            void*     pData,
            UInt32    dataLen,
            void*     pDefines,
            void*     pInclude,
            string    pSkipConstants,
            UInt32    flags,
            void*     pPool,
            out void* ppEffect,
            out void* ppCompilationErrors
        );

        internal static readonly FieldInfo pComPtr, technique_pComPtr;

        static EffectUtils () {
            pComPtr = typeof(Effect).GetField("pComPtr", BindingFlags.Instance | BindingFlags.NonPublic);
            technique_pComPtr = typeof(EffectTechnique).GetField("pComPtr", BindingFlags.Instance | BindingFlags.NonPublic);
        }
#endif

        public static Effect EffectFromFxcOutput (GraphicsDevice device, Stream stream) {
            if (device == null)
                throw new NullReferenceException("device");

            using (var ms = new MemoryStream()) {
                stream.CopyTo(ms);
                return EffectFromFxcOutput(device, ms.GetBuffer());
            }
        }

        public static unsafe Effect EffectFromFxcOutput (GraphicsDevice device, byte[] bytes) {
#if WINDOWS
            var pDev = GraphicsDeviceUtils.GetIDirect3DDevice9(device);
            void* pEffect, pTemp;
            fixed (byte* pBytes = bytes) {
                var hr = D3DXCreateEffectEx(pDev, pBytes, (uint)bytes.Length, null, null, "", 0, null, out pEffect, out pTemp);
                if (hr != 0)
                    throw Marshal.GetExceptionForHR(hr);
            }
            var t = typeof(Effect);
            var ctors = t.GetConstructors(
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance
            );
            var ctor = ctors[0];
            var result = ctor.Invoke(new object[] { new IntPtr(pEffect), device });
            var nativeResult = (Effect)result;
            return nativeResult.Clone();
        }

        public static unsafe void* GetUnboxedID3DXEffect (this Effect effect) {
            return Pointer.Unbox(pComPtr.GetValue(effect));
        }

        public static unsafe ID3DXEffect GetID3DXEffect (this Effect effect) {
            var unboxedPointer = effect.GetUnboxedID3DXEffect();
            var obj = Marshal.GetObjectForIUnknown(new IntPtr(unboxedPointer));
            var typedObject = (ID3DXEffect)obj;
            return typedObject;
        }
#else
            if (device == null)
                throw new NullReferenceException("device");

            return new Effect(device, bytes);
        }
#endif
    }

#if WINDOWS
    public static class GraphicsDeviceUtils {
        public static class VTables {
            public static class IDirect3DDevice9 {
                public const uint GetDisplayMode = 32;
            }
        }

        internal static readonly FieldInfo pComPtr;

        static GraphicsDeviceUtils () {
            pComPtr = typeof(GraphicsDevice).GetField("pComPtr", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        }

        public static unsafe void* GetIDirect3DDevice9 (this GraphicsDevice device) {
            return Pointer.Unbox(pComPtr.GetValue(device));
        }

        public static unsafe uint GetRefreshRate (this GraphicsDevice device) {
            void* pDevice = GetIDirect3DDevice9(device);
            void* pGetDisplayMode = COMUtils.AccessVTable(pDevice, VTables.IDirect3DDevice9.GetDisplayMode);

            var getDisplayMode = (GetDisplayModeDelegate)Marshal.GetDelegateForFunctionPointer(new IntPtr(pGetDisplayMode), typeof(GetDisplayModeDelegate));
            D3DDISPLAYMODE displayMode;

            var rv = getDisplayMode(pDevice, 0, out displayMode);
            if (rv == 0)
                return displayMode.RefreshRate;
            else
                throw new COMException("GetDisplayMode failed", rv);
        }
    }

    [Flags]
    public enum D3DLOCK : uint {
        READONLY         = 0x00000010,
        DISCARD          = 0x00002000,
        NOOVERWRITE      = 0x00001000,
        NOSYSLOCK        = 0x00000800,
        DONOTWAIT        = 0x00004000,
        NO_DIRTY_UPDATE  = 0x00008000
    }

    public enum D3DFORMAT : uint {
        UNKNOWN              =  0,

        R8G8B8               = 20,
        A8R8G8B8             = 21,
        X8R8G8B8             = 22,
        R5G6B5               = 23,
        X1R5G5B5             = 24,
        A1R5G5B5             = 25,
        A4R4G4B4             = 26,
        R3G3B2               = 27,
        A8                   = 28,
        A8R3G3B2             = 29,
        X4R4G4B4             = 30,
        A2B10G10R10          = 31,
        A8B8G8R8             = 32,
        X8B8G8R8             = 33,
        G16R16               = 34,
        A2R10G10B10          = 35,
        A16B16G16R16         = 36,

        A8P8                 = 40,
        P8                   = 41,

        L8                   = 50,
        A8L8                 = 51,
        A4L4                 = 52,

        V8U8                 = 60,
        L6V5U5               = 61,
        X8L8V8U8             = 62,
        Q8W8V8U8             = 63,
        V16U16               = 64,
        A2W10V10U10          = 67,

        D16_LOCKABLE         = 70,
        D32                  = 71,
        D15S1                = 73,
        D24S8                = 75,
        D24X8                = 77,
        D24X4S4              = 79,
        D16                  = 80,

        D32F_LOCKABLE        = 82,
        D24FS8               = 83,

        L16                  = 81,

        VERTEXDATA           =100,
        INDEX16              =101,
        INDEX32              =102,

        Q16W16V16U16         =110,

        R16F                 = 111,
        G16R16F              = 112,
        A16B16G16R16F        = 113,

        R32F                 = 114,
        G32R32F              = 115,
        A32B32G32R32F        = 116,

        CxV8U8               = 117,
    }

    [Flags]
    public enum D3DX_FILTER : uint {
        DEFAULT              = 0xFFFFFFFF,
        NONE                 = 0x00000001,
        POINT                = 0x00000002,
        LINEAR               = 0x00000003,
        TRIANGLE             = 0x00000004,
        BOX                  = 0x00000005,
        MIRROR_U             = 0x00010000,
        MIRROR_V             = 0x00020000,
        MIRROR_W             = 0x00040000,
        MIRROR               = 0x00070000,
        DITHER               = 0x00080000,
        DITHER_DIFFUSION     = 0x00100000,
        SRGB_IN              = 0x00200000,
        SRGB_OUT             = 0x00400000,
        SRGB                 = 0x00600000
    }

    [SuppressUnmanagedCodeSecurity]
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal unsafe delegate int LockRectDelegate (void* pTexture, uint iLevel, D3DLOCKED_RECT* pLockedRect, RECT* pRect, uint flags);

    [SuppressUnmanagedCodeSecurity]
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal unsafe delegate int UnlockRectDelegate (void* pTexture, uint iLevel);

    [SuppressUnmanagedCodeSecurity]
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal unsafe delegate int GetSurfaceLevelDelegate (void* pTexture, uint iLevel, void** pSurface);
#endif

    public static class TextureUtils {
        public static readonly SurfaceFormat ColorSrgbEXT;
        public static readonly SurfaceFormat? Bc7EXT, Bc7SrgbEXT, Dxt5SrgbEXT;

        static TextureUtils () {
#if WINDOWS
            pComPtr = typeof(Texture2D).GetField("pComPtr", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
#endif
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
                    numComponents = 1;
                    return 1;
                case SurfaceFormat.Color:
                    numComponents = 4;
                    return 4;
                case SurfaceFormat.Rgba64:
                    numComponents = 4;
                    return 8;
                case SurfaceFormat.HalfSingle:
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
                // case SurfaceFormat.BC7EXT:
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
                case SurfaceFormat.HalfVector4:
                case SurfaceFormat.Vector4:
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
#if FNA
        [DllImport("FNA3D", CallingConvention = CallingConvention.Cdecl)]
		public static extern void FNA3D_GetSysRendererEXT(
			IntPtr device,
			out FNA3D_SysRendererEXT sysrenderer
		);
#endif
    }
}
