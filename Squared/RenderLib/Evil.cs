using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Squared.Render.Evil {
#if WINDOWS
    [SuppressUnmanagedCodeSecurity]
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal unsafe delegate int GetDisplayModeDelegate (void* pDevice, int iSwapChain, out D3DDISPLAYMODE pMode);

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

    public static class EffectUtils {
        internal static readonly FieldInfo pComPtr, technique_pComPtr;

        static EffectUtils () {
            pComPtr = typeof(Effect).GetField("pComPtr", BindingFlags.Instance | BindingFlags.NonPublic);
            technique_pComPtr = typeof(EffectTechnique).GetField("pComPtr", BindingFlags.Instance | BindingFlags.NonPublic);
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
    }
#endif

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
#endif
}
