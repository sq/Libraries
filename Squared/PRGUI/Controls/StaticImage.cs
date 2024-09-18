using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Game;
using Squared.PRGUI.Controls.SpecialInterfaces;
using Squared.PRGUI.Decorations;
using Squared.PRGUI.Layout;
using Squared.PRGUI.NewEngine;
using Squared.Render;
using Squared.Render.Convenience;
using Squared.Threading;
using Squared.Util;

namespace Squared.PRGUI.Controls.SpecialInterfaces {
    public interface IHasScale {
        public float Scale { get; set; }
    }    
    public interface IHasScaleToFit {
        public bool ScaleToFit { get; set; }
    }
}

namespace Squared.PRGUI.Controls {
    [Flags]
    public enum ImageDimensions {
        None = 0,
        X = 1,
        Y = 2
    }

    public enum StaticImageCompositeMode {
        /// <summary>
        /// Render Image1 on top of Image2
        /// </summary>
        Over,
        /// <summary>
        /// Render Image1 under Image2
        /// </summary>
        Under,
        /// <summary>
        /// Cross-fade between Image and Image2
        /// </summary>
        Crossfade,
        /// <summary>
        /// Image2 painted into Image1 (porter-duff atop)
        /// </summary>
        Atop,
        /// <summary>
        /// Image1 using Image2 as an rgba mask
        /// </summary>
        Masked,
        /// <summary>
        /// A custom material is set, so suppress automated behavior
        /// </summary>
        CustomMaterial
    }

    public class StaticImage : 
        Control, IPostLayoutListener, 
        IHasScale, IHasScaleToFit
    {
        public Vector2 Alignment = new Vector2(0.5f, 0.5f);
        public float Scale { get; set; } = 1.0f;
        public RasterizePasses Pass = RasterizePasses.Content;
        /// <summary>
        /// The auto-selected scale ratio will never be below this value.
        /// </summary>
        public float MinimumScale = 0f;
        /// <summary>
        /// The auto-selected scale ratio will never exceed this value.
        /// </summary>
        public float MaximumScale = 1f;
        public bool ScaleToFitX = true;
        public bool ScaleToFitY = true;
        /// <summary>
        /// If set, the displayed image will shrink or expand (if possible) to fill the entire
        ///  control, preserving aspect ratio.
        /// </summary>
        public bool ScaleToFit {
            get => ScaleToFitX && ScaleToFitY;
            set => ScaleToFitX = ScaleToFitY = value;
        }
        /// <summary>
        /// If set, the control will be auto-sized to fit the image on this axis.
        /// </summary>
        public LayoutDimensions? AutoSizeAxis = null;

        bool AreRecentRectsValid;
        RectF MostRecentContentRect;
        Vector2 MostRecentAvailableSpace;

        private AbstractTextureReference _Image;
        public AbstractTextureReference Image {
            get => _Image;
            set {
                if (_Image == value)
                    return;
                _Image = value;
                if (!value.IsDisposedOrNull)
                    _ShowLoadingSpinner = false;
                InvalidateAutoSize();
            }
        }

        private AbstractTextureReference _Image2;
        public AbstractTextureReference Image2 {
            get => _Image2;
            set {
                if (_Image2 == value)
                    return;
                _Image2 = value;
                InvalidateAutoSize();
            }
        }

        public Tween<float> Image2Opacity = 1f;

        public StaticImageCompositeMode Image2Mode = StaticImageCompositeMode.Over;

        private bool _ShowLoadingSpinner;
        public bool ShowLoadingSpinner {
            get => _ShowLoadingSpinner && _Image.IsDisposedOrNull;
            set => _ShowLoadingSpinner = value;
        }

        public ColorVariable MultiplyColor, AddColor;
        public MaterialParameterValues MaterialParameters;
        public Vector4 RasterizerUserData;
        public Material Material;
        public BlendState BlendState;
        /// <summary>
        /// Creates padding around the image being drawn for shadows and other shader effects
        /// </summary>
        public int DrawExpansion = 0;

        public StaticImage ()
            : base () {
        }

        protected override void GetMaterialAndBlendStateForCompositing (out Material material, out BlendState blendState) {
            if (!Image2.IsDisposedOrNull) {
                material = null;
                blendState = null;
                return;
            }
            if (Appearance.Compositor == null)
                material = Material;
            else
                material = null;
            blendState = BlendState ?? Context.PickDefaultBlendState(Image.Instance ?? Image2.Instance);
        }

        protected override bool NeedsComposition (bool hasOpacity, bool hasTransform) {
            if ((Material != null) && (Appearance.Compositor != null))
                return true;

            if (Appearance.BackgroundColor.IsTransparent)
                hasOpacity = hasTransform = false;

            return base.NeedsComposition(hasOpacity, hasTransform);
        }

        private void InvalidateAutoSize () {
            AreRecentRectsValid = false;
        }

        protected override IDecorator GetDefaultDecorator (IDecorationProvider provider) {
            return provider?.StaticImage ?? provider?.None;
        }

        private float ComputeDisplayScaleRatio (Texture2D instance, float availableWidth, float availableHeight) {
            // HACK
            if (instance == null)
                return Scale;

            var widthScale = ScaleToFitX
                ? availableWidth / instance.Width
                : (float?)null;
            var heightScale = ScaleToFitY
                ? availableHeight / instance.Height
                : (float?)null;

            return Arithmetic.Clamp(
                // FIXME: Move this
                (ControlDimension.Min(widthScale, heightScale) ?? Scale),
                MinimumScale,
                MaximumScale
            );
        }

        void IPostLayoutListener.OnLayoutComplete (ref UIOperationContext context, ref bool relayoutRequested) {
            if (IsLayoutInvalid || ((_Image.Instance ?? _Image2.Instance) == null)) {
                AreRecentRectsValid = false;
                return;
            }

            if (!AutoSizeAxis.HasValue)
                return;

            var result = Context.Engine.Result(LayoutKey);
            var scale = AutoSizeAxis == LayoutDimensions.X ? new Vector2(1, 0) : new Vector2(0, 1);
            if (!AreRecentRectsValid)
                relayoutRequested = true;
            AreRecentRectsValid = true;
            if ((result.ContentRect.Size * scale) != (MostRecentContentRect.Size * scale))
                relayoutRequested = true;
            MostRecentContentRect = result.ContentRect;
            if ((result.AvailableSpace * scale) != (MostRecentAvailableSpace * scale))
                relayoutRequested = true;
            MostRecentAvailableSpace = result.AvailableSpace;
        }

        private void ComputeAutoSize (ref UIOperationContext context, ref ControlDimension width, ref ControlDimension height) {
            if (!AutoSizeAxis.HasValue)
                return;
            if (!AreRecentRectsValid)
                return;

            var img = Image.Instance ?? Image2.Instance;
            // In cases where we are nested inside a container, our available space may be 0.
            // In that case, make do with our content rect.
            var aspace = new Vector2(Math.Max(MostRecentContentRect.Width, MostRecentAvailableSpace.X), Math.Max(MostRecentContentRect.Height, MostRecentAvailableSpace.Y));
            float scale = ComputeDisplayScaleRatio(
                img, 
                (AutoSizeAxis == LayoutDimensions.X) ? width.Constrain(99999, true) : aspace.X, 
                (AutoSizeAxis == LayoutDimensions.Y) ? height.Constrain(99999, true) : aspace.Y
            );
            if (AutoSizeAxis == LayoutDimensions.X)
                width.Minimum = img.Width * scale;
            else if (AutoSizeAxis == LayoutDimensions.Y)
                height.Minimum = img.Height * scale;

            // FIXME
            /*
            if (!AutoSizeWidth && !AutoSizeHeight)
                return;

            var instance = _Image.Instance;
            float? scaleToFit = null;

            while (true) {
                var scaleX = ApplyScaleToAutoSizeX ? (scaleToFit ?? 1f) : 1f;
                if (AutoSizeWidth && (instance != null)) {
                    var fw = instance.Width * Scale.X * scaleX;
                    if (!Width.Fixed.HasValue) {
                        if (Width.Maximum.HasValue)
                            width = Math.Min(fw, Width.Maximum.Value);
                        else
                            width = fw;
                    }
                }
                var scaleY = ApplyScaleToAutoSizeY ? (scaleToFit ?? 1f) : 1f;
                if (AutoSizeHeight && (instance != null)) {
                    var fh = instance.Height * Scale.Y * scaleY;
                    if (!Height.Fixed.HasValue) {
                        if (Height.Maximum.HasValue)
                            height = Math.Min(fh, Height.Maximum.Value);
                        else
                            height = fh;
                    }
                }

                if (scaleToFit.HasValue)
                    break;

                if (MostRecentRectIsValid) {
                    MostRecentScaleToFit = scaleToFit = ComputeScaleToFit(ref MostRecentRect);
                    ;
                } else if (MostRecentScaleToFit.HasValue) {
                    scaleToFit = MostRecentScaleToFit;
                } else {
                    ;
                }

                if (!scaleToFit.HasValue)
                    break;
            }

            if ((MostRecentRect.Width > 0) && (MostRecentRect.Height > 0))
                MostRecentRectIsValid = true;
            */
        }

        protected override void ComputeSizeConstraints (ref UIOperationContext context, ref ControlDimension width, ref ControlDimension height, Vector2 sizeScale) {
            base.ComputeSizeConstraints(ref context, ref width, ref height, sizeScale);
            ComputeAutoSize(ref context, ref width, ref height);
        }

        protected float? ComputeScaleToFit (ref RectF box) {
            // FIXME
            return null;
            /*
            if (!ScaleToFitX && !ScaleToFitY)
                return null;

            var instance = Image.Instance;
            if (instance == null)
                return null;

            float availableWidth = Math.Max(box.Width, 0);
            float availableHeight = Math.Max(box.Height, 0);

            float? scaleFactorX = null, scaleFactorY = null;

            if ((instance.Width > availableWidth) && ScaleToFitX)
                scaleFactorX = availableWidth / Math.Max(instance.Width, 1);

            if ((instance.Height > availableHeight) && ScaleToFitY)
                scaleFactorY = availableHeight / Math.Max(instance.Height, 1);

            var result = scaleFactorX;
            if (scaleFactorX.HasValue && scaleFactorY.HasValue)
                result = Math.Min(scaleFactorX.Value, scaleFactorY.Value);
            else
                result = result ?? scaleFactorY;

            if (result.HasValue)
                result = Math.Min(MaximumScale, result.Value);

            return result;
            */
        }

        private Material SelectMaterialForTwoImages (DefaultMaterialSet materials) {
            switch (Image2Mode) {
                default:
                case StaticImageCompositeMode.Atop:
                    return materials.AtopBitmap;
                case StaticImageCompositeMode.Over:
                    return materials.OverBitmap;
                case StaticImageCompositeMode.Under:
                    return materials.UnderBitmap;
                case StaticImageCompositeMode.Crossfade:
                    return materials.CrossfadeBitmap;
                case StaticImageCompositeMode.Masked:
                    return materials.MaskedBitmap;
            }
        }

        protected override ref BoxRecord OnGenerateLayoutTree (ref UIOperationContext context, ControlKey parent, ControlKey? existingKey) {
            context.UIContext.NotifyTextureUsed(this, Image);
            context.UIContext.NotifyTextureUsed(this, Image2);

            return ref base.OnGenerateLayoutTree(ref context, parent, existingKey);
        }

        protected override void OnRasterize (ref UIOperationContext context, ref RasterizePassSet passSet, DecorationSettings settings, IDecorator decorations) {
            base.OnRasterize(ref context, ref passSet, settings, decorations);
            ref var renderer = ref passSet.Pass(Pass);

            if (Image.IsDisposedOrNull && Image2.IsDisposedOrNull)
                return;

            var instance = Image.Instance;
            var instance2 = Image2.Instance;
            float opacity1 = !settings.IsCompositing ? context.Opacity : 1.0f,
                opacity2 = Image2Opacity.Get(now: context.NowL);

            if (instance == null) {
                instance = instance2;
                instance2 = null;
                opacity1 = opacity2;
                opacity2 = 0.0f;
            }

            var scale = ComputeDisplayScaleRatio(instance ?? instance2, settings.ContentBox.Width, settings.ContentBox.Height);
            var position = new Vector2(
                Arithmetic.Lerp(settings.Box.Left, settings.Box.Extent.X, Alignment.X),
                Arithmetic.Lerp(settings.Box.Top, settings.Box.Extent.Y, Alignment.Y)
            );
            // HACK: Fix images overhanging by a pixel
            position = position.Floor();
            var origin = Alignment;
            Vector4 color4 = MultiplyColor.Get(context.NowL) ?? Vector4.One,
                addColor4 = AddColor.Get(context.NowL) ?? Vector4.Zero;
            // FIXME: Always use context.Opacity?
            if (!settings.IsCompositing)
                color4 *= opacity1;
            pSRGBColor pColor = pSRGBColor.FromPLinear(color4),
                pAddColor = pSRGBColor.FromPLinear(addColor4);

            // FIXME: This won't properly restore state if there is a backing list
            var p = renderer.Parameters;
            renderer.Parameters = default;

            try {
                var defaultBlendState = Context.PickDefaultBlendState(instance);
                var blendState = BlendState ?? (
                    (instance2 != null) 
                        ? RenderStates.PorterDuffOver // The composited shaders always premultiply their inputs if necessary
                        : defaultBlendState
                );
                renderer.Parameters.AddRange(ref MaterialParameters);
                // If the inputs are not premultiplied we should have the compositing shaders premultiply them
                if ((defaultBlendState == BlendState.NonPremultiplied) || (defaultBlendState == RenderStates.PorterDuffNonPremultipliedOver))
                    renderer.Parameters.Add("AutoPremultiplyBlockTextures", true);
                var isPostPremultiplied = (blendState == BlendState.NonPremultiplied) || (blendState == RenderStates.PorterDuffNonPremultipliedOver);
                if (isPostPremultiplied)
                    pColor = pColor.Unpremultiply();

                var rect = new Rectangle(-DrawExpansion, -DrawExpansion, instance.Width + (DrawExpansion * 2), instance.Height + (DrawExpansion * 2));
                if (DrawExpansion != 0) {
                    Vector2 expansionAlignment = (DrawExpansion * scale) * (new Vector2(0.5f) - Alignment) * 2f;
                    // FIXME: This might be backwards
                    position -= expansionAlignment;
                }
                var drawCall = new BitmapDrawCall(instance, position.Round(0)) {
                    Origin = Alignment,
                    ScaleF = scale,
                    MultiplyColor = pColor.ToColor(context.UIContext.IsSRGB),
                    AddColor = pAddColor.ToColor(context.UIContext.IsSRGB),
                    UserData = RasterizerUserData,
                    TextureRegion = instance.BoundsFromRectangle(rect)
                };
                Material material = null;

                if ((instance2 != null) && (instance2 != instance)) {
                    drawCall.Texture2 = instance2;
                    if ((Image2Mode != StaticImageCompositeMode.CustomMaterial) && (Material == null)) {
                        var scale2 = ComputeDisplayScaleRatio(instance2, settings.ContentBox.Width, settings.ContentBox.Height);
                        drawCall.UserData = new Vector4(opacity2);
                        drawCall.AlignTexture2(scale2 / scale, preserveAspectRatio: true, alignment: Alignment);
                        material = SelectMaterialForTwoImages(renderer.Materials);
                    } else {
                        drawCall.TextureRegion2 = drawCall.TextureRegion;
                    }
                }
                // We have the compositor apply our blend state instead
                if (settings.IsCompositing && ((Material ?? material) == null))
                    blendState = BlendState.Opaque;

                renderer.Parameters.Add("TransparentExterior", true);
                renderer.Draw(ref drawCall, material: Material ?? material, blendState: blendState);
            } finally {
                renderer.Parameters = p;
            }

            if (ShowLoadingSpinner)
                context.DecorationProvider.LoadingSpinner?.Rasterize(ref context, ref passSet, ref settings);
        }

        public void CrossfadeTo (Texture2D newImage, float duration) {
            if (newImage == Image.Instance)
                return;

            // Attempt to figure out what the opacity of the image previously was
            var oldOpacity = (Image.IsDisposedOrNull 
                ? Image2Opacity.Get(Context.NowL) 
                : MultiplyColor.Get(Context.NowL)?.W ?? 1f
            );
            Image2 = Image;
            Image = newImage;
            if (Image2.IsDisposedOrNull && !Image.IsDisposedOrNull) {
                // ChangeDirection is inappropriate here since the image was likely previously invisible
                MultiplyColor = Tween<Color>.StartNow(Color.White * oldOpacity, Color.White, duration, now: Context.NowL);
            } else {
                Image2Mode = StaticImageCompositeMode.Crossfade;
                Image2Opacity = Tween.StartNow(oldOpacity, 0f, duration, now: Context.NowL);
            }
        }
    }
}
