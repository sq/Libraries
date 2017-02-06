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
        public class FontSize : IGlyphSource, IDisposable {
            internal FreeTypeFont Font;
            internal FTSize Size;
            internal bool OwnsSize;
            internal Dictionary<char, SrGlyph> Cache = new Dictionary<char, SrGlyph>();
            internal Dictionary<FTBitmap, Texture2D> TextureCache = 
                new Dictionary<FTBitmap, Texture2D>(new ReferenceComparer<FTBitmap>());
            internal float _SizePoints;

            public FontSize (FreeTypeFont font, float sizePoints) {
                Font = font;
                Size = Font.Face.NewSize();
                OwnsSize = true;
                SizePoints = sizePoints;
            }

            internal FontSize (FreeTypeFont font, FTSize size, bool ownsSize) {
                Font = font;
                Size = size;
                OwnsSize = ownsSize;
            }

            public float SizePoints {
                get {
                    return _SizePoints;
                }
                set {
                    Size.Activate();
                    Font.Face.SetCharSize(0, value, 96, 96);
                    Invalidate();
                }
            }

            private Texture2D GetTexture (FTBitmap bitmap) {
                if ((bitmap.Width <= 0) || (bitmap.Rows <= 0))
                    return null;

                Texture2D result;
                if (TextureCache.TryGetValue(bitmap, out result))
                    return result;

                lock (Font.RenderCoordinator.CreateResourceLock)
                    TextureCache[bitmap] = result = new Texture2D(
                        Font.RenderCoordinator.Device, bitmap.Width, bitmap.Rows, false, 
                        SurfaceFormat.Color
                    );

                if (bitmap.PixelMode == PixelMode.Gray) {
                    var temp = new Color[bitmap.Width * bitmap.Rows];
                    for (var y = 0; y < bitmap.Rows; y++) {
                        for (var x = 0; x < bitmap.Width; x++) {
                            var g = bitmap.BufferData[x + (y * bitmap.Pitch)];
                            temp[x + (y * bitmap.Width)] = new Color(g, g, g, g);
                        }
                    }
                    result.SetData(temp);
                } else if (bitmap.PixelMode == PixelMode.Bgra) {
                    // FIXME
                    result.SetData(bitmap.BufferData);
                } else {
                    throw new NotImplementedException("Invalid pixel format");
                }

                return result;
            }

            public bool GetGlyph (char ch, out Glyph glyph) {
                if (Cache.TryGetValue(ch, out glyph))
                    return true;

                if ((ch == '\r') || (ch == '\n') || (ch == '\0'))
                    return false;

                Size.Activate();

                var index = Font.Face.GetCharIndex(ch);
                if (index <= 0)
                    return false;

                var flags = LoadFlags.Color | LoadFlags.Render;
                if (!Font.Hinting)
                    flags |= LoadFlags.NoHinting;

                Font.Face.LoadGlyph(
                    index, flags, LoadTarget.Normal
                );

                var ftgs = Font.Face.Glyph;
                var scaleX = Size.Metrics.ScaleX;
                var scaleY = Size.Metrics.ScaleY;
                var bitmap = ftgs.Bitmap;

                var ascender = Size.Metrics.Ascender.ToSingle();
                var tex = GetTexture(bitmap);
                var metrics = ftgs.Metrics;

                glyph = new SrGlyph {
                    Character = ch,
                    Width = metrics.Width.ToSingle(),
                    LeftSideBearing = metrics.HorizontalBearingX.ToSingle(),
                    RightSideBearing = (
                        metrics.HorizontalAdvance.ToSingle() - 
                            metrics.Width.ToSingle() -
                            metrics.HorizontalBearingX.ToSingle()
                    ),
                    XOffset = ftgs.BitmapLeft - metrics.HorizontalBearingX.ToSingle(),
                    YOffset = -ftgs.BitmapTop + ascender,
                    Texture = tex,
                    LineSpacing = Size.Metrics.Height.ToSingle()
                };

                if (tex != null)
                    glyph.BoundsInTexture = new Rectangle(0, 0, tex.Width, tex.Height);

                Cache[ch] = glyph;
                return true;
            }

            public void Invalidate () {
                foreach (var kvp in TextureCache)
                    Font.RenderCoordinator.DisposeResource(kvp.Value);

                Cache.Clear();
                TextureCache.Clear();
            }

            public void Dispose () {
                if (OwnsSize)
                    Size.Dispose();
            }
        }

        internal RenderCoordinator RenderCoordinator;
        internal Face Face;

        public FontSize DefaultSize { get; private set; }
        // FIXME: Invalidate on set
        public bool Hinting { get; set; }

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
            // DefaultSize = new FontSize(this, 12);
            DefaultSize = new FontSize(this, this.Face.Size, false);
            SizePoints = 12;
        }

        public float SizePoints {
            set {
                DefaultSize.SizePoints = value;
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
