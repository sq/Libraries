using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SharpFont;
using Squared.Game;
using Squared.Render;
using Squared.Render.Mips;
using Squared.Render.Text;
using Squared.Util;
using FtGlyph = SharpFont.Glyph;
using SrGlyph = Squared.Render.Text.Glyph;

namespace Squared.Render.Text {
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

        public class FontSize : IGlyphSource, IKerningProvider, IDisposable {
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
            internal SrGlyph[] LowCache = new SrGlyph[LowCacheSize];
            internal Dictionary<uint, SrGlyph> Cache = new Dictionary<uint, SrGlyph>();
            internal float _SizePoints;
            internal int _Version;
            internal bool? _SDF;

            internal DenseList<WeakReference<IGlyphSourceChangeListener>> ChangeListeners;
            void IGlyphSource.RegisterForChangeNotification (WeakReference<IGlyphSourceChangeListener> listener) {
                if (!ChangeListeners.Contains(listener))
                    ChangeListeners.Add(listener);
            }

            public bool? SDF {
                get => _SDF;
                set {
                    if (_SDF == value)
                        return;

                    _SDF = value;
                    Invalidate();
                }
            }

            public bool IsDisposed { get; private set; }

            public bool ContainsColorGlyphs {
                get => _ContainsColorGlyphs;
                set {
                    if (_ContainsColorGlyphs == value)
                        return;

                    _ContainsColorGlyphs = value;

                    foreach (var atlas in Atlases)
                        if (atlas is DynamicAtlas<Color> dac)
                            dac.MipGenerator = PickMipGenerator(Font, value);
                }
            }

            private bool _ContainsColorGlyphs;

            object IGlyphSource.UniqueKey => this;

            public FontSize (FreeTypeFont font, float sizePoints) {
                Font = font;
                SizePoints = sizePoints;
                Font.Sizes.Add(this);
            }

            public GlyphPixelAlignment? DefaultAlignment { get; set; } = GlyphPixelAlignment.None;

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

            internal bool ActualSDF => SDF ?? Font.SDF;

            private unsafe static MipGeneratorFn PickMipGenerator (FreeTypeFont font, bool rgba) {
                if (rgba)
                    return MipGenerator.Get(MipFormat.pRGBA);

                // TODO: Add a property that controls whether srgb is used. Or is freetype always srgb?
                return font.sRGB
                    // FIXME: Use a sRGB gamma ramp for this?
                    ? MipGenerator.Get(MipFormat.pGray4 | MipFormat.sRGB)
                    : font.MipGen?.Get(MipFormat.pGray4) ?? MipGenerator.Get(MipFormat.pGray4);
            }

            private unsafe DynamicAtlasReservation Upload (FTBitmap bitmap) {
                bool foundRoom = false;
                DynamicAtlasReservation result = default;
                if (bitmap.Buffer == IntPtr.Zero)
                    throw new NullReferenceException("bitmap.Buffer");

                int width = bitmap.Width, rows = bitmap.Rows, pitch = bitmap.Pitch;
                if (bitmap.PixelMode == PixelMode.Bgra)
                    ContainsColorGlyphs = true;

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

                var surfaceFormat = ActualSDF 
                    ? SurfaceFormat.Single // FIXME SurfaceFormat.HalfSingle
                    : (Font.sRGB ? Evil.TextureUtils.ColorSrgbEXT : SurfaceFormat.Color);

                if (!foundRoom) {
                    bool isFirstAtlas = (Atlases.Count == 0) && ((_SizePoints * Font.DPIPercent / 100) <= SkipFirstAtlasThreshold),
                        isLargeAtlas = (_SizePoints * Font.DPIPercent / 100) >= LargeAtlasThreshold;
                    int newAtlasWidth = isFirstAtlas ? FirstAtlasWidth : AtlasWidth,
                        newAtlasHeight = isFirstAtlas ? FirstAtlasHeight : AtlasHeight;
                    if (isLargeAtlas) {
                        newAtlasWidth *= 2;
                        newAtlasHeight *= 2;
                    }

                    IDynamicAtlas newAtlas = ActualSDF
                        ? (IDynamicAtlas)(new DynamicAtlas<float>(
                            Font.RenderCoordinator, newAtlasWidth, newAtlasHeight,
                            surfaceFormat, spacing, Font.SDFMipMapping ? MipGenerator.Get(MipFormat.Single) : null, tag: $"{Font.Face.FamilyName} {SizePoints}pt #{Atlases.Count + 1}"
                        ) { ClearValue = 1024f })
                        : new DynamicAtlas<Color>(
                            Font.RenderCoordinator, newAtlasWidth, newAtlasHeight,
                            surfaceFormat, spacing, Font.MipMapping ? PickMipGenerator(Font, ContainsColorGlyphs) : null, tag: $"{Font.Face.FamilyName} {SizePoints}pt #{Atlases.Count + 1}"
                        );
                    Atlases.Add(newAtlas);
                    if (!newAtlas.TryReserve(widthW, heightW, out result))
                        throw new InvalidOperationException("Character too large for atlas");
                }

                var pSrc = (byte*)bitmap.Buffer;

                if (ActualSDF) {
                    fixed (float* pPixels = result.GetPixels<float>()) {
                        switch (bitmap.PixelMode) {
                            case PixelMode.Gray:
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
                                break;

                            default:
                                throw new NotImplementedException("Unsupported pixel mode: " + bitmap.PixelMode);
                        }
                    }
                } else {
                    var srgb = (surfaceFormat != SurfaceFormat.Color);
                    var table = Font.GammaRamp?.GammaTable;
                    fixed (Color* pPixels = result.GetPixels<Color>()) {

                        var pDest = (byte*)pPixels;
                        switch (bitmap.PixelMode) {
                            case PixelMode.Bgra:
                                for (var y = 0; y < rows; y++) {
                                    var rowOffset = result.Atlas.Width * (y + result.Y + Font.GlyphMargin) + (result.X + Font.GlyphMargin);
                                    var pDestRow = pDest + (rowOffset * 4);
                                    int yPitch = y * pitch;

                                    // FIXME: Implement gamma table somehow? Because the glyphs are already premultiplied, I'm not sure how
                                    // FIXME: SRGB
                                    for (var x = 0; x < width; x++) {
                                        var ppSrc = pSrc + (x * 4) + yPitch;
                                    
                                        pDestRow[3] = ppSrc[3];
                                        pDestRow[2] = ppSrc[0];
                                        pDestRow[1] = ppSrc[1];
                                        pDestRow[0] = ppSrc[2];
                                        pDestRow += 4;
                                    }
                                }
                                break;

                            case PixelMode.Gray:
                                for (var y = 0; y < rows; y++) {
                                    var rowOffset = result.Atlas.Width * (y + result.Y + Font.GlyphMargin) + (result.X + Font.GlyphMargin);
                                    var pDestRow = pDest + (rowOffset * 4);
                                    int yPitch = y * pitch;

                                    if (ActualSDF) {
                                        for (var x = 0; x < width; x++) {
                                            var a = pSrc[x + yPitch];
                                    
                                            pDestRow[0] = pDestRow[1] = pDestRow[2] = pDestRow[3] = a;
                                            pDestRow += 4;
                                        }
                                    } else {
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
                                break;

                            case PixelMode.Mono:
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
                                break;

                            default:
                                throw new NotImplementedException("Unsupported pixel mode: " + bitmap.PixelMode);
                        }
                    }
                }

                result.Invalidate();
                return result;
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

            private void ApplyWidthNormalization (bool normalizeNumberWidths) {
                NeedNormalization = false;
                Color? nullableDefaultColor = null;
                Color defaultColor;
                SrGlyph temp;
                float maxWidth = -1;

                for (uint i = FirstDigit; i <= LastDigit; i++) {
                    if (LowCache[i].Texture.IsInitialized)
                        continue;
                    if (Font.DefaultGlyphColors.TryGetValue(i, out defaultColor))
                        nullableDefaultColor = defaultColor;
                    PopulateGlyphCache(i, out temp, nullableDefaultColor);
                    maxWidth = Math.Max(maxWidth, temp.WidthIncludingBearing);
                }

                maxWidth = (float)Math.Round(maxWidth, 0);
                if (maxWidth < 0)
                    return;

                {
                    // Figure space (specified to be the same size as a number)
                    PopulateGlyphCache(0x2007, out var figureSpace, null);
                    var padding = maxWidth - figureSpace.WidthIncludingBearing;
                    figureSpace.LeftSideBearing += (padding / 2f);
                    figureSpace.RightSideBearing += (padding / 2f);
                    Cache[0x2007] = figureSpace;
                }

                if (normalizeNumberWidths) {
                    for (uint i = FirstDigit; i <= LastDigit; i++) {
                        var padding = maxWidth - LowCache[i].WidthIncludingBearing;
                        LowCache[i].LeftSideBearing += padding / 2f;
                        LowCache[i].RightSideBearing += padding / 2f;
                    }
                }
            }

            public bool GetGlyph (uint ch, out Glyph glyph) {
                if (IsDisposed) {
                    glyph = default(Glyph);
                    return false;
                }

                if (ch < LowCacheSize) {
                    if (LowCache[ch].Texture.IsInitialized) {
                        if (NeedNormalization)
                            ApplyWidthNormalization(Font.EqualizeNumberWidths);

                        glyph = LowCache[ch];
                        return true;
                    }
                } else if ((ch == 0x2007) && NeedNormalization)
                    ApplyWidthNormalization(Font.EqualizeNumberWidths);

                Color? nullableDefaultColor = null;
                Color defaultColor;
                if (Font.DefaultGlyphColors.TryGetValue(ch, out defaultColor))
                    nullableDefaultColor = defaultColor;
                
                if (Cache.TryGetValue(ch, out glyph)) {
                    glyph.DefaultColor = nullableDefaultColor;
                    return true;
                }

                if ((ch == '\r') || (ch == '\n') || (ch == '\0'))
                    return false;

                return PopulateGlyphCache(ch, out glyph, nullableDefaultColor);
            }

            private bool PopulateGlyphCache (uint ch, out Glyph glyph, Color? defaultColor) {
                var resolution = (uint)(BaseDPI * Font.DPIPercent / 100);
                var size = Font.GetFTSize(_SizePoints, resolution);

                uint index;

                if (ch == '\t')
                    index = Font.Face.GetCharIndex(' ');
                else
                    index = Font.Face.GetCharIndex(ch);

                if (index <= 0) {
                    glyph = default(Glyph);
                    return false;
                }

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

                Font.Face.RenderGlyphEXT(ActualSDF ? RenderMode.VerticalLcd + 1 : RenderMode.Normal);
                var ftgs = Font.Face.Glyph;

                var bitmap = ftgs.Bitmap;

                DynamicAtlasReservation texRegion = default;
                if ((bitmap.Width > 0) && (bitmap.Rows > 0))
                    texRegion = Upload(bitmap);

                var ascender = sizeMetrics.Ascender.ToSingle();
                var glyphMetrics = ftgs.Metrics;
                var advance = glyphMetrics.HorizontalAdvance.ToSingle();
                if (ch == '\t')
                    advance *= Font.TabSize;

                var scaleFactor = 100f / Font.DPIPercent;

                var widthMetric = glyphMetrics.Width.ToSingle();
                var bearingXMetric = glyphMetrics.HorizontalBearingX.ToSingle();

                var rect = texRegion.Rectangle;
                var isNumber = (ch >= '0' && ch <= '9');

                glyph = new SrGlyph {
                    KerningProvider = (Font.EnableKerning && (Font.GPOS != null) && (!isNumber || !Font.EqualizeNumberWidths)) 
                        ? this 
                        : null,
                    GlyphId = index,
                    Character = ch,
                    Width = widthMetric,
                    LeftSideBearing = bearingXMetric,
                    RightSideBearing = (
                        advance -
                            widthMetric -
                            glyphMetrics.HorizontalBearingX.ToSingle()
                    ),
                    XOffset = ftgs.BitmapLeft - bearingXMetric - Font.GlyphMargin,
                    YOffset = -ftgs.BitmapTop + ascender - Font.GlyphMargin + Font.VerticalOffset + VerticalOffset,
                    // FIXME: This will become invalid if the extra spacing changes
                    // FIXME: Scale the spacing appropriately based on ratios
                    LineSpacing = sizeMetrics.Height.ToSingle() + ExtraLineSpacing,
                    DefaultColor = defaultColor,
                    Baseline = sizeMetrics.Ascender.ToSingle()
                };

                if (texRegion.Atlas != null) {
                    glyph.Texture = new AbstractTextureReference(texRegion.Atlas);
                    glyph.BoundsInTexture = texRegion.Atlas.BoundsFromRectangle(in rect);
                }

                // HACK
                if (ch <= 0xCFFF) {
                    // Some fonts have weirdly-sized space characters
                    if (char.IsWhiteSpace((char)ch))
                        glyph.RightSideBearing = (float)Math.Round(glyph.RightSideBearing);
                }

                if (ch < LowCacheSize)
                    LowCache[ch] = glyph;
                Cache[ch] = glyph;

                if (NeedNormalization)
                    ApplyWidthNormalization(Font.EqualizeNumberWidths);

                return true;
            }

            public void Invalidate () {
                if (IsDisposed)
                    return;

                _Version++;
                NeedNormalization = true;

                foreach (var atlas in Atlases)
                    atlas.Clear();

                Array.Clear(LowCache, 0, LowCacheSize);
                Cache.Clear();

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
                Array.Clear(LowCache, 0, LowCacheSize);
                Cache.Clear();
                foreach (var atlas in Atlases)
                    atlas.Dispose();
                Atlases.Clear();
            }

            bool IKerningProvider.TryGetKerning (uint glyphId, uint nextGlyphId, ref KerningData thisGlyph, ref KerningData nextGlyph) {
                bool found = false;
                ValueRecord value1 = default, value2 = default;
                foreach (var table in Font.GPOS.Lookups) {
                    if (table.TryGetValue((int)glyphId, (int)nextGlyphId, out value1, out value2)) {
                        found = true;
                        break;
                    }
                }

                if (found) {
                    float scaleX = _CachedMetrics.ScaleX.ToSingle() / 64f,
                        scaleY = _CachedMetrics.ScaleY.ToSingle() / 64f;
                    thisGlyph.RightSideBearing = value1.XAdvance * scaleX;
                    thisGlyph.XOffset = value1.XPlacement * scaleX;
                    thisGlyph.YOffset = value1.YPlacement * scaleY;
                    nextGlyph.RightSideBearing = value2.XAdvance * scaleX;
                    nextGlyph.XOffset = value2.XPlacement * scaleX;
                    nextGlyph.YOffset = value2.YPlacement * scaleY;
                }

                return found;
            }
        }

        private float _CachedSizePoints;
        private uint _CachedResolution;
        private Face _CachedFace;
        private FTSize _CachedSize;

        private FTSize GetFTSize (float sizePoints, uint resolution) {
            if (
                (_CachedSizePoints != sizePoints) || 
                (_CachedResolution != resolution) ||
                (_CachedFace != Face)
            ) {
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

        public GlyphPixelAlignment? DefaultAlignment {
            get => DefaultSize.DefaultAlignment;
            set => DefaultSize.DefaultAlignment = value;
        }

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
        /// <summary>
        /// If set, the texture's r/g/b channels will be sRGB-encoded while the a channel will be linear
        /// </summary>
        private bool _sRGB;
        public bool sRGB {
            get => _sRGB;
            set {
                if (Evil.TextureUtils.ColorSrgbEXT != SurfaceFormat.Color)
                    _sRGB = value;
                else
                    _sRGB = false;
            }
        }
        public bool SDF;
        public int TabSize { get; set; }
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

        private bool _EnableKerning, _EqualizeNumberWidths;
        private double _Gamma;
        private GammaRamp GammaRamp;
        private MipGenerator.WithGammaRamp MipGen;
        private HashSet<FontSize> Sizes = new HashSet<FontSize>(new ReferenceComparer<FontSize>());

        public Dictionary<uint, Color> DefaultGlyphColors = new Dictionary<uint, Color>();

        public float VerticalOffset;
        public GPOSTable GPOS {
            get; private set;
        } 

        public double Gamma {
            get {
                return _Gamma;
            }
            set {
                _Gamma = value;
                if (value == 1) {
                    GammaRamp = null;
                    MipGen = null;
                } else {
                    GammaRamp = new GammaRamp(value);
                    MipGen = new MipGenerator.WithGammaRamp(GammaRamp);
                }
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

        public FreeTypeFont (RenderCoordinator rc, Stream stream, int faceIndex = 0) {
            RenderCoordinator = rc;
            var buffer = new byte[stream.Length];
            stream.Read(buffer, 0, (int)stream.Length);
            Face = new Face(new Library(), buffer, faceIndex);
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
            var err = FT.FT_OpenType_Validate(Face.Handle, OpenTypeValidationFlags.Gpos, out _, out _, out var gposTable, out _, out _);
            if ((err == 0) && (gposTable != default)) {
                GPOS = new GPOSTable(this, gposTable);
                ;
            } else {
                GPOS = null;
            }
        }

        private void Initialize () {
            Gamma = 1.0;
            DPIPercent = 100;
            MipMapping = true;
            GlyphMargin = 0;
            DefaultSize = new FontSize(this, 12);
            TabSize = 4;
            sRGB = false;

            if (Face.GlyphCount <= 0)
                throw new Exception("Loaded font contains no glyphs or is corrupt.");

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

        public bool GetGlyph (uint ch, out SrGlyph glyph) {
            return DefaultSize.GetGlyph(ch, out glyph);
        }

        public void Dispose () {
            if (IsDisposed)
                return;

            IsDisposed = true;

            foreach (var size in Sizes)
                size.Dispose();

            GPOS?.Dispose();
            Sizes.Clear();
            Face.Dispose();
        }
    }
}
