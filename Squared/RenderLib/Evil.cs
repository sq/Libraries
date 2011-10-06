using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Xna.Framework.Graphics;

namespace Squared.Render.Evil {
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

    internal unsafe delegate int GetSurfaceLevelDelegate (void* pTexture, uint iLevel, void** pSurface);
    internal unsafe delegate uint ReleaseDelegate (void* pObj);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    public static class TextureUtils {
        public static class VTables {
            public static class IDirect3DTexture9 {
                public const uint GetSurfaceLevel = 72;
            }
        }

        [DllImport("d3dx9_41.dll")]
        private static unsafe extern int D3DXLoadSurfaceFromMemory (
            void* pDestSurface,
            void* pDestPalette,
            RECT* pDestRect,
            void* pSrcMemory,
            D3DFORMAT srcFormat,
            uint srcPitch,
            void* pSrcPalette,
            RECT* pSrcRect,
            D3DX_FILTER filter,
            uint colorKey
        );

        internal static FieldInfo pComPtr;

        static TextureUtils () {
            pComPtr = typeof(Texture2D).GetField("pComPtr", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        }

        /// <summary>
        /// Retrieves a pointer to the IDirect3DTexture9 for the specified texture.
        /// </summary>
        /// <param name="texture">The texture.</param>
        /// <returns>The texture's IDirect3DTexture9 pointer.</returns>
        public static unsafe void* GetIDirect3DTexture9 (this Texture2D texture) {
            return Pointer.Unbox(pComPtr.GetValue(texture));
        }

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

        /// <summary>
        /// Retrieves a pointer to the IDirect3DSurface9 for one of the specified texture's mip levels.
        /// </summary>
        /// <param name="texture">The texture to retrieve a mip level from.</param>
        /// <param name="level">The index of the mip level.</param>
        /// <returns>A pointer to the mip level's surface.</returns>
        public static unsafe void* GetSurfaceLevel (this Texture2D texture, int level) {
            void* pTexture = texture.GetIDirect3DTexture9();
            void* pGetSurfaceLevel = AccessVTable(pTexture, VTables.IDirect3DTexture9.GetSurfaceLevel);
            void* pSurface;

            var getSurfaceLevel = (GetSurfaceLevelDelegate)Marshal.GetDelegateForFunctionPointer(new IntPtr(pGetSurfaceLevel), typeof(GetSurfaceLevelDelegate));
            var rv = getSurfaceLevel(pTexture, 0, &pSurface);
            if (rv == 0)
                return pSurface;
            else
                throw new COMException("GetSurfaceLevel failed", rv);
        }

        /// <summary>
        /// Copies pixels from an address in memory into a mip level of Texture2D, converting them from one format to another if necessary.
        /// </summary>
        /// <param name="texture">The texture to copy to.</param>
        /// <param name="level">The index into the texture's mip levels.</param>
        /// <param name="pData">The address of the pixel data.</param>
        /// <param name="width">The width of the pixel data (in pixels).</param>
        /// <param name="height">The height of the pixel data (in pixels).</param>
        /// <param name="pitch">The number of bytes occupied by a single row of the pixel data (including padding at the end of rows).</param>
        /// <param name="pixelFormat">The format of the pixel data.</param>
        public static unsafe void SetData (
            this Texture2D texture, int level, void* pData, 
            int width, int height, uint pitch, 
            D3DFORMAT pixelFormat
        ) {
            var rect = new RECT {
                Top = 0,
                Left = 0,
                Right = width,
                Bottom = height
            };

            void* pSurface = GetSurfaceLevel(texture, level);

            try {
                var rv = D3DXLoadSurfaceFromMemory(pSurface, null, &rect, pData, pixelFormat, pitch, null, &rect, D3DX_FILTER.NONE, 0);
                if (rv != 0)
                    throw new COMException("D3DXLoadSurfaceFromMemory failed", rv);
            } finally {
                Marshal.Release(new IntPtr(pSurface));
            }
        }
    }
}
