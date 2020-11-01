using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SharpFont;
using Squared.Game;
using Squared.Render;
using Squared.Render.Text;
using Squared.Util;
using FtGlyph = SharpFont.Glyph;
using SrGlyph = Squared.Render.Text.Glyph;

namespace Squared.Render.Text {
    public class FreeTypeFont : IGlyphSource, IDisposable {
        private static EmbeddedDLLLoader DllLoader;

        static FreeTypeFont () {
            try {
                var loader = new Util.EmbeddedDLLLoader(Assembly.GetExecutingAssembly());
                loader.Load("freetype6.dll");
            } catch (Exception exc) {
                Console.Error.WriteLine("Failed to load freetype6.dll: {0}", exc.Message);
            }
        }

        public static uint BaseDPI = 96;

        public class FontSize : IGlyphSource, IDisposable {
            public const int LowCacheSize = 256;
            public const int FirstAtlasWidth = 512, FirstAtlasHeight = 512;
            // FIXME: Randomly selected value, probably too small or too big
            public static float SmallFirstAtlasThreshold = 22;
            public const int AtlasWidth = 1024, AtlasHeight = 1024;

            public bool IsDisposed { get; private set; }

            internal List<DynamicAtlas<Color>> Atlases = new List<DynamicAtlas<Color>>();
            internal FreeTypeFont Font;
            internal SrGlyph[] LowCache = new SrGlyph[LowCacheSize];
            internal Dictionary<uint, SrGlyph> Cache = new Dictionary<uint, SrGlyph>();
            internal float _SizePoints;
            internal int _Version;

            public FontSize (FreeTypeFont font, float sizePoints) {
                Font = font;
                SizePoints = sizePoints;
                Font.Sizes.Add(this);
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

            private unsafe static MipGenerator<Color> PickMipGenerator (FreeTypeFont font) {
                // TODO: Add a property that controls whether srgb is used. Or is freetype always srgb?
                return font.sRGB
                    ? (MipGenerator<Color>)MipGenerator.sRGBPAGray 
                    : MipGenerator.PAGray;
            }

            private unsafe DynamicAtlas<Color>.Reservation Upload (FTBitmap bitmap) {
                bool foundRoom = false;
                DynamicAtlas<Color>.Reservation result = default(DynamicAtlas<Color>.Reservation);

                int width = bitmap.Width, rows = bitmap.Rows, pitch = bitmap.Pitch;

                var widthW = width + (Font.GlyphMargin * 2);
                var heightW = rows + (Font.GlyphMargin * 2);

                foreach (var atlas in Atlases) {
                    if (atlas.TryReserve(widthW, heightW, out result)) {
                        foundRoom = true;
                        break;
                    }
                }

                if (!foundRoom) {
                    var isFirstAtlas = (Atlases.Count == 0) && (_SizePoints < SmallFirstAtlasThreshold);
                    // FIXME: SRGB
                    // var surfaceFormat = Font.sRGB ? SurfaceFormat.ColorSRgbEXT : SurfaceFormat.Color;
                    var surfaceFormat = SurfaceFormat.Color;
                    var newAtlas = new DynamicAtlas<Color>(
                        Font.RenderCoordinator, isFirstAtlas ? FirstAtlasWidth : AtlasWidth, isFirstAtlas ? FirstAtlasHeight : AtlasHeight, 
                        surfaceFormat, 4, Font.MipMapping ? PickMipGenerator(Font) : null
                    );
                    Atlases.Add(newAtlas);
                    if (!newAtlas.TryReserve(widthW, heightW, out result))
                        throw new InvalidOperationException("Character too large for atlas");
                }

                var pSrc = (byte*)bitmap.Buffer;

                fixed (Color* pPixels = result.Atlas.Pixels) {
                    var pDest = (byte*)pPixels;
                    switch (bitmap.PixelMode) {
                        case PixelMode.Gray:
                            var table = Font.GammaTable;
                            // FIXME: SRGB
                            // var srgb = Font.sRGB;
                            var srgb = false;

                            for (var y = 0; y < rows; y++) {
                                var rowOffset = result.Atlas.Width * (y + result.Y + Font.GlyphMargin) + (result.X + Font.GlyphMargin);
                                var pDestRow = pDest + (rowOffset * 4);
                                int yPitch = y * pitch;

                                for (var x = 0; x < width; x++) {
                                    var a = table[pSrc[x + yPitch]];
                                    var g = srgb ? MipGenerator.LinearByteToSRGBByteTable[a] : a;
                                    
                                    pDestRow[3] = a;
                                    pDestRow[2] = pDestRow[1] = pDestRow[0] = g;
                                    pDestRow += 4;
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

                return result;
            }

            private float? _LineSpacing = null;

            public float LineSpacing {
                get {
                    if (!_LineSpacing.HasValue) {
                        Font.Face.SetCharSize(
                            0, _SizePoints, 
                            BaseDPI, BaseDPI
                        );

                        _LineSpacing = Font.Face.Size.Metrics.Height.ToSingle();
                    }

                    return _LineSpacing.Value;
                }
            }

            public bool GetGlyph (uint ch, out Glyph glyph) {
                if (IsDisposed) {
                    glyph = default(Glyph);
                    return false;
                }

                if (ch < LowCacheSize) {
                    if (LowCache[ch].Texture != null) {
                        glyph = LowCache[ch];
                        return true;
                    }
                }

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

                Font.Face.SetCharSize(
                    0, _SizePoints, 
                    (uint)(BaseDPI * Font.DPIPercent / 100), (uint)(BaseDPI * Font.DPIPercent / 100)
                );

                uint index;

                if (ch == '\t')
                    index = Font.Face.GetCharIndex(' ');
                else
                    index = Font.Face.GetCharIndex(ch);

                if (index <= 0)
                    return false;

                var flags = LoadFlags.Render;
                if (!Font.EnableBitmaps)
                    flags |= LoadFlags.NoBitmap;
                if (!Font.Hinting)
                    flags |= LoadFlags.NoHinting;
                if (Font.Monochrome)
                    flags |= LoadFlags.Monochrome;
                else
                    flags |= LoadFlags.Color;

                Font.Face.LoadGlyph(
                    index, flags, Font.Monochrome ? LoadTarget.Mono : LoadTarget.Normal
                );

                var ftgs = Font.Face.Glyph;
                var scaleX = Font.Face.Size.Metrics.ScaleX;
                var scaleY = Font.Face.Size.Metrics.ScaleY;
                var bitmap = ftgs.Bitmap;

                DynamicAtlas<Color>.Reservation texRegion = default(DynamicAtlas<Color>.Reservation);
                if ((bitmap.Width > 0) && (bitmap.Rows > 0))
                    texRegion = Upload(bitmap);

                var ascender = Font.Face.Size.Metrics.Ascender.ToSingle();
                var metrics = ftgs.Metrics;
                var advance = metrics.HorizontalAdvance.ToSingle();
                if (ch == '\t')
                    advance *= Font.TabSize;

                var scaleFactor = 100f / Font.DPIPercent;

                var widthMetric = metrics.Width.ToSingle();
                var bearingXMetric = metrics.HorizontalBearingX.ToSingle();

                var rect = texRegion.Rectangle;

                glyph = new SrGlyph {
                    Character = ch,
                    Width = widthMetric,
                    LeftSideBearing = bearingXMetric,
                    RightSideBearing = (
                        advance -
                            widthMetric -
                            metrics.HorizontalBearingX.ToSingle()
                    ),
                    XOffset = ftgs.BitmapLeft - bearingXMetric - Font.GlyphMargin,
                    YOffset = -ftgs.BitmapTop + ascender - Font.GlyphMargin + Font.VerticalOffset + VerticalOffset,
                    RectInTexture = rect,
                    LineSpacing = Font.Face.Size.Metrics.Height.ToSingle(),
                    DefaultColor = nullableDefaultColor
                };

                if (texRegion.Atlas != null) {
                    glyph.Texture = texRegion.Atlas;
                    glyph.BoundsInTexture = texRegion.Atlas.BoundsFromRectangle(ref rect);
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
                return true;
            }

            public void Invalidate () {
                if (IsDisposed)
                    return;

                _Version++;

                foreach (var atlas in Atlases)
                    atlas.Clear();

                Array.Clear(LowCache, 0, LowCacheSize);
                Cache.Clear();

                _LineSpacing = null;
            }

            float IGlyphSource.DPIScaleFactor {
                get {
                    return Font.DPIPercent / 100f;
                }
            }

            int IGlyphSource.Version {
                get {
                    return _Version;
                }
            }

            public void Dispose () {
                if (IsDisposed)
                    return;

                IsDisposed = true;

                Array.Clear(LowCache, 0, LowCacheSize);
                Cache.Clear();
                foreach (var atlas in Atlases)
                    atlas.Dispose();
            }
        }

        internal RenderCoordinator RenderCoordinator;
        internal Face Face;

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
        public bool EnableBitmaps { get; set; }
        public bool Monochrome { get; set; }
        /// <summary>
        /// If set, the texture's r/g/b channels will be sRGB-encoded while the a channel will be linear
        /// </summary>
        public bool sRGB { get; set; }
        public int TabSize { get; set; }

        private double _Gamma;
        private byte[] GammaTable;
        private HashSet<FontSize> Sizes = new HashSet<FontSize>(new ReferenceComparer<FontSize>());

        public Dictionary<uint, Color> DefaultGlyphColors = new Dictionary<uint, Color>();

        public float VerticalOffset;

        public double Gamma {
            get {
                return _Gamma;
            }
            set {
                _Gamma = value;
                GammaTable = new byte[256];

                double gInv = 1.0 / value;
                for (int i = 0; i < 256; i++) {
                    if (value == 1)
                        GammaTable[i] = (byte)i;
                    else {
                        var gD = i / 255.0;
                        GammaTable[i] = (byte)(Math.Pow(gD, gInv) * 255.0);
                    }
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
            Initialize();
        }

        public FreeTypeFont (RenderCoordinator rc, Stream stream, int faceIndex = 0) {
            RenderCoordinator = rc;
            var buffer = new byte[stream.Length];
            stream.Read(buffer, 0, (int)stream.Length);
            Face = new Face(new Library(), buffer, faceIndex);
            Initialize();
        }

        public FreeTypeFont (RenderCoordinator rc, byte[] buffer, int faceIndex = 0) {
            RenderCoordinator = rc;
            Face = new Face(new Library(), buffer, faceIndex);
            Initialize();
        }

        private void Initialize () {
            Gamma = 1.0;
            DPIPercent = 100;
            MipMapping = true;
            GlyphMargin = 0;
            DefaultSize = new FontSize(this, 12);
            TabSize = 4;
            sRGB = true;

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

        public float LineSpacing {
            get {
                return DefaultSize.LineSpacing;
            }
        }

        int IGlyphSource.Version {
            get {
                return DefaultSize._Version;
            }
        }

        public bool GetGlyph (uint ch, out SrGlyph glyph) {
            return DefaultSize.GetGlyph(ch, out glyph);
        }

        public void Dispose () {
            foreach (var size in Sizes)
                size.Dispose();

            Sizes.Clear();
            Face.Dispose();
        }
    }
}
