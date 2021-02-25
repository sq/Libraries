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
    public class StaticImage : Control {
        public bool AutoSizeWidth = true, AutoSizeHeight = true;
        public bool AutoSize {
            get => AutoSizeWidth && AutoSizeHeight;
            set => AutoSizeWidth = AutoSizeHeight = value;
        }
        public Vector2 Alignment = new Vector2(0.5f, 0.5f);
        public Vector2 Scale = Vector2.One;
        public RasterizePasses Pass = RasterizePasses.Content;
        public bool ScaleToFitX, ScaleToFitY;
        public bool ScaleToFit {
            get => ScaleToFitX && ScaleToFitY;
            set => ScaleToFitX = ScaleToFitY = value;
        }
        public AbstractTextureReference Image;
        public bool ShowLoadingSpinner;

        public StaticImage ()
            : base () {
            CanApplyOpacityWithoutCompositing = true;
        }

        protected override IDecorator GetDefaultDecorator (IDecorationProvider provider) {
            return provider?.StaticImage ?? provider?.None;
        }

        protected override void ComputeFixedSize (out float? fixedWidth, out float? fixedHeight) {
            base.ComputeFixedSize(out fixedWidth, out fixedHeight);
            if (!AutoSizeWidth && !AutoSizeHeight)
                return;

            ComputeSizeConstraints(
                out float? minimumWidth, out float? minimumHeight,
                out float? maximumWidth, out float? maximumHeight
            );

            var instance = Image.Instance;
            float? scaleToFit = null;

            // FIXME
            var computedPadding = default(Margins);

            while (true) {
                if (AutoSizeWidth && (instance != null)) {
                    var fw = instance.Width * Scale.X * (scaleToFit ?? 1f);
                    if (!Width.Fixed.HasValue) {
                        if (maximumWidth.HasValue)
                            fixedWidth = Math.Min(fw, maximumWidth.Value);
                        else
                            fixedWidth = fw;
                    }
                }
                if (AutoSizeHeight && (instance != null)) {
                    var fh = instance.Height * Scale.Y * (scaleToFit ?? 1f);
                    if (!Height.Fixed.HasValue) {
                        if (maximumHeight.HasValue)
                            fixedHeight = Math.Min(fh, maximumHeight.Value);
                        else
                            fixedHeight = fh;
                    }
                }

                if (scaleToFit.HasValue)
                    break;

                var fakeBox = new RectF(0, 0, fixedWidth ?? 9999, fixedHeight ?? 9999);
                scaleToFit = ComputeScaleToFit(ref fakeBox);
                if (!scaleToFit.HasValue)
                    break;
            }
        }

        protected float? ComputeScaleToFit (ref RectF box) {
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
            return result;
        }

        protected override ControlKey OnGenerateLayoutTree (UIOperationContext context, ControlKey parent, ControlKey? existingKey) {
            var result = base.OnGenerateLayoutTree(context, parent, existingKey);

            return result;
        }

        protected override bool IsPassDisabled (RasterizePasses pass, IDecorator decorations) {
            var showSpinner = ShowLoadingSpinner && (pass == RasterizePasses.Above);
            return decorations.IsPassDisabled(pass) && (pass != Pass) && !showSpinner && !ShouldClipContent;
        }

        protected override void OnRasterize (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings, IDecorator decorations) {
            base.OnRasterize(context, ref renderer, settings, decorations);

            if (context.Pass == Pass) {
                var instance = Image.Instance;
                if (instance == null)
                    return;

                var scaleToFit = ComputeScaleToFit(ref settings.ContentBox);
                var scale = 
                    scaleToFit.HasValue 
                        ? new Vector2(scaleToFit.Value)
                        : Scale;
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
    }
}
