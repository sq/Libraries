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
        internal RenderCoordinator RenderCoordinator;
        internal Face Face;
        internal Dictionary<char, SrGlyph> Cache = new Dictionary<char, SrGlyph>();
        internal Dictionary<FTBitmap, Texture2D> TextureCache = 
            new Dictionary<FTBitmap, Texture2D>(new ReferenceComparer<FTBitmap>());

        public FreeTypeFont (RenderCoordinator rc, string filename, int faceIndex = 0) {
            RenderCoordinator = rc;
            Face = new Face(new Library(), filename, faceIndex);
        }

        public FreeTypeFont (RenderCoordinator rc, Stream stream, int faceIndex = 0) {
            RenderCoordinator = rc;
            var buffer = new byte[stream.Length];
            stream.Read(buffer, 0, (int)stream.Length);
            Face = new Face(new Library(), buffer, faceIndex);
        }

        public FreeTypeFont (RenderCoordinator rc, byte[] buffer, int faceIndex = 0) {
            RenderCoordinator = rc;
            Face = new Face(new Library(), buffer, faceIndex);
        }

        public float SizePoints {
            set {
                Face.SetCharSize(0, value, 96, 96);
            }
        }

        public SpriteFont SpriteFont { get { return null; } }

        private Texture2D GetTexture (FTBitmap bitmap) {
            if ((bitmap.Width <= 0) || (bitmap.Rows <= 0))
                return null;

            Texture2D result;
            if (TextureCache.TryGetValue(bitmap, out result))
                return result;

            lock (RenderCoordinator.CreateResourceLock)
                TextureCache[bitmap] = result = new Texture2D(
                    RenderCoordinator.Device, bitmap.Width, bitmap.Rows, false, 
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
                result.SetData(bitmap.BufferData);
            } else {
                throw new NotImplementedException("Invalid pixel format");
            }

            return result;
        }

        public bool GetGlyph (char ch, out SrGlyph glyph) {
            if (Cache.TryGetValue(ch, out glyph))
                return true;

            if (ch < ' ')
                return false;

            var index = Face.GetCharIndex(ch);
            if (index <= 0)
                return false;

            Face.LoadGlyph(
                index, LoadFlags.Color | LoadFlags.Render | LoadFlags.NoHinting, LoadTarget.Normal
            );
            var ftgs = Face.Glyph;
            var scaleX = Face.Size.Metrics.ScaleX;
            var scaleY = Face.Size.Metrics.ScaleY;
            var bitmap = ftgs.Bitmap;

            // using (var ftg = ftgs.GetGlyph()) {
                var ascender = Face.Size.Metrics.Ascender.ToSingle();
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
                    XOffset = ftgs.BitmapLeft,
                    YOffset = -ftgs.BitmapTop + ascender,
                    Texture = tex,
                    LineSpacing = Face.Size.Metrics.Height.ToSingle()
                };

                if (tex != null)
                    glyph.BoundsInTexture = new Rectangle(0, 0, tex.Width, tex.Height);
            // }

            Cache[ch] = glyph;
            return true;
        }

        public bool GetKerning (char left, char right, out Vector2 result) {
            result = Vector2.Zero;
            return false;
        }

        public void Dispose () {
            foreach (var kvp in TextureCache)
                RenderCoordinator.DisposeResource(kvp.Value);

            Cache.Clear();
            TextureCache.Clear();
        }
    }
}
