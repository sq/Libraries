using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SharpFont;
using Squared.Render;
using Squared.Render.Text;
using Squared.Util;
using FtGlyph = SharpFont.Glyph;
using SrGlyph = Squared.Render.Text.Glyph;

namespace Squared.Render.Text {
    public class FreeTypeFont : IGlyphSource, IDisposable {
        public static uint BaseDPI = 96;

        public class FontSize : IGlyphSource, IDisposable {
            public const int AtlasWidth = 1024, AtlasHeight = 1024;

            internal List<DynamicAtlas<Color>> Atlases = new List<DynamicAtlas<Color>>();
            internal FreeTypeFont Font;
            internal Dictionary<char, SrGlyph> Cache = new Dictionary<char, SrGlyph>();
            internal float _SizePoints;
            internal int _Version;

            public FontSize (FreeTypeFont font, float sizePoints) {
                Font = font;
                SizePoints = sizePoints;
            }

            public float SizePoints {
                get {
                    return _SizePoints;
                }
                set {
                    _SizePoints = value;
                    Invalidate();
                }
            }

            private unsafe DynamicAtlas<Color>.Reservation Upload (FTBitmap bitmap) {
                bool foundRoom = false;
                DynamicAtlas<Color>.Reservation result = default(DynamicAtlas<Color>.Reservation);

                var widthW = bitmap.Width + (Font.GlyphMargin * 2);
                var heightW = bitmap.Rows + (Font.GlyphMargin * 2);

                foreach (var atlas in Atlases) {
                    if (atlas.TryReserve(widthW, heightW, out result)) {
                        foundRoom = true;
                        break;
                    }
                }

                if (!foundRoom) {
                    var newAtlas = new DynamicAtlas<Color>(
                        Font.RenderCoordinator, AtlasWidth, AtlasHeight, 
                        SurfaceFormat.Color, 4, Font.MipMapping ? (MipGenerator<Color>)MipGenerator.Color : null
                    );
                    Atlases.Add(newAtlas);
                    if (!newAtlas.TryReserve(widthW, heightW, out result))
                        throw new InvalidOperationException("Character too large for atlas");
                }

                var pixels = result.Atlas.Pixels;
                var pSrc = (byte*)bitmap.Buffer;

                for (var y = 0; y < bitmap.Rows; y++) {
                    var rowOffset = result.Atlas.Width * (y + result.Y + Font.GlyphMargin) + (result.X + Font.GlyphMargin);

                    for (var x = 0; x < bitmap.Width; x++) {
                        var g = pSrc[x + (y * bitmap.Pitch)];
                        pixels[rowOffset + x] = new Color(g, g, g, g);
                    }
                }

                return result;
            }

            public float LineSpacing {
                get {
                    Font.Face.SetCharSize(
                        0, _SizePoints, 
                        BaseDPI, BaseDPI
                    );

                    return Font.Face.Size.Metrics.Height.ToSingle();
                }
            }

            public bool GetGlyph (char ch, out Glyph glyph) {
                if (Cache.TryGetValue(ch, out glyph))
                    return true;

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

                var flags = LoadFlags.Color | LoadFlags.Render;
                if (!Font.Hinting)
                    flags |= LoadFlags.NoHinting;

                Font.Face.LoadGlyph(
                    index, flags, LoadTarget.Normal
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

                glyph = new SrGlyph {
                    Character = ch,
                    Width = metrics.Width.ToSingle(),
                    LeftSideBearing = metrics.HorizontalBearingX.ToSingle(),
                    RightSideBearing = (
                        advance -
                            metrics.Width.ToSingle() -
                            metrics.HorizontalBearingX.ToSingle()
                    ),
                    XOffset = ftgs.BitmapLeft - metrics.HorizontalBearingX.ToSingle() - Font.GlyphMargin,
                    YOffset = -ftgs.BitmapTop + ascender - Font.GlyphMargin,
                    Texture = texRegion.Texture,
                    BoundsInTexture = texRegion.Rectangle,
                    LineSpacing = Font.Face.Size.Metrics.Height.ToSingle()
                };

                Cache[ch] = glyph;
                return true;
            }

            public void Invalidate () {
                _Version++;

                foreach (var atlas in Atlases)
                    atlas.Clear();

                Cache.Clear();
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
            }
        }

        internal RenderCoordinator RenderCoordinator;
        internal Face Face;

        public FontSize DefaultSize { get; private set; }
        // FIXME: Invalidate on set
        public int GlyphMargin { get; set; }
        public int DPIPercent { get; set; }
        public bool Hinting { get; set; }
        public bool MipMapping { get; set; }
        public int TabSize { get; set; }

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
            DPIPercent = 100;
            MipMapping = true;
            GlyphMargin = 0;
            DefaultSize = new FontSize(this, 12);
            TabSize = 4;
        }

        public void Invalidate () {
            DefaultSize.Invalidate();
            // TODO: Invalidate other sizes
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

        public bool GetGlyph (char ch, out SrGlyph glyph) {
            return DefaultSize.GetGlyph(ch, out glyph);
        }

        public void Dispose () {
            Face.Dispose();
        }
    }
}
