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
        /// <summary>
        /// Contains the original dimensions of the loaded image.
        /// </summary>
        public int Width, Height;
        /// <summary>
        /// Performs color-space conversion
        /// </summary>
        public bool sRGBToLinear, sRGBFromLinear;
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

        private unsafe static void ApplyColorSpaceConversion (STB.Image img, TextureLoadOptions options) {
            if (img.IsFloatingPoint)
                throw new NotImplementedException();
            var pData = (byte*)img.Data;
            var pEnd = pData + (img.Width * img.Height * img.ChannelCount);
            var table = options.sRGBFromLinear ? ColorSpace.LinearByteTosRGBByteTable : ColorSpace.sRGBByteToLinearByteTable;
            for (; pData < pEnd; pData++)
                *pData = table[*pData];
        }

        protected override Texture2D CreateInstance (Stream stream, object data) {
            var options = (TextureLoadOptions)data ?? DefaultOptions ?? new TextureLoadOptions();
            using (var img = new STB.Image(stream, false, options.Premultiply, options.FloatingPoint)) {
                if (options.sRGBFromLinear || options.sRGBToLinear)
                    ApplyColorSpaceConversion(img, options);
                return img.CreateTexture(Coordinator, options.GenerateMips, options.PadToPowerOfTwo);
            }
        }
    }
}
