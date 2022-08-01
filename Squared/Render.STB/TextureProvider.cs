using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Graphics;
using Squared.Render.Resources;
using Squared.Threading;

namespace Squared.Render {
    public class TextureLoadOptions {
        public bool Premultiply = true;
        public bool FloatingPoint;
        // If the source image is more than 8 bpp, enable loading it as 16bpp
        public bool Enable16Bit;
        // If the source image is grayscale, enable loading it as grayscale
        public bool EnableGrayscale;
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
        /// <summary>
        /// The texture already contains sRGB data which should not be converted
        /// </summary>
        public bool sRGB;

        public override string ToString () {
            return "TextureLoadOptions {{ Premultiply={Premultiply}, GenerateMips={GenerateMips} }}";
        }
    }

    public class Texture2DProvider : ResourceProvider<Texture2D> {
        private OnFutureResolvedWithData DisposeHandler;

        new public TextureLoadOptions DefaultOptions {
            get {
                return (TextureLoadOptions)base.DefaultOptions;
            }
            set {
                base.DefaultOptions = value;
            }
        }

        public Texture2DProvider (Assembly assembly, RenderCoordinator coordinator) 
        : this (
            new EmbeddedResourceStreamProvider(assembly), coordinator
        ) {
        }

        public Texture2DProvider (IResourceProviderStreamSource source, RenderCoordinator coordinator) 
        : base (
            source, coordinator, 
            enableThreadedCreate: false, enableThreadedPreload: true
        ) {
            DisposeHandler = _DisposeHandler;
        }

        public Texture2D Load (string name, TextureLoadOptions options, bool cached = true, bool optional = false) {
            return base.LoadSync(name, options, cached, optional);
        }

        private void _DisposeHandler (IFuture future, object resource) {
            Coordinator.DisposeResource((IDisposable)resource);
        }

        private unsafe static void ApplyColorSpaceConversion (STB.Image img, TextureLoadOptions options) {
            if (img.IsFloatingPoint || img.Is16Bit)
                throw new NotImplementedException();
            var pData = (byte*)img.Data;
            var pEnd = pData + img.DataLength;
            var table = options.sRGBFromLinear ? ColorSpace.LinearByteTosRGBByteTable : ColorSpace.sRGBByteToLinearByteTable;
            for (; pData < pEnd; pData++)
                *pData = table[*pData];
        }

        public static STB.Image DefaultPreload (string name, Stream stream, TextureLoadOptions options) {
            var image = new STB.Image(
                stream, false, options.Premultiply, options.FloatingPoint, 
                options.Enable16Bit, options.GenerateMips, options.sRGBFromLinear || options.sRGB,
                options.EnableGrayscale
            );
            if (options.sRGBFromLinear || options.sRGBToLinear)
                ApplyColorSpaceConversion(image, options);
            return image;
        }

        protected override object PreloadInstance (string name, Stream stream, object data) {
            var options = (TextureLoadOptions)data ?? DefaultOptions ?? new TextureLoadOptions();
            return DefaultPreload(name, stream, options);
        }

        protected override Future<Texture2D> CreateInstance (string name, Stream stream, object data, object preloadedData, bool async) {
            var options = (TextureLoadOptions)data ?? DefaultOptions ?? new TextureLoadOptions();
            var img = (STB.Image)preloadedData;
            if (async) {
                var f = img.CreateTextureAsync(Coordinator, !EnableThreadedCreate, options.PadToPowerOfTwo, options.sRGBFromLinear || options.sRGB);
                f.RegisterOnComplete(DisposeHandler, img);
                return f;
            } else {
                using (img)
                    return new Future<Texture2D>(img.CreateTexture(Coordinator, options.PadToPowerOfTwo, options.sRGBFromLinear || options.sRGB));
            }
        }
    }
}
