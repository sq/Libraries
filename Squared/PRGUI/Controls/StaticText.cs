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
    public class StaticTextBase : Control, IPostLayoutListener, Accessibility.IReadingTarget {
        /// <summary>
        /// If true, the control will have its size set exactly to fit its content.
        /// If false, the control will be expanded to fit its content but will not shrink.
        /// </summary>
        public bool AutoSizeIsMaximum = true;

        /// <summary>
        /// If set, accessibility reading will use this control's tooltip instead of its text.
        /// </summary>
        public bool UseTooltipForReading = false;

        public const float AutoSizePadding = 3f;
        public const bool DiagnosticText = false;

        public Material TextMaterial = null;
        protected DynamicStringLayout Content = new DynamicStringLayout();
        private DynamicStringLayout ContentMeasurement = null;
        private bool _AutoSizeWidth = true, _AutoSizeHeight = true;
        private bool _NeedRelayout;
        private float? MostRecentContentWidth = null;

        private float? AutoSizeComputedWidth, AutoSizeComputedHeight;

        public StaticTextBase ()
            : base () {
            // Multiline = false;
        }

        bool? _RichText;

        public bool RichText {
            get => _RichText ?? Content.RichText;
            set {
                _RichText = value;
                Content.RichText = value;
            }
        }

        bool _ScaleToFit;

        protected bool ScaleToFit {
            get => _ScaleToFit;
            set {
                if (_ScaleToFit == value)
                    return;
                _ScaleToFit = true;
                Invalidate();
            }
        }

        protected bool Multiline {
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

        protected bool Wrap {
            get {
                return Content.WordWrap || Content.CharacterWrap;
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
            protected set {
                Content.Text = value;
            }
        }

        internal void SetText (AbstractString text) {
            if (Text.TextEquals(text, StringComparison.Ordinal))
                return;

            Text = text;
        }

        protected override void ComputeFixedSize (out float? fixedWidth, out float? fixedHeight) {
            base.ComputeFixedSize(out fixedWidth, out fixedHeight);
            /*
            if (Context == null)
                return;
            */
            // var parentRect = Context.Layout.GetRect(parent);

            if (AutoSizeWidth && AutoSizeIsMaximum && !Width.Fixed.HasValue)
                fixedWidth = AutoSizeComputedWidth ?? fixedWidth;
            if (AutoSizeHeight && AutoSizeIsMaximum && !Height.Fixed.HasValue)
                fixedHeight = AutoSizeComputedHeight ?? fixedHeight;

            Width.Constrain(ref fixedWidth);
            Height.Constrain(ref fixedHeight);
        }

        protected override void ComputeSizeConstraints (out float? minimumWidth, out float? minimumHeight, out float? maximumWidth, out float? maximumHeight) {
            base.ComputeSizeConstraints(out minimumWidth, out minimumHeight, out maximumWidth, out maximumHeight);
            if (AutoSizeIsMaximum)
                return;

            if (AutoSizeWidth)
                minimumWidth = AutoSizeComputedWidth;
            if (AutoSizeHeight)
                minimumHeight = AutoSizeComputedHeight;
        }

        private void ConfigureMeasurement () {
            if (ContentMeasurement == null)
                ContentMeasurement = new DynamicStringLayout();
            if (Content.GlyphSource == null)
                throw new NullReferenceException();
            ContentMeasurement.Copy(Content);
            ContentMeasurement.MeasureOnly = true;
            // HACK: If we never get painted (to validate our main content layout), then
            //  Content.IsValid will be false forever and we will recompute our measurement
            //  layout every frame. Not great, so... whatever
            Content.Get();
        }

        protected StringLayout GetCurrentLayout (bool measurement) {
            if (_RichText.HasValue)
                Content.RichText = _RichText.Value;
            if (Content.RichText)
                Content.RichTextConfiguration = Context.RichTextConfiguration;

            if (measurement) {
                if (!Content.IsValid || (ContentMeasurement == null)) {
                    AutoSizeComputedHeight = AutoSizeComputedWidth = null;
                    ConfigureMeasurement();
                }
                if (!ContentMeasurement.IsValid) {
                    AutoSizeComputedHeight = AutoSizeComputedWidth = null;
                    _NeedRelayout = true;
                }
                return ContentMeasurement.Get();
            } else {
                if (!Content.IsValid) {
                    if (ContentMeasurement != null)
                        ConfigureMeasurement();
                    _NeedRelayout = true;
                }
                return Content.Get();
            }
        }

        private IDecorator _CachedTextDecorations,
            _CachedDecorations;
        private Margins _CachedPadding;
        private bool? _CachedContentIsSingleLine;

        protected void ComputeAutoSize (UIOperationContext context) {
            // FIXME: If we start out constrained (by our parent size, etc) we will compute
            //  a compressed auto-size value here, and it will never be updated even if our parent
            //  gets bigger

            if (!AutoSizeWidth && !AutoSizeHeight) {
                AutoSizeComputedHeight = AutoSizeComputedWidth = null;
                return;
            }

            var textDecorations = GetTextDecorator(context.DecorationProvider, context.DefaultTextDecorator);
            var decorations = GetDecorator(context.DecorationProvider, context.DefaultDecorator);
            var fontChanged = UpdateFont(context, textDecorations, decorations);

            ComputePadding(context, decorations, out Margins computedPadding);

            var contentChanged = (ContentMeasurement?.IsValid == false) || !Content.IsValid;
            if (contentChanged || fontChanged)
                AutoSizeComputedWidth = AutoSizeComputedHeight = null;

            if (
                (_CachedTextDecorations != textDecorations) ||
                (_CachedDecorations != decorations) ||
                !_CachedPadding.Equals(ref computedPadding)
            ) {
                AutoSizeComputedWidth = AutoSizeComputedHeight = null;
            }

            if (
                ((AutoSizeComputedWidth != null) == AutoSizeWidth) &&
                ((AutoSizeComputedHeight != null) == AutoSizeHeight)
            )
                return;

            _CachedTextDecorations = textDecorations;
            _CachedDecorations = decorations;
            _CachedPadding = computedPadding;

            if (contentChanged || fontChanged || (_CachedContentIsSingleLine == null)) {
                // HACK: If we're pretty certain the text will be exactly one line long and we don't
                //  care how wide it is, just return the line spacing without performing layout
                _CachedContentIsSingleLine = (AutoSizeHeight && !AutoSizeWidth) &&
                    (!Content.CharacterWrap && !Content.WordWrap) &&
                    (
                        (Content.LineLimit == 1) ||
                        Content.Text.Length < 1 ||
                        ((Content.Text.Length < 512) && !Content.Text.Contains('\n'))
                    );
            }

            if (_CachedContentIsSingleLine == true) {
                if (contentChanged)
                    GetCurrentLayout(true);
                AutoSizeComputedHeight = (float)Math.Ceiling(Content.GlyphSource.LineSpacing + computedPadding.Size.Y);
                return;
            }

            var layout = GetCurrentLayout(true);
            if (AutoSizeWidth)
                AutoSizeComputedWidth = (float)Math.Ceiling(layout.UnconstrainedSize.X + computedPadding.Size.X);
            if (AutoSizeHeight)
                AutoSizeComputedHeight = (float)Math.Ceiling(layout.Size.Y + computedPadding.Size.Y);
        }

        protected void Invalidate () {
            _NeedRelayout = true;
            AutoSizeComputedHeight = AutoSizeComputedWidth = null;
            Content.Invalidate();
            ContentMeasurement?.Invalidate();
            // HACK: Ensure we do not erroneously wrap content that is intended to be used as an input for auto-size
            if (AutoSizeWidth)
                MostRecentContentWidth = null;
        }

        protected override ControlKey OnGenerateLayoutTree (UIOperationContext context, ControlKey parent, ControlKey? existingKey) {
            ComputeAutoSize(context);
            UpdateLineBreak(context, GetDecorator(context.DecorationProvider, context.DefaultDecorator));
            if (!Content.IsValid)
                ComputeAutoSize(context);
            var result = base.OnGenerateLayoutTree(context, parent, existingKey);
            return result;
        }

        protected override IDecorator GetDefaultDecorator (IDecorationProvider provider) {
            return provider?.StaticText;
        }

        protected float? ComputeTextWidthLimit (UIOperationContext context, IDecorator decorations) {
            if (_ScaleToFit)
                return null;

            ComputePadding(context, decorations, out Margins computedPadding);
            float? constrainedWidth = null;
            if (MostRecentContentWidth.HasValue) {
                float computed = MostRecentContentWidth.Value;
                if (Width.Maximum.HasValue)
                    constrainedWidth = Math.Min(computed, Width.Maximum.Value - computedPadding.X);
                else
                    constrainedWidth = computed;
            } else
                constrainedWidth = Width.Maximum - computedPadding.X;

            var limit = Width.Fixed - computedPadding.X ?? constrainedWidth;
            if (limit.HasValue)
                // HACK: Suppress jitter
                return (float)Math.Ceiling(limit.Value) + AutoSizePadding;
            else
                return null;
        }

        protected float ComputeScaleToFit (ref StringLayout layout, ref RectF box, ref Margins margins) {
            if (!_ScaleToFit)
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

            ComputePadding(context, decorations, out Margins computedPadding);

            var overrideColor = GetTextColor(context.NowL);
            Color? defaultColor = null;
            Material material;
            var textDecorations = GetTextDecorator(context.DecorationProvider, context.DefaultTextDecorator);
            GetTextSettings(context, textDecorations, decorations, settings.State, out material, ref defaultColor);

            Content.DefaultColor = defaultColor ?? Color.White;

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
            textOffset.Y += Math.Min(extraSpaceY, (layout.Size.Y - scaledSize.Y)) * 0.5f;

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
                material: material, samplerState: RenderStates.Text,
                scale: textScale, multiplyColor: overrideColor?.ToColor()
            );
        }

        protected void UpdateLineBreak (UIOperationContext context, IDecorator decorations) {
            var textWidthLimit = ComputeTextWidthLimit(context, decorations);
            if (textWidthLimit.HasValue && !_ScaleToFit)
                Content.LineBreakAtX = textWidthLimit;
            else
                Content.LineBreakAtX = null;
        }

        private IGlyphSource _MostRecentFontFromDecorations;

        private bool SyncWithCurrentFontFromDecorations (IGlyphSource font) {
            var result = false;
            if (font != _MostRecentFontFromDecorations) {
                if (Content.GlyphSource == _MostRecentFontFromDecorations) {
                    Content.GlyphSource = font;
                    result = true;
                }
                _MostRecentFontFromDecorations = font;
            }

            return result;
        }

        protected bool UpdateFont (UIOperationContext context, IDecorator textDecorations, IDecorator decorations) {
            var font = textDecorations?.GlyphSource ?? decorations.GlyphSource;
            if (font == null)
                throw new NullReferenceException($"Decorators provided no font for control {this} ({textDecorations}, {decorations})");
            return SyncWithCurrentFontFromDecorations(font);
        }

        protected bool GetTextSettings (UIOperationContext context, IDecorator textDecorations, IDecorator decorations, ControlStates state, out Material material, ref Color? color) {
            (textDecorations ?? decorations).GetTextSettings(context, state, out material, ref color);
            SyncWithCurrentFontFromDecorations(textDecorations?.GlyphSource ?? decorations.GlyphSource);
            if (TextMaterial != null)
                material = TextMaterial;
            return false;
        }

        protected string GetPlainText () {
            return RichText
                ? Squared.Render.Text.RichText.ToPlainText(Text)
                : Text.ToString();
        }

        protected string GetTrimmedText (string text) {
            if (text == null)
                return null;

            var s = text.Replace('\r', ' ').Replace('\n', ' ') ?? "";
            if (s.Length > 16)
                return s.Substring(0, 16) + "...";
            else
                return s;
        }

        public override string ToString () {
            if (DebugLabel != null)
                return $"{DebugLabel} '{GetTrimmedText(GetPlainText())}'";
            else
                return $"{GetType().Name} #{GetHashCode():X8} '{GetTrimmedText(GetPlainText())}'";
        }

        protected virtual AbstractString GetReadingText () {
            var plainText = GetPlainText();
            if (UseTooltipForReading || string.IsNullOrWhiteSpace(plainText))
                return TooltipContent.Get(this);

            return plainText;
        }

        protected virtual void FormatValueInto (StringBuilder sb) {
            sb.Append(Text);
        }

        AbstractString Accessibility.IReadingTarget.Text => GetReadingText();
        void Accessibility.IReadingTarget.FormatValueInto (StringBuilder sb) => FormatValueInto(sb);

        void IPostLayoutListener.OnLayoutComplete (
            UIOperationContext context, ref bool relayoutRequested
        ) {
            if (_NeedRelayout) {
                relayoutRequested = true;
                _NeedRelayout = false;
            }

            // FIXME: This is probably wrong?
            if (!AutoSizeWidth && !Wrap && MostRecentContentWidth.HasValue)
                return;

            var contentBox = context.Layout.GetContentRect(LayoutKey);
            MostRecentContentWidth = contentBox.Width;
        }
    }

    public class StaticText : StaticTextBase {
        new public DynamicStringLayout Content => base.Content;
        new public AbstractString Text {
            get => base.Text;
            set => base.Text = value;
        }
        new public bool Wrap {
            get => base.Wrap;
            set => base.Wrap = value;
        }
        new public bool Multiline {
            get => base.Multiline;
            set => base.Multiline = value;
        }
        new public bool ScaleToFit {
            get => base.ScaleToFit;
            set => base.ScaleToFit = value;
        }

        new public void Invalidate () => base.Invalidate();

        public StaticText ()
            : base () {
            AcceptsMouseInput = true;
        }
    }
}
