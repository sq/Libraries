using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Graphics;
using Squared.Render.DistanceField;
using Squared.Render.Resources;
using Squared.Threading;
using Microsoft.Xna.Framework;

namespace Squared.Render {
    public class TextureLoadOptions {
        public struct ResultData {
            /// <summary>
            /// Contains the original dimensions of the loaded image. Populated after loading.
            /// </summary>
            public int Width, Height;
        }

        public bool? Premultiply;
        public bool FloatingPoint;
        // If the source image is more than 8 bpp, enable loading it as 16bpp
        public bool Enable16Bit;
        // If the source image is grayscale, enable loading it as grayscale
        public bool EnableGrayscale;
        public bool GenerateMips;
        public bool GenerateDistanceField;
        /// <summary>
        /// Pads the bottom and right edges of the image so its width and height are a power of two.
        /// The original width/height are stored in <see cref="Result"/>.
        /// </summary>
        public bool PadToPowerOfTwo;
        /// <summary>
        /// Performs color-space conversion
        /// </summary>
        public bool sRGBToLinear, sRGBFromLinear;
        /// <summary>
        /// The texture already contains sRGB data which should not be converted
        /// </summary>
        public bool sRGB;
        /// <summary>
        /// If nonzero, specifies a maximum width for the image. If the loaded image is
        ///  larger than this, it will be shrunk.
        /// The original width/height are stored in <see cref="Result"/>.
        /// </summary>
        public int MaxWidth = 0;
        /// <summary>
        /// If nonzero, specifies a maximum height for the image. If the loaded image is
        ///  larger than this, it will be shrunk.
        /// The original width/height are stored in <see cref="Result"/>.
        /// </summary>
        public int MaxHeight = 0;
        /// <summary>
        /// Contains information on the loaded image.
        /// </summary>
        public ResultData Result;

        public double ComputeScaleRatio (int width, int height) =>
            ComputeScaleRatio(width, height, MaxWidth, MaxHeight);

        public static double ComputeScaleRatio (int width, int height, int maxWidth, int maxHeight) {
            double scaleX = maxWidth > 0 ? (double)maxWidth / width : 1.0,
                scaleY = maxHeight > 0 ? (double)maxHeight / height : 1.0;
            var scaleRatio = Math.Min(scaleX, scaleY);
            if (scaleRatio > 1)
                return 1;
            else
                return scaleRatio;
        }

        public override string ToString () {
            return "TextureLoadOptions {{ Premultiply={Premultiply}, GenerateMips={GenerateMips} }}";
        }
    }

    public class Texture2DProvider : ResourceProvider<Texture2D> {
        protected class GDFTFClosure {
            public STB.Image Image;
            public TextureLoadOptions Options;
            public string Name;
        }

        private readonly ConditionalWeakTable<Texture2D, Texture2D> DistanceFields = 
            new ConditionalWeakTable<Texture2D, Texture2D>();

        private EventHandler<EventArgs> OnTextureWithDistanceFieldDisposed;
        private OnFutureResolvedWithData _DisposeHandler, _GenerateDistanceFieldThenDispose;

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
            _DisposeHandler = DisposeHandler;
            _GenerateDistanceFieldThenDispose = GenerateDistanceFieldThenDispose;
            OnTextureWithDistanceFieldDisposed = _OnTextureWithDistanceFieldDisposed;
        }

        public Texture2D Load (string name, TextureLoadOptions options, bool cached = true, bool optional = false) {
            return base.LoadSync(name, options, cached, optional);
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
                stream, false, options.Premultiply ?? true, options.FloatingPoint, 
                options.Enable16Bit, options.GenerateMips, options.sRGBFromLinear || options.sRGB,
                options.EnableGrayscale, options.MaxWidth, options.MaxHeight
            );
            if (options.sRGBFromLinear || options.sRGBToLinear)
                ApplyColorSpaceConversion(image, options);
            return image;
        }

        protected override object PreloadInstance (string name, Stream stream, object data) {
            var options = (TextureLoadOptions)data ?? DefaultOptions ?? new TextureLoadOptions();
            return DefaultPreload(name, stream, options);
        }

        protected unsafe override Future<Texture2D> CreateInstance (string name, Stream stream, object data, object preloadedData, bool async) {
            var options = (TextureLoadOptions)data ?? DefaultOptions ?? new TextureLoadOptions();
            var img = (STB.Image)preloadedData;
            if (async) {
                var f = img.CreateTextureAsync(Coordinator, !EnableThreadedCreate, options.PadToPowerOfTwo, options.sRGBFromLinear || options.sRGB, name: name);
                if (options.GenerateDistanceField)
                    f.RegisterOnComplete(_GenerateDistanceFieldThenDispose, new GDFTFClosure { Image = img, Options = options, Name = name });
                else
                    f.RegisterOnComplete(_DisposeHandler, img);
                return f;
            } else {
                var result = new Future<Texture2D>(img.CreateTexture(Coordinator, options.PadToPowerOfTwo, options.sRGBFromLinear || options.sRGB, name: name));
                if (options.GenerateDistanceField)
                    GenerateDistanceFieldThenDispose(result, new GDFTFClosure { Image = img, Options = options, Name = name });
                else
                    DisposeHandler(result, img);
                return result;
            }
        }

        protected virtual void DisposeHandler (IFuture future, object resource) {
            if (resource is GDFTFClosure c)
                Coordinator.DisposeResource(c.Image);
            else
                Coordinator.DisposeResource(resource as IDisposable);
        }

        protected virtual void OnCacheMiss (CacheKey lhs, CacheKey rhs) {
            System.Diagnostics.Debug.WriteLine($"Texture cache miss for '{lhs.Name}' due to options");
        }

        protected override bool AreKeysEqual (ref CacheKey lhs, ref CacheKey rhs) {
            if (!base.AreKeysEqual(ref lhs, ref rhs))
                return false;

            var oLhs = lhs.Data as TextureLoadOptions;
            var oRhs = rhs.Data as TextureLoadOptions;
            var result = (oLhs?.GenerateMips == oRhs?.GenerateMips) && (oLhs?.GenerateDistanceField == oRhs?.GenerateDistanceField) &&
                (oLhs?.Premultiply == oRhs?.Premultiply) && (oLhs?.PadToPowerOfTwo == oRhs?.PadToPowerOfTwo) &&
                (oLhs?.FloatingPoint == oRhs?.FloatingPoint);
            if (!result)
                OnCacheMiss(lhs, rhs);
            return result;
        }

        protected unsafe virtual void GenerateDistanceFieldThenDispose (IFuture f, object resource) {
            var closure = (GDFTFClosure)resource;
            var img = closure.Image;
            var options = closure.Options;
            // FIXME: Optimize this
            float[] buf;
            var config = new JumpFloodConfig {
                Width = img.Width,
                Height = img.Height,
                ThreadGroup = Coordinator.ThreadGroup
            };
            var format = img.GetFormat(options.sRGB | options.sRGBFromLinear, img.ChannelCount);
            switch (format) {
                case SurfaceFormat.Alpha8:
                    buf = JumpFlood.GenerateDistanceField((byte*)img.Data, config);
                    break;
                case SurfaceFormat.Color:
                case SurfaceFormat.ColorBgraEXT:
                case SurfaceFormat.ColorSrgbEXT:
                    buf = JumpFlood.GenerateDistanceField((Color*)img.Data, config);
                    break;
                case SurfaceFormat.Single:
                    buf = JumpFlood.GenerateDistanceField((float*)img.Data, config);
                    break;
                case SurfaceFormat.Vector4:
                    buf = JumpFlood.GenerateDistanceField((Vector4*)img.Data, config);
                    break;
                default:
                    throw new Exception($"Pixel format {format} not supported by distance field generator");
            }

            Texture2D df;
            lock (Coordinator.CreateResourceLock)
                df = new Texture2D(Coordinator.Device, img.Width, img.Height, false, SurfaceFormat.Single) {
                    Name = closure.Name,
                };

            lock (Coordinator.UseResourceLock)
                df.SetData(buf);

            lock (DistanceFields)
                DistanceFields.Add((Texture2D)f.Result, df);

            DisposeHandler(f, img);
        }

        public void DisposeDistanceField (Texture2D texture) {
            lock (DistanceFields) {
                if (!DistanceFields.TryGetValue(texture, out var df))
                    return;

                DistanceFields.Remove(texture);
                Coordinator.DisposeResource(df);
            }
        }

        public Texture2D GetDistanceField (Texture2D texture) {
            if (texture == null)
                return null;

            lock (DistanceFields) {
                if (!DistanceFields.TryGetValue(texture, out var result))
                    return null;
                else
                    return result;
            }
        }

        public void SetDistanceField (Texture2D texture, Texture2D distanceField) {
            texture.Disposing += OnTextureWithDistanceFieldDisposed;
            lock (DistanceFields)
                DistanceFields.Add(texture, distanceField);
        }

        private void _OnTextureWithDistanceFieldDisposed (object sender, EventArgs e) {
            if (sender is Texture2D tex)
                DisposeDistanceField(tex);
        }

        public override void AddAllInstancesTo (ICollection<Texture2D> result) {
            lock (FirstLevelCache)
            foreach (var entry in Entries()) {
                if (!entry.Future.GetResult(out var tex))
                    continue;
                if (tex?.IsDisposed == false)
                    result.Add(tex);

                var df = GetDistanceField(tex);
                if (df?.IsDisposed == false)
                    result.Add(df);
            }
        }
    }
}
