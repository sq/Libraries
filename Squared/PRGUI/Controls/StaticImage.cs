using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
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

    public class StaticImage : Control, IPostLayoutListener {
        public Vector2 Alignment = new Vector2(0.5f, 0.5f);
        public Vector2 Scale = Vector2.One;
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
                InvalidateAutoSize();
            }
        }

        public bool ShowLoadingSpinner;

        public StaticImage ()
            : base () {
            CanApplyOpacityWithoutCompositing = true;
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

        private Vector2 ComputeDisplayScaleRatio (float availableWidth, float availableHeight) {
            // HACK
            if (Image.Instance == null)
                return Vector2.One;

            var widthScale = ScaleToFitX
                ? availableWidth / Image.Instance.Width
                : (float?)null;
            var heightScale = ScaleToFitY
                ? availableHeight / Image.Instance.Height
                : (float?)null;

            var scaleF = Arithmetic.Clamp(
                // FIXME: Move this
                ControlDimension.Min(widthScale, heightScale) ?? 1,
                MinimumScale,
                MaximumScale
            );
            return new Vector2(scaleF, scaleF);
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

        protected override void OnRasterize (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings, IDecorator decorations) {
            base.OnRasterize(context, ref renderer, settings, decorations);

            if (context.Pass == Pass) {
                if (Image.IsDisposedOrNull)
                    return;

                var instance = Image.Instance;
                context.UIContext.NotifyTextureUsed(this, Image);

                var scale = ComputeDisplayScaleRatio(settings.ContentBox.Width, settings.ContentBox.Height);
                var position = new Vector2(
                    Arithmetic.Lerp(settings.Box.Left, settings.Box.Extent.X, Alignment.X),
                    Arithmetic.Lerp(settings.Box.Top, settings.Box.Extent.Y, Alignment.Y)
                );
                var origin = Alignment;
                renderer.Draw(
                    instance, position.Round(0),
                    origin: Alignment, scale: scale,
                    multiplyColor: Color.White * context.Opacity
                );
            }

            if (ShowLoadingSpinner && (context.Pass == RasterizePasses.Above)) {
                // FIXME
                var center = settings.ContentBox.Center;
                var radius = Math.Min(settings.ContentBox.Size.Length(), 48);
                var angle1 = (float)(Time.Seconds * 360 * 1.33f);
                var color1 = Color.White;
                var color2 = color1 * 0.8f;
                renderer.RasterizeArc(
                    center, angle1, 48, radius, 6,
                    1f, color1, color2, Color.Black * 0.8f
                );
            }
        }

        void IPostLayoutListener.OnLayoutComplete (UIOperationContext context, ref bool relayoutRequested) {
            if ((LayoutKey.IsInvalid) || (_Image.Instance == null)) {
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
