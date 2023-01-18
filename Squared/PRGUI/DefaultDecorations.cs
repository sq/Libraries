using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Game;
using Squared.PRGUI.Decorations;
using Squared.PRGUI.Layout;
using Squared.Render;
using Squared.Render.Convenience;
using Squared.Render.RasterShape;
using Squared.Render.Text;
using Squared.Util;

namespace Squared.PRGUI {
    public class DefaultDecorationColorScheme {
        public pSRGBColor Focused = new Color(170, 200, 255),
            Active = new Color(245, 245, 245),
            Inactive = new Color(170, 170, 170),
            ContainerOutline = new Color(32, 32, 32) * 0.5f,
            InertOutline = new Color(255, 255, 255) * 0.33f,
            TooltipOutline = new Color(16, 16, 16) * 0.5f,
            ScrollbarThumb = new Color(200, 200, 200),
            ScrollbarTrack = new Color(48, 48, 48),
            AcceleratorOutline = pSRGBColor.White();

        public pSRGBColor TitleFill = new Color(40, 100, 120),
            ContainerFill = pSRGBColor.Transparent,
            InertFill = pSRGBColor.Transparent,
            SelectionFill = new Color(200, 230, 255),
            TooltipFill = new Color(48, 48, 48),
            SliderFill = pSRGBColor.Black(0.1f),
            AcceleratorFill = pSRGBColor.Black(0.8f),
            GaugeFill = pSRGBColor.Black(0.1f),
            GaugeLimitFill = new Color(64, 64, 64),
            GaugeValueFill = pSRGBColor.Transparent;

        public Color SelectedText = new Color(0, 30, 55),
            TitleText = Color.White,
            Text = Color.White,
            TooltipText = Color.White,
            AcceleratorText = Color.White;

        public Color? FloatingContainerOutline, 
            FloatingContainerFill,
            WindowFill = new Color(60, 60, 60);

        public float GaugeFillAlpha1 = 0.5f, GaugeFillAlpha2 = 1.0f,
            GaugeLimitAlpha = 0.7f,
            GaugeFillBrightness1 = 1.0f, GaugeFillBrightness2 = 1.0f;

        public DefaultDecorationColorScheme () {
            GaugeValueFill = SelectionFill;
        }
    }

    public class DefaultDecorations : IDecorationProvider, IAnimationProvider {
        public DefaultDecorationColorScheme ColorScheme =
            new DefaultDecorationColorScheme();

        public readonly DefaultMaterialSet Materials;
        public readonly float GlobalDefaultMargin,
            GlobalDefaultMarginCollapsed;

        public IDecorator None { get; set; }

        public IDecorator Button { get; set; }
        public IDecorator Container { get; set; }
        public IDecorator TitledContainer { get; set; }
        public IDecorator ContainerTitle { get; set; }
        public IDecorator FloatingContainer { get; set; }
        public IDecorator Window { get; set; }
        public IDecorator WindowTitle { get; set; }
        public IDecorator StaticText { get; set; }
        public IDecorator StaticImage { get; set; }
        public IDecorator EditableText { get; set; }
        public IDecorator Selection { get; set; }
        public IDecorator Tooltip { get; set; }
        public IDecorator Menu { get; set; }
        public IDecorator MenuSelection { get; set; }
        public IDecorator ListBox { get; set; }
        public IDecorator ListSelection { get; set; }
        public IDecorator CompositionPreview { get; set; }
        public IDecorator Checkbox { get; set; }
        public IDecorator RadioButton { get; set; }
        public IDecorator Slider { get; set; }
        public IDecorator SliderThumb { get; set; }
        public IDecorator Dropdown { get; set; }
        public IDecorator DropdownArrow { get; set; }
        public IDecorator AcceleratorLabel { get; set; }
        public IDecorator AcceleratorTarget { get; set; }
        public IDecorator ParameterGauge { get; set; }
        public IDecorator Gauge { get; set; }
        public IDecorator VirtualCursor { get; set; }
        public IDecorator VirtualCursorAnchor { get; set; }
        public IDecorator Tab { get; set; }
        public IDecorator TabPage { get; set; }
        public IDecorator Canvas { get; set; }
        public IDecorator HyperTextHotspot { get; set; }
        public IDecorator LoadingSpinner { get; set; }

        public float AnimationDurationMultiplier { get; set; }

        public IControlAnimation ShowModalDialog { get; set; }
        public IControlAnimation HideModalDialog { get; set; }
        public IControlAnimation ShowMenu { get; set; }
        public IControlAnimation HideMenu { get; set; }

        public IMetricsProvider Description { get; set; }

        public IWidgetDecorator<ScrollbarState> Scrollbar { get; set; }

        private Vector2 _SizeScaleRatio;
        /// <summary>
        /// Sets a scale factor for minimum/fixed/maximum sizes
        /// </summary>
        public Vector2 SizeScaleRatio {
            get => _SizeScaleRatio;
            set {
                _SizeScaleRatio = value;
                UpdateScaledSizes();
            }
        }
        /// <summary>
        /// Sets a global scale factor for both padding and margins
        /// </summary>
        public Vector2 SpacingScaleRatio { get; set; }
        /// <summary>
        /// Sets an additional scale factor for padding
        /// </summary>
        public Vector2 PaddingScaleRatio { get; set; }
        /// <summary>
        /// Sets an additional scale factor for margins
        /// </summary>
        public Vector2 MarginScaleRatio { get; set; }
        /// <summary>
        /// Sets a scale factor for outline thickness
        /// </summary>
        public float OutlineScaleRatio { get; set; }

        public Material TextMaterial, ShadedTextMaterial, SelectedTextMaterial;

        // This method is huge and basically runs once so the JIT shouldn't waste time optimizing it
        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public DefaultDecorations (DefaultMaterialSet materials, float defaultMargin = 6, float defaultMarginCollapsed = 4) {
            Materials = materials;

            TextMaterial = materials.Get(materials.ScreenSpaceShadowedBitmap, blendState: BlendState.AlphaBlend, clone: true);
            ShadedTextMaterial = materials.Get(materials.ScreenSpaceBitmap, blendState: BlendState.AlphaBlend, clone: true);
            SelectedTextMaterial = materials.Get(materials.ScreenSpaceBitmap, blendState: BlendState.AlphaBlend, clone: true);

            GlobalDefaultMargin = defaultMargin;
            GlobalDefaultMarginCollapsed = defaultMarginCollapsed;

            AnimationDurationMultiplier = 1f;
            _SizeScaleRatio = Vector2.One;
            PaddingScaleRatio = Vector2.One;
            MarginScaleRatio = Vector2.One;
            SpacingScaleRatio = Vector2.One;
            OutlineScaleRatio = 1f;

            InteractableShadow = new RasterShadowSettings {
                Color = pSRGBColor.Black(0.25f),
                Offset = new Vector2(1.5f, 2f),
                Softness = 5f
            };

            ContainerShadow = null;
            ScrollbarThumbShadow = null;

            FloatingContainerShadow = new RasterShadowSettings {
                Color = pSRGBColor.Black(0.33f),
                Offset = new Vector2(2.5f, 3f),
                Softness = 8f
            };

            EditableShadow = new RasterShadowSettings {
                Color = pSRGBColor.Black(0.3f),
                Offset = new Vector2(1.25f, 1.5f),
                Softness = 6f,
                Expansion = 0.4f,
                Inside = true
            };

            SliderShadow = new RasterShadowSettings {
                Color = pSRGBColor.Black(0.2f),
                Offset = new Vector2(1.25f, 1.5f),
                Softness = 6f,
                Expansion = 0.4f,
                Inside = true
            };

            GaugeShadow = SliderShadow;

            SelectionShadow = new RasterShadowSettings {
                Color = pSRGBColor.White(0.15f),
                Offset = new Vector2(1.15f, 1.35f),
                Softness = 2f
            };

            AcceleratorTargetShadow = new RasterShadowSettings {
                Color = pSRGBColor.Black(0.5f),
                Softness = 10f,
                Expansion = 1.5f
            };

            None = new DelegateDecorator {
                Below = None_Below,
                ContentClip = None_ContentClip,
                GetTextSettings = GetTextSettings_None,
                GetFont = () => DefaultFont,
            };

            Button = new DelegateDecorator {
                Margins = new Margins(GlobalDefaultMargin),
                Padding = new Margins(8, 8),
                UnscaledPadding = new Margins(8, 0),
                GetContentAdjustment = GetContentAdjustment_Button,
                GetTextSettings = GetTextSettings_Button,
                GetFont = () => ButtonFont ?? DefaultFont,
                Below = Button_Below,
                Above = Button_Above
            };

            Tab = new DelegateDecorator {
                Margins = new Margins(4, 4, 2, 2),
                Padding = new Margins(8, 4),
                UnscaledPadding = new Margins(8, 0),
                GetContentAdjustment = GetContentAdjustment_Button,
                GetTextSettings = GetTextSettings_Button,
                GetFont = () => ButtonFont ?? DefaultFont,
                Below = Tab_Below,
                Above = Tab_Above
            };

            TabPage = new DelegateDecorator {
                Margins = new Margins(2),
                Padding = new Margins(0, 0),
                Below = TabPage_Below
            };

            Checkbox = new DelegateDecorator {
                Margins = new Margins(GlobalDefaultMarginCollapsed, GlobalDefaultMargin),
                GetTextSettings = GetTextSettings_Button,
                GetFont = () => DefaultFont,
                Below = Checkbox_Below,
                Above = Checkbox_Above
            };

            RadioButton = new DelegateDecorator {
                Margins = new Margins(GlobalDefaultMarginCollapsed, GlobalDefaultMargin),
                GetTextSettings = GetTextSettings_Button,
                GetFont = () => DefaultFont,
                Below = RadioButton_Below,
                Above = RadioButton_Above
            };

            Container = new DelegateDecorator {
                Margins = new Margins(GlobalDefaultMargin),
                GetTextSettings = GetTextSettings,
                Below = Container_Below,
                ContentClip = Container_ContentClip,
            };

            FloatingContainer = new DelegateDecorator {
                Padding = new Margins(4),
                GetTextSettings = GetTextSettings,
                Below = FloatingContainer_Below,
                // FIXME: Separate routine?
                ContentClip = Container_ContentClip,
            };

            ContainerTitle = new DelegateDecorator {
                Padding = new Margins(6, 3, 6, 4),
                Margins = new Margins(0, 0, 0, 2),
                GetTextSettings = GetTextSettings_Title,
                GetFont = () => TitleFont ?? DefaultFont,
                Below = ContainerTitle_Below
            };

            Window = new DelegateDecorator {
                Padding = new Margins(4),
                GetTextSettings = GetTextSettings,
                Below = Window_Below,
                // FIXME: Separate routine?
                ContentClip = Container_ContentClip,
            };

            WindowTitle = new DelegateDecorator {
                Padding = new Margins(6, 3, 6, 4),
                Margins = new Margins(0, 0, 0, 2),
                GetTextSettings = GetTextSettings_Title,
                GetFont = () => TitleFont ?? DefaultFont,
                Below = WindowTitle_Below
            };

            StaticText = new DelegateDecorator {
                Margins = new Margins(GlobalDefaultMarginCollapsed, GlobalDefaultMargin, GlobalDefaultMarginCollapsed, GlobalDefaultMarginCollapsed),
                Padding = new Margins(6),
                GetTextSettings = GetTextSettings,
                GetFont = () => DefaultFont,
                Below = StaticText_Below,
            };

            // FIXME: StaticImage

            Tooltip = new DelegateDecorator {
                Margins = new Margins(8),
                Padding = new Margins(10, 10),
                UnscaledPadding = new Margins(2, 0),
                GetTextSettings = GetTextSettings_Tooltip,
                GetFont = () => TooltipFont ?? DefaultFont,
                Below = Tooltip_Below,
            };

            Menu = new DelegateDecorator {
                // Keep the menu from cramming up against the edges of the screen
                Margins = new Margins(4),
                Padding = new Margins(6),
                GetTextSettings = GetTextSettings_Tooltip,
                GetFont = () => DefaultFont,
                Below = Menu_Below,
                // FIXME: Separate routine?
                ContentClip = Container_ContentClip,
            };

            // FIXME
            ListBox = new DelegateDecorator {
                Margins = new Margins(GlobalDefaultMargin),
                Padding = new Margins(2),
                GetTextSettings = GetTextSettings,
                GetFont = () => DefaultFont,
                Below = Container_Below,
                ContentClip = Container_ContentClip,
            };

            EditableText = new DelegateDecorator {
                Margins = new Margins(GlobalDefaultMargin),
                Padding = new Margins(6),
                GetTextSettings = GetTextSettings,
                GetFont = () => DefaultFont,
                // FIXME
                Below = EditableText_Below,
                ContentClip = EditableText_ContentClip
            };

            Selection = new DelegateDecorator {
                GetTextSettings = GetTextSettings_Selection,
                Below = Selection_Below,
            };

            MenuSelection = new DelegateDecorator {
                GetTextSettings = GetTextSettings_Selection,
                Below = MenuSelection_Below,
            };

            ListSelection = new DelegateDecorator {
                GetTextSettings = GetTextSettings_Selection,
                Margins = new Margins(1),
                Below = ListSelection_Below,
            };

            CompositionPreview = new DelegateDecorator {
                GetTextSettings = GetTextSettings_Selection,
                Below = CompositionPreview_Below,
            };

            Description = new DelegateDecorator {
                GetTextSettings = GetTextSettings_Description,
                GetFont = () => DefaultFont,
            };

            Slider = new DelegateDecorator {
                Margins = new Margins(4, 4),
                Padding = new Margins(0, 0, 0, 2.75f),
                Below = Slider_Below,
                GetTextSettings = GetTextSettings
            };

            Gauge = new DelegateDecorator {
                Margins = new Margins(4, 4),
                Padding = new Margins(2),
                Below = Gauge_Below,
                Content = Gauge_Content,
                GetTextSettings = GetTextSettings
            };

            SliderThumb = new DelegateDecorator {
                Margins = new Margins(0),
                Padding = new Margins(6),
                Below = SliderThumb_Below,
                Above = SliderThumb_Above
            };

            Dropdown = new DelegateDecorator {
                Margins = new Margins(GlobalDefaultMargin),
                Padding = new Margins(6, 8),
                UnscaledPadding = new Margins(4, 0),
                GetContentAdjustment = GetContentAdjustment_Button,
                GetTextSettings = GetTextSettings_Button,
                GetFont = () => ButtonFont ?? DefaultFont,
                Below = Button_Below,
                Above = Button_Above
            };

            DropdownArrow = new DelegateDecorator {
                UnscaledPadding = new Margins(DropdownArrowWidth, DropdownArrowHeight, 0, 0),
                Padding = new Margins(DropdownArrowPadding),
                Above = DropdownArrow_Above
            };

            AcceleratorLabel = new DelegateDecorator {
                Padding = new Margins(6, 4, 6, 4),
                GetTextSettings = GetTextSettings_AcceleratorLabel,
                GetFont = () => AcceleratorFont ?? TooltipFont ?? DefaultFont,
                Below = AcceleratorLabel_Below
            };

            AcceleratorTarget = new DelegateDecorator {
                Below = AcceleratorTarget_Below
            };

            ParameterGauge = new DelegateDecorator {
                Below = ParameterGauge_Below,
                Margins = new Margins(1),
                // Top+bottom padding = height of fill/track
                // Left+right padding = minimum width of fill
                Padding = new Margins(5.5f, 0, 0, 8.5f)
            };

            VirtualCursor = new DelegateDecorator {
                Padding = new Margins(24),
                Above = VirtualCursor_Above
            };

            VirtualCursorAnchor = new DelegateDecorator {
                Above = VirtualCursorAnchor_Above
            };

            HyperTextHotspot = new DelegateDecorator {
                Below = None_Below
            };

            LoadingSpinner = new DelegateDecorator {
                Above = LoadingSpinner_Above
            };

            Scrollbar = new DelegateWidgetDecorator<ScrollbarState> {
                MinimumSize = new Vector2(ScrollbarSize, ScrollbarSize),
                Above = Scrollbar_Above,
                OnMouseEvent = Scrollbar_OnMouseEvent,
                OnHitTest = Scrollbar_OnHitTest,
                Padding = new Margins(1)
            };

            ShowMenu = new FadeAnimation {
                To = 1f,
                DefaultDuration = MenuShowDuration
            };

            HideMenu = new FadeAnimation {
                To = 0f,
                DefaultDuration = MenuHideDuration,
                RemoveAtEnd = true
            };

            ShowModalDialog = new FadeAnimation {
                To = 1f,
                DefaultDuration = ModalDialogShowDuration
            };

            HideModalDialog = new FadeAnimation {
                To = 0f,
                DefaultDuration = ModalDialogHideDuration,
                RemoveAtEnd = true
            };

            UpdateScaledSizes();
        }

        public float LoadingSpinnerRadius = 128f,
            LoadingSpinnerLength = 68f,
            LoadingSpinnerThickness = 6.5f,
            LoadingSpinnerSpeed = 0.75f;

        protected virtual void LoadingSpinner_Above (ref UIOperationContext context, ref ImperativeRenderer renderer, ref DecorationSettings settings) {
            const float boxPadding = 8f;
            var center = settings.ContentBox.Center;
            var sizeScale = (SizeScaleRatio.X + SizeScaleRatio.Y) / 2f;
            var fillRadius = Arithmetic.Clamp(LoadingSpinnerThickness * sizeScale, 4f, 16f);
            var boxSize = Math.Min(settings.ContentBox.Width, settings.ContentBox.Height) - (fillRadius * 2f) - boxPadding;
            var radius = Math.Min(boxSize / 2f, LoadingSpinnerRadius * sizeScale);
            var angle1 = (float)(Time.Seconds * 360 * LoadingSpinnerSpeed) + center.X + (center.Y * 1.7f);
            var outlineRadius = Button_GetOutlineSize(1f);
            var alpha = (settings.TextColor?.ToColor() ?? Color.White).A / 255f;
            renderer.RasterizeArc(
                center, angle1, LoadingSpinnerLength, radius, fillRadius + outlineRadius,
                0f, pSRGBColor.Transparent, pSRGBColor.Black(0.6f * alpha), Color.Transparent,
                fill: RasterFillMode.Along, annularRadius: outlineRadius
            );
            renderer.RasterizeArc(
                center, angle1, LoadingSpinnerLength, radius, fillRadius,
                0f, pSRGBColor.Transparent, pSRGBColor.White(alpha), Color.Transparent,
                fill: RasterFillMode.Along,
                layer: renderer.Layer + 1
            );
        }

        protected float Button_GetOutlineSize (float baseSize) {
            return (float)Math.Round(baseSize * OutlineScaleRatio, 1, MidpointRounding.AwayFromZero);
        }

        private void UpdateScaledSizes () {
            ((DelegateDecorator)Checkbox).Padding =
                ((DelegateDecorator)RadioButton).Padding =
                new Margins(6 + CheckboxSize + 4, 6, 6, 6);
        }

        public IGlyphSource DefaultFont,
            ButtonFont,
            TitleFont,
            TooltipFont,
            AcceleratorFont;

        public const float MenuShowDuration = 0.1f,
            MenuHideDuration = 0.25f,
            ModalDialogShowDuration = 0.1f,
            ModalDialogHideDuration = 0.25f;

        public float InteractableCornerRadius = 6f, 
            InertCornerRadius = 3f, 
            ContainerCornerRadius = 3f, 
            TitleCornerRadius = 3f,
            SelectionCornerRadius = 1.9f,
            SelectionPadding = 1f,
            MenuSelectionCornerRadius = 8f,
            ListSelectionCornerRadius = 3f,
            EditableTextCornerRadius = 4.5f,
            SliderCornerRadius = 4.5f,
            TabCornerRadius = 10f;
        public float? FloatingContainerCornerRadius = 7f,
            TooltipCornerRadius = 8f;
        public float InactiveOutlineThickness = 1f, 
            ActiveOutlineThickness = 1.3f, 
            PressedOutlineThickness = 2f,
            InertOutlineThickness = 1f,
            EditableFocusedOutlineThickness = 1.2f;
        public float EdgeGleamOpacity = 0.4f,
            EdgeGleamThickness = 1.2f;
        public float ScrollbarSize = 18f, 
            ScrollbarRadius = 3f,
            ScrollbarMinThumbSize = 24f;
        public float FocusFadeLength = 0.1f,
            HoverFadeLength = 0.075f;

        public RasterShadowSettings? InteractableShadow, 
            ContainerShadow,
            FloatingContainerShadow,
            ScrollbarThumbShadow,
            TitleShadow,
            EditableShadow,
            SelectionShadow,
            TooltipShadow,
            SliderShadow,
            AcceleratorTargetShadow,
            GaugeShadow,
            GaugeValueShadow;

        public const float DropdownArrowWidth = 16, DropdownArrowHeight = 11, DropdownArrowPadding = 4;
        public float ScaledCheckboxSize => CheckboxSize * (SizeScaleRatio.Length() / 1.41421354f);
        public float CheckboxSize = 32;
        public float DisabledTextAlpha = 0.5f;

        void IDecorationProvider.OnEvent<T> (Control control, string name, T args) => OnControlEvent(control, name, args);

        protected virtual void OnControlEvent<T> (Control control, string name, T args) {
        }

        public float GetHoveringAlpha (ref UIOperationContext context, ControlStates state, out bool isHovering, float? fadeLength = null) {
            isHovering = state.IsFlagged(ControlStates.Hovering);

            float previousAlpha = 0f, newAlpha = 0f;
            if (state.IsFlagged(ControlStates.PreviouslyHovering))
                previousAlpha = 1f - Arithmetic.Saturate((float)TimeSpan.FromTicks(context.NowL - context.UIContext.LastHoverLoss).TotalSeconds / (fadeLength ?? HoverFadeLength));
            if (isHovering)
                newAlpha = Arithmetic.Saturate((float)TimeSpan.FromTicks(context.NowL - context.UIContext.LastHoverGain).TotalSeconds / (fadeLength ?? HoverFadeLength));

            // It's possible to both be the previous and current hovering control (mouse moved off and then back on), so in that case
            //  we sum both alpha values to minimize any glitch and cause the edge to become bright faster
            return Arithmetic.Saturate(previousAlpha + newAlpha);
        }

        public float GetFocusedAlpha (ref UIOperationContext context, ControlStates state, out bool isFocused, bool includeContains = true, float? fadeLength = null) {
            var previouslyFocused = state.IsFlagged(ControlStates.PreviouslyFocused);
            isFocused = state.IsFlagged(ControlStates.Focused);
            var fadeFlag = isFocused;
            if (includeContains && state.IsFlagged(ControlStates.ContainsFocus)) {
                isFocused = true;
                // We aren't focused but are in the focus chain (a child or modal inherited focus from us), so it's not possible
                //  to trivially get this animation right. So just suppress it
                if (!fadeFlag)
                    return 1;
            }

            // HACK because we want mouseover to feel really responsive
            // FIXME: Link this to the hovering alpha transition
            if (state.IsFlagged(ControlStates.Hovering) && isFocused)
                return 1;

            var result = (float)TimeSpan.FromTicks(context.NowL - context.UIContext.LastFocusChange).TotalSeconds / (fadeLength ?? FocusFadeLength);
            if (!isFocused)
                return state.IsFlagged(ControlStates.PreviouslyFocused)
                    ? 1 - Arithmetic.Saturate(result)
                    : 0;
            else
                return Arithmetic.Saturate(result);
        }

        public virtual void Button_Below_Common (
            UIOperationContext context, DecorationSettings settings, 
            out float alpha, out float focusedAlpha, 
            out float thickness, out float pulse,
            out pSRGBColor baseColor, out pSRGBColor outlineColor 
        ) {
            var now = context.NowL;
            var nowF = context.Now;
            var state = settings.State;
            focusedAlpha = GetFocusedAlpha(ref context, settings.State, out bool isFocused);
            baseColor = settings.BackgroundColor ?? (
                pSRGBColor.Lerp(ColorScheme.Inactive, ColorScheme.Focused, focusedAlpha)
            );
            var hasColor = settings.BackgroundColor.HasValue;
            var colorIsGray = hasColor && (baseColor.ColorDelta <= 0.05f);
            // HACK: If the background color isn't saturated, use the focused color for the outline
            pSRGBColor? outlineBaseColor = (colorIsGray && isFocused) ? ColorScheme.Focused : (pSRGBColor?)null;
            var pulseThickness = Arithmetic.PulseSine(nowF / 3f, 0, 0.4f);

            pulse = 0;
            if (
                state.IsFlagged(ControlStates.Pressed) ||
                // HACK
                state.IsFlagged(ControlStates.Checked)
            ) {
                alpha = hasColor ? 0.95f : 0.8f;
                thickness = PressedOutlineThickness;
                if (hasColor) {
                    // Intensify the color if the button has a custom color
                    baseColor = settings.BackgroundColor.Value.AdjustBrightness(1.4f);
                } else
                    baseColor = ColorScheme.Active;
                outlineColor = (outlineBaseColor ?? baseColor) + (hasColor ? 0.4f : 0.05f);
            } else if (state.IsFlagged(ControlStates.Hovering)) {
                // FIXME: Animate this
                alpha = hasColor 
                    ? 0.95f 
                    : Arithmetic.Lerp(0.55f, 0.8f, focusedAlpha);
                thickness = ActiveOutlineThickness + pulseThickness;
                pulse = Arithmetic.PulseSine(nowF / 2.5f, 0f, 0.15f);
                if (hasColor)
                    outlineColor = (outlineBaseColor ?? baseColor) + (hasColor ? 0.3f : 0f);
                else
                    outlineColor = (outlineBaseColor ?? baseColor);
            } else {
                alpha = hasColor
                    ? Arithmetic.Lerp(0.85f, 0.95f, focusedAlpha)
                    : Arithmetic.Lerp(0.4f, 0.75f, focusedAlpha);
                thickness = Arithmetic.Lerp(InactiveOutlineThickness, ActiveOutlineThickness + pulseThickness, focusedAlpha);
                if (hasColor && !colorIsGray)
                    outlineColor = (outlineBaseColor ?? baseColor) + Arithmetic.Lerp(0.05f, 0.3f, focusedAlpha);
                else
                    outlineColor = (outlineBaseColor ?? baseColor);
            }
        }

        protected virtual void ConfigureTexture (
            string type, ref DecorationSettings settings, out Texture2D texture, out Bounds textureRegion, out RasterTextureSettings textureSettings
        ) {
            texture = settings.GetTexture();
            textureRegion = settings.GetTextureRegion();
            textureSettings = settings.GetTextureSettings();
            if (settings.HasStateFlag(ControlStates.Disabled))
                textureSettings.Saturation *= 0.5f;
        }

        protected virtual void Button_Below (ref UIOperationContext context, ref ImperativeRenderer renderer, ref DecorationSettings settings) {
            float alpha, thickness, pulse;
            pSRGBColor baseColor, outlineColor;
            Button_Below_Common(
                context, settings, out alpha, out _, 
                out thickness, out pulse, 
                out baseColor, out outlineColor
            );

            var color1 = baseColor;
            var color2 = baseColor;

            ConfigureTexture("Button", ref settings, out var texture, out var textureRegion, out var textureSettings);

            float base1 = 0.85f, base2 = 0.35f;
            if (settings.BackgroundColor.HasValue) {
                if (settings.BackgroundColor.Value.IsTransparent)
                    return;

                color1 = color1.AdjustBrightness(1.2f);
                base1 = 0.95f;
                base2 = 0.75f;
            } else if (texture != null) {
                base1 = 0.95f;
                base2 = 0.7f;
            }

            var fillAlpha1 = Math.Min((base1 + pulse) * alpha, 1f);
            var fillAlpha2 = Math.Min((base2 + pulse) * alpha, 1f);

            settings.Box.SnapAndInset(out Vector2 a, out Vector2 b);
            renderer.RasterizeRectangle(
                a, b,
                radius: InteractableCornerRadius,
                outlineRadius: Button_GetOutlineSize(thickness), outlineColor: outlineColor * alpha,
                innerColor: color1 * fillAlpha1, outerColor: color2 * fillAlpha2,
                fill: new RasterFillSettings {
                    Mode = RasterFillMode.RadialEnclosing, Size = 0.95f,
                },
                shadow: InteractableShadow,
                texture: texture,
                textureRegion: textureRegion,
                textureSettings: textureSettings
            );
        }

        protected virtual void Button_Above (ref UIOperationContext context, ref ImperativeRenderer renderer, ref DecorationSettings settings) {
            var alpha = GetHoveringAlpha(ref context, settings.State, out bool isHovering);
            if (alpha <= 0)
                return;

            settings.Box.SnapAndInset(out Vector2 a, out Vector2 b);
            float fillSize = Math.Max(0.05f, Math.Min(0.9f, 64f / settings.Box.Height));

            renderer.RasterizeRectangle(
                a, b,
                radius: InteractableCornerRadius,
                outlineRadius: 0, outlineColor: Color.Transparent,
                innerColor: pSRGBColor.White(EdgeGleamOpacity * alpha), outerColor: pSRGBColor.White(0.0f),
                fill: new RasterFillSettings {
                    Mode = RasterFillMode.Angular,
                    Size = fillSize,
                    Offset = -Arithmetic.PulseSine(context.Now / 4f, 0f, 0.05f),
                    Angle = Arithmetic.PulseCyclicExp(context.Now / 2f, 3),
                },
                annularRadius: Button_GetOutlineSize(EdgeGleamThickness),
                blendState: BlendState.Additive
            );
        }

        protected virtual void TabPage_Below (ref UIOperationContext context, ref ImperativeRenderer renderer, ref DecorationSettings settings) {
            settings.State = ControlStates.Checked;
            float alpha, thickness, pulse;
            pSRGBColor baseColor, outlineColor;
            Button_Below_Common(
                context, settings, out alpha, out _,
                out thickness, out pulse, 
                out baseColor, out outlineColor
            );

            var radiusCW = new Vector4(2, 2, 2, 2);

            var color1 = Color.Transparent;
            var color2 = color1;

            ConfigureTexture("TabPage", ref settings, out var texture, out var textureRegion, out var textureSettings);

            settings.Box.SnapAndInset(out Vector2 a, out Vector2 b);
            renderer.RasterizeRectangle(
                a, b,
                radiusCW: radiusCW,
                outlineRadius: Button_GetOutlineSize(thickness), outlineColor: outlineColor * alpha,
                innerColor: color1, outerColor: color2,
                fill: new RasterFillSettings {
                    Mode = RasterFillMode.RadialEnclosing,
                    Size = 0.95f,
                },
                shadow: InteractableShadow,
                texture: texture,
                textureRegion: textureRegion,
                textureSettings: textureSettings
            );
        }

        private Vector4 GetTabRadius (ref DecorationSettings settings) {
            switch (settings.Traits.FirstOrDefault()) {
                default:
                case "top":
                    return new Vector4(TabCornerRadius, TabCornerRadius, 0, 0);
                case "left":
                    return new Vector4(TabCornerRadius, 0, 0, TabCornerRadius);
            }
        }

        protected virtual void Tab_Below (ref UIOperationContext context, ref ImperativeRenderer renderer, ref DecorationSettings settings) {
            float alpha, thickness, pulse;
            pSRGBColor baseColor, outlineColor;
            Button_Below_Common(
                context, settings, out alpha, out _,
                out thickness, out pulse, 
                out baseColor, out outlineColor
            );

            var color1 = baseColor;
            var color2 = baseColor;

            float base1 = 0.85f, base2 = 0.35f;
            if (settings.BackgroundColor.HasValue) {
                color1 = color1.AdjustBrightness(1.2f);
                base1 = 0.95f;
                base2 = 0.75f;
            }

            var fillAlpha1 = Math.Min((base1 + pulse) * alpha, 1f);
            var fillAlpha2 = Math.Min((base2 + pulse) * alpha, 1f);

            var radiusCW = GetTabRadius(ref settings);

            ConfigureTexture("Tab", ref settings, out var texture, out var textureRegion, out var textureSettings);

            settings.Box.SnapAndInset(out Vector2 a, out Vector2 b);
            renderer.RasterizeRectangle(
                a, b,
                radiusCW: radiusCW,
                outlineRadius: Button_GetOutlineSize(thickness), outlineColor: outlineColor * alpha,
                innerColor: color1 * fillAlpha1, outerColor: color2 * fillAlpha2,
                fill: new RasterFillSettings {
                    Mode = RasterFillMode.RadialEnclosing,
                    Size = 0.95f,
                },
                shadow: InteractableShadow,
                texture: texture,
                textureRegion: textureRegion,
                textureSettings: textureSettings
            );
        }

        protected virtual void Tab_Above (ref UIOperationContext context, ref ImperativeRenderer renderer, ref DecorationSettings settings) {
            var alpha = GetHoveringAlpha(ref context, settings.State, out bool isHovering);
            if (alpha <= 0)
                return;

            settings.Box.SnapAndInset(out Vector2 a, out Vector2 b);
            float fillSize = Math.Max(0.05f, Math.Min(0.9f, 64f / settings.Box.Height));

            var radiusCW = GetTabRadius(ref settings);

            renderer.RasterizeRectangle(
                a, b,
                radiusCW: radiusCW,
                outlineRadius: 0, outlineColor: Color.Transparent,
                innerColor: pSRGBColor.White(EdgeGleamOpacity * alpha), outerColor: pSRGBColor.White(0.0f),
                fill: new RasterFillSettings {
                    Mode = RasterFillMode.Angular,
                    Size = fillSize,
                    Offset = -Arithmetic.PulseSine(context.Now / 4f, 0f, 0.05f),
                    Angle = Arithmetic.PulseCyclicExp(context.Now / 2f, 3),
                },
                annularRadius: Button_GetOutlineSize(EdgeGleamThickness),
                blendState: BlendState.Additive
            );
        }

        protected virtual void DropdownArrow_Above (ref UIOperationContext context, ref ImperativeRenderer renderer, ref DecorationSettings settings) {
            var isPressed = settings.HasStateFlag(ControlStates.Pressed);
            GetContentAdjustment_Button(ref context, settings.State, out Vector2 offset, out Vector2 scale);
            settings.ContentBox.SnapAndInset(out Vector2 tl, out Vector2 br);

            var scaleSz = SizeScaleRatio;
            var scalePadding = PaddingScaleRatio * SpacingScaleRatio;
            var pad = DropdownArrowPadding * scalePadding.X;
            var ySpace = (float)Math.Floor((settings.ContentBox.Height - DropdownArrowHeight * scaleSz.Y) / 2f);
            var a = new Vector2(br.X + offset.X + pad, tl.Y + ySpace + offset.Y);
            var b = a + new Vector2(DropdownArrowWidth, DropdownArrowHeight) * scaleSz;
            var color = Color.White;
            var outlineColor = Color.Black;

            renderer.RasterizeTriangle(
                a, new Vector2(b.X, a.Y),
                new Vector2((a.X + b.X) / 2f, b.Y),
                radius: 1f, outlineRadius: Button_GetOutlineSize(1f),
                innerColor: color, outerColor: color, 
                outlineColor: outlineColor
            );
        }

        protected virtual void Slider_Below (ref UIOperationContext context, ref ImperativeRenderer renderer, ref DecorationSettings settings) {
            ConfigureTexture("Slider", ref settings, out var texture, out var textureRegion, out var textureSettings);

            settings.Box.SnapAndInset(out Vector2 a, out Vector2 b);
            renderer.RasterizeRectangle(
                a, b,
                radius: SliderCornerRadius,
                outlineRadius: Button_GetOutlineSize(InertOutlineThickness), outlineColor: Color.Transparent,
                innerColor: settings.BackgroundColor ?? ColorScheme.SliderFill, 
                outerColor: settings.BackgroundColor ?? ColorScheme.SliderFill,
                shadow: SliderShadow,
                texture: texture,
                textureRegion: textureRegion,
                textureSettings: textureSettings
            );
        }

        protected virtual void Gauge_Below (ref UIOperationContext context, ref ImperativeRenderer renderer, ref DecorationSettings settings) {
            ConfigureTexture("Gauge_Below", ref settings, out var texture, out var textureRegion, out var textureSettings);

            settings.Box.SnapAndInset(out Vector2 a, out Vector2 b);
            var direction = settings.Traits.FirstOrDefault();
            if ((direction == "cw") || (direction == "ccw")) {
                renderer.RasterizeArc(
                    settings.Box.Center, 
                    // HACK
                    startAngleDegrees: 0f, sizeDegrees: 360f,
                    ringRadius: settings.ContentBox.Width, fillRadius: settings.ContentBox.Height,
                    outlineRadius: Button_GetOutlineSize(InertOutlineThickness), outlineColor: Color.Transparent,
                    innerColor: settings.BackgroundColor ?? ColorScheme.GaugeFill, 
                    outerColor: settings.BackgroundColor ?? ColorScheme.GaugeFill,
                    shadow: GaugeShadow,
                    texture: texture,
                    textureRegion: textureRegion,
                    textureSettings: textureSettings
                );
            } else {
                renderer.RasterizeRectangle(
                    a, b,
                    radius: SliderCornerRadius,
                    outlineRadius: Button_GetOutlineSize(InertOutlineThickness), outlineColor: Color.Transparent,
                    innerColor: settings.BackgroundColor ?? ColorScheme.GaugeFill, 
                    outerColor: settings.BackgroundColor ?? ColorScheme.GaugeFill,
                    shadow: GaugeShadow,
                    texture: texture,
                    textureRegion: textureRegion,
                    textureSettings: textureSettings
                );
            }
        }

        public bool Gauge_Fill_Setup (
            UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings,
            out bool isCircular, out float outlineRadius, out Vector4 cornerRadiuses,
            out float alpha1, out float alpha2, out string direction, out Vector2 ca, out Vector2 cb,
            out float gradientPower, out pSRGBColor fillColor1, out pSRGBColor fillColor2, 
            out pSRGBColor outlineColor, out RasterFillMode fillMode
        ) {
            isCircular = false;
            outlineRadius = Button_GetOutlineSize(1f);
            cornerRadiuses = Vector4.One;
            alpha1 = alpha2 = 0f;

            settings.Box.SnapAndInset(out Vector2 a, out Vector2 b);
            ca = a + (settings.ContentBox.Position - settings.Box.Position).Round();
            cb = b - (settings.Box.Extent - settings.ContentBox.Extent).Round();
            fillColor1 = fillColor2 = outlineColor = default(pSRGBColor);
            fillMode = default(RasterFillMode);
            gradientPower = settings.HasTrait("eased-gradient") ? 2.5f : 1f;

            // Select fill mode and gradient direction based on orientation
            direction = settings.Traits.FirstOrDefault();
            switch (direction) {
                default:
                case "ltr":
                case "rtl":
                    if (ca.X >= cb.X)
                        return false;
                    fillMode = RasterFillMode.Angular + (direction == "rtl" ? 270 : 90);
                    break;
                case "ttb":
                case "btt":
                    if (ca.Y >= cb.Y)
                        return false;
                    fillMode = RasterFillMode.Angular + (direction == "btt" ? 180 : 0);
                    break;
                case "cw":
                case "ccw":
                    fillMode = RasterFillMode.Along;
                    isCircular = true;
                    break;
            }

            float radiusA = settings.HasTrait("hard-start") ? 0f : 1f,
                radiusB = settings.HasTrait("hard-end") ? 0f : 1f;

            // HACK: Based on orientation, disable snapping for the growing edge of the fill
            //  along the growth axis so that it can shrink/expand smoothly while staying snapped
            //  at the other 3 edges
            switch (direction) {
                default:
                case "ltr":
                    cb.X = settings.ContentBox.Extent.X;
                    // TL, TR, BR, BL
                    cornerRadiuses = new Vector4(radiusA, radiusB, radiusB, radiusA);
                    break;
                case "rtl":
                    ca.X = settings.ContentBox.Position.X;
                    cornerRadiuses = new Vector4(radiusA, radiusB, radiusB, radiusA);
                    break;
                case "ttb":
                    cb.Y = settings.ContentBox.Extent.Y;
                    cornerRadiuses = new Vector4(radiusA, radiusA, radiusB, radiusB);
                    break;
                case "btt":
                    ca.Y = settings.ContentBox.Position.Y;
                    cornerRadiuses = new Vector4(radiusA, radiusA, radiusB, radiusB);
                    break;
                case "cw":
                case "ccw":
                    break;
            }

            bool fixedValues = settings.HasTrait("fixed-endpoints");
            float value1 = fixedValues ? 0 : settings.UserData.X,
                value2 = fixedValues ? 1 : settings.UserData.Y,
                alphaDelta = ColorScheme.GaugeFillAlpha2 - ColorScheme.GaugeFillAlpha1;

            alpha1 = Arithmetic.Saturate(ColorScheme.GaugeFillAlpha1 + (alphaDelta * value1));
            alpha2 = Arithmetic.Saturate(ColorScheme.GaugeFillAlpha1 + (alphaDelta * value2));

            var contrast = settings.HasTrait("high-contrast") ? 2f : 1f;
            float brightnessDelta = (ColorScheme.GaugeFillBrightness2 - ColorScheme.GaugeFillBrightness1) * contrast,
                brightness1 = ColorScheme.GaugeFillBrightness1 + (brightnessDelta * value1),
                brightness2 = ColorScheme.GaugeFillBrightness1 + (brightnessDelta * value2);

            // FIXME: Padding will make this slightly wrong
            pSRGBColor fillColor = settings.TextColor ?? ColorScheme.GaugeValueFill;
            if (settings.HasTrait("limit")) {
                alpha1 = alpha2 = ColorScheme.GaugeLimitAlpha;
                fillColor = ColorScheme.GaugeLimitFill;
            } else if (settings.HasTrait("static")) {
                alpha1 = alpha2 = 1.0f;
                brightness1 = brightness2 = 1.0f;
            }
            fillColor1 = fillColor.AdjustBrightness(brightness1) * alpha1;
            fillColor2 = fillColor.AdjustBrightness(brightness2) * alpha2;
            outlineColor = fillColor;

            return true;
        }

        protected virtual void Gauge_Content (ref UIOperationContext context, ref ImperativeRenderer renderer, ref DecorationSettings settings) {
            Gauge_Fill_Setup(
                context, ref renderer, settings,
                out bool isCircular, out float outlineRadius, out Vector4 cornerRadiuses,
                out float alpha1, out float alpha2, out string direction,
                out Vector2 ca, out Vector2 cb, out float gradientPower,
                out pSRGBColor fillColor1, out pSRGBColor fillColor2,
                out pSRGBColor outlineColor, out RasterFillMode fillMode
            );

            ConfigureTexture("Gauge_Content", ref settings, out var texture, out var textureRegion, out var textureSettings);

            if (isCircular) {
                // HACK: Ensure that the alpha values equalize as we approach a full circle, otherwise the 
                //  gradient will glitch at the point where the ends meet
                var fadeRamp = (settings.ContentBox.Top - 260f) / 70f;
                alpha1 = Arithmetic.Lerp(alpha1, alpha2, fadeRamp);

                float temp = alpha1;
                if (direction == "ccw") {
                    alpha1 = alpha2;
                    alpha2 = temp;
                }

                renderer.RasterizeArc(
                    settings.Box.Center, 
                    // HACK
                    startAngleDegrees: settings.ContentBox.Left, sizeDegrees: settings.ContentBox.Top,
                    ringRadius: settings.ContentBox.Width, fillRadius: settings.ContentBox.Height,
                    outlineRadius: outlineRadius, outlineColor: outlineColor * 0.5f,
                    fill: new RasterFillSettings(
                        fillMode, offset: settings.UserData.Z,
                        size: (settings.UserData.W != 0) ? Math.Abs(settings.UserData.W) : 1,
                        repeat: settings.UserData.W < 0, gradientPower: gradientPower
                    ), 
                    innerColor: fillColor1, outerColor: fillColor2,
                    shadow: GaugeValueShadow,
                    texture: texture,
                    textureRegion: textureRegion,
                    textureSettings: textureSettings,
                    endRounding: 0f
                );
            } else {
                renderer.RasterizeRectangle(
                    ca, cb,
                    radiusCW: SliderCornerRadius * cornerRadiuses,
                    outlineRadius: outlineRadius, outlineColor: outlineColor * 0.5f,
                    fill: new RasterFillSettings(
                        fillMode, offset: settings.UserData.Z, 
                        size: (settings.UserData.W != 0) ? Math.Abs(settings.UserData.W) : 1,
                        repeat: settings.UserData.W < 0, gradientPower: gradientPower
                    ), innerColor: fillColor1, outerColor: fillColor2,
                    shadow: GaugeValueShadow,
                    texture: texture,
                    textureRegion: textureRegion,
                    textureSettings: textureSettings
                );
            }
        }

        protected virtual void SliderThumb_Below (ref UIOperationContext context, ref ImperativeRenderer renderer, ref DecorationSettings settings) {
            float alpha, thickness, pulse;
            pSRGBColor baseColor, outlineColor;
            Button_Below_Common(
                context, settings, out alpha, out _,
                out thickness, out pulse, 
                out baseColor, out outlineColor
            );
            
            alpha *= 1.5f;

            settings.Box.SnapAndInset(out Vector2 a, out Vector2 b);
            var tip = (b - a).X * 0.8f;
            renderer.RasterizeRectangle(
                a, b,
                radiusCW: new Vector4(InertCornerRadius, InertCornerRadius, tip, tip),
                outlineRadius: Button_GetOutlineSize(thickness), outlineColor: outlineColor * alpha,
                innerColor: baseColor * ((0.85f + pulse) * alpha), outerColor: baseColor * ((0.35f + pulse) * alpha),
                fill: new RasterFillSettings {
                    Mode = RasterFillMode.Vertical,
                    Size = 0.95f,
                },
                shadow: InteractableShadow
            );
        }

        protected virtual void SliderThumb_Above (ref UIOperationContext context, ref ImperativeRenderer renderer, ref DecorationSettings settings) {
            var alpha = GetHoveringAlpha(ref context, settings.State, out bool isHovering);
            if (alpha <= 0)
                return;

            settings.Box.SnapAndInset(out Vector2 a, out Vector2 b);
            var tip = (b - a).X * 0.8f;
            float fillSize = Math.Max(0.05f, Math.Min(0.9f, 64f / settings.Box.Height));

            renderer.RasterizeRectangle(
                a, b,
                radiusCW: new Vector4(InertCornerRadius, InertCornerRadius, tip, tip),
                outlineRadius: 0, outlineColor: Color.Transparent,
                innerColor: pSRGBColor.White(EdgeGleamOpacity * alpha), outerColor: pSRGBColor.White(0.0f),
                fill: new RasterFillSettings {
                    Mode = RasterFillMode.Angular,
                    Size = fillSize,
                    Offset = -Arithmetic.PulseSine(context.Now / 4f, 0f, 0.05f),
                    Angle = Arithmetic.PulseCyclicExp(context.Now / 2f, 3),
                },
                annularRadius: Button_GetOutlineSize(EdgeGleamThickness),
                blendState: BlendState.Additive
            );
        }

        protected virtual void AdjustRectForCheckbox (ref DecorationSettings mutableSettings) {
            var box = mutableSettings.Box;
            // FIXME: Scaling this will make the text crowded
            var size = ScaledCheckboxSize;
            mutableSettings.Box = new RectF(box.Left + 2, box.Top + (box.Height - size) / 2, size, size);
        }

        protected virtual void Checkbox_Below (ref UIOperationContext context, ref ImperativeRenderer renderer, ref DecorationSettings settings) {
            // HACK
            var temp = settings;
            temp.State &= ~ControlStates.Checked;
            AdjustRectForCheckbox(ref temp);
            Button_Below(ref context, ref renderer, ref temp);
        }

        private RasterPolygonVertex[] CheckboxTemp = new RasterPolygonVertex[3];

        protected virtual void Checkbox_Above (ref UIOperationContext context, ref ImperativeRenderer renderer, ref DecorationSettings _settings) {
            var settings = _settings;
            AdjustRectForCheckbox(ref settings);

            var ha = GetHoveringAlpha(ref context, settings.State, out bool isHovering);
            var fa = GetFocusedAlpha(ref context, settings.State, out bool isFocused);
            var isChecked = settings.HasStateFlag(ControlStates.Checked);
            if (isHovering || isChecked || isFocused) {
                var a = isChecked
                    ? 1f
                    : Math.Max(ha, fa) * 0.5f;
                pSRGBColor f = pSRGBColor.White(a), o = pSRGBColor.Black(a * 0.66f);
                CheckboxTemp[0].Position = new Vector2(settings.Box.Left + 8f, settings.Box.Center.Y + 1.75f);
                CheckboxTemp[1].Position = new Vector2(settings.Box.Center.X, settings.Box.Extent.Y - 6.5f);
                CheckboxTemp[2].Position = new Vector2(settings.Box.Extent.X - 8.5f, settings.Box.Top + 7f);
                var so = renderer.RasterSoftOutlines;
                renderer.RasterSoftOutlines = true;
                renderer.RasterizePolygon(
                    new ArraySegment<RasterPolygonVertex>(CheckboxTemp),
                    false, radius: 2f, outlineRadius: 0.9f,
                    f, f, o
                );
                renderer.RasterSoftOutlines = so;
            }
            Button_Above(ref context, ref renderer, ref settings);
        }

        protected virtual void RadioButton_Below (ref UIOperationContext context, ref ImperativeRenderer renderer, ref DecorationSettings _settings) {
            var settings = _settings;
            // HACK
            settings.State &= ~ControlStates.Checked;
            AdjustRectForCheckbox(ref settings);

            float alpha, thickness, pulse;
            pSRGBColor baseColor, outlineColor;
            Button_Below_Common(
                context, settings, out alpha, out _,
                out thickness, out pulse, 
                out baseColor, out outlineColor
            );

            settings.Box.SnapAndInset(out Vector2 a, out Vector2 b);
            renderer.RasterizeRectangle(
                a, b,
                radius: ScaledCheckboxSize * 0.45f,
                outlineRadius: thickness, outlineColor: outlineColor * alpha,
                innerColor: baseColor * ((0.85f + pulse) * alpha), outerColor: baseColor * ((0.35f + pulse) * alpha),
                fill: new RasterFillSettings {
                    Mode = RasterFillMode.RadialEnclosing, Size = 0.95f,
                },
                shadow: InteractableShadow
            );
        }

        protected virtual void RadioButton_Above (ref UIOperationContext context, ref ImperativeRenderer renderer, ref DecorationSettings _settings) {
            var settings = _settings;
            AdjustRectForCheckbox(ref settings);

            var ha = GetHoveringAlpha(ref context, settings.State, out bool isHovering);
            var fa = GetFocusedAlpha(ref context, settings.State, out bool isFocused);
            var isChecked = settings.HasStateFlag(ControlStates.Checked);
            if (isHovering || isChecked || isFocused) {
                pSRGBColor 
                    f = pSRGBColor.White(
                        isChecked
                            ? 1f
                            : Math.Max(ha, fa) * 0.2f
                    ),
                    o = pSRGBColor.White(
                        isChecked
                            ? 0.6f
                            : Math.Max(ha, fa) * 0.4f
                    );

                var so = renderer.RasterSoftOutlines;
                renderer.RasterSoftOutlines = true;
                renderer.RasterizeEllipse(
                    settings.Box.Center - (Vector2.One * 0.1f), 
                    Vector2.One * (isChecked ? 8f : 7f),
                    outlineRadius: Button_GetOutlineSize(1.2f), 
                    innerColor: f, 
                    outerColor: f, 
                    outlineColor: o
                );
                renderer.RasterSoftOutlines = so;
            }

            var alpha = GetHoveringAlpha(ref context, settings.State, out _);
            if (alpha <= 0)
                return;

            settings.Box.SnapAndInset(out Vector2 a, out Vector2 b);
            float fillSize = Math.Max(0.05f, Math.Min(0.9f, 64f / settings.Box.Height));

            renderer.RasterizeRectangle(
                a, b,
                radius: ScaledCheckboxSize * 0.45f,
                outlineRadius: 0, outlineColor: Color.Transparent,
                innerColor: pSRGBColor.White(EdgeGleamOpacity * alpha), outerColor: pSRGBColor.White(0.0f),
                fill: new RasterFillSettings {
                    Mode = RasterFillMode.Angular,
                    Size = fillSize,
                    Offset = -Arithmetic.PulseSine(context.Now / 4f, 0f, 0.05f),
                    Angle = Arithmetic.PulseCyclicExp(context.Now / 2f, 3),
                },
                annularRadius: Button_GetOutlineSize(EdgeGleamThickness),
                blendState: BlendState.Additive
            );
        }

        protected virtual void Container_Below (ref UIOperationContext context, ref ImperativeRenderer renderer, ref DecorationSettings settings) {
            ConfigureTexture("Container", ref settings, out var texture, out var textureRegion, out var textureSettings);

            settings.Box.SnapAndInset(out Vector2 a, out Vector2 b);
            // FIXME: Should we draw the outline in Above?
            renderer.RasterizeRectangle(
                a, b,
                radius: ContainerCornerRadius,
                outlineRadius: Button_GetOutlineSize(InertOutlineThickness), outlineColor: ColorScheme.ContainerOutline,
                innerColor: settings.BackgroundColor ?? ColorScheme.ContainerFill, 
                outerColor: settings.BackgroundColor ?? ColorScheme.ContainerFill,
                shadow: ContainerShadow,
                texture: texture,
                textureRegion: textureRegion,
                textureSettings: textureSettings
            );
        }

        protected virtual void FloatingContainer_Below (ref UIOperationContext context, ref ImperativeRenderer renderer, ref DecorationSettings settings) {
            ConfigureTexture("FloatingContainer", ref settings, out var texture, out var textureRegion, out var textureSettings);

            settings.Box.SnapAndInset(out Vector2 a, out Vector2 b);
            // FIXME: Should we draw the outline in Above?
            renderer.RasterizeRectangle(
                a, b,
                radius: FloatingContainerCornerRadius ?? ContainerCornerRadius,
                outlineRadius: Button_GetOutlineSize(InertOutlineThickness), outlineColor: ColorScheme.FloatingContainerOutline ?? ColorScheme.ContainerOutline,
                innerColor: settings.BackgroundColor ?? ColorScheme.FloatingContainerFill ?? ColorScheme.ContainerFill, 
                outerColor: settings.BackgroundColor ?? ColorScheme.FloatingContainerFill ?? ColorScheme.ContainerFill,
                shadow: FloatingContainerShadow ?? ContainerShadow,
                texture: texture,
                textureRegion: textureRegion,
                textureSettings: textureSettings
            );
        }

        protected virtual void Window_Below (ref UIOperationContext context, ref ImperativeRenderer renderer, ref DecorationSettings settings) {
            ConfigureTexture("Window", ref settings, out var texture, out var textureRegion, out var textureSettings);

            // HACK: Handle having composition disabled for windows, since in some cases it's desirable to do that
            var fillColor = (pSRGBColor)(settings.BackgroundColor ?? ColorScheme.WindowFill ?? ColorScheme.FloatingContainerFill ?? ColorScheme.ContainerFill)
                * context.Opacity;
            var shadow = FloatingContainerShadow ?? ContainerShadow;
            if (shadow.HasValue) {
                var _ = shadow.Value;
                _.Color *= context.Opacity;
                shadow = _;
            }

            settings.Box.SnapAndInset(out Vector2 a, out Vector2 b);
            // FIXME: Should we draw the outline in Above?
            renderer.RasterizeRectangle(
                a, b,
                radius: FloatingContainerCornerRadius ?? ContainerCornerRadius,
                outlineRadius: Button_GetOutlineSize(InertOutlineThickness), 
                outlineColor: (ColorScheme.FloatingContainerOutline ?? ColorScheme.ContainerOutline) * context.Opacity,
                innerColor: fillColor, 
                outerColor: fillColor,
                shadow: shadow,
                texture: texture,
                textureRegion: textureRegion,
                textureSettings: textureSettings
            );
        }

        protected virtual void Tooltip_Below (ref UIOperationContext context, ref ImperativeRenderer renderer, ref DecorationSettings settings) {
            ConfigureTexture("Tooltip", ref settings, out var texture, out var textureRegion, out var textureSettings);

            settings.Box.SnapAndInset(out Vector2 a, out Vector2 b);
            // FIXME: Should we draw the outline in Above?
            var color1 = settings.BackgroundColor ?? ColorScheme.TooltipFill;
            var color2 = (color1.ToVector4() * 1.25f);
            color2.W = 1;
            renderer.RasterizeRectangle(
                a, b,
                radius: TooltipCornerRadius ?? FloatingContainerCornerRadius ?? ContainerCornerRadius,
                outlineRadius: Button_GetOutlineSize(InertOutlineThickness), outlineColor: ColorScheme.TooltipOutline,
                innerColor: color2, 
                outerColor: color1,
                shadow: TooltipShadow ?? FloatingContainerShadow,
                texture: texture,
                textureRegion: textureRegion,
                textureSettings: textureSettings
            );
        }

        protected virtual void Menu_Below (ref UIOperationContext context, ref ImperativeRenderer renderer, ref DecorationSettings settings) {
            ConfigureTexture("Menu", ref settings, out var texture, out var textureRegion, out var textureSettings);

            settings.Box.SnapAndInset(out Vector2 a, out Vector2 b);
            // FIXME: Should we draw the outline in Above?
            var color1 = settings.BackgroundColor ?? ColorScheme.TooltipFill;
            var color2 = (color1.ToVector4() * 1.25f);
            float outlineRadius = Button_GetOutlineSize(InertOutlineThickness),
                radius = TooltipCornerRadius ?? FloatingContainerCornerRadius ?? ContainerCornerRadius;
            // For any corners that are aligned with our anchor (if we have one), we make that corner
            //  sharp instead of rounded to subtly convey what this menu is attached to
            var alwaysRounded = settings.HasTrait("always-rounded");
            var radiusCw = new Vector4(
                !alwaysRounded && settings.HasTrait("aligned-tl") ? 0f : radius,
                !alwaysRounded && settings.HasTrait("aligned-tr") ? 0f : radius,
                !alwaysRounded && settings.HasTrait("aligned-br") ? 0f : radius,
                !alwaysRounded && settings.HasTrait("aligned-bl") ? 0f : radius
            );
            color2.W = 1;
            renderer.RasterizeRectangle(
                a, b,
                radiusCW: radiusCw,
                outlineRadius: outlineRadius, 
                outlineColor: ColorScheme.TooltipOutline,
                innerColor: color2, 
                outerColor: color1,
                shadow: TooltipShadow ?? FloatingContainerShadow,
                texture: texture,
                textureRegion: textureRegion,
                textureSettings: textureSettings
            );
        }

        protected virtual void StaticText_Below (ref UIOperationContext context, ref ImperativeRenderer renderer, ref DecorationSettings settings) {
            ConfigureTexture("StaticText", ref settings, out var texture, out var textureRegion, out var textureSettings);

            if (!settings.BackgroundColor.HasValue && (texture == null))
                return;

            settings.Box.SnapAndInset(out Vector2 a, out Vector2 b);
            // FIXME: Should we draw the outline in Above?
            renderer.RasterizeRectangle(
                a, b,
                radius: InertCornerRadius,
                outlineRadius: 0, outlineColor: Color.Transparent,
                innerColor: settings.BackgroundColor.Value, outerColor: settings.BackgroundColor.Value,
                texture: texture,
                textureRegion: textureRegion,
                textureSettings: textureSettings
            );
        }

        protected virtual void EditableText_Below (ref UIOperationContext context, ref ImperativeRenderer renderer, ref DecorationSettings settings) {
            ConfigureTexture("EditableText", ref settings, out var texture, out var textureRegion, out var textureSettings);

            var focusedAlpha = GetFocusedAlpha(ref context, settings.State, out bool isFocused);
            settings.Box.SnapAndInset(out Vector2 a, out Vector2 b);
            renderer.RasterizeRectangle(
                a, b,
                radius: EditableTextCornerRadius,
                // FIXME: Lerp this?
                outlineRadius: Button_GetOutlineSize(isFocused
                    ? EditableFocusedOutlineThickness 
                    : InactiveOutlineThickness), 
                outlineColor: pSRGBColor.Lerp(ColorScheme.ContainerOutline, ColorScheme.Focused, focusedAlpha),
                // FIXME: Separate textarea fill color?
                innerColor: (settings.BackgroundColor ?? ColorScheme.ContainerFill), 
                outerColor: (settings.BackgroundColor ?? ColorScheme.ContainerFill),
                shadow: EditableShadow,
                texture: texture,
                textureRegion: textureRegion,
                textureSettings: textureSettings
            );
        }

        protected virtual void EditableText_ContentClip (ref UIOperationContext context, ref ImperativeRenderer renderer, ref DecorationSettings settings) {
            settings.Box.SnapAndInset(out Vector2 a, out Vector2 b);
            renderer.RasterizeRectangle(
                a, b,
                radius: EditableTextCornerRadius,
                outlineRadius: 0, 
                outlineColor: Color.Transparent,
                innerColor: Color.White, 
                outerColor: Color.White,
                blendState: RenderStates.DrawNone
            );
        }

        protected virtual void Container_ContentClip (ref UIOperationContext context, ref ImperativeRenderer renderer, ref DecorationSettings settings) {
            settings.Box.SnapAndInset(out Vector2 a, out Vector2 b);
            renderer.RasterizeRectangle(
                a, b,
                radius: ContainerCornerRadius,
                outlineRadius: 0, outlineColor: Color.Transparent,
                innerColor: Color.White, outerColor: Color.White,
                blendState: RenderStates.DrawNone
            );
        }

        // HACK: Even if a control is undecorated, explicit background colors should work
        protected virtual void None_Below (ref UIOperationContext context, ref ImperativeRenderer renderer, ref DecorationSettings settings) {
            if (!settings.BackgroundColor.HasValue)
                return;

            settings.Box.SnapAndInset(out Vector2 a, out Vector2 b);
            renderer.RasterizeRectangle(
                a, b,
                radius: 0,
                outlineRadius: 0, outlineColor: Color.Transparent,
                innerColor: settings.BackgroundColor.Value, outerColor: settings.BackgroundColor.Value
            );
        }

        // HACK: Even if a control is undecorated, it still needs to be able to rasterize its clip region
        protected virtual void None_ContentClip (ref UIOperationContext context, ref ImperativeRenderer renderer, ref DecorationSettings settings) {
            settings.Box.SnapAndInset(out Vector2 a, out Vector2 b);
            renderer.RasterizeRectangle(
                a, b,
                radius: 0,
                outlineRadius: 0, outlineColor: Color.Transparent,
                innerColor: Color.White, outerColor: Color.White,
                blendState: RenderStates.DrawNone
            );
        }

        protected virtual void Scrollbar_ComputeBoxes (
            DecorationSettings settings, ref ScrollbarState data, out float scrollDivisor,
            out Vector2 trackA, out Vector2 trackB,
            out Vector2 thumbA, out Vector2 thumbB
        ) {
            var psize = PaddingScaleRatio * SpacingScaleRatio;
            var padding = Scrollbar.Padding;
            Margins.Scale(ref padding, in psize);
            settings.Box.SnapAndInset(out Vector2 ba, out Vector2 bb, padding + Scrollbar.UnscaledPadding);

            var vRadius = new Vector2(ScrollbarRadius);
            float min = 0, max = 0;
            if (data.ContentSize > data.ViewportSize) {
                float divisor = Math.Max(0.1f, data.ContentSize);
                scrollDivisor = data.ViewportSize;
                var thumbSize = data.ViewportSize / divisor;
                var thumbSizePx = thumbSize * data.ViewportSize;
                if (thumbSizePx < ScrollbarMinThumbSize) {
                    var expansion = ScrollbarMinThumbSize - thumbSizePx;
                    var expansionFrac = expansion / data.ViewportSize;
                    var expansionContentPx = expansionFrac * divisor;
                    divisor = Math.Max(divisor + expansionContentPx, 0.1f);
                    // FIXME
                    scrollDivisor = scrollDivisor;
                    thumbSizePx = ScrollbarMinThumbSize;
                }
                var thumbSizeFrac = thumbSizePx / data.ViewportSize;
                min = Arithmetic.Saturate(data.Position / divisor);
                max = min + thumbSizeFrac;
            } else {
                scrollDivisor = 0;
            }

            var effectiveScrollbarSize = ScrollbarSize * SizeScaleRatio;
            float maxOffset = 0;
            if (data.HasCounterpart && (data.ContentSize > data.ViewportSize)) {
                maxOffset = (data.Horizontal ? effectiveScrollbarSize.X : effectiveScrollbarSize.Y);
                // FIXME
                // divisor = Math.Max(0.1f, divisor - maxOffset);
            }
            trackA = data.Horizontal
                ? new Vector2(ba.X, bb.Y - effectiveScrollbarSize.Y)
                : new Vector2(bb.X - effectiveScrollbarSize.X, ba.Y);
            trackB = bb;

            if (data.Horizontal) {
                var b = trackB.X - maxOffset;
                thumbA.X = Arithmetic.Lerp(trackA.X, b, min);
                thumbA.Y = trackA.Y;
                thumbB.X = Arithmetic.Lerp(trackA.X, b, max);
                thumbB.Y = trackB.Y;
            } else {
                var b = trackB.Y - maxOffset;
                thumbA.X = trackA.X;
                thumbA.Y = Arithmetic.Lerp(trackA.Y, b, min);
                thumbB.X = trackB.X;
                thumbB.Y = Arithmetic.Lerp(trackA.Y, b, max);
            }
        }

        protected virtual bool Scrollbar_UpdateDrag (ref ScrollbarState data, MouseEventArgs args) {
            if (!data.DragInitialMousePosition.HasValue)
                return false;

            var dragDistance = data.Horizontal
                ? args.GlobalPosition.X - data.DragInitialMousePosition.Value.X
                : args.GlobalPosition.Y - data.DragInitialMousePosition.Value.Y;
            var dragDeltaUnit = dragDistance / data.DragSizePx;
            var dragDeltaScaled = dragDeltaUnit * data.ContentSize;
            data.Position = data.DragInitialPosition + dragDeltaScaled;
            return true;
        }

        protected virtual bool Scrollbar_OnHitTest (ref DecorationSettings settings, ref ScrollbarState data, Vector2 position) {
            Scrollbar_ComputeBoxes(
                settings, ref data, out float sizePx,
                out Vector2 trackA, out Vector2 trackB,
                out Vector2 thumbA, out Vector2 thumbB
            );

            RectF box1 = new RectF(trackA, trackB - trackA);
            return box1.Contains(position);
        }

        protected virtual bool Scrollbar_OnMouseEvent (
            ref DecorationSettings settings, ref ScrollbarState data, string eventName, MouseEventArgs args
        ) {
            Scrollbar_ComputeBoxes(
                settings, ref data, out float sizePx,
                out Vector2 trackA, out Vector2 trackB,
                out Vector2 thumbA, out Vector2 thumbB
            );

            var thumb = new RectF(thumbA, thumbB - thumbA);
            var processed = false;
            if (
                thumb.Contains(args.GlobalPosition) || 
                data.DragInitialMousePosition.HasValue
            ) {
                if (eventName == UIEvents.MouseDown) {
                    data.DragSizePx = sizePx;
                    data.DragInitialPosition = data.Position;
                    data.DragInitialMousePosition = args.GlobalPosition;
                } else if (eventName == UIEvents.MouseMove) {
                    if (args.Buttons == MouseButtons.Left)
                        processed = Scrollbar_UpdateDrag(ref data, args);
                } else if (eventName == UIEvents.MouseUp) {
                    processed = Scrollbar_UpdateDrag(ref data, args);
                    data.DragInitialMousePosition = null;
                }
            }

            var track = new RectF(trackA, trackB - trackA);
            if (track.Contains(args.GlobalPosition))
                processed = true;

            return processed;
        }

        protected virtual void Scrollbar_Above (ref UIOperationContext context, ref ImperativeRenderer renderer, ref DecorationSettings settings, ref ScrollbarState data) {
            Scrollbar_ComputeBoxes(
                settings, ref data, out float divisor,
                out Vector2 trackA, out Vector2 trackB,
                out Vector2 thumbA, out Vector2 thumbB
            );

            renderer.RasterizeRectangle(
                trackA, trackB,
                radius: ScrollbarRadius,
                outlineRadius: 0, outlineColor: Color.Transparent,
                innerColor: ColorScheme.ScrollbarTrack, outerColor: ColorScheme.ScrollbarTrack,
                sortKey: 1
            );

            if (data.ContentSize <= data.ViewportSize)
                return;

            renderer.RasterizeRectangle(
                thumbA, thumbB,
                radius: ScrollbarRadius,
                outlineRadius: 0, outlineColor: Color.Transparent,
                innerColor: ColorScheme.ScrollbarThumb, outerColor: ColorScheme.ScrollbarThumb * 0.8f,
                fill: RasterFillMode.Radial,
                shadow: ScrollbarThumbShadow,
                sortKey: 2
            );
        }

        protected virtual void ContainerTitle_Below (ref UIOperationContext context, ref ImperativeRenderer renderer, ref DecorationSettings settings) {
            float cornerRadius = ContainerCornerRadius,
                cornerRadius2 = settings.HasStateFlag(ControlStates.Pressed) // HACK: When collapsed, round all corners
                    ? cornerRadius
                    : 0;
            TitleCommon_Below(ref renderer, settings, cornerRadius, cornerRadius2);
        }

        protected virtual void WindowTitle_Below (ref UIOperationContext context, ref ImperativeRenderer renderer, ref DecorationSettings settings) {
            float cornerRadius = FloatingContainerCornerRadius ?? ContainerCornerRadius,
                cornerRadius2 = settings.HasStateFlag(ControlStates.Pressed) // HACK: When collapsed, round all corners
                    ? cornerRadius
                    : 0;
            TitleCommon_Below(ref renderer, settings, cornerRadius, cornerRadius2);
        }

        protected virtual void TitleCommon_Below (ref ImperativeRenderer renderer, DecorationSettings settings, float cornerRadius, float cornerRadius2) {
            var containsFocus = settings.HasStateFlag(ControlStates.ContainsFocus);
            settings.Box.SnapAndInset(out Vector2 a, out Vector2 b);
            // FIXME: Should we draw the outline in Above?
            var color1 = (pSRGBColor)(containsFocus ? ColorScheme.TitleFill : ColorScheme.TitleFill.ToGrayscale(0.85f));
            var color2 = color1.ToVector4() * 0.8f;
            color2.W = 1;

            ConfigureTexture("Title", ref settings, out var texture, out var textureRegion, out var textureSettings);

            renderer.RasterizeRectangle(
                a, b,
                radiusCW: new Vector4(cornerRadius, cornerRadius, cornerRadius2, cornerRadius2),
                outlineRadius: 0, outlineColor: Color.Transparent,
                innerColor: color1, outerColor: color2,
                fill: RasterFillMode.Vertical,
                shadow: TitleShadow,
                texture: texture,
                textureSettings: textureSettings,
                textureRegion: textureRegion
            );
        }

        protected virtual void Selection_Below (ref UIOperationContext context, ref ImperativeRenderer renderer, ref DecorationSettings settings) {
            settings.Box.SnapAndInset(out Vector2 a, out Vector2 b, -SelectionPadding);
            var isCaret = (settings.Box.Width <= 0.5f);
            var focusedAlpha = GetFocusedAlpha(ref context, settings.State, out bool isFocused);
            if (settings.HasStateFlag(ControlStates.ContainsFocus))
                isFocused = true;
            var fillColor = ColorScheme.SelectionFill *
                Arithmetic.Lerp(0.45f, Arithmetic.Pulse(context.Now / 2f, 0.7f, 0.8f), focusedAlpha) *
                (isCaret ? 1.8f : 1f);
            var outlineAlpha = !isCaret
                ? focusedAlpha
                : 0f;
            var outlineColor = Color.Lerp(Color.Transparent, Color.White, outlineAlpha);

            ConfigureTexture("Selection", ref settings, out var texture, out var textureRegion, out var textureSettings);

            renderer.RasterizeRectangle(
                a, b,
                radius: SelectionCornerRadius,
                outlineRadius: outlineAlpha * 0.9f,
                outlineColor: outlineColor,
                innerColor: fillColor, outerColor: fillColor,
                shadow: SelectionShadow,
                texture: texture,
                textureSettings: textureSettings,
                textureRegion: textureRegion
            );
        }

        protected virtual void MenuSelection_Below (ref UIOperationContext context, ref ImperativeRenderer renderer, ref DecorationSettings settings) {
            settings.Box.SnapAndInset(out Vector2 a, out Vector2 b, -SelectionPadding);
            var isFocused = settings.HasStateFlag(ControlStates.Focused) ||
                settings.HasStateFlag(ControlStates.ContainsFocus);

            ConfigureTexture("MenuSelection", ref settings, out var texture, out var textureRegion, out var textureSettings);

            var fillColor = (
                isFocused || (texture != null)
                    ? ColorScheme.SelectionFill
                    : pSRGBColor.Lerp(ColorScheme.SelectionFill, ColorScheme.SelectionFill.ToGrayscale(0.65f), 0.5f)
                );
            fillColor *= Arithmetic.Pulse(context.Now / 2f, 0.9f, 1f);

            if ((texture != null) && !isFocused) {
                textureSettings.Saturation -= 0.35f;
                textureSettings.Brightness -= 0.1f;
            }

            renderer.RasterizeRectangle(
                a, b,
                radius: MenuSelectionCornerRadius,
                outlineRadius: 0.9f, outlineColor: fillColor,
                innerColor: fillColor, outerColor: fillColor * 0.75f,
                fill: RasterFillMode.RadialEnclosing,
                texture: texture,
                textureSettings: textureSettings,
                textureRegion: textureRegion
            );
        }

        protected virtual void ListSelection_Below (ref UIOperationContext context, ref ImperativeRenderer renderer, ref DecorationSettings settings) {
            settings.Box.SnapAndInset(out Vector2 a, out Vector2 b, -SelectionPadding);
            var isFocused = settings.HasStateFlag(ControlStates.Focused) ||
                settings.HasStateFlag(ControlStates.ContainsFocus);
            ConfigureTexture("ListSelection", ref settings, out var texture, out var textureRegion, out var textureSettings);

            var fillColor = (
                isFocused || (texture != null)
                    ? ColorScheme.SelectionFill
                    : pSRGBColor.Lerp(ColorScheme.SelectionFill, ColorScheme.SelectionFill.ToGrayscale(0.65f), 0.5f)
                );
            var outlineColor = (isFocused)
                ? Color.White
                : fillColor * 0.5f;

            if ((texture != null) && !isFocused) {
                textureSettings.Saturation -= 0.35f;
                textureSettings.Brightness -= 0.1f;
            }

            renderer.RasterizeRectangle(
                a, b,
                radius: ListSelectionCornerRadius,
                outlineRadius: 0.9f, outlineColor: outlineColor,
                innerColor: fillColor, outerColor: fillColor,
                fill: RasterFillMode.Horizontal,
                texture: texture,
                textureSettings: textureSettings,
                textureRegion: textureRegion
            );
        }

        protected virtual void CompositionPreview_Below (ref UIOperationContext context, ref ImperativeRenderer renderer, ref DecorationSettings settings) {
            settings.Box.SnapAndInset(out Vector2 a, out Vector2 b, -SelectionPadding);
            var fillColor = ColorScheme.SelectionFill;
            var outlineColor = Color.White;

            renderer.RasterizeRectangle(
                a, b,
                radius: SelectionCornerRadius,
                outlineRadius: 0.7f, 
                outlineColor: outlineColor,
                innerColor: fillColor, outerColor: fillColor,
                shadow: SelectionShadow
            );
        }

        protected virtual void AcceleratorLabel_Below (ref UIOperationContext context, ref ImperativeRenderer renderer, ref DecorationSettings settings) {
            ConfigureTexture("AcceleratorLabel", ref settings, out var texture, out var textureRegion, out var textureSettings);

            // HACK
            Vector4 radius;
            switch (settings.Traits.FirstOrDefault()) {
                case "inside":
                    radius = new Vector4(0, 0, 5, 0);
                    break;
                case "below":
                    radius = new Vector4(0, 0, 5, 5);
                    break;
                case "stacked":
                    radius = new Vector4(5);
                    break;
                case "above":
                default:
                    radius = new Vector4(5, 5, 0, 0);
                    break;
            }
            settings.Box.SnapAndInset(out Vector2 a, out Vector2 b);
            // FIXME
            renderer.RasterizeRectangle(
                a, b,
                radiusCW: radius,
                outlineRadius: Button_GetOutlineSize(1f), outlineColor: ColorScheme.AcceleratorOutline,
                innerColor: ColorScheme.AcceleratorFill, outerColor: ColorScheme.AcceleratorFill,
                shadow: null,
                texture: texture,
                textureRegion: textureRegion,
                textureSettings: textureSettings
            );
        }

        protected virtual void AcceleratorTarget_Below (ref UIOperationContext context, ref ImperativeRenderer renderer, ref DecorationSettings settings) {
            settings.Box.SnapAndInset(out Vector2 a, out Vector2 b);
            var outlineColor = ColorScheme.AcceleratorOutline * Arithmetic.PulseSine((context.Now / 1.3f) + (a.X / 512), 0.65f, 1.0f);
            // FIXME
            renderer.RasterizeRectangle(
                a, b,
                radius: 0f,
                outlineRadius: Button_GetOutlineSize(1f), outlineColor: outlineColor,
                innerColor: pSRGBColor.Transparent, outerColor: pSRGBColor.Transparent,
                shadow: AcceleratorTargetShadow
            );
        }

        protected virtual void ParameterGauge_Below (ref UIOperationContext context, ref ImperativeRenderer renderer, ref DecorationSettings settings) {
            var radius = new Vector4(InertCornerRadius, InertCornerRadius, InertCornerRadius, InertCornerRadius);
            bool isFocused = settings.HasStateFlag(ControlStates.Focused),
                isHovering = settings.HasStateFlag(ControlStates.Hovering);
            var outlineRadius = isFocused ? 1f : 0f;
            var outlineColor = pSRGBColor.Black(0.8f);
            settings.ContentBox.SnapAndInset(out Vector2 a, out Vector2 b);
            float alpha1 = (isFocused ? 0.75f : 0.6f) * (isHovering ? 0.75f : 0.6f);
            float alpha2 = (isFocused ? 1.0f : 0.7f) * (isHovering ? 1.0f : 0.75f);
            alpha2 = Arithmetic.Lerp(alpha1, alpha2, (b.X - a.X) / (settings.Box.Width));
            renderer.RasterizeRectangle(
                a, b, radiusCW: radius,
                outlineRadius: Button_GetOutlineSize(outlineRadius), outlineColor: outlineColor,
                innerColor: pSRGBColor.White(alpha1), outerColor: pSRGBColor.White(alpha2),
                fill: RasterFillMode.Horizontal
            );
        }

        // FIXME: Why this offset?
        static Vector2 WeirdVirtualCursorOffset = new Vector2(0.9f);
        public float VirtualCursorThickness = 2.5f,
            VirtualCursorOutlineThickness = 1.5f,
            VirtualCursorAnchorRadius1 = 2.5f,
            VirtualCursorAnchorRadius2 = 1.55f,
            VirtualCursorOutlineAlpha = 0.9f,
            VirtualCursorLockedAlpha = 0.55f,
            VirtualCursorUnlockedAlpha = 0.95f;
        public pSRGBColor VirtualCursorColor = Color.White,
            VirtualCursorOutlineColor = Color.Black;

        protected virtual void VirtualCursor_Above (ref UIOperationContext context, ref ImperativeRenderer renderer, ref DecorationSettings settings) {
            var center = settings.Box.Center + WeirdVirtualCursorOffset;
            var radius = (settings.Box.Size / 2f).X; // HACK
            var showCenter = !settings.HasStateFlag(ControlStates.Disabled);
            var alpha = settings.HasStateFlag(ControlStates.Disabled) ? VirtualCursorLockedAlpha : VirtualCursorUnlockedAlpha;
            var thickness = (showCenter ? 1.2f : 1.0f) * VirtualCursorThickness;
            float fillAlpha = (alpha * 0.85f) + 0.05f, fillAlpha2 = (alpha * 0.85f) - 0.35f,
                fillOffset = (float)Time.Seconds * 0.4f;
            renderer.RasterSoftOutlines = true;
            renderer.RasterizeArc(
                center, 0f, 360f, radius, thickness * SizeScaleRatio.X, // HACK
                innerColor: VirtualCursorColor * fillAlpha, outerColor: VirtualCursorColor * fillAlpha2,
                outlineRadius: Button_GetOutlineSize(VirtualCursorOutlineThickness), outlineColor: VirtualCursorOutlineColor * (alpha * VirtualCursorOutlineAlpha),
                fill: new RasterFillSettings {
                    Mode = RasterFillMode.Along,
                    Size = 0.25f,
                    Repeat = true,
                    Offset = fillOffset
                }
            );
        }

        protected virtual void VirtualCursorAnchor_Above (ref UIOperationContext context, ref ImperativeRenderer renderer, ref DecorationSettings settings) {
            if (settings.HasStateFlag(ControlStates.Disabled))
                return;

            var unsnapped = settings.Box.Position + WeirdVirtualCursorOffset;
            var snapped = settings.Box.Extent + WeirdVirtualCursorOffset;
            var distance = (snapped - unsnapped).Length();
            var alpha = 0.9f;
            var outlineRadius = 1.75f;

            if (distance >= 0.5f) {
                renderer.RasterizeLineSegment(
                    a: unsnapped, b: snapped,
                    startRadius: VirtualCursorAnchorRadius2, endRadius: VirtualCursorAnchorRadius1,
                    innerColor: pSRGBColor.White(alpha * 0.75f), outerColor: pSRGBColor.White(),
                    outlineRadius: Button_GetOutlineSize(outlineRadius), outlineColor: pSRGBColor.Black(alpha)
                );
            } else {
                var fillAlpha = alpha * 0.85f;
                renderer.RasterizeEllipse(
                    snapped, new Vector2(1.7f),
                    innerColor: pSRGBColor.White(fillAlpha), outerColor: pSRGBColor.White(fillAlpha),
                    outlineRadius: Button_GetOutlineSize(outlineRadius), outlineColor: pSRGBColor.Black(alpha)
                );
            }
        }

        public bool GetTextSettings (
            ref UIOperationContext context, ControlStates state, 
            out Material material, ref Color? color, out Vector4 userData
        ) {
            return GetTextSettings(ref context, state, out material, ref color, out userData, TextStyle.Normal);
        }

        public bool GetTextSettings (
            ref UIOperationContext context, ControlStates state, 
            out Material material, ref Color? color, out Vector4 userData, TextStyle style
        ) {
            if (!color.HasValue)
                color = ColorScheme.Text;

            if (state.IsFlagged(ControlStates.Disabled))
                color = color?.ToGrayscale(DisabledTextAlpha);

            switch (style) {
                case TextStyle.Normal:
                default:
                    material = TextMaterial;
                    userData = default;
                    break;
                case TextStyle.Selected:
                    material = SelectedTextMaterial;
                    userData = new Vector4(1, 1, 1, 3);
                    break;
                case TextStyle.Shaded:
                    material = ShadedTextMaterial;
                    userData = default;
                    break;
            }
            return true;
        }

        public bool GetTextSettings_Description (
            ref UIOperationContext context, ControlStates state, 
            out Material material, ref Color? color, out Vector4 userData
        ) {
            // HACK: Pass selected=true to get the unshadowed material
            var result = GetTextSettings(ref context, state, out material, ref color, out userData, TextStyle.Shaded);
            if (color.HasValue)
                color = color.Value * 0.5f;
            return result;
        }

        protected virtual bool GetTextSettings_None (
            ref UIOperationContext context, ControlStates state, 
            out Material material, ref Color? color, out Vector4 userData
        ) {
            state &= ~ControlStates.Focused;
            state &= ~ControlStates.Checked;
            GetTextSettings(ref context, state, out material, ref color, out userData, TextStyle.Normal);
            return true;
        }

        protected virtual bool GetTextSettings_Button (
            ref UIOperationContext context, ControlStates state, 
            out Material material, ref Color? color, out Vector4 userData
        ) {
            GetTextSettings(ref context, state, out material, ref color, out userData);
            return true;
        }

        protected virtual void GetContentAdjustment_Button (ref UIOperationContext context, ControlStates state, out Vector2 offset, out Vector2 scale) {
            scale = Vector2.One;
            if (state.IsFlagged(ControlStates.Pressed)) {
                offset = new Vector2(0, 2);
            } else {
                offset = new Vector2(0, 0);
            }
        }

        protected virtual bool GetTextSettings_Title (
            ref UIOperationContext context, ControlStates state, 
            out Material material, ref Color? color, out Vector4 userData
        ) {
            if (color == null)
                color = ColorScheme.TitleText;
            GetTextSettings(ref context, state, out material, ref color, out userData);
            return true;
        }

        protected virtual bool GetTextSettings_AcceleratorLabel (
            ref UIOperationContext context, ControlStates state, 
            out Material material, ref Color? color, out Vector4 userData
        ) {
            if (color == null)
                color = ColorScheme.AcceleratorText;
            GetTextSettings(ref context, state, out material, ref color, out userData);
            return true;
        }

        protected virtual bool GetTextSettings_Tooltip (
            ref UIOperationContext context, ControlStates state, 
            out Material material, ref Color? color, out Vector4 userData
        ) {
            if (color == null)
                color = ColorScheme.TooltipText;
            GetTextSettings(ref context, state, out material, ref color, out userData);
            return true;
        }

        protected virtual bool GetTextSettings_Selection (
            ref UIOperationContext context, ControlStates state, 
            out Material material, ref Color? color, out Vector4 userData
        ) {
            GetTextSettings(ref context, state, out material, ref color, out userData, TextStyle.Selected);
            color = ColorScheme.SelectedText;
            return true;
        }
    }

    public sealed class FadeAnimation : IControlAnimation {
        public float DefaultDuration { get; set; }
        public float? From;
        public float To;
        public bool RemoveAtEnd;

        void IControlAnimation.End (Control control, bool cancelled) {
            if (RemoveAtEnd && !cancelled) {
                if (control.TryGetParent(out var parent) && parent is IControlContainer icc)
                    icc.Children.Remove(control);
                else
                    control.Context.Controls.Remove(control);
            }
        }

        void IControlAnimation.Start (Control control, long now, float duration) {
            control.Appearance.Opacity = Tween.StartNow(
                From ?? control.Appearance.Opacity.Get(now), To,
                seconds: duration, now: now
            );
        }
    }
}
