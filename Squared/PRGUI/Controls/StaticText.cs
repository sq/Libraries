using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Squared.Game;
using Squared.PRGUI.Accessibility;
using Squared.PRGUI.Decorations;
using Squared.PRGUI.Layout;
using Squared.Render;
using Squared.Render.Convenience;
using Squared.Render.Text;
using Squared.Util;
using Squared.Util.Text;

namespace Squared.PRGUI.Controls {
    public class StaticTextBase : Control, IPostLayoutListener, Accessibility.IReadingTarget {
        public static GlyphPixelAlignment DefaultGlyphPixelAlignment = GlyphPixelAlignment.Default;

        /// <summary>
        /// Rasterizes boxes for each text control's box, content box, and text layout box
        /// Also rasterizes a yellow line for the wrap/break threshold
        /// </summary>
        public static bool VisualizeLayout = false;

        /// <summary>
        /// If true, the control will have its size set exactly to fit its content.
        /// If false, the control will be expanded to fit its content but will not shrink.
        /// </summary>
        public bool AutoSizeIsMaximum = true;

        /// <summary>
        /// If set, accessibility reading will use this control's tooltip instead of its text.
        /// </summary>
        public bool UseTooltipForReading = false;

        public const float LineBreakRightPadding = 1.1f;
        public const float AutoSizePadding = 0.5f;
        public const bool DiagnosticText = false;

        public Material TextMaterial = null;
        protected DynamicStringLayout Content = new DynamicStringLayout {
            HideOverflow = true,
            RecordUsedTextures = true,
            AlignToPixels = DefaultGlyphPixelAlignment,
            ExpandHorizontallyWhenAligning = false
        };
        private DynamicStringLayout ContentMeasurement = null;
        private bool _AutoSizeWidth = true, _AutoSizeHeight = true;
        private bool _NeedRelayout;
        private float? MostRecentContentBoxWidth = null, MostRecentWidth = null;

        protected int? CharacterLimit { get; set; }

        private float? AutoSizeComputedWidth, AutoSizeComputedHeight;
        private float AutoSizeComputedContentHeight;
        private float MostRecentXScaleFactor = 1, MostRecentYScaleFactor = 1;
        private ControlDimension MostRecentWidthConstraint;

        protected virtual Material CustomTextMaterial => null;

        protected Vector4? RasterizerUserData;

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

        bool _ScaleToFitX, _ScaleToFitY;

        protected virtual RichTextConfiguration GetRichTextConfiguration() => Context.RichTextConfiguration;

        protected bool ScaleToFitX {
            get => _ScaleToFitX;
            set {
                if (_ScaleToFitX == value)
                    return;
                _ScaleToFitX = value;
                Invalidate();
            }
        }

        protected bool ScaleToFitY {
            get => _ScaleToFitY;
            set {
                if (_ScaleToFitY == value)
                    return;
                _ScaleToFitY = value;
                Invalidate();
            }
        }

        protected bool ScaleToFit {
            get => _ScaleToFitX && _ScaleToFitY;
            set {
                if ((_ScaleToFitX == value) && (_ScaleToFitY == value))
                    return;
                _ScaleToFitX = _ScaleToFitY = value;
                Invalidate();
            }
        }

        protected StringLayoutFilter LayoutFilter {
            get; set;
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
            get => Content.WordWrap || Content.CharacterWrap;
            set {
                Content.CharacterWrap = value;
                Content.WordWrap = value;
            }
        }

        public HorizontalAlignment TextAlignment {
            get => Content.Alignment;
            set => Content.Alignment = value;
        }

        public float VerticalAlignment = 0.5f;

        public float Scale {
            get => Content.Scale;
            set {
                if (Content.Scale == value)
                    return;
                Content.Scale = value;
                ResetMeasurement();
            }
        }

        public float? MinScale = null;

        public AbstractString Text {
            get => Content.Text;
            protected set {
                SetText(value, null);
            }
        }

        protected bool SetText (AbstractString value, bool? onlyIfTextChanged = null) {
            // Don't perform a text compare for long strings
            var compareText = (onlyIfTextChanged ?? true) && (value.Length < 10240) && value.IsString;
            Content.SetText(value, compareText, false);
            if (!compareText)
                Content.Invalidate();
            return true;
        }

        internal bool SetTextInternal (AbstractString value, bool? onlyIfTextChanged = null) {
            return SetText(value, onlyIfTextChanged);
        }

        protected override void ComputeSizeConstraints (ref UIOperationContext context, ref ControlDimension width, ref ControlDimension height, Vector2 sizeScale) {
            base.ComputeSizeConstraints(ref context, ref width, ref height, sizeScale);

            if (AutoSizeWidth) {
                // FIXME: This is how it previously worked, but it might make sense for autosize to override Fixed
                if (AutoSizeIsMaximum && !width.Maximum.HasValue && !width.Fixed.HasValue)
                    width.Fixed = AutoSizeComputedWidth ?? width.Maximum;
                width.Minimum = ControlDimension.Max(width.Minimum, AutoSizeComputedWidth);
            }
            if (AutoSizeHeight) {
                if (AutoSizeIsMaximum && !height.Fixed.HasValue)
                    height.Maximum = AutoSizeComputedHeight ?? height.Maximum;
                height.Minimum = ControlDimension.Max(height.Minimum, AutoSizeComputedHeight);
            }

            // HACK
            MostRecentWidthConstraint = width;

            // FIXME: Do we need this? Should controls always do it?
            /*
            width.Constrain(ref width.Fixed, false);
            height.Constrain(ref height.Fixed, false);
            */
        }

        private void ConfigureMeasurement () {
            if (ContentMeasurement == null)
                ContentMeasurement = new DynamicStringLayout();
            if (Content.GlyphSource == null)
                throw new NullReferenceException("GlyphSource");
            ContentMeasurement.Copy(Content);
            ContentMeasurement.MeasureOnly = true;
            // HACK: If we never get painted (to validate our main content layout), then
            //  Content.IsValid will be false forever and we will recompute our measurement
            //  layout every frame. Not great, so... whatever
            Content.Get();
        }

        protected void ResetMeasurement () {
            ResetAutoSize();
            Content.DesiredWidth = 0;
            Content.LineBreakAtX = null;
            Content.Invalidate();
            if (ContentMeasurement != null) {
                ContentMeasurement.DesiredWidth = 0;
                ContentMeasurement.LineBreakAtX = null;
                ContentMeasurement.Invalidate();
            }
            _CachedPadding = default;
            _CachedLineBreakPoint = -99999f;
            _CachedContentIsSingleLine = null;
            _NeedRelayout = true;
            // TODO: Clear decoration cache too?
        }

        protected void ResetAutoSize () {
            AutoSizeComputedContentHeight = 0;
            AutoSizeComputedHeight = AutoSizeComputedWidth = null;
            // HACK: Ensure we do not erroneously wrap content that is intended to be used as an input for auto-size
            if (AutoSizeWidth)
                MostRecentContentBoxWidth = null;
        }

        private int _CachedTextVersion, _CachedTextLength;

        protected void AutoResetMeasurement () {
            // If wrapping and autosize are both enabled, text changes can cause
            //  very bad unnecessary wrapping to happen, so we want to forcibly reset
            //  all our measurement data to prevent it
            if (
                (_CachedTextVersion != Content.TextVersion) ||
                (_CachedTextLength != Content.Text.Length)
            ) {
                _CachedTextVersion = Content.TextVersion;
                _CachedTextLength = Content.Text.Length;
                ResetMeasurement();
            }
        }

        protected StringLayout GetCurrentLayout (bool measurement, bool autoReset = true) {
            if (_RichText.HasValue)
                Content.RichText = _RichText.Value;
            if (Content.RichText)
                Content.RichTextConfiguration = GetRichTextConfiguration();

            if (autoReset)
                AutoResetMeasurement();

            if (measurement) {
                if (!Content.IsValid || (ContentMeasurement == null)) {
                    ResetAutoSize();
                    ConfigureMeasurement();
                }
                if (!ContentMeasurement.IsValid) {
                    ResetAutoSize();
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
        private float _CachedLineBreakPoint;
        private bool? _CachedContentIsSingleLine;

        protected void ComputeAutoSize (ref UIOperationContext context, ref Margins computedPadding, ref Margins computedMargins) {
            // FIXME: If we start out constrained (by our parent size, etc) we will compute
            //  a compressed auto-size value here, and it will never be updated even if our parent
            //  gets bigger

            if (!AutoSizeWidth && !AutoSizeHeight) {
                ResetAutoSize();
                return;
            }

            var decorationProvider = context.DecorationProvider;
            var textDecorations = GetTextDecorator(decorationProvider, context.DefaultTextDecorator);
            var decorations = GetDecorator(decorationProvider, context.DefaultDecorator);
            var fontChanged = UpdateFont(ref context, textDecorations, decorations);

            var contentChanged = (ContentMeasurement?.IsValid == false) || !Content.IsValid;
            if (contentChanged || fontChanged)
                ResetAutoSize();

            var lineBreakPoint = Content.LineBreakAtX ?? 99999f;
            if (Math.Abs(_CachedLineBreakPoint - lineBreakPoint) > 0.5f)
                ResetAutoSize();

            if (
                (_CachedTextDecorations != textDecorations) ||
                (_CachedDecorations != decorations) ||
                !_CachedPadding.Equals(in computedPadding)
            ) {
                ResetAutoSize();
            }

            if (
                ((AutoSizeComputedWidth != null) == AutoSizeWidth) &&
                ((AutoSizeComputedHeight != null) == AutoSizeHeight)
            )
                return;

            _CachedLineBreakPoint = lineBreakPoint;
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
                AutoSizeComputedContentHeight = (Content.GlyphSource.LineSpacing * Content.Scale);
                AutoSizeComputedHeight = (float)Math.Ceiling(AutoSizeComputedContentHeight + computedPadding.Y);
                return;
            }

            var layout = GetCurrentLayout(true);
            if (AutoSizeWidth) {
                // HACK
                float w = Wrap
                    ? layout.Size.X
                    : layout.UnconstrainedSize.X;
                AutoSizeComputedWidth = (float)Math.Ceiling(w + computedPadding.X + AutoSizePadding);
                // FIXME: Something is wrong here if padding scale is active
                /* if ((sr.X > 1) || (sr.Y > 1))
                    AutoSizeComputedWidth += 1;
                    */
            }
            if (AutoSizeHeight) {
                AutoSizeComputedContentHeight = (layout.Size.Y);
                AutoSizeComputedHeight = (float)Math.Ceiling((layout.Size.Y) + computedPadding.Y);
            }
        }

        public override void InvalidateLayout () {
            base.InvalidateLayout();
            Invalidate();
        }

        protected void Invalidate () {
            _NeedRelayout = true;
            ResetAutoSize();
            Content.Invalidate();
            ContentMeasurement?.Invalidate();
        }

        protected override ControlKey OnGenerateLayoutTree (ref UIOperationContext context, ControlKey parent, ControlKey? existingKey) {
            var decorations = GetDecorator(context.DecorationProvider, context.DefaultDecorator);
            ComputeEffectiveSpacing(ref context, decorations, out Margins computedPadding, out Margins computedMargins);
            ComputeAutoSize(ref context, ref computedPadding, ref computedMargins);
            UpdateLineBreak(ref context, decorations, null, ref computedPadding, ref computedMargins);
            ComputeAutoSize(ref context, ref computedPadding, ref computedMargins);
            var result = base.OnGenerateLayoutTree(ref context, parent, existingKey);
            context.Layout.SetTag(result, LayoutTags.Text);

            // HACK: Ensure that we report all the textures we use even if we're not currently being rasterized
            if (Content.IsValid) {
                var layout = GetCurrentLayout(false);
                foreach (var tex in layout.UsedTextures)
                    context.UIContext.NotifyTextureUsed(this, tex);
            }

            return result;
        }

        protected override IDecorator GetDefaultDecorator (IDecorationProvider provider) {
            return provider?.StaticText ?? provider?.None;
        }

        protected float? ComputeTextWidthLimit (ref UIOperationContext context, IDecorator decorations, float? currentWidth, ref Margins computedPadding, ref Margins computedMargins) {
            ComputeEffectiveScaleRatios(context.DecorationProvider, out Vector2 paddingScale, out Vector2 marginScale, out Vector2 sizeScale);
            var spaceExpansion = 1.0f;
            var hasMinScale = (MinScale ?? 0) > 0.01f;
            if (_ScaleToFitX && hasMinScale)
                spaceExpansion = 1.0f / MinScale.Value;

            var width = Width;
            var height = Height;
            // HACK
            ComputeSizeConstraints(ref context, ref width, ref height, sizeScale);
            var hasWidthConstraint = (width.Fixed ?? width.Maximum).HasValue;
            var maxPx = (width.Fixed ?? width.Maximum) - computedPadding.X;

            // FIXME: Minimum of 1 instead of 0?
            if (TextAlignment >= HorizontalAlignment.JustifyCharacters)
                Content.DesiredWidth = (float)Math.Max(Math.Floor((width.Minimum ?? 0) - AutoSizePadding - computedPadding.X), 0);
            else
                Content.DesiredWidth = 0;

            currentWidth = currentWidth ?? MostRecentContentBoxWidth;

            if (!currentWidth.HasValue && !maxPx.HasValue)
                return null;

            if (maxPx.HasValue) {
                if (hasMinScale || (!_ScaleToFitX && !_ScaleToFitY)) {
                    if (Content.WordWrap && _ScaleToFitX) {
                        // HACK: This set of properties ensures that when horizontal scale to fit
                        //  is enabled, the text will prefer to word-wrap at its normal line boundaries,
                        //  and if the words overhang the content will then shrink to prevent them from 
                        //  overhanging the control's boundaries
                        Content.CharacterWrap = false;
                        Content.WordWrap = true;
                        Content.HideOverflow = false;
                        return maxPx.Value;
                    } else
                        return maxPx.Value * spaceExpansion;
                } else if (
                    hasWidthConstraint && 
                    (Content.WordWrap || Content.CharacterWrap) && 
                    (_ScaleToFitX || _ScaleToFitY)
                ) {
                    // HACK: If the user has set word/character wrap along with a constraint and enabled
                    //  auto scale, we want to suppress overflow so that the laid out text can overhang
                    Content.HideOverflow = false;
                    return maxPx.Value * spaceExpansion;
                } else
                    return null;
                
            } else if (!AutoSizeWidth && currentWidth.HasValue) {
                if (ScaleToFitX || ScaleToFitY) {
                    if (!hasMinScale)
                        return null;
                    
                }
                // FIXME: Handle MinScale
                /*
                if (!Wrap && ScaleToFitX)
                    return null;
                */
                // FIXME: Should we do this?
                return currentWidth.Value * spaceExpansion;
            } else {
                return null;
            }

            /*

            if (MostRecentContentBoxWidth.HasValue) {
                // FIXME
                float computed = MostRecentContentBoxWidth.Value * spaceExpansion;
                if (max.HasValue)
                    constrainedWidth = Math.Min(computed, max.Value);
                // FIXME: Without this, static text with wrapping turned on (the default...) will by default end up too small
                //  if they start out with a small string of text and are never updated with new text to invalidate their layout
                // Ideal algorithm here is probably 'if there is no maximum or fixed width, use our most recent width as an approximation
                //  of the size constraint from our parent'
                else if (!AutoSizeWidth)
                    constrainedWidth = computed;
            } else
                constrainedWidth = max;

            if (constrainedWidth.HasValue) {
                if (_ScaleToFitY)
                    constrainedWidth = constrainedWidth.Value / MostRecentYScaleFactor;
                // HACK: Suppress jitter
                return (float)Math.Ceiling(constrainedWidth.Value) + AutoSizePadding;
            } else
                return null;
            */
        }

        protected float ComputeScaleToFit (Vector2 constrainedSize, Vector2 unconstrainedSize, ref RectF box, ref Margins margins) {
            if (!_ScaleToFitX && !_ScaleToFitY) {
                MostRecentXScaleFactor = MostRecentYScaleFactor = 1;
                return 1;
            }

            float availableWidth = Math.Max(box.Width - margins.X, 0);
            float availableHeight = Math.Max(box.Height - margins.Y, 0);

            var size = (Wrap && _ScaleToFitX) ? constrainedSize : unconstrainedSize;

            if ((size.X > availableWidth) && _ScaleToFitX) {
                MostRecentXScaleFactor = availableWidth / (size.X + 0.1f);
            } else {
                MostRecentXScaleFactor = 1;
            }
            if ((size.Y > availableHeight) && _ScaleToFitY) {
                MostRecentYScaleFactor = availableHeight / (size.Y + 0.1f);
            } else {
                MostRecentYScaleFactor = 1;
            }

            var result = Math.Min(MostRecentXScaleFactor, MostRecentYScaleFactor);
            return result;
        }

        protected override bool IsPassDisabled (RasterizePasses pass, IDecorator decorations) {
            return decorations.IsPassDisabled(pass) && (pass != RasterizePasses.Content) && !ShouldClipContent;
        }

        protected Vector2 _LastDrawOffset, _LastDrawScale;
        protected BitmapDrawCall[] _LayoutFilterScratchBuffer;

        protected Vector2 ApplyScaleConstraints (Vector2 scale) {
            if (MinScale.HasValue) {
                var sizeFactor = Math.Min(scale.X / MinScale.Value, scale.Y / MinScale.Value);
                if (sizeFactor < 1)
                    return scale * 1.0f / sizeFactor;
            }

            return scale;
        }

        protected override void OnRasterize (ref UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings, IDecorator decorations) {
            // FIXME: This method allocates object[] sometimes

            base.OnRasterize(ref context, ref renderer, settings, decorations);

            if (context.Pass != RasterizePasses.Content)
                return;

            ComputeEffectiveSpacing(ref context, decorations, out Margins computedPadding, out Margins computedMargins);

            var overrideColor = GetTextColor(context.NowL);
            Color? defaultColor = 
                Appearance.TextColorIsDefault
                    ? overrideColor?.ToColor()
                    : (Color?)null;
            if (Appearance.TextColorIsDefault)
                overrideColor = null;
            Material material;
            var textDecorations = GetTextDecorator(context.DecorationProvider, context.DefaultTextDecorator);
            GetTextSettings(ref context, textDecorations, decorations, settings.State, out material, ref defaultColor, out Vector4 userData);
            material = CustomTextMaterial ?? material;

            Content.DefaultColor = defaultColor ?? Color.White;

            settings.Box.SnapAndInset(out Vector2 a, out Vector2 b);
            settings.ContentBox.SnapAndInset(out Vector2 ca, out Vector2 cb);

            Vector2 textOffset = Vector2.Zero, textScale = Vector2.One;
            decorations?.GetContentAdjustment(ref context, settings.State, out textOffset, out textScale);

            UpdateLineBreak(ref context, decorations, settings.ContentBox.Width, ref computedPadding, ref computedMargins);

            var layout = GetCurrentLayout(false, false);
            textScale *= ComputeScaleToFit(layout.Size, layout.UnconstrainedSize, ref settings.Box, ref computedPadding);
            textScale = ApplyScaleConstraints(textScale);

            var scaledSize = (layout.Size * textScale).Floor();

            // If a fallback glyph source's child sources are different heights, the autosize can end up producing
            //  a box that is too big for the content. In that case, we want to center it vertically
            bool willCenterBasedOnAutoSize = (AutoSizeComputedHeight.HasValue) && (AutoSizeComputedContentHeight > scaledSize.Y),
                heightGovernedByAutoSize = false;
            if (willCenterBasedOnAutoSize) {
                var width = Width;
                var height = Height;
                // FIXME: SizeScale
                ComputeSizeConstraints(ref context, ref width, ref height, context.DecorationProvider.SizeScaleRatio);
                // If the control's height is being set by a size constraint even though autosize is enabled (ugh, 
                //  don't do this), then we don't want to do vertical centering based on the autosize value since
                //  that will look completely wrong
                heightGovernedByAutoSize = !height.Fixed.HasValue && ((height.Maximum ?? 0) > AutoSizeComputedHeight);
            }

            if (heightGovernedByAutoSize) {
                var autoSizeYCentering = (AutoSizeComputedContentHeight - scaledSize.Y) * VerticalAlignment;
                textOffset.Y += autoSizeYCentering;
            } else {
                // Vertically center the text as configured
                textOffset.Y += (settings.ContentBox.Height - scaledSize.Y) * VerticalAlignment;
            }

            var cpx = computedPadding.X;
            var centeringWidth = scaledSize.X;
            var xSpace = (b.X - a.X) - centeringWidth - cpx;
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

            if (VisualizeLayout)
                DoVisualizeLayout(ref renderer, ca, cb, textOffset, scaledSize);

            // FIXME: Why is this here?
            /*
            if (layout.DrawCalls.Count > 0)
                renderer.Layer += 1;
            */

            var segment = layout.DrawCalls;
            if (segment.Count <= 0)
                return;

            if (LayoutFilter != null) {
                var size = layout.DrawCalls.Count;
                if ((_LayoutFilterScratchBuffer == null) || (_LayoutFilterScratchBuffer.Length < size))
                    _LayoutFilterScratchBuffer = new BitmapDrawCall[size + 256];
                var temp = layout;
                Array.Copy(layout.DrawCalls.Array, layout.DrawCalls.Offset, _LayoutFilterScratchBuffer, 0, layout.DrawCalls.Count);
                temp.DrawCalls = new ArraySegment<BitmapDrawCall>(_LayoutFilterScratchBuffer, 0, layout.DrawCalls.Count);
                LayoutFilter(this, ref temp);
                segment = temp.DrawCalls;
            } else {
                _LayoutFilterScratchBuffer = null;
            }

            if (CharacterLimit != null)
                segment = new ArraySegment<BitmapDrawCall>(segment.Array, segment.Offset, Math.Min(CharacterLimit.Value, segment.Count));

            var opacity = context.Opacity;
            var multiplyOpacity = (opacity < 1) ? opacity : (float?)null;

            renderer.DrawMultiple(
                segment, offset: (textOffset + ca).Floor(),
                material: material, samplerState: RenderStates.Text,
                scale: textScale, multiplyColor: overrideColor?.ToColor(),
                userData: RasterizerUserData ?? userData, multiplyOpacity: multiplyOpacity 
            );

            _LastDrawOffset = textOffset.Floor();
            _LastDrawScale = textScale;
        }

        private void DoVisualizeLayout (ref  ImperativeRenderer renderer, Vector2 ca, Vector2 cb, Vector2 textOffset, Vector2 scaledSize) {
            renderer.RasterizeRectangle(textOffset + ca, textOffset + ca + scaledSize, 0f, 1f, Color.Transparent, Color.Transparent, outlineColor: Color.Blue, layer: 1);
            if (Content.LineBreakAtX.HasValue) {
                var la = new Vector2(ca.X, ca.Y) + new Vector2(Content.LineBreakAtX ?? 0, 0);
                var lb = new Vector2(ca.X, cb.Y) + new Vector2(Content.LineBreakAtX ?? 0, 0);
                var lc = new Vector2(ca.X, la.Y);
                renderer.RasterizeLineSegment(la, lb, 1f, Color.Green, layer: 2);
                renderer.RasterizeLineSegment(la, lc, 1f, Color.Green, layer: 2);
            }

            foreach (var contentBox in Content.Boxes)
                renderer.RasterizeRectangle(
                    contentBox.TopLeft + textOffset + ca, contentBox.BottomRight + textOffset + ca, 1f, 1f,
                    Color.Transparent, Color.Transparent, Color.Red, layer: 3
                );

            renderer.Layer += 1;
        }

        protected void UpdateLineBreak (ref UIOperationContext context, IDecorator decorations, float? currentWidth, ref Margins computedPadding, ref Margins computedMargins) {
            var textWidthLimit = ComputeTextWidthLimit(ref context, decorations, currentWidth, ref computedPadding, ref computedMargins);
            if (textWidthLimit.HasValue)
                Content.LineBreakAtX = (float)Math.Ceiling(textWidthLimit.Value + LineBreakRightPadding);
            else
                Content.LineBreakAtX = null;
        }

        private IGlyphSource _MostRecentFont;
        private int _MostRecentFontVersion;
        private bool SyncWithCurrentFont (IGlyphSource font) {
            var result = false;
            if ((font != _MostRecentFont) || (font.Version != _MostRecentFontVersion)) {
                ResetMeasurement();
                if (Content.GlyphSource == _MostRecentFont) {
                    Content.GlyphSource = font;
                    result = true;
                }
                _MostRecentFont = font;
                _MostRecentFontVersion = font.Version;
            }

            return result;
        }

        protected bool UpdateFont (ref UIOperationContext context, IDecorator textDecorations, IDecorator decorations) {
            IGlyphSource font = null;
            if (Appearance.GlyphSourceProvider != null)
                font = Appearance.GlyphSourceProvider();
            font = font ?? Appearance.GlyphSource ?? textDecorations?.GlyphSource ?? decorations.GlyphSource;
            if (font == null)
                throw new NullReferenceException($"Decorators provided no font for control {this} ({textDecorations}, {decorations})");
            return SyncWithCurrentFont(font);
        }

        protected bool GetTextSettings (
            ref UIOperationContext context, IDecorator textDecorations, IDecorator decorations, ControlStates state, 
            out Material material, ref Color? color, out Vector4 userData
        ) {
            (textDecorations ?? decorations).GetTextSettings(ref context, state, out material, ref color, out userData);
            SyncWithCurrentFont(Appearance.GlyphSource ?? textDecorations?.GlyphSource ?? decorations.GlyphSource);
            if (TextMaterial != null)
                material = TextMaterial;
            return false;
        }

        protected string GetPlainText () {
            return RichText
                ? Squared.Render.Text.RichText.ToPlainText(Text)
                : Text.ToString();
        }

        public static string GetTrimmedText (string text) {
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
                return TooltipContent.GetPlainText(this);

            return plainText;
        }

        protected virtual void FormatValueInto (StringBuilder sb) {
            sb.Append(Text);
        }

        AbstractString Accessibility.IReadingTarget.Text => GetReadingText();
        void Accessibility.IReadingTarget.FormatValueInto (StringBuilder sb) => FormatValueInto(sb);

        protected virtual void OnLayoutComplete( ref UIOperationContext context, ref bool relayoutRequested) {
            if (_NeedRelayout) {
                relayoutRequested = true;
                _NeedRelayout = false;
            }

            context.Layout.GetRects(LayoutKey, out RectF box, out RectF contentBox);
            MostRecentContentBoxWidth = contentBox.Width;
            MostRecentWidth = box.Width;

            // FIXME: This is probably wrong?
            if (!AutoSizeWidth && !Wrap && MostRecentContentBoxWidth.HasValue)
                return;
        }

        void IPostLayoutListener.OnLayoutComplete (
            ref UIOperationContext context, ref bool relayoutRequested
        ) {
            OnLayoutComplete(ref context, ref relayoutRequested);
        }
    }

    public delegate void StringLayoutFilter (StaticTextBase control, ref StringLayout layout);

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
        new public bool ScaleToFitX {
            get => base.ScaleToFitX;
            set => base.ScaleToFitX = value;
        }
        new public bool ScaleToFitY {
            get => base.ScaleToFitY;
            set => base.ScaleToFitY = value;
        }
        new public bool AcceptsMouseInput {
            get => base.AcceptsMouseInput;
            set => base.AcceptsMouseInput = value;
        }
        new public StringLayoutFilter LayoutFilter {
            get => base.LayoutFilter;
            set => base.LayoutFilter = value;
        }
        new public int? CharacterLimit {
            get => base.CharacterLimit;
            set => base.CharacterLimit = value;
        }

        public RichTextConfiguration RichTextConfiguration { get; set; }
        protected override RichTextConfiguration GetRichTextConfiguration () =>
            RichTextConfiguration ?? base.GetRichTextConfiguration();

        new public void Invalidate () => base.Invalidate();

        new public bool SetText (AbstractString value, bool? onlyIfTextChanged = null) =>
            base.SetText(value, onlyIfTextChanged);

        public StaticText ()
            : base () {
            Content.WordWrap = true;
            Content.CharacterWrap = false;
        }

        protected override bool CanApplyOpacityWithoutCompositing => Appearance.BackgroundColor.IsTransparent && (CustomTextMaterial == null);
    }
}
