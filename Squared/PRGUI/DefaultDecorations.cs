﻿using System;
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
using Squared.Render.RasterShape;
using Squared.Render.Text;
using Squared.Util;

namespace Squared.PRGUI {
    public class DefaultDecorations : IDecorationProvider, IAnimationProvider {
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
        public IDecorator AcceleratorLabel { get; set; }
        public IDecorator AcceleratorTarget { get; set; }
        public IDecorator ParameterGauge { get; set; }
        public IDecorator Gauge { get; set; }
        public IDecorator VirtualCursor { get; set; }
        public IDecorator VirtualCursorAnchor { get; set; }

        public float AnimationDurationMultiplier { get; set; }

        public IControlAnimation ShowModalDialog { get; set; }
        public IControlAnimation HideModalDialog { get; set; }
        public IControlAnimation ShowMenu { get; set; }
        public IControlAnimation HideMenu { get; set; }

        public IMetricsProvider Description { get; set; }

        public IWidgetDecorator<ScrollbarState> Scrollbar { get; set; }

        private Vector2 _SizeScaleRatio;
        public Vector2 SizeScaleRatio {
            get => _SizeScaleRatio;
            set {
                _SizeScaleRatio = value;
                UpdateScaledSizes();
            }
        }
        public Vector2 SpacingScaleRatio { get; set; }

        private Material TextMaterial, SelectedTextMaterial;

        public DefaultDecorations (DefaultMaterialSet materials, float defaultMargin = 6, float defaultMarginCollapsed = 4) {
            Materials = materials;

            TextMaterial = materials.Get(materials.ScreenSpaceShadowedBitmap, blendState: BlendState.AlphaBlend);
            SelectedTextMaterial = materials.Get(materials.ScreenSpaceBitmap, blendState: BlendState.AlphaBlend);

            GlobalDefaultMargin = defaultMargin;
            GlobalDefaultMarginCollapsed = defaultMarginCollapsed;

            AnimationDurationMultiplier = 1f;
            _SizeScaleRatio = Vector2.One;
            SpacingScaleRatio = Vector2.One;

            InteractableShadow = new RasterShadowSettings {
                Color = Color.Black * 0.25f,
                Offset = new Vector2(1.5f, 2f),
                Softness = 5f
            };

            ContainerShadow = null;
            ScrollbarThumbShadow = null;

            FloatingContainerShadow = new RasterShadowSettings {
                Color = Color.Black * 0.33f,
                Offset = new Vector2(2.5f, 3f),
                Softness = 8f
            };

            EditableShadow = new RasterShadowSettings {
                Color = Color.Black * 0.3f,
                Offset = new Vector2(1.25f, 1.5f),
                Softness = 6f,
                Expansion = 0.4f,
                Inside = true
            };

            SliderShadow = new RasterShadowSettings {
                Color = Color.Black * 0.2f,
                Offset = new Vector2(1.25f, 1.5f),
                Softness = 6f,
                Expansion = 0.4f,
                Inside = true
            };

            GaugeShadow = SliderShadow;

            SelectionShadow = new RasterShadowSettings {
                Color = Color.White * 0.15f,
                Offset = new Vector2(1.15f, 1.35f),
                Softness = 2f
            };

            AcceleratorTargetShadow = new RasterShadowSettings {
                Color = Color.Black * 0.5f,
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
                Padding = new Margins(16, 8),
                GetContentAdjustment = GetContentAdjustment_Button,
                GetTextSettings = GetTextSettings_Button,
                GetFont = () => ButtonFont ?? DefaultFont,
                Below = Button_Below,
                Above = Button_Above
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
                Padding = new Margins(4, 32, 4, 4),
                GetTextSettings = GetTextSettings,
                Below = FloatingContainer_Below,
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
                Margins = new Margins(GlobalDefaultMarginCollapsed, GlobalDefaultMargin),
                Padding = new Margins(6),
                GetTextSettings = GetTextSettings,
                GetFont = () => DefaultFont,
                Below = StaticText_Below,
            };

            Tooltip = new DelegateDecorator {
                Padding = new Margins(12, 8, 12, 8),
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
                Below = Tooltip_Below,
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
                Content = Selection_Content,
            };

            MenuSelection = new DelegateDecorator {
                GetTextSettings = GetTextSettings_Selection,
                Content = MenuSelection_Content,
            };

            ListSelection = new DelegateDecorator {
                GetTextSettings = GetTextSettings_Selection,
                Margins = new Margins(1),
                Content = ListSelection_Content,
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
                Margins = new Margins(GlobalDefaultMargin),
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
                Padding = new Margins(16, 8, 16 + DropdownArrowWidth + 6, 8),
                GetContentAdjustment = GetContentAdjustment_Button,
                GetTextSettings = GetTextSettings_Button,
                GetFont = () => ButtonFont ?? DefaultFont,
                Below = Button_Below,
                Above = Dropdown_Above
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
                Padding = new Margins(4, 0, 0, 7.5f)
            };

            VirtualCursor = new DelegateDecorator {
                Padding = new Margins(24),
                Above = VirtualCursor_Above
            };

            VirtualCursorAnchor = new DelegateDecorator {
                Above = VirtualCursorAnchor_Above
            };

            Scrollbar = new DelegateWidgetDecorator<ScrollbarState> {
                MinimumSize = new Vector2(ScrollbarSize, ScrollbarSize),
                Above = Scrollbar_Above,
                OnMouseEvent = Scrollbar_OnMouseEvent,
                OnHitTest = Scrollbar_OnHitTest
            };

            ShowMenu = new FadeAnimation {
                To = 1f,
                DefaultDuration = MenuShowDuration
            };

            HideMenu = new FadeAnimation {
                To = 0f,
                DefaultDuration = MenuHideDuration
            };

            ShowModalDialog = new FadeAnimation {
                To = 1f,
                DefaultDuration = ModalDialogShowDuration
            };

            HideModalDialog = new FadeAnimation {
                To = 0f,
                DefaultDuration = ModalDialogHideDuration
            };

            GaugeValueFillColor = SelectionFillColor;

            UpdateScaledSizes();
        }

        private void UpdateScaledSizes () {
            ((DelegateDecorator)Checkbox).Padding =
                ((DelegateDecorator)RadioButton).Padding =
                new Margins(6 + ScaledCheckboxSize + (4 * SizeScaleRatio.X), 6, 6, 6);
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
            SliderCornerRadius = 4.5f;
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

        public Color FocusedColor = new Color(200, 220, 255),
            ActiveColor = new Color(240, 240, 240),
            InactiveColor = new Color(180, 180, 180),
            ContainerOutlineColor = new Color(32, 32, 32) * 0.5f,
            InertOutlineColor = new Color(255, 255, 255) * 0.33f,
            TooltipOutlineColor = new Color(16, 16, 16) * 0.5f,
            ScrollbarThumbColor = new Color(200, 200, 200),
            ScrollbarTrackColor = new Color(48, 48, 48),
            AcceleratorOutlineColor = Color.White;

        public Color TitleFillColor = new Color(40, 100, 120),
            ContainerFillColor = Color.Transparent,
            InertFillColor = Color.Transparent,
            SelectionFillColor = new Color(200, 230, 255),
            TooltipFillColor = new Color(48, 48, 48),
            SliderFillColor = Color.Black * 0.1f,
            AcceleratorFillColor = Color.Black * 0.8f,
            GaugeFillColor = Color.Black * 0.1f,
            GaugeValueFillColor;

        public Color SelectedTextColor = new Color(0, 30, 55),
            TitleTextColor = Color.White,
            TextColor = Color.White,
            TooltipTextColor = Color.White,
            AcceleratorTextColor = Color.White;

        public const float DropdownArrowWidth = 16, DropdownArrowHeight = 11, DropdownArrowPadding = 8;
        public float ScaledCheckboxSize => CheckboxSize * (SizeScaleRatio.Length() / 1.41421354f);
        public float CheckboxSize = 32;
        public float DisabledTextAlpha = 0.5f;

        public Color? FloatingContainerOutlineColor, 
            FloatingContainerFillColor;

        private void Button_Below_Common (
            UIOperationContext context, DecorationSettings settings, 
            out float alpha, out float thickness, out float pulse,
            out pSRGBColor baseColor, out pSRGBColor outlineColor 
        ) {
            var state = settings.State;
            var isFocused = state.IsFlagged(ControlStates.Focused);
            baseColor = settings.BackgroundColor ?? (pSRGBColor)(
                isFocused
                    ? FocusedColor
                    : InactiveColor
            );
            var hasColor = settings.BackgroundColor.HasValue;
            var colorIsGray = hasColor && (baseColor.ColorDelta <= 0.05f);
            // HACK: If the background color isn't saturated, use the focused color for the outline
            pSRGBColor? outlineBaseColor = (colorIsGray && isFocused) ? FocusedColor : (pSRGBColor?)null;
            var pulseThickness = Arithmetic.PulseSine(context.Now / 3f, 0, 0.4f);

            pulse = 0;
            if (
                state.IsFlagged(ControlStates.Pressed) ||
                // HACK
                state.IsFlagged(ControlStates.Checked)
            ) {
                alpha = hasColor ? 1f : 0.9f;
                thickness = PressedOutlineThickness;
                if (hasColor) {
                    // Intensify the color if the button has a custom color
                    baseColor = (settings.BackgroundColor.Value.ToVector4()) * 1.25f;
                    baseColor.Vector4.W = 1;
                } else
                    baseColor = ActiveColor;
                outlineColor = (outlineBaseColor ?? baseColor) + (hasColor ? 0.4f : 0.05f);
            } else if (state.IsFlagged(ControlStates.Hovering)) {
                alpha = hasColor 
                    ? 0.95f 
                    : (isFocused ? 0.8f : 0.55f);
                thickness = ActiveOutlineThickness + pulseThickness;
                pulse = Arithmetic.PulseSine(context.Now / 2.5f, 0f, 0.15f);
                if (hasColor)
                    outlineColor = (outlineBaseColor ?? baseColor) + (hasColor ? 0.3f : 0f);
                else
                    outlineColor = (outlineBaseColor ?? baseColor);
            } else {
                alpha = hasColor
                    ? (isFocused ? 0.95f : 0.85f)
                    : (isFocused ? 0.75f : 0.4f);
                thickness = isFocused
                    ? ActiveOutlineThickness + pulseThickness
                    : InactiveOutlineThickness;
                if (hasColor && !colorIsGray)
                    outlineColor = (outlineBaseColor ?? baseColor) + (isFocused ? 0.3f : 0.05f);
                else
                    outlineColor = (outlineBaseColor ?? baseColor);
            }
        }

        private void Button_Below (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings) {
            float alpha, thickness, pulse;
            pSRGBColor baseColor, outlineColor;
            Button_Below_Common(
                context, settings, out alpha, out thickness, out pulse, 
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

            settings.Box.SnapAndInset(out Vector2 a, out Vector2 b);
            renderer.RasterizeRectangle(
                a, b,
                radius: InteractableCornerRadius,
                outlineRadius: thickness, outlineColor: outlineColor * alpha,
                innerColor: color1 * fillAlpha1, outerColor: color2 * fillAlpha2,
                fillMode: RasterFillMode.RadialEnclosing, fillSize: 0.95f,
                shadow: InteractableShadow,
                texture: settings.GetTexture(),
                textureRegion: settings.GetTextureRegion(),
                textureSettings: settings.GetTextureSettings()
            );
        }

        private void Button_Above (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings) {
            if (!settings.State.IsFlagged(ControlStates.Hovering))
                return;

            settings.Box.SnapAndInset(out Vector2 a, out Vector2 b);
            float fillSize = Math.Max(0.05f, Math.Min(0.9f, 64f / settings.Box.Height));

            renderer.RasterizeRectangle(
                a, b,
                radius: InteractableCornerRadius,
                outlineRadius: 0, outlineColor: Color.Transparent,
                innerColor: Color.White * EdgeGleamOpacity, outerColor: Color.White * 0.0f,
                fillMode: RasterFillMode.Angular,
                fillSize: fillSize,
                fillOffset: -Arithmetic.PulseSine(context.Now / 4f, 0f, 0.05f),
                fillAngle: Arithmetic.PulseCyclicExp(context.Now / 2f, 3),
                annularRadius: EdgeGleamThickness,
                blendState: BlendState.Additive
            );
        }

        private void Dropdown_Above (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings) {
            Button_Above(context, ref renderer, settings);
            var isPressed = settings.State.IsFlagged(ControlStates.Pressed);
            GetContentAdjustment_Button(context, settings.State, out Vector2 offset, out Vector2 scale);
            settings.ContentBox.SnapAndInset(out Vector2 tl, out Vector2 br);

            var ySpace = (float)Math.Floor((settings.ContentBox.Height - DropdownArrowHeight) / 2f);
            var a = new Vector2(br.X + DropdownArrowPadding + offset.X, tl.Y + ySpace + offset.Y);
            var b = a + new Vector2(DropdownArrowWidth, DropdownArrowHeight);
            var color = Color.White;
            var outlineColor = Color.Black;

            renderer.RasterizeTriangle(
                a, new Vector2(b.X, a.Y),
                new Vector2((a.X + b.X) / 2f, b.Y),
                radius: 1f, outlineRadius: 1f,
                innerColor: color, outerColor: color, 
                outlineColor: outlineColor
            );
        }

        private void Slider_Below (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings) {
            settings.Box.SnapAndInset(out Vector2 a, out Vector2 b);
            renderer.RasterizeRectangle(
                a, b,
                radius: SliderCornerRadius,
                outlineRadius: InertOutlineThickness, outlineColor: Color.Transparent,
                innerColor: settings.BackgroundColor ?? SliderFillColor, 
                outerColor: settings.BackgroundColor ?? SliderFillColor,
                shadow: SliderShadow,
                texture: settings.GetTexture(),
                textureRegion: settings.GetTextureRegion(),
                textureSettings: settings.GetTextureSettings()
            );
        }

        private void Gauge_Below (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings) {
            settings.Box.SnapAndInset(out Vector2 a, out Vector2 b);
            renderer.RasterizeRectangle(
                a, b,
                radius: SliderCornerRadius,
                outlineRadius: InertOutlineThickness, outlineColor: Color.Transparent,
                innerColor: settings.BackgroundColor ?? GaugeFillColor, 
                outerColor: settings.BackgroundColor ?? GaugeFillColor,
                shadow: GaugeShadow,
                texture: settings.GetTexture(),
                textureRegion: settings.GetTextureRegion(),
                textureSettings: settings.GetTextureSettings()
            );
        }

        private void Gauge_Content (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings) {
            settings.ContentBox.SnapAndInset(out Vector2 a, out Vector2 b);
            float x0, x1, x2;
            RasterFillMode fillMode;
            var direction = settings.Traits.FirstOrDefault();
            switch (direction) {
                default:
                case "ltr":
                case "rtl":
                    x0 = settings.Box.Left;
                    x1 = settings.ContentBox.Extent.X;
                    x2 = settings.Box.Extent.X;
                    fillMode = RasterFillMode.Angular + (direction == "rtl" ? 270 : 90);
                    break;
                case "ttb":
                case "btt":
                    x0 = settings.Box.Top;
                    x1 = settings.ContentBox.Extent.Y;
                    x2 = settings.Box.Extent.Y;
                    fillMode = RasterFillMode.Angular + (direction == "btt" ? 180 : 0);
                    break;
            }
            if (x2 <= x1)
                return;
            var alpha1 = 0.4f;
            // FIXME: Padding will make this slightly wrong
            var alpha2 = Arithmetic.Saturate(alpha1 + (0.7f * ((x1 - x0) / (x2 - x0))));
            var fillColor = settings.TextColor ?? GaugeValueFillColor;
            renderer.RasterizeRectangle(
                a, b,
                radius: SliderCornerRadius,
                outlineRadius: 1f, outlineColor: fillColor * 0.5f,
                fillMode: fillMode,
                innerColor: fillColor * alpha1, 
                outerColor: fillColor * alpha2,
                shadow: GaugeValueShadow,
                texture: settings.GetTexture(),
                textureRegion: settings.GetTextureRegion(),
                textureSettings: settings.GetTextureSettings()
            );
        }

        private void SliderThumb_Below (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings) {
            float alpha, thickness, pulse;
            pSRGBColor baseColor, outlineColor;
            Button_Below_Common(
                context, settings, out alpha, out thickness, out pulse, 
                out baseColor, out outlineColor
            );
            
            alpha *= 1.5f;

            settings.Box.SnapAndInset(out Vector2 a, out Vector2 b);
            var tip = (b - a).X * 0.8f;
            renderer.RasterizeRectangle(
                a, b,
                radiusCW: new Vector4(InertCornerRadius, InertCornerRadius, tip, tip),
                outlineRadius: thickness, outlineColor: outlineColor * alpha,
                innerColor: baseColor * ((0.85f + pulse) * alpha), outerColor: baseColor * ((0.35f + pulse) * alpha),
                fillMode: RasterFillMode.Vertical, fillSize: 0.95f,
                shadow: InteractableShadow
            );
        }

        private void SliderThumb_Above (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings) {
            if (!settings.State.IsFlagged(ControlStates.Hovering))
                return;

            settings.Box.SnapAndInset(out Vector2 a, out Vector2 b);
            var tip = (b - a).X * 0.8f;
            float fillSize = Math.Max(0.05f, Math.Min(0.9f, 64f / settings.Box.Height));

            renderer.RasterizeRectangle(
                a, b,
                radiusCW: new Vector4(InertCornerRadius, InertCornerRadius, tip, tip),
                outlineRadius: 0, outlineColor: Color.Transparent,
                innerColor: Color.White * EdgeGleamOpacity, outerColor: Color.White * 0.0f,
                fillMode: RasterFillMode.Angular,
                fillSize: fillSize,
                fillOffset: -Arithmetic.PulseSine(context.Now / 4f, 0f, 0.05f),
                fillAngle: Arithmetic.PulseCyclicExp(context.Now / 2f, 3),
                annularRadius: EdgeGleamThickness,
                blendState: BlendState.Additive
            );
        }

        private void AdjustRectForCheckbox (ref DecorationSettings settings) {
            var box = settings.Box;
            // FIXME: Scaling this will make the text crowded
            var size = ScaledCheckboxSize;
            settings.Box = new RectF(box.Left + 2, box.Top + (box.Height - size) / 2, size, size);
        }

        private void Checkbox_Below (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings) {
            // HACK
            settings.State &= ~ControlStates.Checked;
            AdjustRectForCheckbox(ref settings);
            Button_Below(context, ref renderer, settings);
        }

        private void Checkbox_Above (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings) {
            AdjustRectForCheckbox(ref settings);
            var isHovering = settings.State.IsFlagged(ControlStates.Hovering);
            var isChecked = settings.State.IsFlagged(ControlStates.Checked);
            if (isHovering || isChecked) {
                var f = Color.White * (isChecked ? 1 : 0.2f);
                var o = Color.White * (isChecked ? 0.7f : 0f);
                Vector2 a = new Vector2(settings.Box.Left + 8f, settings.Box.Center.Y + 1.75f),
                    b = new Vector2(settings.Box.Center.X, settings.Box.Extent.Y - 6.5f),
                    c = new Vector2(settings.Box.Extent.X - 7.75f, settings.Box.Top + 7f);
                var so = renderer.RasterSoftOutlines;
                renderer.RasterSoftOutlines = true;
                renderer.RasterizeLineSegment(
                    a, b, startRadius: isChecked ? 1.7f : 1.4f, endRadius: null,
                    outlineRadius: 0.8f, innerColor: f, outerColor: f, outlineColor: o
                );
                renderer.RasterizeLineSegment(
                    b, c, startRadius: isChecked ? 1.65f : 1.4f, endRadius: isChecked ? 1.8f : 1.5f,
                    outlineRadius: 0.8f, innerColor: f, outerColor: f, outlineColor: o
                );
                renderer.RasterSoftOutlines = so;
            }
            Button_Above(context, ref renderer, settings);
        }

        private void RadioButton_Below (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings) {
            // HACK
            settings.State &= ~ControlStates.Checked;
            AdjustRectForCheckbox(ref settings);

            float alpha, thickness, pulse;
            pSRGBColor baseColor, outlineColor;
            Button_Below_Common(
                context, settings, out alpha, out thickness, out pulse, 
                out baseColor, out outlineColor
            );

            settings.Box.SnapAndInset(out Vector2 a, out Vector2 b);
            renderer.RasterizeRectangle(
                a, b,
                radius: ScaledCheckboxSize * 0.45f,
                outlineRadius: thickness, outlineColor: outlineColor * alpha,
                innerColor: baseColor * ((0.85f + pulse) * alpha), outerColor: baseColor * ((0.35f + pulse) * alpha),
                fillMode: RasterFillMode.RadialEnclosing, fillSize: 0.95f,
                shadow: InteractableShadow
            );
        }

        private void RadioButton_Above (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings) {
            AdjustRectForCheckbox(ref settings);

            var isHovering = settings.State.IsFlagged(ControlStates.Hovering);
            var isChecked = settings.State.IsFlagged(ControlStates.Checked);
            if (isHovering || isChecked) {
                var f = Color.White * (isChecked ? 1 : 0.2f);
                var o = Color.White * (isChecked ? 0.6f : 0.4f);
                var so = renderer.RasterSoftOutlines;
                renderer.RasterSoftOutlines = true;
                renderer.RasterizeEllipse(
                    settings.Box.Center - (Vector2.One * 0.1f), Vector2.One * (isChecked ? 8f : 7f), 
                    1.2f, f, f, o
                );
                renderer.RasterSoftOutlines = so;
            }

            if (!settings.State.IsFlagged(ControlStates.Hovering))
                return;

            settings.Box.SnapAndInset(out Vector2 a, out Vector2 b);
            float fillSize = Math.Max(0.05f, Math.Min(0.9f, 64f / settings.Box.Height));

            renderer.RasterizeRectangle(
                a, b,
                radius: ScaledCheckboxSize * 0.45f,
                outlineRadius: 0, outlineColor: Color.Transparent,
                innerColor: Color.White * EdgeGleamOpacity, outerColor: Color.White * 0.0f,
                fillMode: RasterFillMode.Angular,
                fillSize: fillSize,
                fillOffset: -Arithmetic.PulseSine(context.Now / 4f, 0f, 0.05f),
                fillAngle: Arithmetic.PulseCyclicExp(context.Now / 2f, 3),
                annularRadius: EdgeGleamThickness,
                blendState: BlendState.Additive
            );
        }

        private void Container_Below (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings) {
            settings.Box.SnapAndInset(out Vector2 a, out Vector2 b);
            // FIXME: Should we draw the outline in Above?
            renderer.RasterizeRectangle(
                a, b,
                radius: ContainerCornerRadius,
                outlineRadius: InertOutlineThickness, outlineColor: ContainerOutlineColor,
                innerColor: settings.BackgroundColor ?? ContainerFillColor, 
                outerColor: settings.BackgroundColor ?? ContainerFillColor,
                shadow: ContainerShadow,
                texture: settings.GetTexture(),
                textureRegion: settings.GetTextureRegion(),
                textureSettings: settings.GetTextureSettings()
            );
        }

        private void FloatingContainer_Below (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings) {
            settings.Box.SnapAndInset(out Vector2 a, out Vector2 b);
            // FIXME: Should we draw the outline in Above?
            renderer.RasterizeRectangle(
                a, b,
                radius: FloatingContainerCornerRadius ?? ContainerCornerRadius,
                outlineRadius: InertOutlineThickness, outlineColor: FloatingContainerOutlineColor ?? ContainerOutlineColor,
                innerColor: settings.BackgroundColor ?? FloatingContainerFillColor ?? ContainerFillColor, 
                outerColor: settings.BackgroundColor ?? FloatingContainerFillColor ?? ContainerFillColor,
                shadow: FloatingContainerShadow ?? ContainerShadow,
                texture: settings.GetTexture(),
                textureRegion: settings.GetTextureRegion(),
                textureSettings: settings.GetTextureSettings()
            );
        }

        private void Tooltip_Below (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings) {
            settings.Box.SnapAndInset(out Vector2 a, out Vector2 b);
            // FIXME: Should we draw the outline in Above?
            var color1 = (pSRGBColor)TooltipFillColor;
            var color2 = (color1.ToVector4() * 1.25f);
            color2.W = 1;
            renderer.RasterizeRectangle(
                a, b,
                radius: TooltipCornerRadius ?? FloatingContainerCornerRadius ?? ContainerCornerRadius,
                outlineRadius: InertOutlineThickness, outlineColor: TooltipOutlineColor,
                innerColor: settings.BackgroundColor ?? color2, 
                outerColor: settings.BackgroundColor ?? color1,
                shadow: TooltipShadow ?? FloatingContainerShadow,
                texture: settings.GetTexture(),
                textureRegion: settings.GetTextureRegion(),
                textureSettings: settings.GetTextureSettings()
            );
        }

        private void StaticText_Below (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings) {
            if (!settings.BackgroundColor.HasValue && (settings.GetTexture() == null))
                return;

            settings.Box.SnapAndInset(out Vector2 a, out Vector2 b);
            // FIXME: Should we draw the outline in Above?
            renderer.RasterizeRectangle(
                a, b,
                radius: InertCornerRadius,
                outlineRadius: 0, outlineColor: Color.Transparent,
                innerColor: settings.BackgroundColor.Value, outerColor: settings.BackgroundColor.Value,
                texture: settings.GetTexture(),
                textureRegion: settings.GetTextureRegion(),
                textureSettings: settings.GetTextureSettings()
            );
        }

        private void EditableText_Below (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings) {
            bool isFocused = settings.State.IsFlagged(ControlStates.Focused),
                isHovering = settings.State.IsFlagged(ControlStates.Hovering);
            settings.Box.SnapAndInset(out Vector2 a, out Vector2 b);
            renderer.RasterizeRectangle(
                a, b,
                radius: EditableTextCornerRadius,
                outlineRadius: isFocused
                    ? EditableFocusedOutlineThickness 
                    : InactiveOutlineThickness, 
                outlineColor: isFocused
                    ? FocusedColor
                    : ContainerOutlineColor,
                // FIXME: Separate textarea fill color?
                innerColor: (settings.BackgroundColor ?? ContainerFillColor), 
                outerColor: (settings.BackgroundColor ?? ContainerFillColor),
                shadow: EditableShadow,
                texture: settings.GetTexture(),
                textureRegion: settings.GetTextureRegion(),
                textureSettings: settings.GetTextureSettings()
            );
        }

        private void EditableText_ContentClip (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings) {
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

        private void Container_ContentClip (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings) {
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
        private void None_Below (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings) {
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
        private void None_ContentClip (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings) {
            settings.Box.SnapAndInset(out Vector2 a, out Vector2 b);
            renderer.RasterizeRectangle(
                a, b,
                radius: 0,
                outlineRadius: 0, outlineColor: Color.Transparent,
                innerColor: Color.White, outerColor: Color.White,
                blendState: RenderStates.DrawNone
            );
        }

        private void Scrollbar_ComputeBoxes (
            DecorationSettings settings, ref ScrollbarState data, out float scrollDivisor,
            out Vector2 trackA, out Vector2 trackB,
            out Vector2 thumbA, out Vector2 thumbB
        ) {
            var box = settings.Box;

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
                ? new Vector2(box.Left, box.Extent.Y - effectiveScrollbarSize.Y)
                : new Vector2(box.Extent.X - effectiveScrollbarSize.X, box.Top);
            trackB = box.Extent;

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

        private bool Scrollbar_UpdateDrag (ref ScrollbarState data, MouseEventArgs args) {
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

        private bool Scrollbar_OnHitTest (DecorationSettings settings, ref ScrollbarState data, Vector2 position) {
            Scrollbar_ComputeBoxes(
                settings, ref data, out float sizePx,
                out Vector2 trackA, out Vector2 trackB,
                out Vector2 thumbA, out Vector2 thumbB
            );

            RectF box1 = new RectF(trackA, trackB - trackA);
            return box1.Contains(position);
        }

        private bool Scrollbar_OnMouseEvent (
            DecorationSettings settings, ref ScrollbarState data, string eventName, MouseEventArgs args
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
            if (
                track.Contains(args.GlobalPosition) && 
                (
                    (eventName != UIEvents.MouseMove) ||
                    (args.Buttons != MouseButtons.None)
                )
            )
                processed = true;

            return processed;
        }

        private void Scrollbar_Above (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings, ref ScrollbarState data) {
            Scrollbar_ComputeBoxes(
                settings, ref data, out float divisor,
                out Vector2 trackA, out Vector2 trackB,
                out Vector2 thumbA, out Vector2 thumbB
            );

            renderer.RasterizeRectangle(
                trackA, trackB,
                radius: ScrollbarRadius,
                outlineRadius: 0, outlineColor: Color.Transparent,
                innerColor: ScrollbarTrackColor, outerColor: ScrollbarTrackColor
            );

            if (data.ContentSize <= data.ViewportSize)
                return;

            renderer.Layer += 1;

            renderer.RasterizeRectangle(
                thumbA, thumbB,
                radius: ScrollbarRadius,
                outlineRadius: 0, outlineColor: Color.Transparent,
                innerColor: ScrollbarThumbColor, outerColor: ScrollbarThumbColor * 0.8f,
                fillMode: RasterFillMode.Radial,
                shadow: ScrollbarThumbShadow
            );
        }

        private void ContainerTitle_Below (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings) {
            float cornerRadius = ContainerCornerRadius,
                cornerRadius2 = settings.State.IsFlagged(ControlStates.Pressed) // HACK: When collapsed, round all corners
                    ? cornerRadius
                    : 0;
            TitleCommon_Below(ref renderer, settings, cornerRadius, cornerRadius2);
        }

        private void WindowTitle_Below (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings) {
            float cornerRadius = FloatingContainerCornerRadius ?? ContainerCornerRadius,
                cornerRadius2 = settings.State.IsFlagged(ControlStates.Pressed) // HACK: When collapsed, round all corners
                    ? cornerRadius
                    : 0;
            TitleCommon_Below(ref renderer, settings, cornerRadius, cornerRadius2);
        }

        private void TitleCommon_Below (ref ImperativeRenderer renderer, DecorationSettings settings, float cornerRadius, float cornerRadius2) {
            var containsFocus = settings.State.IsFlagged(ControlStates.ContainsFocus);
            settings.Box.SnapAndInset(out Vector2 a, out Vector2 b);
            // FIXME: Should we draw the outline in Above?
            var color1 = (pSRGBColor)(containsFocus ? TitleFillColor : TitleFillColor.ToGrayscale(0.85f));
            var color2 = color1.ToVector4() * 0.8f;
            color2.W = 1;
            renderer.RasterizeRectangle(
                a, b,
                radiusCW: new Vector4(cornerRadius, cornerRadius, cornerRadius2, cornerRadius2),
                outlineRadius: 0, outlineColor: Color.Transparent,
                innerColor: color1, outerColor: color2,
                fillMode: RasterFillMode.Vertical,
                shadow: TitleShadow
            );
        }

        private void Selection_Content (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings) {
            settings.Box.SnapAndInset(out Vector2 a, out Vector2 b, -SelectionPadding);
            var isCaret = (settings.Box.Width <= 0.5f);
            var isFocused = settings.State.IsFlagged(ControlStates.Focused) ||
                settings.State.IsFlagged(ControlStates.ContainsFocus);
            var fillColor = SelectionFillColor *
                (isFocused
                    ? Arithmetic.Pulse(context.Now / 2f, 0.7f, 0.8f)
                    : 0.45f
                ) * (isCaret ? 1.8f : 1f);
            var outlineColor = (isFocused && !isCaret)
                ? Color.White
                : Color.Transparent;

            renderer.RasterizeRectangle(
                a, b,
                radius: SelectionCornerRadius,
                outlineRadius: (isFocused && !isCaret) ? 0.9f : 0f, 
                outlineColor: outlineColor,
                innerColor: fillColor, outerColor: fillColor,
                shadow: SelectionShadow
            );
        }

        private void MenuSelection_Content (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings) {
            settings.Box.SnapAndInset(out Vector2 a, out Vector2 b, -SelectionPadding);
            var fillColor = (pSRGBColor)SelectionFillColor * Arithmetic.Pulse(context.Now / 2f, 0.9f, 1f);

            renderer.RasterizeRectangle(
                a, b,
                radius: MenuSelectionCornerRadius,
                outlineRadius: 0.9f, outlineColor: fillColor,
                innerColor: fillColor, outerColor: fillColor * 0.6f,
                fillMode: RasterFillMode.Horizontal
            );
        }

        private void ListSelection_Content (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings) {
            settings.Box.SnapAndInset(out Vector2 a, out Vector2 b, -SelectionPadding);
            var isFocused = settings.State.IsFlagged(ControlStates.Focused) ||
                settings.State.IsFlagged(ControlStates.ContainsFocus);
            var fillColor = (pSRGBColor)(
                 isFocused
                    ? SelectionFillColor
                    : Color.Lerp(SelectionFillColor, SelectionFillColor.ToGrayscale(0.65f), 0.5f)
                );
            var outlineColor = (isFocused)
                ? Color.White
                : fillColor * 0.5f;

            renderer.RasterizeRectangle(
                a, b,
                radius: ListSelectionCornerRadius,
                outlineRadius: 0.9f, outlineColor: outlineColor,
                innerColor: fillColor, outerColor: fillColor,
                fillMode: RasterFillMode.Horizontal
            );
        }

        private void CompositionPreview_Below (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings) {
            settings.Box.SnapAndInset(out Vector2 a, out Vector2 b, -SelectionPadding);
            var fillColor = SelectionFillColor;
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

        private void AcceleratorLabel_Below (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings) {
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
                outlineRadius: 1f, outlineColor: AcceleratorOutlineColor,
                innerColor: AcceleratorFillColor, outerColor: AcceleratorFillColor,
                shadow: null,
                texture: settings.GetTexture(),
                textureRegion: settings.GetTextureRegion(),
                textureSettings: settings.GetTextureSettings()
            );
        }

        private void AcceleratorTarget_Below (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings) {
            settings.Box.SnapAndInset(out Vector2 a, out Vector2 b);
            var outlineColor = AcceleratorOutlineColor * Arithmetic.PulseSine((context.Now / 1.3f) + (a.X / 512), 0.65f, 1.0f);
            // FIXME
            renderer.RasterizeRectangle(
                a, b,
                radius: 0f,
                outlineRadius: 1f, outlineColor: outlineColor,
                innerColor: Color.Transparent, outerColor: Color.Transparent,
                shadow: AcceleratorTargetShadow
            );
        }

        private void ParameterGauge_Below (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings) {
            var radius = new Vector4(InertCornerRadius, InertCornerRadius, InertCornerRadius, InertCornerRadius);
            bool isFocused = settings.State.IsFlagged(ControlStates.Focused),
                isHovering = settings.State.IsFlagged(ControlStates.Hovering);
            var outlineRadius = isFocused ? 1f : 0f;
            var outlineColor = Color.Black * 0.8f;
            settings.ContentBox.SnapAndInset(out Vector2 a, out Vector2 b);
            float alpha1 = (isFocused ? 0.75f : 0.6f) * (isHovering ? 0.75f : 0.6f);
            float alpha2 = (isFocused ? 1.0f : 0.7f) * (isHovering ? 1.0f : 0.75f);
            alpha2 = Arithmetic.Lerp(alpha1, alpha2, (b.X - a.X) / (settings.Box.Width));
            renderer.RasterizeRectangle(
                a, b, radiusCW: radius,
                outlineRadius: outlineRadius, outlineColor: outlineColor,
                innerColor: Color.White * alpha1, outerColor: Color.White * alpha2,
                fillMode: RasterFillMode.Horizontal
            );
        }

        // FIXME: Why this offset?
        static Vector2 WeirdVirtualCursorOffset = new Vector2(0.9f);

        private void VirtualCursor_Above (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings) {
            var center = settings.Box.Center + WeirdVirtualCursorOffset;
            var radius = settings.Box.Size / 2f;
            var showCenter = !settings.State.IsFlagged(ControlStates.Disabled);
            var alpha = settings.State.IsFlagged(ControlStates.Disabled) ? 0.5f : 0.9f;
            var fillAlpha = alpha * 0.85f;
            var outlineRadius = 1.75f;
            renderer.RasterSoftOutlines = true;
            renderer.RasterizeEllipse(
                center, radius, annularRadius: showCenter ? 1.65f : 1.2f,
                innerColor: Color.White * fillAlpha, outerColor: Color.White * fillAlpha,
                outlineRadius: outlineRadius, outlineColor: Color.Black * alpha
            );
        }

        private void VirtualCursorAnchor_Above (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings) {
            if (settings.State.IsFlagged(ControlStates.Disabled))
                return;

            var unsnapped = settings.Box.Position + WeirdVirtualCursorOffset;
            var snapped = settings.Box.Extent + WeirdVirtualCursorOffset;
            var distance = (snapped - unsnapped).Length();
            var alpha = 0.9f;
            var outlineRadius = 1.75f;

            if (distance >= 0.5f) {
                renderer.RasterizeLineSegment(
                    a: unsnapped, b: snapped,
                    startRadius: 1.55f, endRadius: 2.5f,
                    innerColor: Color.White * (alpha * 0.75f), outerColor: Color.White,
                    outlineRadius: outlineRadius, outlineColor: Color.Black * alpha
                );
            } else {
                var fillAlpha = alpha * 0.85f;
                renderer.RasterizeEllipse(
                    snapped, new Vector2(1.7f),
                    innerColor: Color.White * fillAlpha, outerColor: Color.White * fillAlpha,
                    outlineRadius: outlineRadius, outlineColor: Color.Black * alpha
                );
            }
        }

        public bool GetTextSettings (
            UIOperationContext context, ControlStates state, 
            out Material material, ref Color? color
        ) {
            return GetTextSettings(context, state, out material, ref color, false);
        }

        public bool GetTextSettings (
            UIOperationContext context, ControlStates state, 
            out Material material, ref Color? color, bool selected
        ) {
            if (!color.HasValue)
                color = TextColor;

            if (state.IsFlagged(ControlStates.Disabled))
                color = color?.ToGrayscale(DisabledTextAlpha);

            material = selected
                ? SelectedTextMaterial
                : TextMaterial;
            return true;
        }

        public bool GetTextSettings_Description (
            UIOperationContext context, ControlStates state, 
            out Material material, ref Color? color
        ) {
            // HACK: Pass selected=true to get the unshadowed material
            var result = GetTextSettings(context, state, out material, ref color, true);
            if (color.HasValue)
                color = color.Value * 0.5f;
            return result;
        }

        private bool GetTextSettings_None (
            UIOperationContext context, ControlStates state, 
            out Material material, ref Color? color
        ) {
            state &= ~ControlStates.Focused;
            state &= ~ControlStates.Checked;
            GetTextSettings(context, state, out material, ref color, false);
            return true;
        }

        private bool GetTextSettings_Button (
            UIOperationContext context, ControlStates state, 
            out Material material, ref Color? color
        ) {
            GetTextSettings(context, state, out material, ref color);
            return true;
        }

        private void GetContentAdjustment_Button (UIOperationContext context, ControlStates state, out Vector2 offset, out Vector2 scale) {
            scale = Vector2.One;
            if (state.IsFlagged(ControlStates.Pressed)) {
                offset = new Vector2(0, 2);
            } else {
                offset = new Vector2(0, 0);
            }
        }

        private bool GetTextSettings_Title (
            UIOperationContext context, ControlStates state, 
            out Material material, ref Color? color
        ) {
            if (color == null)
                color = TitleTextColor;
            GetTextSettings(context, state, out material, ref color);
            return true;
        }

        private bool GetTextSettings_AcceleratorLabel (
            UIOperationContext context, ControlStates state, 
            out Material material, ref Color? color
        ) {
            if (color == null)
                color = AcceleratorTextColor;
            GetTextSettings(context, state, out material, ref color);
            return true;
        }

        private bool GetTextSettings_Tooltip (
            UIOperationContext context, ControlStates state, 
            out Material material, ref Color? color
        ) {
            if (color == null)
                color = TooltipTextColor;
            GetTextSettings(context, state, out material, ref color);
            return true;
        }

        private bool GetTextSettings_Selection (
            UIOperationContext context, ControlStates state, 
            out Material material, ref Color? color
        ) {
            GetTextSettings(context, state, out material, ref color, selected: true);
            color = SelectedTextColor;
            return true;
        }
    }

    public class FadeAnimation : IControlAnimation {
        public float DefaultDuration { get; set; }
        public float? From;
        public float To;

        void IControlAnimation.End (Control control, bool cancelled) {
        }

        void IControlAnimation.Start (Control control, long now, float duration) {
            control.Appearance.Opacity = Tween.StartNow(
                From ?? control.Appearance.Opacity.Get(now), To,
                seconds: duration, now: now
            );
        }
    }
}
