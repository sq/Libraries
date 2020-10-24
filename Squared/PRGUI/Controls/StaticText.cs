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
using Squared.Render.Text;
using Squared.Util;
using Squared.Util.Text;

namespace Squared.PRGUI.Controls {
    public class StaticText : Control, IPostLayoutListener {
        public const bool DiagnosticText = false;

        public Tween<Vector4>? TextColorPLinear = null;
        private bool _TextColorEventFired;
        public Material TextMaterial = null;
        public DynamicStringLayout Content = new DynamicStringLayout();
        private DynamicStringLayout ContentMeasurement = null;
        private bool _AutoSizeWidth = true, _AutoSizeHeight = true;
        private bool _NeedRelayout;
        private float? MostRecentContentWidth = null;

        private float? AutoSizeComputedWidth, AutoSizeComputedHeight;

        public StaticText ()
            : base () {
            // Multiline = false;
        }

        bool _ScaleToFit;

        public bool ScaleToFit {
            get => _ScaleToFit;
            set {
                if (_ScaleToFit == value)
                    return;
                _ScaleToFit = true;
                Invalidate();
            }
        }

        public Tween<Color>? TextColor {
            set => UpdateColor(ref TextColorPLinear, value);
        }

        public Tween<pSRGBColor>? TextColorPSRGB {
            set => UpdateColor(ref TextColorPLinear, value);
        }

        public bool Multiline {
            get => Content.LineLimit != 1;
            set {
                Content.LineLimit = value ? int.MaxValue : 1;
            }
        }

        public bool AutoSizeWidth {
            get => _AutoSizeWidth;
            set {
                if (_AutoSizeWidth == value)
                    return;
                _AutoSizeWidth = value;
                Content.Invalidate();
            }
        }

        public bool AutoSizeHeight {
            get => _AutoSizeHeight;
            set {
                if (_AutoSizeHeight == value)
                    return;
                _AutoSizeHeight = value;
                Content.Invalidate();
            }
        }

        public bool AutoSize {
            set {
                AutoSizeWidth = AutoSizeHeight = value;
            }
        }

        public bool Wrap {
            get {
                return Content.WordWrap;
            }
            set {
                Content.CharacterWrap = value;
                Content.WordWrap = value;
            }
        }

        public HorizontalAlignment TextAlignment {
            get {
                return Content.Alignment;
            }
            set {
                Content.Alignment = value;
            }
        }

        public AbstractString Text {
            get {
                return Content.Text;
            }
            set {
                Content.Text = value;
            }
        }

        protected override void ComputeFixedSize (out float? fixedWidth, out float? fixedHeight) {
            base.ComputeFixedSize(out fixedWidth, out fixedHeight);
            /*
            if (Context == null)
                return;
            */
            // var parentRect = Context.Layout.GetRect(parent);

            if (AutoSizeWidth && !FixedWidth.HasValue)
                fixedWidth = AutoSizeComputedWidth ?? fixedWidth;
            if (AutoSizeHeight && !FixedHeight.HasValue)
                fixedHeight = AutoSizeComputedHeight ?? fixedHeight;

            if (MinimumWidth.HasValue && fixedWidth.HasValue)
                fixedWidth = Math.Max(MinimumWidth.Value, fixedWidth.Value);
            if (MinimumHeight.HasValue && fixedHeight.HasValue)
                fixedHeight = Math.Max(MinimumHeight.Value, fixedHeight.Value);

            if (MaximumWidth.HasValue && fixedWidth.HasValue)
                fixedWidth = Math.Min(MaximumWidth.Value, fixedWidth.Value);
            if (MaximumHeight.HasValue && fixedHeight.HasValue)
                fixedHeight = Math.Min(MaximumHeight.Value, fixedHeight.Value);
        }

        private void ConfigureMeasurement () {
            if (ContentMeasurement == null)
                ContentMeasurement = new DynamicStringLayout();
            ContentMeasurement.Copy(Content);
        }

        private StringLayout GetCurrentLayout (bool measurement) {
            if (measurement) {
                if (!Content.IsValid || (ContentMeasurement == null))
                    ConfigureMeasurement();
                if (!ContentMeasurement.IsValid)
                    _NeedRelayout = true;
                return ContentMeasurement.Get();
            } else {
                if (!Content.IsValid) {
                    ConfigureMeasurement();
                    _NeedRelayout = true;
                }
                return Content.Get();
            }
        }

        private void ComputeAutoSize (UIOperationContext context) {
            // FIXME: If we start out constrained (by our parent size, etc) we will compute
            //  a compressed auto-size value here, and it will never be updated even if our parent
            //  gets bigger

            AutoSizeComputedHeight = AutoSizeComputedWidth = null;
            if (!AutoSizeWidth && !AutoSizeHeight)
                return;

            var decorations = GetDecorations(context.DecorationProvider);
            var textDecorations = GetTextDecorations(context.DecorationProvider);
            UpdateFont(context, textDecorations);

            var computedPadding = ComputePadding(context, decorations);
            if (ScaleToFit)
                return;

            var layout = GetCurrentLayout(true);
            if (AutoSizeWidth)
                AutoSizeComputedWidth = layout.UnconstrainedSize.X + computedPadding.Size.X;
            if (AutoSizeHeight)
                AutoSizeComputedHeight = layout.Size.Y + computedPadding.Size.Y;
        }

        public void Invalidate () {
            _NeedRelayout = true;
            Content.Invalidate();
            ContentMeasurement?.Invalidate();
        }

        protected override ControlKey OnGenerateLayoutTree (UIOperationContext context, ControlKey parent, ControlKey? existingKey) {
            ComputeAutoSize(context);
            UpdateLineBreak(context, GetDecorations(context.DecorationProvider));
            var result = base.OnGenerateLayoutTree(context, parent, existingKey);
            return result;
        }

        protected override IDecorator GetDefaultDecorations (IDecorationProvider provider) {
            return provider?.StaticText;
        }

        protected float? ComputeTextWidthLimit (UIOperationContext context, IDecorator decorations) {
            if (ScaleToFit)
                return null;

            var computedPadding = ComputePadding(context, decorations);
            float? constrainedWidth = null;
            if (MostRecentContentWidth.HasValue) {
                float computed = MostRecentContentWidth.Value;
                if (MaximumWidth.HasValue)
                    constrainedWidth = Math.Min(computed, MaximumWidth.Value - computedPadding.X);
                else
                    constrainedWidth = computed;
            } else
                constrainedWidth = MaximumWidth - computedPadding.X;

            var limit = FixedWidth - computedPadding.X ?? constrainedWidth;
            if (limit.HasValue)
                // HACK: Suppress jitter
                return (float)Math.Ceiling(limit.Value + 0.5);
            else
                return null;
        }

        protected pSRGBColor? GetTextColor (long now) {
            var v4 = AutoFireTweenEvent(now, UIEvents.BackgroundColorTweenEnded, ref TextColorPLinear, ref _TextColorEventFired);
            if (!v4.HasValue)
                return null;
            return pSRGBColor.FromPLinear(v4.Value);
        }

        private float ComputeScaleToFit (ref StringLayout layout, ref RectF box, ref Margins margins) {
            if (!ScaleToFit)
                return 1;

            float availableWidth = Math.Max(box.Width - margins.X, 0);
            float availableHeight = Math.Max(box.Height - margins.Y, 0);

            float scaleFactor = 1;
            if (layout.UnconstrainedSize.X > availableWidth)
                scaleFactor = Math.Min(scaleFactor, availableWidth / (layout.UnconstrainedSize.X + 0.1f));
            if (layout.UnconstrainedSize.Y > availableHeight)
                scaleFactor = Math.Min(scaleFactor, availableHeight / (layout.UnconstrainedSize.Y + 0.1f));
            return scaleFactor;
        }

        protected override void OnRasterize (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings, IDecorator decorations) {
            base.OnRasterize(context, ref renderer, settings, decorations);

            if (context.Pass != RasterizePasses.Content)
                return;

            var computedPadding = ComputePadding(context, decorations);

            var overrideColor = GetTextColor(context.NowL);
            Material material;
            var textDecorations = GetTextDecorations(context.DecorationProvider);
            GetTextSettings(context, textDecorations, settings.State, out material, ref overrideColor);

            settings.Box.SnapAndInset(out Vector2 a, out Vector2 b);

            Vector2 textOffset = Vector2.Zero, textScale = Vector2.One;
            decorations?.GetContentAdjustment(context, settings.State, out textOffset, out textScale);
            textOffset += a + new Vector2(computedPadding.Left, computedPadding.Top);

            UpdateLineBreak(context, decorations);

            var layout = GetCurrentLayout(false);
            textScale *= ComputeScaleToFit(ref layout, ref settings.Box, ref computedPadding);

            var scaledSize = layout.Size * textScale;

            // Recenter the text if it's been scaled by the decorator
            float extraSpaceY = Math.Max(settings.Box.Height - scaledSize.Y - computedPadding.Y, 0);
            textOffset.Y += Math.Min(extraSpaceY, (layout.Size.Y - scaledSize.Y));

            var xSpace = (b.X - a.X) - scaledSize.X - computedPadding.X;
            switch (Content.Alignment) {
                case HorizontalAlignment.Left:
                    break;
                case HorizontalAlignment.Center:
                    textOffset.X += (xSpace / 2f);
                    break;
                case HorizontalAlignment.Right:
                    textOffset.X += xSpace;
                    break;
            }

            renderer.Layer += 1;

            renderer.DrawMultiple(
                layout.DrawCalls, offset: textOffset.Floor(),
                material: material, samplerState: RenderStates.Text, multiplyColor: overrideColor?.ToColor(),
                scale: textScale
            );
        }

        private void UpdateLineBreak (UIOperationContext context, IDecorator decorations) {
            var textWidthLimit = ComputeTextWidthLimit(context, decorations);
            if (textWidthLimit.HasValue && !ScaleToFit)
                Content.LineBreakAtX = textWidthLimit;
            else
                Content.LineBreakAtX = null;
        }

        protected void UpdateFont (UIOperationContext context, IDecorator decorations) {
            pSRGBColor? temp2 = null;
            GetTextSettings(context, decorations, default(ControlStates), out Material temp, ref temp2);
        }

        protected void GetTextSettings (UIOperationContext context, IDecorator decorations, ControlStates state, out Material material, ref pSRGBColor? color) {
            decorations.GetTextSettings(context, state, out material, out IGlyphSource font, ref color);
            if (Content.GlyphSource == null)
                Content.GlyphSource = font;
            if (TextMaterial != null)
                material = TextMaterial;
        }

        private string GetTrimmedText () {
            var s = Text.ToString().Replace('\r', ' ').Replace('\n', ' ') ?? "";
            if (s.Length > 16)
                return s.Substring(0, 16) + "...";
            else
                return s;
        }

        public override string ToString () {
            return $"{GetType().Name} #{GetHashCode():X8} '{GetTrimmedText()}'";
        }

        void IPostLayoutListener.OnLayoutComplete (UIOperationContext context, ref bool relayoutRequested) {
            if (_NeedRelayout) {
                relayoutRequested = true;
                _NeedRelayout = false;
            }

            var decorations = GetDecorations(context.DecorationProvider);
            var box = context.Layout.GetRect(LayoutKey);

            var contentBox = context.Layout.GetContentRect(LayoutKey);
            MostRecentContentWidth = contentBox.Width;
        }
    }
}
