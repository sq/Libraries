using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
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
        [Flags]
        private enum StaticTextStateFlags : ushort {
            AutoSizeWidth                  = 0b1,
            AutoSizeHeight                 = 0b10,
            AutoSizeIsMaximum              = 0b100,
            NeedRelayout                   = 0b1000,
            ScaleToFitX                    = 0b10000,
            ScaleToFitY                    = 0b100000,
            DidUseTextures                 = 0b1000000,
            RichTextIsSet                  = 0b10000000,
            RichTextValue                  = 0b100000000,
            UseTooltipForReadingIsSet      = 0b1000000000,
            UseTooltipForReadingValue      = 0b10000000000,
            CachedContentIsSingleLineIsSet = 0b100000000000,
            CachedContentIsSingleLineValue = 0b1000000000000,
        }

        public StaticTextBase ()
            : base () {
            // Multiline = false;
        }

        public static GlyphPixelAlignment DefaultGlyphPixelAlignment = GlyphPixelAlignment.Default;
        public const float LineBreakRightPadding = 1.1f;
        public const float AutoSizePadding = 0.5f;
        public const bool DiagnosticText = false;

        /// <summary>
        /// Rasterizes boxes for each text control's box, content box, and text layout box
        /// Also rasterizes a yellow line for the wrap/break threshold
        /// </summary>
        public static bool VisualizeLayout = false;

        /// <summary>
        /// If true, the control will have its size set exactly to fit its content.
        /// If false, the control will be expanded to fit its content but will not shrink.
        /// </summary>
        public bool AutoSizeIsMaximum {
            get => GetInternalFlag(StaticTextStateFlags.AutoSizeIsMaximum);
            set => SetInternalFlag(StaticTextStateFlags.AutoSizeIsMaximum, value);
        }

        /// <summary>
        /// If set, accessibility reading will use this control's tooltip instead of its text.
        /// The default is to do this automatically if the control's text is short (1-2 characters)
        ///  and the control has a tooltip.
        /// </summary>
        public bool? UseTooltipForReading {
            get => GetInternalFlag(StaticTextStateFlags.UseTooltipForReadingIsSet, StaticTextStateFlags.UseTooltipForReadingValue);
            set => SetInternalFlag(StaticTextStateFlags.UseTooltipForReadingIsSet, StaticTextStateFlags.UseTooltipForReadingValue, value);
        }

        public Material TextMaterial = null;
        protected DynamicStringLayout Content = new DynamicStringLayout {
            HideOverflow = true,
            RecordUsedTextures = true,
            AlignToPixels = DefaultGlyphPixelAlignment,
            ExpandHorizontallyWhenAligning = false,
            DisableMarkers = true
        };
        private DynamicStringLayout ContentMeasurement = null;
        private float? MostRecentWidthForLineBreaking = null, 
            MostRecentWidth = null;

        protected int? CharacterLimit { get; set; }

        private float? AutoSizeComputedWidth, AutoSizeComputedHeight;
        private float AutoSizeComputedContentHeight;
        private float MostRecentXScaleFactor = 1, MostRecentYScaleFactor = 1;
        private ControlDimension MostRecentWidthConstraint;

        protected virtual Material CustomTextMaterial => null;

        public Vector4? RasterizerUserData;

        public float VerticalAlignment = 0.5f;

        protected float? MinScale = null;

        private int _CachedTextVersion, _CachedTextLength;

        private IDecorator _CachedTextDecorations,
            _CachedDecorations;
        private Margins _CachedPadding;
        private float _CachedLineBreakPoint;

        protected Vector2 _LastDrawOffset, _LastDrawScale;
        protected BitmapDrawCall[] _LayoutFilterScratchBuffer;

        private IGlyphSource _MostRecentFont;
        private int _MostRecentFontVersion;
        protected RasterizePasses Pass = RasterizePasses.Content;

        private StaticTextStateFlags InternalState = 
            StaticTextStateFlags.AutoSizeWidth | StaticTextStateFlags.AutoSizeHeight | StaticTextStateFlags.AutoSizeIsMaximum;

        internal bool TextLayoutIsIncomplete => IsLayoutInvalid || 
            Content.IsAwaitingDependencies || 
            // In some cases the measurement layout will be awaiting dependencies but content isn't, because we haven't been rendered yet
            (ContentMeasurement?.IsAwaitingDependencies ?? false);

        private bool GetInternalFlag (StaticTextStateFlags flag) {
            return (InternalState & flag) == flag;
        }

        private bool? GetInternalFlag (StaticTextStateFlags isSetFlag, StaticTextStateFlags valueFlag) {
            if ((InternalState & isSetFlag) != isSetFlag)
                return null;
            else
                return (InternalState & valueFlag) == valueFlag;
        }

        private void SetInternalFlag (StaticTextStateFlags flag, bool state) {
            if (state)
                InternalState |= flag;
            else
                InternalState &= ~flag;
        }

        private bool ChangeInternalFlag (StaticTextStateFlags flag, bool newState) {
            if (GetInternalFlag(flag) == newState)
                return false;

            SetInternalFlag(flag, newState);
            return true;
        }

        private void SetInternalFlag (StaticTextStateFlags isSetFlag, StaticTextStateFlags valueFlag, bool? state) {
            SetInternalFlag(isSetFlag, state.HasValue);
            SetInternalFlag(valueFlag, state.HasValue && state.Value);
        }

        private bool ChangeInternalFlag (StaticTextStateFlags isSetFlag, StaticTextStateFlags valueFlag, bool? newState) {
            if (GetInternalFlag(isSetFlag, valueFlag) == newState)
                return false;

            SetInternalFlag(isSetFlag, valueFlag, newState);
            return true;
        }

        public bool RichText {
            get => GetInternalFlag(StaticTextStateFlags.RichTextIsSet, StaticTextStateFlags.RichTextValue) ?? Content.RichText;
            set {
                SetInternalFlag(StaticTextStateFlags.RichTextIsSet, StaticTextStateFlags.RichTextValue, value);
                Content.RichText = value;
            }
        }

        protected virtual RichTextConfiguration GetRichTextConfiguration() => Context.RichTextConfiguration;

        protected bool ScaleToFitX {
            get => GetInternalFlag(StaticTextStateFlags.ScaleToFitX);
            set {
                if (ChangeInternalFlag(StaticTextStateFlags.ScaleToFitX, value))
                    Invalidate();
            }
        }

        protected bool ScaleToFitY {
            get => GetInternalFlag(StaticTextStateFlags.ScaleToFitY);
            set {
                if (ChangeInternalFlag(StaticTextStateFlags.ScaleToFitY, value))
                    Invalidate();
            }
        }

        protected bool ScaleToFit {
            get => GetInternalFlag(StaticTextStateFlags.ScaleToFitX) && GetInternalFlag(StaticTextStateFlags.ScaleToFitY);
            set {
                bool a = ChangeInternalFlag(StaticTextStateFlags.ScaleToFitX, value),
                    b = ChangeInternalFlag(StaticTextStateFlags.ScaleToFitY, value);

                if (a || b)
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
            get => GetInternalFlag(StaticTextStateFlags.AutoSizeWidth);
            set {
                if (ChangeInternalFlag(StaticTextStateFlags.AutoSizeWidth, value))
                    Content.Invalidate();
            }
        }

        public bool AutoSizeHeight {
            get => GetInternalFlag(StaticTextStateFlags.AutoSizeHeight);
            set {
                if (ChangeInternalFlag(StaticTextStateFlags.AutoSizeHeight, value))
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

        public float Scale {
            get => Content.Scale;
            set {
                if (Content.Scale == value)
                    return;
                Content.Scale = value;
                ResetMeasurement();
            }
        }

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
                if (AutoSizeIsMaximum && !width.HasMaximum && !width.HasFixed)
                    width.Fixed = AutoSizeComputedWidth ?? width.Maximum;
                width.Minimum = ControlDimension.Max(width.Minimum, AutoSizeComputedWidth);
            }
            if (AutoSizeHeight) {
                if (AutoSizeIsMaximum && !height.HasFixed)
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
            SetInternalFlag(StaticTextStateFlags.CachedContentIsSingleLineIsSet, StaticTextStateFlags.CachedContentIsSingleLineValue, null);
            SetInternalFlag(StaticTextStateFlags.NeedRelayout, true);
            // TODO: Clear decoration cache too?
        }

        protected void ResetAutoSize () {
            AutoSizeComputedContentHeight = 0;
            AutoSizeComputedHeight = AutoSizeComputedWidth = null;
            // HACK: Ensure we do not erroneously wrap content that is intended to be used as an input for auto-size
            if (AutoSizeWidth)
                MostRecentWidthForLineBreaking = null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected void AutoResetMeasurement () {
            // If wrapping and autosize are both enabled, text changes can cause
            //  very bad unnecessary wrapping to happen, so we want to forcibly reset
            //  all our measurement data to prevent it
            var textVersion = Content.TextVersion;
            var textLength = Content.Text.Length;
            if (
                (_CachedTextVersion != textVersion) ||
                (_CachedTextLength != textLength)
            ) {
                _CachedTextVersion = textVersion;
                _CachedTextLength = textLength;
                ResetMeasurement();
            }
        }

        protected void GetCurrentLayout (out StringLayout result, bool measurement, bool autoReset = true) {
            if (GetInternalFlag(StaticTextStateFlags.RichTextIsSet))
                Content.RichText = GetInternalFlag(StaticTextStateFlags.RichTextValue);
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
                    SetInternalFlag(StaticTextStateFlags.NeedRelayout, true);
                }
                ContentMeasurement.Get(out result);
            } else {
                if (!Content.IsValid) {
                    if (ContentMeasurement != null)
                        ConfigureMeasurement();
                    SetInternalFlag(StaticTextStateFlags.NeedRelayout, true);
                }
                Content.Get(out result);
                SetInternalFlag(StaticTextStateFlags.DidUseTextures, (result.UsedTextures?.Count ?? 0) > 0);
            }
        }

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

            if (contentChanged || fontChanged || !GetInternalFlag(StaticTextStateFlags.CachedContentIsSingleLineIsSet)) {
                // HACK: If we're pretty certain the text will be exactly one line long and we don't
                //  care how wide it is, just return the line spacing without performing layout
                SetInternalFlag(
                    StaticTextStateFlags.CachedContentIsSingleLineIsSet, StaticTextStateFlags.CachedContentIsSingleLineValue,
                    (AutoSizeHeight && !AutoSizeWidth) &&
                    (!Content.CharacterWrap && !Content.WordWrap) &&
                    (
                        (Content.LineLimit == 1) ||
                        Content.Text.Length < 1 ||
                        ((Content.Text.Length < 512) && !Content.Text.Contains('\n'))
                    )
                );
            }

            if (GetInternalFlag(StaticTextStateFlags.CachedContentIsSingleLineIsSet, StaticTextStateFlags.CachedContentIsSingleLineValue) == true) {
                AutoSizeComputedContentHeight = (Content.GlyphSource.LineSpacing * Content.Scale);
                AutoSizeComputedHeight = (float)Math.Ceiling(AutoSizeComputedContentHeight + computedPadding.Y);
                return;
            }

            GetCurrentLayout(out var layout, true);
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
            SetInternalFlag(StaticTextStateFlags.NeedRelayout, true);
            ResetAutoSize();
            Content.Invalidate();
            ContentMeasurement?.Invalidate();
        }

        protected override ControlKey OnGenerateLayoutTree (ref UIOperationContext context, ControlKey parent, ControlKey? existingKey) {
            var decorationProvider = context.DecorationProvider;
            var decorations = GetDecorator(decorationProvider, context.DefaultDecorator);
            ComputeEffectiveSpacing(ref context, decorationProvider, decorations, out Margins computedPadding, out Margins computedMargins);
            ComputeAutoSize(ref context, ref computedPadding, ref computedMargins);
            UpdateLineBreak(ref context, decorations, null, ref computedPadding, ref computedMargins);
            ComputeAutoSize(ref context, ref computedPadding, ref computedMargins);
            var result = base.OnGenerateLayoutTree(ref context, parent, existingKey);
            if (result.IsInvalid)
                return result;
            Record(ref context).Tag = LayoutTags.Text;

            // HACK: Ensure that we report all the textures we use even if we're not currently being rasterized
            if (Content.IsValid && GetInternalFlag(StaticTextStateFlags.DidUseTextures)) {
                // FIXME: This is slow
                GetCurrentLayout(out var layout, false);
                if (layout.UsedTextures != null)
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
            if (ScaleToFitX && hasMinScale)
                spaceExpansion = 1.0f / MinScale.Value;

            var width = Width;
            var height = Height;
            // HACK
            ComputeSizeConstraints(ref context, ref width, ref height, sizeScale);
            var hasWidthConstraint = (width.Fixed ?? width.Maximum).HasValue;
            var maxPx = (width.Fixed ?? width.Maximum) - computedPadding.X;

            if (TextAlignment >= HorizontalAlignment.JustifyCharacters) {
                // FIXME: Minimum of 1 instead of 0?
                var dw = (float)Math.Max(Math.Floor((Width.Minimum ?? 0) - AutoSizePadding - computedPadding.X), 0);
                // HACK: If AutoSize is disabled and there is no size constraint, we should try to use the line break point as
                //  our desired size for expansion so that our text will properly fill our available space. Not doing this will
                //  result in an ugly transparent gutter on the right side after justification.
                // FIXME: Should S.R do this?
                if ((dw <= 1) && Content.LineBreakAtX.HasValue && Content.ExpandHorizontallyWhenAligning && AutoSizeIsMaximum)
                    dw = Content.LineBreakAtX.Value;
                else if ((dw <= 1) && Content.ExpandHorizontallyWhenAligning && MostRecentWidth.HasValue)
                    // HACK: In justify mode if expansion is enabled, we want to fill our entire box even if we have no explicit
                    //  width constraint. This allows having multiple autosized columns in a container that will be justified
                    //  naturally with each one filling its space with text
                    // Without doing this, there would be a gutter on the right side.
                    dw = MostRecentWidth.Value - computedPadding.X - AutoSizePadding;
                Content.DesiredWidth = dw;
            } else
                Content.DesiredWidth = 0;

            if (currentWidth.HasValue)
                MostRecentWidthForLineBreaking = currentWidth;
            else
                currentWidth = MostRecentWidthForLineBreaking;

            if (!currentWidth.HasValue && !maxPx.HasValue)
                return null;

            if (maxPx.HasValue) {
                bool scaleToFitX = ScaleToFitX, scaleToFitY = ScaleToFitY;
                if (hasMinScale || (!scaleToFitX && !scaleToFitY)) {
                    if (Content.WordWrap && scaleToFitX) {
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
                    (scaleToFitX || scaleToFitY)
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
            bool scaleToFitX = ScaleToFitX, scaleToFitY = ScaleToFitY;
            if (!scaleToFitX && !scaleToFitY) {
                MostRecentXScaleFactor = MostRecentYScaleFactor = 1;
                return 1;
            }

            float availableWidth = Math.Max(box.Width - margins.X, 0);
            float availableHeight = Math.Max(box.Height - margins.Y, 0);

            var size = (Wrap && scaleToFitX) ? constrainedSize : unconstrainedSize;

            if ((size.X > availableWidth) && scaleToFitX) {
                MostRecentXScaleFactor = availableWidth / (size.X + 0.1f);
            } else {
                MostRecentXScaleFactor = 1;
            }
            if ((size.Y > availableHeight) && scaleToFitY) {
                MostRecentYScaleFactor = availableHeight / (size.Y + 0.1f);
            } else {
                MostRecentYScaleFactor = 1;
            }

            var result = Math.Min(MostRecentXScaleFactor, MostRecentYScaleFactor);
            return result;
        }

        protected override bool IsPassDisabled (RasterizePasses pass, IDecorator decorations) {
            return decorations.IsPassDisabled(pass) && (pass != Pass) && !ShouldClipContent;
        }

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

            if (context.Pass != Pass)
                return;

            var decorationProvider = context.DecorationProvider;
            ComputeEffectiveSpacing(ref context, decorationProvider, decorations, out Margins computedPadding, out Margins computedMargins);

            var overrideColor = GetTextColor(context.NowL);
            Color? defaultColor = 
                Appearance.TextColorIsDefault
                    ? overrideColor?.ToColor()
                    : (Color?)null;
            if (Appearance.TextColorIsDefault)
                overrideColor = null;
            Material material;
            var textDecorations = GetTextDecorator(decorationProvider, context.DefaultTextDecorator);
            GetTextSettings(ref context, textDecorations, decorations, settings.State, out material, ref defaultColor, out Vector4 userData);
            material = Appearance.TextMaterial ?? CustomTextMaterial ?? material;

            Content.DefaultColor = defaultColor ?? Color.White;

            settings.Box.SnapAndInset(out Vector2 a, out Vector2 b);
            settings.ContentBox.SnapAndInset(out Vector2 ca, out Vector2 cb);

            Vector2 textOffset = Vector2.Zero, textScale = Vector2.One;
            decorations?.GetContentAdjustment(ref context, settings.State, out textOffset, out textScale);

            UpdateLineBreak(ref context, decorations, settings.ContentBox.Width, ref computedPadding, ref computedMargins);

            GetCurrentLayout(out var layout, false, false);
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
                heightGovernedByAutoSize = !height.HasFixed && ((height.Maximum ?? 0) > AutoSizeComputedHeight);
            }

            if (heightGovernedByAutoSize) {
                var autoSizeYCentering = (AutoSizeComputedContentHeight - scaledSize.Y) * VerticalAlignment;
                textOffset.Y += autoSizeYCentering;
            } else {
                // Vertically center the text as configured
                textOffset.Y += (settings.ContentBox.Height - scaledSize.Y) * VerticalAlignment;
            }

            var cpx = computedPadding.X;
            var centeringWidth = (layout.LineCount > 1) 
                ? scaledSize.X
                // HACK: Some fonts generate lots of whitespace at the end of a line, so for single-line
                //  text we can produce better optical centering by using the rightmost pixel instead
                : Math.Min(scaledSize.X, layout.LastCharacterBounds.BottomRight.X);
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
                userData: RasterizerUserData ?? userData, 
                multiplyOpacity: multiplyOpacity,
                blendState: (material == null) ? RenderStates.PorterDuffOver : null
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
            float? newValue = textWidthLimit.HasValue
                ? (float)Math.Ceiling(textWidthLimit.Value + LineBreakRightPadding)
                : (float?)null;
            Content.LineBreakAtX = newValue;
        }

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
            font = font ?? Appearance.GlyphSource;
            if (font == null) {
                var tempBox = default(RectF);
                MakeDecorationSettings(ref tempBox, ref tempBox, GetCurrentState(ref context), false, true, out var settings);
                font = GetGlyphSource(ref context, textDecorations, ref settings) ?? 
                    GetGlyphSource(ref context, decorations, ref settings);
            }
            if (font == null)
                throw new NullReferenceException($"Decorators provided no font for control {this} ({textDecorations}, {decorations})");
            return SyncWithCurrentFont(font);
        }

        protected bool GetTextSettings (
            ref UIOperationContext context, IDecorator textDecorations, IDecorator decorations, ControlStates state, 
            out Material material, ref Color? color, out Vector4 userData
        ) {
            (textDecorations ?? decorations).GetTextSettings(ref context, state, out material, ref color, out userData);
            SyncWithCurrentFont(Appearance.GlyphSource ?? GetGlyphSource(ref context, textDecorations) ?? GetGlyphSource(ref context, decorations));
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
            var useTooltipByDefault = string.IsNullOrWhiteSpace(plainText) || (plainText.Length <= 2);
            if (UseTooltipForReading ?? useTooltipByDefault)
                return TooltipContent.GetPlainText(this);

            return plainText;
        }

        protected virtual void FormatValueInto (StringBuilder sb) {
            sb.Append(Text);
        }

        AbstractString Accessibility.IReadingTarget.Text => GetReadingText();
        void Accessibility.IReadingTarget.FormatValueInto (StringBuilder sb) => FormatValueInto(sb);

        protected virtual void OnLayoutComplete( ref UIOperationContext context, ref bool relayoutRequested) {
            if (LayoutKey.IsInvalid)
                return;

            if (ChangeInternalFlag(StaticTextStateFlags.NeedRelayout, false))
                relayoutRequested = true;

            ref var result = ref LayoutResult(ref context);
            if (result.Rect.Width != MostRecentWidth) {
                MostRecentWidth = result.Rect.Width;
                // HACK: This ensures that if our width changes, we recompute our justified layout to eliminate
                //  any gutter on the right side.
                if (
                    (TextAlignment >= HorizontalAlignment.JustifyCharacters) &&
                    Content.ExpandHorizontallyWhenAligning
                ) {
                    relayoutRequested = true;
                }
            }
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
        new public RasterizePasses Pass {
            get => base.Pass;
            set => base.Pass = value;
        }
        new public float? MinScale {
            get => base.MinScale;
            set => base.MinScale = value;
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

        protected override bool NeedsComposition (bool hasOpacity, bool hasTransform) {
            if (Appearance.BackgroundColor.IsTransparent && (CustomTextMaterial == null) && !(this is HyperText)) {
                hasOpacity = hasTransform = false;
            }
            return base.NeedsComposition(hasOpacity, hasTransform);
        }
    }
}
