using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Graphics;

namespace Squared.Render {
    public class TextureLoadOptions {
        public bool Premultiply = true;
        public bool FloatingPoint;
        public bool GenerateMips;
        /// <summary>
        /// Pads the bottom and right edges of the image so its width and height are a power of two.
        /// The original width/height are stored in properties of this object.
        /// </summary>
        public bool PadToPowerOfTwo;
        public UInt32[] Palette;
        public int PaletteTextureHeight = 1;

        /// <summary>
        /// Contains the palette of the loaded image.
        /// </summary>
        public Texture2D PaletteTexture;
        /// <summary>
        /// Contains the original dimensions of the loaded image.
        /// </summary>
        public int Width, Height;
    }

    public class EmbeddedTexture2DProvider : EmbeddedResourceProvider<Texture2D> {
        new public TextureLoadOptions DefaultOptions {
            get {
                return (TextureLoadOptions)base.DefaultOptions;
            }
            set {
                base.DefaultOptions = value;
            }
        }

        public EmbeddedTexture2DProvider (Assembly assembly, RenderCoordinator coordinator) 
            : base(assembly, coordinator) {
        }

        public EmbeddedTexture2DProvider (RenderCoordinator coordinator) 
            : base(Assembly.GetCallingAssembly(), coordinator) {
        }

        public Texture2D Load (string name, TextureLoadOptions options, bool cached = true, bool optional = false) {
            return base.Load(name, options, cached, optional);
        }

        protected override Texture2D CreateInstance (Stream stream, object data) {
            var options = (TextureLoadOptions)data ?? DefaultOptions ?? new TextureLoadOptions();
            if (options.Palette != null && options.GenerateMips)
                throw new ArgumentException("Cannot generate mips for a paletted image");
            using (var img = new STB.Image(stream, false, options.Premultiply, options.FloatingPoint, options.Palette)) {
                if (options.Palette != null)
                    options.PaletteTexture = img.CreatePaletteTexture(Coordinator, options.PaletteTextureHeight);

                return img.CreateTexture(Coordinator, options.GenerateMips, options.PadToPowerOfTwo);
            }
        }
    }
}
