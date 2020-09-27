using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Game;
using Squared.PRGUI.Layout;
using Squared.Render;
using Squared.Render.Convenience;
using Squared.Render.RasterShape;
using Squared.Render.Text;
using Squared.Util;

namespace Squared.PRGUI.Decorations {
    public struct DecorationSettings {
        public RectF Box, ContentBox;
        public ControlStates State;
        public pSRGBColor? BackgroundColor;
    }

    public interface IBaseDecorator {
        Margins Margins { get; }
        Margins Padding { get; }
        Vector2 PressedInset { get; }
        bool GetTextSettings (UIOperationContext context, ControlStates state, out Material material, out IGlyphSource font, ref Color? color);
    }

    public interface IWidgetDecorator<TData> : IBaseDecorator {
        Vector2 MinimumSize { get; }
        void Rasterize (UIOperationContext context, DecorationSettings settings, ref TData data);
    }

    public interface IDecorator : IBaseDecorator {
        void Rasterize (UIOperationContext context, DecorationSettings settings);
    }

    public interface IDecorationProvider {
        IDecorator Container { get; }
        IDecorator FloatingContainer { get; }
        IDecorator Window { get; }
        IDecorator WindowTitle { get; }
        IDecorator StaticText { get; }
        IDecorator EditableText { get; }
        IDecorator Selection { get; }
        IDecorator Button { get; }
        IDecorator Tooltip { get; }
        IDecorator CompositionPreview { get; }
        IWidgetDecorator<ScrollbarState> Scrollbar { get; }
    }

    public delegate bool TextSettingsGetter (UIOperationContext context, ControlStates state, out Material material, out IGlyphSource font, ref Color? color);

    public abstract class DelegateBaseDecorator : IBaseDecorator {
        public Margins Margins { get; set; }
        public Margins Padding { get; set; }
        public Vector2 PressedInset { get; set; }
        public TextSettingsGetter GetTextSettings;

        bool IBaseDecorator.GetTextSettings (UIOperationContext context, ControlStates state, out Material material, out IGlyphSource font, ref Color? color) {
            if (GetTextSettings != null)
                return GetTextSettings(context, state, out material, out font, ref color);
            else {
                material = default(Material);
                font = default(IGlyphSource);
                return false;
            }
        }
    }

    public sealed class DelegateDecorator : DelegateBaseDecorator, IDecorator {
        public Action<UIOperationContext, DecorationSettings> Below, Content, Above, ContentClip;

        public DelegateDecorator Clone () {
            return new DelegateDecorator {
                Below = Below,
                Content = Content,
                Above = Above,
                ContentClip = ContentClip,
                Margins = Margins,
                Padding = Padding,
                GetTextSettings = GetTextSettings,
                PressedInset = PressedInset
            };
        }

        void IDecorator.Rasterize (UIOperationContext context, DecorationSettings settings) {
            switch (context.Pass) {
                case RasterizePasses.Below:
                    if (Below != null)
                        Below(context, settings);
                    return;
                case RasterizePasses.Content:
                    if (Content != null)
                        Content(context, settings);
                    return;
                case RasterizePasses.Above:
                    if (Above != null)
                        Above(context, settings);
                    return;
                case RasterizePasses.ContentClip:
                    if (ContentClip != null)
                        ContentClip(context, settings);
                    return;
            }
        }
    }

    public delegate void WidgetDecoratorRasterizer<TData> (UIOperationContext context, DecorationSettings settings, ref TData data);

    public sealed class DelegateWidgetDecorator<TData> : DelegateBaseDecorator, IWidgetDecorator<TData> {
        public Vector2 MinimumSize { get; set; }
        public WidgetDecoratorRasterizer<TData> Below, Content, Above, ContentClip;

        void IWidgetDecorator<TData>.Rasterize (UIOperationContext context, DecorationSettings settings, ref TData data) {
            switch (context.Pass) {
                case RasterizePasses.Below:
                    if (Below != null)
                        Below(context, settings, ref data);
                    return;
                case RasterizePasses.Content:
                    if (Content != null)
                        Content(context, settings, ref data);
                    return;
                case RasterizePasses.Above:
                    if (Above != null)
                        Above(context, settings, ref data);
                    return;
                case RasterizePasses.ContentClip:
                    if (ContentClip != null)
                        ContentClip(context, settings, ref data);
                    return;
            }
        }
    }

    public class DefaultDecorations : IDecorationProvider {
        public IDecorator Button { get; set; }
        public IDecorator Container { get; set; }
        public IDecorator FloatingContainer { get; set; }
        public IDecorator Window { get; set; }
        public IDecorator WindowTitle { get; set; }
        public IDecorator StaticText { get; set; }
        public IDecorator EditableText { get; set; }
        public IDecorator Selection { get; set; }
        public IDecorator Tooltip { get; set; }
        public IDecorator CompositionPreview { get; set; }
        public IWidgetDecorator<ScrollbarState> Scrollbar { get; set; }

        public IGlyphSource DefaultFont,
            ButtonFont,
            TitleFont,
            TooltipFont;

        public float InteractableCornerRadius = 6f, 
            InertCornerRadius = 3f, 
            ContainerCornerRadius = 3f, 
            TitleCornerRadius = 4f,
            SelectionCornerRadius = 1.33f,
            SelectionPadding = 1f;
        public float? FloatingContainerCornerRadius = null,
            TooltipCornerRadius = null;
        public float InactiveOutlineThickness = 1f, 
            ActiveOutlineThickness = 1.3f, 
            PressedOutlineThickness = 2f,
            InertOutlineThickness = 1f,
            EditableFocusedOutlineThickness = 1.2f;
        public float ScrollbarSize = 14f, 
            ScrollbarRadius = 3f;

        public RasterShadowSettings? InteractableShadow, 
            ContainerShadow,
            FloatingContainerShadow,
            ScrollbarThumbShadow,
            TitleShadow,
            EditableShadow,
            SelectionShadow,
            TooltipShadow;

        public Color FocusedColor = new Color(200, 230, 255),
            ActiveColor = new Color(240, 240, 240),
            InactiveColor = new Color(180, 180, 180),
            ContainerOutlineColor = new Color(32, 32, 32),
            InertOutlineColor = new Color(255, 255, 255) * 0.33f,
            TooltipOutlineColor = new Color(16, 16, 16) * 0.5f,
            ScrollbarThumbColor = new Color(220, 220, 220),
            ScrollbarTrackColor = new Color(32, 32, 32);

        public Color TitleFillColor = new Color(50, 120, 160),
            ContainerFillColor = Color.Transparent,
            InertFillColor = Color.Transparent,
            SelectionFillColor = new Color(200, 230, 255),
            TooltipFillColor = new Color(48, 48, 48);

        public Color SelectedTextColor = new Color(0, 30, 55),
            TitleTextColor = Color.White,
            TextColor = Color.White,
            TooltipTextColor = Color.White;

        public float DisabledTextAlpha = 0.5f;

        public Color? FloatingContainerOutlineColor, 
            FloatingContainerFillColor;

        private void Button_Below (UIOperationContext context, DecorationSettings settings) {
            var state = settings.State;

            float alpha, thickness;
            var baseColor = settings.BackgroundColor ?? (pSRGBColor)(
                state.IsFlagged(ControlStates.Focused)
                    ? FocusedColor
                    : InactiveColor
            );

            var hasColor = settings.BackgroundColor.HasValue;

            float pulse = 0;
            if (state.IsFlagged(ControlStates.Pressed)) {
                alpha = hasColor ? 1f : 0.9f;
                thickness = PressedOutlineThickness;
                if (hasColor) {
                    // Intensify the color if the button has a custom color
                    baseColor = (settings.BackgroundColor.Value.ToVector4()) * 1.25f;
                    baseColor.Vector4.W = 1;
                } else
                    baseColor = ActiveColor;
            } else if (state.IsFlagged(ControlStates.Hovering)) {
                alpha = hasColor ? 0.9f : 0.75f;
                thickness = ActiveOutlineThickness;
                pulse = Arithmetic.PulseSine(context.Now / 3.33f, 0f, 0.08f);
            } else {
                alpha = hasColor 
                    ? (state.IsFlagged(ControlStates.Focused) ? 0.9f : 0.8f)
                    : (state.IsFlagged(ControlStates.Focused) ? 0.5f : 0.35f);
                thickness = state.IsFlagged(ControlStates.Focused) ? ActiveOutlineThickness : InactiveOutlineThickness;
            }

            settings.Box.SnapAndInset(out Vector2 a, out Vector2 b, InteractableCornerRadius);
            context.Renderer.RasterizeRectangle(
                a, b,
                radius: InteractableCornerRadius,
                outlineRadius: thickness, outlineColor: baseColor * alpha,
                innerColor: baseColor * ((0.85f + pulse) * alpha), outerColor: baseColor * ((0.35f + pulse) * alpha),
                fillMode: RasterFillMode.RadialEnclosing, fillSize: 0.95f,
                shadow: InteractableShadow
            );
        }

        private void Button_Above (UIOperationContext context, DecorationSettings settings) {
            if (!settings.State.IsFlagged(ControlStates.Hovering))
                return;

            settings.Box.SnapAndInset(out Vector2 a, out Vector2 b, InteractableCornerRadius);
            float fillSize = Math.Max(0.05f, Math.Min(0.9f, 64f / settings.Box.Height));

            context.Renderer.RasterizeRectangle(
                a, b,
                radius: InteractableCornerRadius,
                outlineRadius: 0, outlineColor: Color.Transparent,
                innerColor: Color.White * 0.5f, outerColor: Color.White * 0.0f,
                fillMode: RasterFillMode.Angular,
                fillSize: fillSize,
                fillOffset: -Arithmetic.PulseSine(context.Now / 4f, 0f, 0.05f),
                fillAngle: Arithmetic.PulseCyclicExp(context.Now / 2f, 3),
                annularRadius: 1.1f,
                blendState: BlendState.Additive
            );
        }

        private void Container_Below (UIOperationContext context, DecorationSettings settings) {
            settings.Box.SnapAndInset(out Vector2 a, out Vector2 b, ContainerCornerRadius);
            // FIXME: Should we draw the outline in Above?
            context.Renderer.RasterizeRectangle(
                a, b,
                radius: ContainerCornerRadius,
                outlineRadius: InertOutlineThickness, outlineColor: ContainerOutlineColor,
                innerColor: settings.BackgroundColor ?? ContainerFillColor, 
                outerColor: settings.BackgroundColor ?? ContainerFillColor,
                shadow: ContainerShadow
            );
        }

        private void FloatingContainer_Below (UIOperationContext context, DecorationSettings settings) {
            settings.Box.SnapAndInset(out Vector2 a, out Vector2 b, FloatingContainerCornerRadius ?? ContainerCornerRadius);
            // FIXME: Should we draw the outline in Above?
            context.Renderer.RasterizeRectangle(
                a, b,
                radius: FloatingContainerCornerRadius ?? ContainerCornerRadius,
                outlineRadius: InertOutlineThickness, outlineColor: FloatingContainerOutlineColor ?? ContainerOutlineColor,
                innerColor: settings.BackgroundColor ?? FloatingContainerFillColor ?? ContainerFillColor, 
                outerColor: settings.BackgroundColor ?? FloatingContainerFillColor ?? ContainerFillColor,
                shadow: FloatingContainerShadow ?? ContainerShadow
            );
        }

        private void Tooltip_Below (UIOperationContext context, DecorationSettings settings) {
            settings.Box.SnapAndInset(out Vector2 a, out Vector2 b, FloatingContainerCornerRadius ?? ContainerCornerRadius);
            // FIXME: Should we draw the outline in Above?
            context.Renderer.RasterizeRectangle(
                a, b,
                radius: TooltipCornerRadius ?? FloatingContainerCornerRadius ?? ContainerCornerRadius,
                outlineRadius: InertOutlineThickness, outlineColor: TooltipOutlineColor,
                innerColor: settings.BackgroundColor ?? TooltipFillColor, 
                outerColor: settings.BackgroundColor ?? TooltipFillColor,
                shadow: TooltipShadow ?? FloatingContainerShadow
            );
        }

        private void StaticText_Below (UIOperationContext context, DecorationSettings settings) {
            if (!settings.BackgroundColor.HasValue)
                return;

            settings.Box.SnapAndInset(out Vector2 a, out Vector2 b, InertCornerRadius);
            // FIXME: Should we draw the outline in Above?
            context.Renderer.RasterizeRectangle(
                a, b,
                radius: InertCornerRadius,
                outlineRadius: 0, outlineColor: Color.Transparent,
                innerColor: settings.BackgroundColor.Value, outerColor: settings.BackgroundColor.Value
            );
        }

        private void EditableText_Below (UIOperationContext context, DecorationSettings settings) {
            bool isFocused = settings.State.IsFlagged(ControlStates.Focused),
                isHovering = settings.State.IsFlagged(ControlStates.Hovering);
            settings.Box.SnapAndInset(out Vector2 a, out Vector2 b, InertCornerRadius);
            context.Renderer.RasterizeRectangle(
                a, b,
                radius: InertCornerRadius,
                outlineRadius: isFocused
                    ? EditableFocusedOutlineThickness 
                    : InactiveOutlineThickness, 
                outlineColor: isFocused
                    ? FocusedColor
                    : ContainerOutlineColor,
                innerColor: settings.BackgroundColor.Value, 
                outerColor: settings.BackgroundColor.Value,
                shadow: EditableShadow
            );
        }

        private void EditableText_ContentClip (UIOperationContext context, DecorationSettings settings) {
            settings.Box.SnapAndInset(out Vector2 a, out Vector2 b, InertCornerRadius);
            context.Renderer.RasterizeRectangle(
                a, b,
                radius: InertCornerRadius,
                outlineRadius: 0, 
                outlineColor: Color.Transparent,
                innerColor: Color.White, 
                outerColor: Color.White,
                blendState: RenderStates.DrawNone
            );
        }

        private void EditableText_Above (UIOperationContext context, DecorationSettings settings) {
            if (!settings.State.IsFlagged(ControlStates.Focused))
                return;

            settings.Box.SnapAndInset(out Vector2 a, out Vector2 b, InertCornerRadius);
            float fillSize = Math.Max(0.05f, Math.Min(0.9f, 64f / settings.Box.Height));

            context.Renderer.RasterizeRectangle(
                a, b,
                radius: InertCornerRadius,
                outlineRadius: 0, outlineColor: Color.Transparent,
                innerColor: Color.White * 0.5f, outerColor: Color.White * 0.0f,
                fillMode: RasterFillMode.Angular,
                fillSize: fillSize,
                fillOffset: -Arithmetic.PulseSine(context.Now / 4f, 0f, 0.05f),
                fillAngle: Arithmetic.PulseCyclicExp(context.Now / 2f, 3),
                annularRadius: 1.1f,
                blendState: BlendState.Additive
            );
        }

        private void Container_ContentClip (UIOperationContext context, DecorationSettings settings) {
            settings.Box.SnapAndInset(out Vector2 a, out Vector2 b, ContainerCornerRadius);
            context.Renderer.RasterizeRectangle(
                a, b,
                radius: ContainerCornerRadius,
                outlineRadius: 0, outlineColor: Color.Transparent,
                innerColor: Color.White, outerColor: Color.White,
                blendState: RenderStates.DrawNone
            );
        }

        private void Scrollbar_Above (UIOperationContext context, DecorationSettings settings, ref ScrollbarState data) {
            var box = settings.Box;

            var vRadius = new Vector2(ScrollbarRadius);
            var totalOverflow = Math.Max(data.ContentSize - data.ViewportSize, 0.1f);
            float min = Math.Max(data.Position / data.ContentSize, 0f);
            float size = data.ViewportSize / Math.Max(data.ContentSize, 0.1f);
            float max = Math.Min(1.0f, min + size);

            var sizePx = data.Horizontal ? box.Width - 1 : box.Height - 1;
            if (data.HasCounterpart)
                sizePx -= ScrollbarSize;
            var a = data.Horizontal
                ? new Vector2(box.Left, box.Extent.Y - ScrollbarSize)
                : new Vector2(box.Extent.X - ScrollbarSize, box.Top);
            var b = box.Extent;

            context.Renderer.RasterizeRectangle(
                a + vRadius, b - vRadius,
                radius: ScrollbarRadius,
                outlineRadius: 0, outlineColor: Color.Transparent,
                innerColor: ScrollbarTrackColor, outerColor: ScrollbarTrackColor
            );

            if (data.ContentSize <= data.ViewportSize)
                return;

            if (data.Horizontal) {
                a.X += (sizePx * min);
                b.X = box.Left + (sizePx * max);
            } else {
                a.Y += (sizePx * min);
                b.Y = box.Top + (sizePx * max);
            }

            context.Renderer.Layer += 1;

            context.Renderer.RasterizeRectangle(
                a + vRadius, b - vRadius,
                radius: ScrollbarRadius,
                outlineRadius: 0, outlineColor: Color.Transparent,
                innerColor: ScrollbarThumbColor, outerColor: ScrollbarThumbColor * 0.8f,
                fillMode: RasterFillMode.Radial,
                shadow: ScrollbarThumbShadow
            );
        }

        private void WindowTitle_Below (UIOperationContext context, DecorationSettings settings) {
            settings.Box.SnapAndInset(out Vector2 a, out Vector2 b, TitleCornerRadius);
            // FIXME: Should we draw the outline in Above?
            context.Renderer.RasterizeRectangle(
                a, b,
                radius: TitleCornerRadius,
                outlineRadius: 0, outlineColor: Color.Transparent,
                innerColor: TitleFillColor, outerColor: TitleFillColor,
                shadow: TitleShadow
            );
        }

        private void Selection_Content (UIOperationContext context, DecorationSettings settings) {
            settings.Box.SnapAndInset(out Vector2 a, out Vector2 b, SelectionCornerRadius - SelectionPadding);
            var isCaret = (settings.Box.Width <= 0.5f);
            var isFocused = settings.State.IsFlagged(ControlStates.Focused);
            var fillColor = SelectionFillColor *
                (isFocused
                    ? Arithmetic.Pulse(context.Now, 0.65f, 0.8f)
                    : 0.55f
                ) * (isCaret ? 1.8f : 1f);
            var outlineColor = (isFocused && !isCaret)
                ? Color.White
                : Color.Transparent;

            context.Renderer.RasterizeRectangle(
                a, b,
                radius: SelectionCornerRadius,
                outlineRadius: (isFocused && !isCaret) ? 0.7f : 0f, 
                outlineColor: outlineColor,
                innerColor: fillColor, outerColor: fillColor,
                shadow: SelectionShadow
            );
        }

        private void CompositionPreview_Below (UIOperationContext context, DecorationSettings settings) {
            settings.Box.SnapAndInset(out Vector2 a, out Vector2 b, SelectionCornerRadius - SelectionPadding);
            var fillColor = SelectionFillColor;
            var outlineColor = Color.White;

            context.Renderer.RasterizeRectangle(
                a, b,
                radius: SelectionCornerRadius,
                outlineRadius: 0.7f, 
                outlineColor: outlineColor,
                innerColor: fillColor, outerColor: fillColor,
                shadow: SelectionShadow
            );
        }

        public bool GetTextSettings (
            UIOperationContext context, ControlStates state, 
            out Material material, out IGlyphSource font, ref Color? color
        ) {
            if (color == null)
                color = TextColor;

            if (state.IsFlagged(ControlStates.Disabled))
                color = color.Value.ToGrayscale(DisabledTextAlpha);

            font = DefaultFont;
            material = context.Renderer.Materials?.Get(
                context.Renderer.Materials?.ScreenSpaceShadowedBitmap, blendState: BlendState.AlphaBlend
            );
            return true;
        }

        private bool GetTextSettings_Button (
            UIOperationContext context, ControlStates state, 
            out Material material, out IGlyphSource font, ref Color? color
        ) {
            GetTextSettings(context, state, out material, out font, ref color);
            font = ButtonFont ?? font;
            return true;
        }

        private bool GetTextSettings_Title (
            UIOperationContext context, ControlStates state, 
            out Material material, out IGlyphSource font, ref Color? color
        ) {
            if (color == null)
                color = TitleTextColor;
            GetTextSettings(context, state, out material, out font, ref color);
            font = TitleFont ?? font;
            return true;
        }

        private bool GetTextSettings_Tooltip (
            UIOperationContext context, ControlStates state, 
            out Material material, out IGlyphSource font, ref Color? color
        ) {
            if (color == null)
                color = TooltipTextColor;
            GetTextSettings(context, state, out material, out font, ref color);
            font = TooltipFont ?? font;
            return true;
        }

        private bool GetTextSettings_Selection (
            UIOperationContext context, ControlStates state, 
            out Material material, out IGlyphSource font, ref Color? color
        ) {
            GetTextSettings(context, state, out material, out font, ref color);
            color = SelectedTextColor;
            return true;
        }

        public DefaultDecorations () {
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
                Color = Color.Black * 0.45f,
                Offset = new Vector2(1.25f, 1.5f),
                Softness = 8f,
                Inside = true
            };

            SelectionShadow = new RasterShadowSettings {
                Color = Color.White * 0.2f,
                Offset = new Vector2(1.25f, 1.5f),
                Softness = 2f
            };

            Button = new DelegateDecorator {
                Margins = new Margins(4),
                Padding = new Margins(6),
                PressedInset = new Vector2(0, 1),
                GetTextSettings = GetTextSettings_Button,
                Below = Button_Below,
                Above = Button_Above
            };

            Container = new DelegateDecorator {
                Margins = new Margins(4),
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

            Window = new DelegateDecorator {
                Padding = new Margins(4, 32, 4, 4),
                GetTextSettings = GetTextSettings,
                Below = FloatingContainer_Below,
                // FIXME: Separate routine?
                ContentClip = Container_ContentClip,
            };

            WindowTitle = new DelegateDecorator {
                Padding = new Margins(6, 4, 6, 6),
                Margins = new Margins(0, 0, 0, 2),
                GetTextSettings = GetTextSettings_Title,
                Below = WindowTitle_Below
            };

            StaticText = new DelegateDecorator {
                Margins = new Margins(2, 4),
                Padding = new Margins(6),
                GetTextSettings = GetTextSettings,
                Below = StaticText_Below,
            };

            Tooltip = new DelegateDecorator {
                Padding = new Margins(12, 8, 12, 8),
                GetTextSettings = GetTextSettings_Tooltip,
                Below = Tooltip_Below,
            };

            EditableText = new DelegateDecorator {
                Margins = new Margins(4),
                Padding = new Margins(6),
                GetTextSettings = GetTextSettings,
                // FIXME
                Below = EditableText_Below,
                ContentClip = EditableText_ContentClip
            };

            Selection = new DelegateDecorator {
                GetTextSettings = GetTextSettings_Selection,
                Content = Selection_Content,
            };

            CompositionPreview = new DelegateDecorator {
                GetTextSettings = GetTextSettings_Selection,
                Below = CompositionPreview_Below,
            };

            Scrollbar = new DelegateWidgetDecorator<ScrollbarState> {
                MinimumSize = new Vector2(ScrollbarSize, ScrollbarSize),
                Above = Scrollbar_Above
            };
        }
    }

    public struct ScrollbarState {
        public float Position;
        public float ContentSize, ViewportSize;
        public float? DragInitialPosition;
        public bool HasCounterpart, Horizontal;
    }
}
