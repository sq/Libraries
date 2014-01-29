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
    internal unsafe delegate int GetSurfaceLevelDelegate (void* pTexture, uint iLevel, void** pSurface);
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
    }

    public static class TextureUtils {
        public static class VTables {
            public static class IDirect3DTexture9 {
                public const uint GetSurfaceLevel = 72;
            }
        }

        [DllImport("d3dx9_41.dll")]
        [SuppressUnmanagedCodeSecurity]
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

        internal static readonly FieldInfo pComPtr;

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
        /// Retrieves a pointer to the IDirect3DSurface9 for one of the specified texture's mip levels.
        /// </summary>
        /// <param name="texture">The texture to retrieve a mip level from.</param>
        /// <param name="level">The index of the mip level.</param>
        /// <returns>A pointer to the mip level's surface.</returns>
        public static unsafe void* GetSurfaceLevel (this Texture2D texture, int level) {
            void* pTexture = texture.GetIDirect3DTexture9();
            void* pGetSurfaceLevel = COMUtils.AccessVTable(pTexture, VTables.IDirect3DTexture9.GetSurfaceLevel);
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
            this Texture2D texture, void* pSurface, void* pData, 
            int width, int height, uint pitch, 
            D3DFORMAT pixelFormat
        ) {
            var rect = new RECT {
                Top = 0,
                Left = 0,
                Right = width,
                Bottom = height
            };

            SetData(texture, pSurface, pData, ref rect, pitch, ref rect, pixelFormat);
        }

        /// <summary>
        /// Copies pixels from an address in memory into a mip level of Texture2D, converting them from one format to another if necessary.
        /// </summary>
        /// <param name="texture">The texture to copy to.</param>
        /// <param name="level">The index into the texture's mip levels.</param>
        /// <param name="pData">The address of the pixel data.</param>
        /// <param name="destRect">The destination rectangle.</param>
        /// <param name="pitch">The number of bytes occupied by a single row of the pixel data (including padding at the end of rows).</param>
        /// <param name="sourceRect">The source rectangle.</param>
        /// <param name="pixelFormat">The format of the pixel data.</param>
        public static unsafe void SetData (
            this Texture2D texture, void* pSurface, void* pData,
            ref RECT destRect, uint pitch, ref RECT sourceRect,
            D3DFORMAT pixelFormat
        ) {
            fixed (RECT* pDestRect = &destRect)
            fixed (RECT* pSourceRect = &sourceRect) {
                var rv = D3DXLoadSurfaceFromMemory(pSurface, null, pDestRect, pData, pixelFormat, pitch, null, pSourceRect, D3DX_FILTER.NONE, 0);
                if (rv != 0)
                    throw new COMException("D3DXLoadSurfaceFromMemory failed", rv);
            }
        }
    }
#endif

    public static class FontUtils {
        public struct FontFields {
            public Texture2D Texture;
            public List<Rectangle> GlyphRectangles;
            public List<Rectangle> CropRectangles;
            public List<char> Characters;
            public List<Vector3> Kerning;
        }

        public struct GlyphSource {
            public readonly SpriteFont Font;
            public readonly Texture2D Texture;

#if !PSM
            public readonly FontFields Fields;
            public readonly int DefaultCharacterIndex;

            private Glyph MakeGlyphForCharacter (char ch, int characterIndex) {
                var kerning = Fields.Kerning[characterIndex];

                return new Glyph {
                    Character = ch,
                    Texture = Texture,
                    BoundsInTexture = Fields.GlyphRectangles[characterIndex],
                    Cropping = Fields.CropRectangles[characterIndex],
                    LeftSideBearing = kerning.X,
                    RightSideBearing = kerning.Z,
                    WidthIncludingBearings = kerning.X + kerning.Y + kerning.Z,
                    Width = kerning.Y
                };
            }
#endif

            public bool GetGlyph (char ch, out Glyph result) {
#if PSM
                Microsoft.Xna.Framework.Graphics.SpriteFont.Glyph temp;
                if (!Font.GetGlyph(ch, out temp)) {
                    result = default(Glyph);
                    return false;
                }
                
                result = new Glyph {
                    Character = ch,
                    Texture = Texture,
                    BoundsInTexture = temp.BoundsInTexture,
                    Cropping = temp.Cropping,
                    LeftSideBearing = temp.LeftSideBearing,
                    RightSideBearing = temp.RightSideBearing,
                    WidthIncludingBearings = temp.WidthIncludingBearings,
                    Width = temp.Width
                };
                return true;
#else
                var characterIndex = Fields.Characters.BinarySearch(ch);
                if (characterIndex < 0)
                    characterIndex = DefaultCharacterIndex;

                if (characterIndex < 0) {
                    result = default(Glyph);
                    return false;
                }

                result = MakeGlyphForCharacter(ch, characterIndex);
                return true;
#endif
            }

            public GlyphSource (SpriteFont font) {
                Font = font;

#if !PSM
                if (textureValue != null) {
                    // XNA SpriteFont
                    GetPrivateFields(font, out Fields);
                    Texture = Fields.Texture;

                    if (Font.DefaultCharacter.HasValue)
                        DefaultCharacterIndex = Fields.Characters.BinarySearch(Font.DefaultCharacter.Value);
                    else
                        DefaultCharacterIndex = -1;
                } else {
                    throw new NotImplementedException("Unsupported SpriteFont implementation");

                }
#else
                Texture = font.Texture;
#endif
            }
        }

        internal static readonly FieldInfo textureValue, glyphData, croppingData, kerning, characterMap;

        static FontUtils () {
            var tSpriteFont = typeof(SpriteFont);
            textureValue = GetPrivateField(tSpriteFont, "textureValue");
            glyphData = GetPrivateField(tSpriteFont, "glyphData");
            croppingData = GetPrivateField(tSpriteFont, "croppingData");
            kerning = GetPrivateField(tSpriteFont, "kerning");
            characterMap = GetPrivateField(tSpriteFont, "characterMap");
        }

        private static FieldInfo GetPrivateField (Type type, string fieldName) {
            return type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        }

        public static GlyphSource GetGlyphSource (this SpriteFont font) {
            return new GlyphSource(font);
        }

        public static void GetPrivateFields (this SpriteFont font, out FontFields result) {
            result = new FontFields {
                Texture = (Texture2D)(textureValue).GetValue(font),
                GlyphRectangles = (List<Rectangle>)glyphData.GetValue(font),
                CropRectangles = (List<Rectangle>)croppingData.GetValue(font),
                Characters = (List<char>)characterMap.GetValue(font),
                Kerning = (List<Vector3>)kerning.GetValue(font)
            };
        }
    }

    public struct Glyph {
        public Texture2D Texture;
        public char Character;
        public Rectangle BoundsInTexture;
        public Rectangle Cropping;
        public float LeftSideBearing;
        public float RightSideBearing;
        public float Width;
        public float WidthIncludingBearings;
    }

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
}
