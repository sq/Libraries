using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Game;
using Squared.PRGUI.Decorations;
using Squared.PRGUI.Layout;
using Squared.Render;
using Squared.Render.Convenience;
using Squared.Util;

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

    public class StaticImage : Control, IPostLayoutListener {
        public Vector2 Alignment = new Vector2(0.5f, 0.5f);
        public float Scale = 1.0f;
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
        /// If set, the control will attempt to automatically expand to contain the image on these axes.
        /// </summary>
        public ImageDimensions ExpandAxes;
        /// <summary>
        /// If set, the control will attempt to automatically shrink on these axes to eliminate empty space.
        /// </summary>
        public ImageDimensions ShrinkAxes;

        bool AreRecentRectsValid;
        RectF MostRecentParentContentRect, MostRecentContentRect;

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
            blendState = BlendState ?? Context.PickDefaultBlendState(Image.Instance);
        }

        protected override bool NeedsComposition (bool hasOpacity, bool hasTransform) {
            if ((Material != null) && (Appearance.Compositor != null))
                return true;

            if (Appearance.BackgroundColor.IsTransparent)
                hasOpacity = hasTransform = false;

            return base.NeedsComposition(hasOpacity, hasTransform);
        }

        public void SetFixedAxes (ImageDimensions axes) {
            ExpandAxes |= axes;
            ShrinkAxes |= axes;
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

        private void ComputeAutoSize (ref UIOperationContext context, ref ControlDimension width, ref ControlDimension height) {
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
            // FIXME
            /*
            var newW = Math.Max(w ?? -9999, Width.Minimum ?? -9999);
            var newH = Math.Max(h ?? -9999, Height.Minimum ?? -9999);
            Width.Minimum = (newW > -9999) ? newW : (float?)null;
            Height.Minimum = (newH > -9999) ? newH : (float?)null;
            */
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

        protected override bool IsPassDisabled (RasterizePasses pass, IDecorator decorations) {
            var showSpinner = ShowLoadingSpinner && (pass == RasterizePasses.Above);
            return decorations.IsPassDisabled(pass) && (pass != Pass) && !showSpinner && !ShouldClipContent;
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

        protected override ControlKey OnGenerateLayoutTree (ref UIOperationContext context, ControlKey parent, ControlKey? existingKey) {
            context.UIContext.NotifyTextureUsed(this, Image);
            context.UIContext.NotifyTextureUsed(this, Image2);

            return base.OnGenerateLayoutTree(ref context, parent, existingKey);
        }

        protected override void OnRasterize (ref UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings, IDecorator decorations) {
            base.OnRasterize(ref context, ref renderer, settings, decorations);

            if (context.Pass == Pass) {
                if (Image.IsDisposedOrNull)
                    return;

                var instance = Image.Instance;
                var instance2 = Image2.Instance;

                var scale = ComputeDisplayScaleRatio(instance, settings.ContentBox.Width, settings.ContentBox.Height);
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
                    color4 *= context.Opacity;
                pSRGBColor pColor = pSRGBColor.FromPLinear(color4),
                    pAddColor = pSRGBColor.FromPLinear(addColor4);
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
                        var opacity2 = Image2Opacity.Get(now: context.NowL);
                        var scale2 = ComputeDisplayScaleRatio(instance2, settings.ContentBox.Width, settings.ContentBox.Height);
                        drawCall.UserData = new Vector4(opacity2);
                        drawCall.AlignTexture2(scale2 / scale, preserveAspectRatio: true);
                        material = SelectMaterialForTwoImages(renderer.Materials);
                    } else {
                        drawCall.TextureRegion2 = drawCall.TextureRegion;
                    }
                }

                // HACK
                var p = renderer.Parameters;
                var defaultBlendState = Context.PickDefaultBlendState(drawCall.Texture1.Instance);
                var blendState = BlendState ?? (
                    (instance2 != null) 
                        ? RenderStates.PorterDuffOver // The composited shaders always premultiply their inputs if necessary
                        : defaultBlendState
                );
                // We have the compositor apply our blend state instead
                if (settings.IsCompositing && ((Material ?? material) == null))
                    blendState = BlendState.Opaque;
                renderer.Parameters.AddRange(ref MaterialParameters);
                // If the inputs are not premultiplied we should have the compositing shaders premultiply them
                if (defaultBlendState == BlendState.NonPremultiplied)
                    renderer.Parameters.Add("AutoPremultiplyBlockTextures", true);
                renderer.Draw(ref drawCall, material: Material ?? material, blendState: blendState);
                renderer.Parameters = p;
            }

            if (ShowLoadingSpinner)
                context.DecorationProvider.LoadingSpinner?.Rasterize(ref context, ref renderer, ref settings);
        }

        void IPostLayoutListener.OnLayoutComplete (ref UIOperationContext context, ref bool relayoutRequested) {
            if (IsLayoutInvalid || (_Image.Instance == null)) {
                AreRecentRectsValid = false;
                return;
            }

            var contentRect = GetRect(contentRect: true);
            var parentContentRect = TryGetParent(out Control parent)
                ? parent.GetRect(contentRect: true)
                : context.UIContext.CanvasRect;

            MostRecentContentRect = contentRect;
            MostRecentParentContentRect = parentContentRect;
            AreRecentRectsValid = false;

            // FIXME
            /*
            if (newRect.Size != MostRecentRect.Size) {
                if (ScaleToFitX && (newRect.Width != MostRecentRect.Width))
                    MostRecentScaleToFit = null;
                if (ScaleToFitY && (newRect.Height != MostRecentRect.Height))
                    MostRecentScaleToFit = null;
                MostRecentRectIsValid = false;
                MostRecentRect = newRect;
            }

            if (!MostRecentRectIsValid)
                relayoutRequested = true;
            */
        }
    }
}
