﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SharpFont;
using Squared.Game;
using Squared.Render;
using Squared.Render.Mips;
using Squared.Render.Text;
using Squared.Render.Text.OpenType;
using Squared.Render.TextLayout2;
using Squared.Util;
using Squared.Util.DeclarativeSort;
using Squared.Util.Text;
using FtGlyph = SharpFont.Glyph;
using SrGlyph = Squared.Render.Text.Glyph;

namespace Squared.Render.Text {
    public enum FreeTypeFontFormat {
        /// <summary>
        /// Stores the font atlas in a luminance texture
        /// </summary>
        Gray,
        /// <summary>
        /// Stores the font atlas in an ARGB texture with SRGB encoding
        /// </summary>
        SRGB,
        /// <summary>
        /// Stores the font atlas in an ARGB texture with linear encoding
        /// </summary>
        Linear,
        /// <summary>
        /// Stores the font atlas as a signed distance field.
        /// </summary>
        DistanceField,
    }

    public class FreeTypeFont : IGlyphSource, IDisposable {
        private static EmbeddedDLLLoader DllLoader;

        public bool IsDisposed { get; private set; }

        static FreeTypeFont () {
            try {
                DllLoader = new Util.EmbeddedDLLLoader(Assembly.GetExecutingAssembly());
                DllLoader.Load("freetype6.dll");
            } catch (Exception exc) {
                Console.Error.WriteLine("Failed to load freetype6.dll: {0}", exc.Message);
            }
        }

        public static uint BaseDPI = 96;

        public class FontSize : IGlyphSource, IKerningProvider, ILigatureProvider, IDisposable {
            public const int LowCacheSize = 256;
            // HACK: The first atlas we create for a font should be smaller as long as the font size itself is small enough
            public const int FirstAtlasWidth = 512, FirstAtlasHeight = 512;
            // FIXME: Randomly selected value, probably too small or too big. This is after DPI is applied (so 200% -> 2x the input DPI)
            // If a font's size is bigger than this we double the size of all of its atlases (i.e. first is 1024, the rest are 2048)
            public static float LargeAtlasThreshold = 42,
                // If a font's size is bigger than THIS, we skip having a small first atlas, since it probably won't be big enough.
                SkipFirstAtlasThreshold = 82;
            public const int AtlasWidth = 1024, AtlasHeight = 1024;

            internal List<IDynamicAtlas> Atlases = new List<IDynamicAtlas>();
            internal FreeTypeFont Font;
            internal SrGlyph[] LowCacheByCodepoint = new SrGlyph[LowCacheSize];
            // HACK: Pre-allocate with a reasonable amount of space to avoid spurious reallocations when first being filled up
            internal Dictionary<uint, SrGlyph> CacheByGlyphId = new Dictionary<uint, SrGlyph>(512, UintComparer.Instance),
                CacheByCodepoint = new Dictionary<uint, SrGlyph>(512, UintComparer.Instance);
            internal float _SizePoints;
            internal int _Version;
            private FreeTypeFontFormat? _OverrideFormat;

            internal DenseList<WeakReference<IGlyphSourceChangeListener>> ChangeListeners;
            void IGlyphSource.RegisterForChangeNotification (WeakReference<IGlyphSourceChangeListener> listener) {
                if (!ChangeListeners.Contains(listener))
                    ChangeListeners.Add(listener);
            }

            public FreeTypeFontFormat Format => _OverrideFormat ?? Font.Format;
            public FreeTypeFontFormat? OverrideFormat {
                get => _OverrideFormat;
                set {
                    if (_OverrideFormat == value) 
                        return;
                    _OverrideFormat = value;
                    Invalidate();
                }
            }

            public bool SDF => Format == FreeTypeFontFormat.DistanceField;

            private bool _ContainsColorGlyphs;
            public bool ContainsColorGlyphs => (Format switch {
                FreeTypeFontFormat.Linear => _ContainsColorGlyphs,
                FreeTypeFontFormat.SRGB => _ContainsColorGlyphs,
                _ => false,
            });

            public bool IsDisposed { get; private set; }

            object IGlyphSource.UniqueKey => this;

            public FontSize (FreeTypeFont font, float sizePoints) {
                Font = font;
                SizePoints = sizePoints;
                Font.Sizes.Add(this);
                for (int i = 0; i < LowCacheByCodepoint.Length; i++)
                    LowCacheByCodepoint[i].GlyphIndex = uint.MaxValue;
            }


            public float VerticalOffset;

            public float SizePoints {
                get {
                    return _SizePoints;
                }
                set {
                    if (_SizePoints == value)
                        return;

                    _SizePoints = value;
                    Invalidate();
                }
            }

            public override string ToString () =>
                $"{Font} {SizePoints}pt";


            private unsafe static MipGeneratorFn PickMipGenerator (FreeTypeFont font, FreeTypeFontFormat format) {
                MipFormat mf;
                switch (format) {
                    case FreeTypeFontFormat.Linear:
                        mf = MipFormat.pRGBA;
                        break;
                    case FreeTypeFontFormat.SRGB:
                        mf = MipFormat.pRGBA | MipFormat.sRGB;
                        break;
                    case FreeTypeFontFormat.Gray:
                        mf = MipFormat.Gray1;
                        break;
                    case FreeTypeFontFormat.DistanceField:
                        if (font.SDFMipMapping)
                            mf = MipFormat.Gray1;
                        else
                            return null;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(format));
                }

                if (!font.MipMapping)
                    return null;

                return font?.MipGen?.Get(mf) ?? MipGenerator.Get(mf);
            }

            private unsafe DynamicAtlasReservation Upload (FTBitmap bitmap) {
                bool foundRoom = false;
                DynamicAtlasReservation result = default;
                if (bitmap.Buffer == IntPtr.Zero)
                    throw new NullReferenceException("bitmap.Buffer");

                int width = bitmap.Width, rows = bitmap.Rows, pitch = bitmap.Pitch;
                if (bitmap.PixelMode == PixelMode.Bgra)
                    _ContainsColorGlyphs = true;

                // FIXME: Dynamic
                int spacing = 4;
                var widthW = width + (Font.GlyphMargin * 2);
                var heightW = rows + (Font.GlyphMargin * 2);

                foreach (var atlas in Atlases) {
                    if (atlas.TryReserve(widthW, heightW, out result)) {
                        foundRoom = true;
                        break;
                    }
                }

                if (!foundRoom) {
                    bool isIconFont = Font.IsIconFont,
                        isFirstAtlas = (Atlases.Count == 0) && (isIconFont || ((_SizePoints * Font.DPIPercent / 100) <= SkipFirstAtlasThreshold)),
                        isLargeAtlas = !isIconFont && ((_SizePoints * Font.DPIPercent / 100) >= LargeAtlasThreshold);
                    int newAtlasWidth = isFirstAtlas ? FirstAtlasWidth : AtlasWidth,
                        newAtlasHeight = isFirstAtlas ? FirstAtlasHeight : AtlasHeight;
                    if (isLargeAtlas) {
                        newAtlasWidth *= 2;
                        newAtlasHeight *= 2;
                    }

                    var tag = $"{Font.Face.FamilyName} {Font.Face.StyleName} {SizePoints}pt #{Atlases.Count + 1}";

                    var newAtlas = CreateAtlas(newAtlasWidth, newAtlasHeight, spacing, tag);
                    Atlases.Add(newAtlas);
                    if (!newAtlas.TryReserve(widthW, heightW, out result))
                        throw new InvalidOperationException("Character too large for atlas");
                }

                var pSrc = (byte*)bitmap.Buffer;

                if (SDF) {
                    var pPixels = (float*)result.Atlas.Data;
                    switch (bitmap.PixelMode) {
                        case PixelMode.Gray:
                            UploadGrayAsSDF(bitmap, result, width, rows, pitch, pSrc, pPixels);
                            break;

                        default:
                            throw new NotImplementedException("Unsupported pixel mode: " + bitmap.PixelMode);
                    }
                } else {
                    var sRGBConversion = Format == FreeTypeFontFormat.SRGB;
                    var table = Font.GammaRamp?.GammaTable;
                    var pDest = (byte*)result.Atlas.Data;
                    switch (bitmap.PixelMode) {
                        case PixelMode.Bgra:
                            UploadBgra(result, width, rows, pitch, pSrc, pDest);
                            break;

                        case PixelMode.Gray:
                            UploadGray(result, width, rows, pitch, pSrc, sRGBConversion, table, pDest);
                            break;

                        case PixelMode.Mono:
                            UploadMono(result, rows, pitch, pSrc, pDest);
                            break;

                        default:
                            throw new NotImplementedException("Unsupported pixel mode: " + bitmap.PixelMode);
                    }
                }

                result.Invalidate();
                return result;
            }

            private unsafe void UploadGrayAsSDF (FTBitmap bitmap, DynamicAtlasReservation result, int width, int rows, int pitch, byte* pSrc, float* pPixels) {
                float valueScale = -(new Vector2(bitmap.Width, bitmap.Rows)).Length() / 2f;
                for (var y = 0; y < rows; y++) {
                    var rowOffset = result.Atlas.Width * (y + result.Y + Font.GlyphMargin) + (result.X + Font.GlyphMargin);
                    var pDestRow = pPixels + rowOffset;
                    int yPitch = y * pitch;

                    for (var x = 0; x < width; x++) {
                        var a = pSrc[x + yPitch] - 128f;
                        a = (a / 128f) * valueScale;

                        *pDestRow++ = a;
                    }
                }
            }

            private unsafe void UploadMono (DynamicAtlasReservation result, int rows, int pitch, byte* pSrc, byte* pDest) {
                if ((Format == FreeTypeFontFormat.Linear) || (Format == FreeTypeFontFormat.SRGB)) {
                    for (var y = 0; y < rows; y++) {
                        var rowOffset = result.Atlas.Width * (y + result.Y + Font.GlyphMargin) + (result.X + Font.GlyphMargin);
                        var pDestRow = pDest + (rowOffset * 4);
                        int yPitch = y * pitch;

                        for (int x = 0; x < pitch; x++, pDestRow += (8 * 4)) {
                            var bits = pSrc[x + yPitch];

                            for (int i = 0; i < 8; i++) {
                                int iy = 7 - i;
                                byte g = ((bits & (1 << iy)) != 0) ? (byte)255 : (byte)0;
                                var pElt = pDestRow + (i * 4);
                                pElt[3] = pElt[2] = pElt[1] = pElt[0] = g;
                            }
                        }
                    }
                } else if (Format == FreeTypeFontFormat.Gray) {
                    for (var y = 0; y < rows; y++) {
                        var rowOffset = result.Atlas.Width * (y + result.Y + Font.GlyphMargin) + (result.X + Font.GlyphMargin);
                        var pDestRow = pDest + rowOffset;
                        int yPitch = y * pitch;

                        for (int x = 0; x < pitch; x++, pDestRow += 8) {
                            var bits = pSrc[x + yPitch];

                            for (int i = 0; i < 8; i++) {
                                int iy = 7 - i;
                                byte g = ((bits & (1 << iy)) != 0) ? (byte)255 : (byte)0;
                                var pElt = pDestRow + i;
                                *pElt = g;
                            }
                        }
                    }                
                } else {
                    throw new NotImplementedException(Format.ToString());
                }
            }

            private unsafe void UploadGray (DynamicAtlasReservation result, int width, int rows, int pitch, byte* pSrc, bool srgb, byte[] table, byte* pDest) {
                var destIsGray = Format == FreeTypeFontFormat.Gray;
                for (var y = 0; y < rows; y++) {
                    var rowOffset = result.Atlas.Width * (y + result.Y + Font.GlyphMargin) + (result.X + Font.GlyphMargin);
                    int yPitch = y * pitch;

                    if (destIsGray) {
                        var pDestRow = pDest + rowOffset;
                        if (table == null) {
                            for (var x = 0; x < width; x++) {
                                var a = pSrc[x + yPitch];

                                *pDestRow++ = a;
                            }
                        } else {
                            for (var x = 0; x < width; x++) {
                                var a = table[pSrc[x + yPitch]];

                                *pDestRow++ = a;
                            }
                        }
                    } else {
                        var pDestRow = pDest + (rowOffset * 4);
                        if (table == null) {
                            for (var x = 0; x < width; x++) {
                                var a = pSrc[x + yPitch];
                                var g = srgb ? ColorSpace.LinearByteTosRGBByteTable[a] : a;

                                pDestRow[3] = a;
                                pDestRow[2] = pDestRow[1] = pDestRow[0] = g;
                                pDestRow += 4;
                            }
                        } else {
                            for (var x = 0; x < width; x++) {
                                var a = table[pSrc[x + yPitch]];
                                var g = srgb ? ColorSpace.LinearByteTosRGBByteTable[a] : a;

                                pDestRow[3] = a;
                                pDestRow[2] = pDestRow[1] = pDestRow[0] = g;
                                pDestRow += 4;
                            }
                        }
                    }
                }
            }

            private unsafe void UploadBgra (DynamicAtlasReservation result, int width, int rows, int pitch, byte* pSrc, byte* pDest) {
                var destIsGray = Format == FreeTypeFontFormat.Gray;
                if (destIsGray)
                    throw new NotImplementedException($"BGRA -> {Format}");

                for (var y = 0; y < rows; y++) {
                    var rowOffset = result.Atlas.Width * (y + result.Y + Font.GlyphMargin) + (result.X + Font.GlyphMargin);
                    var pDestRow = pDest + (rowOffset * 4);
                    int yPitch = y * pitch;

                    // FIXME: Implement gamma table somehow? Because the glyphs are already premultiplied, I'm not sure how
                    // FIXME: SRGB -> Linear
                    for (var x = 0; x < width; x++) {
                        var ppSrc = pSrc + (x * 4) + yPitch;

                        pDestRow[3] = ppSrc[3];
                        pDestRow[2] = ppSrc[0];
                        pDestRow[1] = ppSrc[1];
                        pDestRow[0] = ppSrc[2];
                        pDestRow += 4;
                    }
                }
            }

            private IDynamicAtlas CreateAtlas (int newAtlasWidth, int newAtlasHeight, int spacing, string tag) {
                switch (Format) {
                    case FreeTypeFontFormat.SRGB:
                    case FreeTypeFontFormat.Linear:
                        return new DynamicAtlas<Color>(
                            Font.RenderCoordinator, newAtlasWidth, newAtlasHeight,
                            (Format == FreeTypeFontFormat.SRGB)
                                ? SurfaceFormat.ColorSrgbEXT
                                : SurfaceFormat.Color, 
                            spacing, PickMipGenerator(Font, Format), tag: tag
                        );
                    case FreeTypeFontFormat.Gray:
                        return new DynamicAtlas<byte>(
                            Font.RenderCoordinator, newAtlasWidth, newAtlasHeight,
                            SurfaceFormat.Alpha8, 
                            spacing, PickMipGenerator(Font, Format), tag: tag
                        );
                    case FreeTypeFontFormat.DistanceField:
                        return new DynamicAtlas<float>(
                            Font.RenderCoordinator, newAtlasWidth, newAtlasHeight,
                            SurfaceFormat.Single,
                            spacing, PickMipGenerator(Font, Format), tag: tag
                        ) { ClearValue = 1024f };
                    default:
                        throw new ArgumentOutOfRangeException(nameof(Format));
                }
            }

            // FIXME: We need to invalidate cached glyphs when this changes
            public float ExtraLineSpacing = 0;

            private float? _LineSpacing = null;

            public float LineSpacing {
                get {
                    if (!_LineSpacing.HasValue) {
                        var size = Font.GetFTSize(_SizePoints, BaseDPI);
                        _LineSpacing = size.Metrics.Height.ToSingle();
                    }

                    return _LineSpacing.Value + ExtraLineSpacing;
                }
            }

            const uint FirstDigit = '0', LastDigit = '9';
            private bool NeedNormalization = true;
            private SizeMetrics _CachedMetrics;
            private float _CachedXDesignUnitsToPx,
                _CachedYDesignUnitsToPx;

            private void ApplyWidthNormalization (bool normalizeNumberWidths) {
                NeedNormalization = false;
                Color? nullableDefaultColor = null;
                Color defaultColor;
                SrGlyph temp;
                float maxWidth = -1;

                for (uint i = FirstDigit; i <= LastDigit; i++) {
                    if (LowCacheByCodepoint[i].GlyphIndex < uint.MaxValue)
                        continue;
                    if (Font.DefaultGlyphColors.TryGetValue(i, out defaultColor))
                        nullableDefaultColor = defaultColor;
                    PopulateGlyphCache(i, nullableDefaultColor, out temp);
                    maxWidth = Math.Max(maxWidth, temp.WidthIncludingBearing);
                }

                maxWidth = (float)Math.Round(maxWidth, 0);
                if (maxWidth < 0)
                    return;

                // Figure space (specified to be the same size as a number)
                if (!PopulateGlyphCache(0x2007, null, out var figureSpace)) {
                    // No figure space glyph, fake one
                    BuildGlyph(GetGlyphIndex(' '), 0x2007, null, out figureSpace);
                }

                var glyphIdForRegularSpace = GetGlyphIndex(' ');
                var padding = maxWidth - figureSpace.WidthIncludingBearing;
                figureSpace.LeftSideBearing += (padding / 2f);
                figureSpace.RightSideBearing += (padding / 2f);

                CacheByCodepoint[0x2007] = figureSpace;
                if ((figureSpace.GlyphIndex > 0) && (glyphIdForRegularSpace != figureSpace.GlyphIndex))
                    CacheByGlyphId[figureSpace.GlyphIndex] = figureSpace;

                if (normalizeNumberWidths) {
                    for (uint i = FirstDigit; i <= LastDigit; i++) {
                        padding = maxWidth - LowCacheByCodepoint[i].WidthIncludingBearing;
                        LowCacheByCodepoint[i].LeftSideBearing += padding / 2f;
                        LowCacheByCodepoint[i].RightSideBearing += padding / 2f;
                    }
                }
            }

            public bool GetGlyph (uint codepoint, out Glyph glyph) {
                if (IsDisposed) {
                    glyph = default(Glyph);
                    return false;
                }

                if (codepoint < LowCacheSize) {
                    // Capture by-reference since width normalization could modify it
                    ref var glyphRef = ref LowCacheByCodepoint[codepoint];
                    if (glyphRef.GlyphIndex < uint.MaxValue) {
                        if (NeedNormalization)
                            ApplyWidthNormalization(Font.EqualizeNumberWidths);

                        glyph = glyphRef;
                        return glyphRef.GlyphIndex > 0;
                    } else
                        ;
                } else if ((codepoint == 0x2007) && NeedNormalization)
                    // Figure space should trigger width normalization since it's a number... ish
                    ApplyWidthNormalization(Font.EqualizeNumberWidths);

                Color? nullableDefaultColor = null;
                Color defaultColor;
                if (Font.DefaultGlyphColors.TryGetValue(codepoint, out defaultColor))
                    nullableDefaultColor = defaultColor;

                if (CacheByCodepoint.TryGetValue(codepoint, out glyph)) {
                    glyph.DefaultColor = nullableDefaultColor;
                    return glyph.GlyphIndex > 0;
                }

                return PopulateGlyphCache(codepoint, nullableDefaultColor, out glyph);
            }

            public uint GetGlyphIndex (uint codepoint) => Font.GetGlyphIndex(codepoint);

            private void BuildGlyph (
                uint index, uint codepoint, Color? defaultColor, out Glyph glyph
            ) {
                var resolution = (uint)(BaseDPI * Font.DPIPercent / 100);
                var size = Font.GetFTSize(_SizePoints, resolution);

                LoadFlags flags = default;
                if (!Font.EnableBitmaps)
                    flags |= LoadFlags.NoBitmap;
                if (!Font.Hinting)
                    flags |= LoadFlags.NoHinting;
                if (Font.Monochrome)
                    flags |= LoadFlags.Monochrome;
                else
                    flags |= LoadFlags.Color;

                var target = LoadTarget.Normal;
                if (Font.Monochrome)
                    target = LoadTarget.Mono;

                Font.Face.LoadGlyph(
                    index, flags, target
                );

                var sizeMetrics = _CachedMetrics = size.Metrics;
                _CachedXDesignUnitsToPx = _CachedMetrics.ScaleX.ToSingle() / 64f;
                _CachedYDesignUnitsToPx = _CachedMetrics.ScaleY.ToSingle() / 64f;

                Font.Face.RenderGlyphEXT(SDF ? RenderMode.VerticalLcd + 1 : RenderMode.Normal);
                var ftgs = Font.Face.Glyph;

                var bitmap = ftgs.Bitmap;

                DynamicAtlasReservation texRegion = default;
                if ((bitmap.Width > 0) && (bitmap.Rows > 0))
                    texRegion = Upload(bitmap);

                var ascender = sizeMetrics.Ascender.ToSingle();
                var glyphMetrics = ftgs.Metrics;
                var advance = glyphMetrics.HorizontalAdvance.ToSingle();

                var scaleFactor = 100f / Font.DPIPercent;

                float widthMetric = glyphMetrics.Width.ToSingle(),
                    heightMetric = glyphMetrics.Height.ToSingle(),
                    bearingXMetric = glyphMetrics.HorizontalBearingX.ToSingle(),
                    bearingYMetric = glyphMetrics.HorizontalBearingY.ToSingle();

                var rect = texRegion.Rectangle;
                var isNumber = (codepoint >= '0' && codepoint <= '9');

                glyph = new SrGlyph {
                    KerningProvider = (Font.EnableKerning && (Font.GPOS != null) &&
                        (!isNumber || !Font.EqualizeNumberWidths)) &&
                        Font.GPOS.HasAnyEntriesForGlyph(index)
                        ? this
                        : null,
                    LigatureProvider = (Font.EnableLigatures && (Font.GSUB != null)) &&
                        Font.GSUB.HasAnyEntriesForGlyph(index)
                        ? this
                        : null,
                    GlyphIndex = index,
                    Character = codepoint,
                    Width = widthMetric,
                    Height = heightMetric,
                    VerticalBearing = bearingYMetric,
                    LeftSideBearing = bearingXMetric,
                    RightSideBearing = (
                        advance -
                            widthMetric -
                            bearingXMetric
                    ),
                    XOffset = ftgs.BitmapLeft - bearingXMetric - Font.GlyphMargin,
                    YOffset = -ftgs.BitmapTop + ascender - Font.GlyphMargin + Font.VerticalOffset + VerticalOffset,
                    // FIXME: This will become invalid if the extra spacing changes
                    // FIXME: Scale the spacing appropriately based on ratios
                    LineSpacing = sizeMetrics.Height.ToSingle() + ExtraLineSpacing,
                    DefaultColor = defaultColor,
                    Baseline = sizeMetrics.Ascender.ToSingle(),
                    XBounds = new Interval(bearingXMetric, bearingXMetric + widthMetric),
                    YBounds = new Interval(-bearingYMetric, -bearingYMetric + heightMetric),
                };

                if (texRegion.Atlas != null) {
                    glyph.Texture = new AbstractTextureReference(texRegion.Atlas);
                    glyph.BoundsInTexture = texRegion.Atlas.BoundsFromRectangle(in rect);
                }
            }

            private bool PopulateGlyphCache (uint codepoint, Color? defaultColor, out Glyph glyph) {
                var result = true;
                uint glyphId;

                glyphId = Font.Face.GetCharIndex(codepoint);

                if (glyphId > 0) {
                    BuildGlyph(glyphId, codepoint, defaultColor, out glyph);

                    // HACK
                    if (codepoint <= 0xCFFF) {
                        // Some fonts have weirdly-sized space characters
                        if (char.IsWhiteSpace((char)codepoint))
                            glyph.RightSideBearing = (float)Math.Round(glyph.RightSideBearing);
                    }
                } else {
                    glyph = default;
                    result = false;
                }

                if (codepoint < LowCacheSize)
                    LowCacheByCodepoint[codepoint] = glyph;
                CacheByGlyphId[glyphId] = glyph;
                CacheByCodepoint[codepoint] = glyph;

                if (NeedNormalization)
                    ApplyWidthNormalization(Font.EqualizeNumberWidths);

                return result;
            }

            public void Invalidate () {
                if (IsDisposed)
                    return;

                _Version++;
                NeedNormalization = true;

                foreach (var atlas in Atlases)
                    atlas.Clear();

                Array.Clear(LowCacheByCodepoint, 0, LowCacheSize);
                for (int i = 0; i < LowCacheByCodepoint.Length; i++)
                    LowCacheByCodepoint[i].GlyphIndex = uint.MaxValue;
                CacheByGlyphId.Clear();
                CacheByCodepoint.Clear();

                _LineSpacing = null;

                foreach (var listener in ChangeListeners)
                    if (listener.TryGetTarget(out var t))
                        t.NotifyChanged(this);
            }

            float IGlyphSource.DPIScaleFactor {
                get {
                    return Font.DPIPercent / 100f;
                }
            }

            int IGlyphSource.Version {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get {
                    return _Version;
                }
            }

            public void Dispose () {
                if (IsDisposed)
                    return;

                IsDisposed = true;

                Font.Sizes.Remove(this);
                Array.Clear(LowCacheByCodepoint, 0, LowCacheSize);
                for (int i = 0; i < LowCacheByCodepoint.Length; i++)
                    LowCacheByCodepoint[i].GlyphIndex = uint.MaxValue;

                CacheByGlyphId.Clear();
                CacheByCodepoint.Clear();

                foreach (var atlas in Atlases)
                    atlas.Dispose();
                Atlases.Clear();
            }

            bool IKerningProvider.TryGetKerning (uint glyphId, uint nextGlyphId, ref KerningData thisGlyph, ref KerningData nextGlyph) {
                bool found = false;
                GPOSValueRecord value1 = default, value2 = default;
                foreach (var table in Font.GPOS.Lookups) {
                    if (table.TryGetValue((int)glyphId, (int)nextGlyphId, ref value1, ref value2)) {
                        found = true;
                        break;
                    }
                }

                if (found) {
                    thisGlyph.RightSideBearing = value1.XAdvance * _CachedXDesignUnitsToPx;
                    thisGlyph.XOffset = value1.XPlacement * _CachedXDesignUnitsToPx;
                    thisGlyph.YOffset = value1.YPlacement * _CachedYDesignUnitsToPx;
                    nextGlyph.RightSideBearing = value2.XAdvance * _CachedXDesignUnitsToPx;
                    nextGlyph.XOffset = value2.XPlacement * _CachedXDesignUnitsToPx;
                    nextGlyph.YOffset = value2.YPlacement * _CachedYDesignUnitsToPx;
                }

                return found;
            }
            
            unsafe int ILigatureProvider.TryGetLigature (ref SrGlyph glyph, AbstractString text, int startOffset) {
                var gsub = Font.GSUB;
                var subst = default(GSUBLigatureSubst);
                var l = text.Length;

                // HACK: Arbitrary look-forward distance.
                const int glyphLimit = 4;
                var glyphs = stackalloc uint[glyphLimit];
                var codepointSizes = stackalloc int[glyphLimit];

                // Perform a single forward scan to decode following codepoints into our stack buffers
                glyphs[0] = glyph.GlyphIndex;
                int i = startOffset + 1;
                for (var k = 1; k < glyphLimit; k++) {
                    if (i < l) {
                        // Record codepoint sizes so we know how many characters we ate once we find
                        //  a ligature, if any
                        var codepoint = StringLayoutEngine2.DecodeCodepoint(text, ref i, l, out _, out codepointSizes[k]);
                        glyphs[k] = Font.GetGlyphIndex(codepoint);
                    } else {
                        glyphs[k] = 0;
                    }
                    i++;
                }

                foreach (var lookup in gsub.Lookups) {
                    foreach (var st in lookup.SubTables) {
                        if (!st.TryGetValue((int)glyph.GlyphIndex, ref subst))
                            continue;

                        for (var j = 0; j < subst.LigatureCount; j++) {
                            ref var ligature = ref subst.Ligatures[j];
                            // HACK: If the ligature is larger than our decode window, just ignore it
                            if (ligature.ComponentCount > glyphLimit)
                                continue;

                            bool match = true;
                            for (var k = 1; k < ligature.ComponentCount; k++) {
                                if (glyphs[k] != ligature.ComponentGlyphIDs[k - 1]) {
                                    match = false;
                                    break;
                                }
                            }

                            if (match) {
                                int charsEaten = 0;
                                for (var k = 1; k < ligature.ComponentCount; k++)
                                    charsEaten += codepointSizes[k];
                                
                                if (!CacheByGlyphId.TryGetValue(ligature.LigatureGlyph, out glyph)) {
                                    BuildGlyph(ligature.LigatureGlyph, 0, null, out glyph);
                                    CacheByGlyphId[ligature.LigatureGlyph] = glyph;
                                }
                                // FIXME
                                return charsEaten;
                            }
                        }
                    }
                }

                return 0;
            }
        }

        private float _CachedSizePoints;
        private uint _CachedResolution;
        private Face _CachedFace;
        private FTSize _CachedSize;
        private uint[] _GlyphIdForCodepointLowCache = new uint[256];
        private Dictionary<uint, uint> _GlyphIdForCodepointCache = new Dictionary<uint, uint>(UintComparer.Instance);

        private FTSize GetFTSize (float sizePoints, uint resolution) {
            if (
                (_CachedSizePoints != sizePoints) || 
                (_CachedResolution != resolution) ||
                (_CachedFace != Face)
            ) {
                _CachedSize?.Dispose();

                Face.SetCharSize(0, sizePoints, resolution, resolution);
                _CachedSizePoints = sizePoints;
                _CachedResolution = resolution;
                _CachedFace = Face;
                _CachedSize = Face.Size;
            }
            return _CachedSize;
        }

        internal RenderCoordinator RenderCoordinator;
        internal Face Face;

        object IGlyphSource.UniqueKey => this;

        public FontSize DefaultSize { get; private set; }
        // FIXME: Invalidate on set
        public int GlyphMargin { get; set; }
        /// <summary>
        /// Increases the resolution of the generated texture by this much to allow compensating for high-DPI
        /// </summary>
        public int DPIPercent { get; set; }
        public bool Hinting { get; set; }
        /// <summary>
        /// Enables generating a full accurate mip chain
        /// </summary>
        public bool MipMapping { get; set; }
        /// <summary>
        /// Enables generating a full mip chain for SDF text. This may not look how you expect
        /// </summary>
        public bool SDFMipMapping { get; set; }
        public bool EnableBitmaps { get; set; } = true;
        public bool Monochrome { get; set; }
        private FreeTypeFontFormat _Format = FreeTypeFontFormat.Gray;
        public FreeTypeFontFormat Format {
            get => _Format;
            set {
                if (_Format == value)
                    return;
                _Format = value;
                Invalidate();
            }
        }
        public bool SDF;

        /// <summary>
        /// If enabled, the 0-9 digits will have padding added to make them the same width
        /// </summary>
        public bool EqualizeNumberWidths {
            get => _EqualizeNumberWidths;
            set {
                if (value == _EqualizeNumberWidths)
                    return;
                _EqualizeNumberWidths = value;
                Invalidate();
            }
        }

        /// <summary>
        /// Enables OpenType kerning (if the font contains a GPOS table).
        /// This adds significant overhead to text layout!
        /// </summary>
        public bool EnableKerning {
            get => _EnableKerning;
            set {
                if (value == _EnableKerning)
                    return;
                _EnableKerning = value;
                Invalidate();
            }
        }

        /// <summary>
        /// Enables OpenType ligatures (if the font contains a GSUB table).
        /// This adds significant overhead to text layout!
        /// </summary>
        public bool EnableLigatures {
            get => _EnableLigatures;
            set {
                if (value == _EnableLigatures)
                    return;
                _EnableLigatures = value;
                Invalidate();
            }
        }

        private bool _EnableKerning, _EnableLigatures, _EqualizeNumberWidths;
        private double _Gamma;
        private GammaRamp GammaRamp;
        private MipGenerator.WithGammaRamp MipGen;
        private HashSet<FontSize> Sizes = new HashSet<FontSize>(new ReferenceComparer<FontSize>());
        private Stream _BaseStream;

        public Dictionary<uint, Color> DefaultGlyphColors = new Dictionary<uint, Color>(UintComparer.Instance);

        public float VerticalOffset;

        public override string ToString () =>
            $"Font '{Face?.FamilyName}' ({Face?.StyleName})";

        public GPOSTable GPOS {
            get; private set;
        } 

        public GSUBTable GSUB {
            get; private set;
        }

        public double Gamma {
            get {
                return _Gamma;
            }
            set {
                if (value == 1) {
                    GammaRamp = null;
                    // MipGen = null;
                } else if (_Gamma != value) {
                    GammaRamp = new GammaRamp(value);
					// FIXME: This looks really bad for high/low gamma values due to rounding
                    // MipGen = new MipGenerator.WithGammaRamp(GammaRamp);
                }
                _Gamma = value;
            }
        }

        float IGlyphSource.DPIScaleFactor {
            get {
                return DPIPercent / 100f;
            }
        }

        public FreeTypeFont (RenderCoordinator rc, string filename, int faceIndex = 0) {
            RenderCoordinator = rc;
            Face = new Face(new Library(), filename, faceIndex);
            ReadTables();
            Initialize();
        }

        public unsafe FreeTypeFont (RenderCoordinator rc, Stream stream, int faceIndex = 0) {
            RenderCoordinator = rc;
            if (stream is UnmanagedMemoryStream ums) {
                _BaseStream = ums;
                Face = new Face(new Library(), (IntPtr)ums.PositionPointer, (int)(ums.Length - ums.Position), faceIndex);
            } else {
                var buffer = new byte[stream.Length];
                stream.Read(buffer, 0, (int)stream.Length);
                Face = new Face(new Library(), buffer, faceIndex);
            }
            ReadTables();
            Initialize();
        }

        public FreeTypeFont (RenderCoordinator rc, byte[] buffer, int faceIndex = 0) {
            RenderCoordinator = rc;
            Face = new Face(new Library(), buffer, faceIndex);
            ReadTables();
            Initialize();
        }

        private void ReadTables () {
            var err = FT.FT_OpenType_Validate(
                Face.Handle, 
                OpenTypeValidationFlags.Gpos | OpenTypeValidationFlags.Gsub, 
                out _, out _, out var gposTable, out var gsubTable, out _
            );
            if ((err == 0) && (gposTable != default)) {
                GPOS = new GPOSTable(this, gposTable);
            } else {
                GPOS = null;
            }
            if ((err == 0) && (gsubTable != default)) {
                GSUB = new GSUBTable(this, gsubTable);
            } else {
                GSUB = null;
            }
        }

        private void Initialize () {
            Gamma = 1.0;
            DPIPercent = 100;
            MipMapping = true;
            GlyphMargin = 0;
            DefaultSize = new FontSize(this, 12);

            if (Face.GlyphCount <= 0)
                throw new Exception("Loaded font contains no glyphs or is corrupt.");

            for (uint i = 0; i < _GlyphIdForCodepointLowCache.Length; i++)
                _GlyphIdForCodepointLowCache[i] = Face.GetCharIndex(i);
        }

        public IEnumerable<uint> SupportedCodepoints {
            get {
                uint glyphIndex;
                var charCode = Face.GetFirstChar(out glyphIndex);

                if (glyphIndex != 0)
                    yield return charCode;

                while (glyphIndex != 0) {
                    charCode = Face.GetNextChar(charCode, out glyphIndex);
                    if (glyphIndex != 0)
                        yield return charCode;
                }
            }
        }

        public void Invalidate () {
            foreach (var size in Sizes)
                size.Invalidate();
        }

        public float SizePoints {
            get {
                return DefaultSize.SizePoints;
            }
            set {
                DefaultSize.SizePoints = value;
            }
        }

        public float ExtraLineSpacing {
            get {
                return DefaultSize.ExtraLineSpacing;
            }
            set {
                DefaultSize.ExtraLineSpacing = value;
            }
        }

        public float LineSpacing {
            get {
                return DefaultSize.LineSpacing;
            }
        }

        void IGlyphSource.RegisterForChangeNotification (WeakReference<IGlyphSourceChangeListener> listener) =>
            ((IGlyphSource)DefaultSize).RegisterForChangeNotification(listener);

        int IGlyphSource.Version {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                return DefaultSize._Version;
            }
        }

        public bool IsIconFont =>
            (GetGlyphIndex('0') <= 0) && 
            (GetGlyphIndex('9') <= 0) &&
            (GetGlyphIndex('a') <= 0) && 
            (GetGlyphIndex('A') <= 0);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint GetGlyphIndex (uint codepoint) {
            if (codepoint < 256)
                return _GlyphIdForCodepointLowCache[codepoint];

            if (_GlyphIdForCodepointCache.TryGetValue(codepoint, out var glyphId))
                return glyphId;

            return GetGlyphIndex_Slow(codepoint);
        }

        private uint GetGlyphIndex_Slow (uint codepoint) {
            var glyphId = Face.GetCharIndex(codepoint);
            _GlyphIdForCodepointCache[codepoint] = glyphId;
            return glyphId;
        }

        public bool GetGlyph (uint ch, out SrGlyph glyph) {
            return DefaultSize.GetGlyph(ch, out glyph);
        }

        public void Dispose () {
            if (IsDisposed)
                return;

            IsDisposed = true;

            _CachedSize?.Dispose();

            foreach (var size in Sizes)
                size.Dispose();

            GPOS?.Dispose();
            Sizes.Clear();
            Face.Dispose();
        }
    }
}
