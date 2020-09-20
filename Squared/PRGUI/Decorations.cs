using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Game;
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
        IDecorator Button { get; }
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
        public IWidgetDecorator<ScrollbarState> Scrollbar { get; set; }

        public IGlyphSource DefaultFont,
            ButtonFont,
            TitleFont;

        public float InteractableCornerRadius = 6f, 
            InertCornerRadius = 3f, 
            ContainerCornerRadius = 3f, 
            TitleCornerRadius = 4f;
        public float? FloatingContainerCornerRadius = null;
        public float InactiveOutlineThickness = 1f, 
            ActiveOutlineThickness = 1.3f, 
            PressedOutlineThickness = 2f,
            InertOutlineThickness = 1f;
        public float ScrollbarSize = 14f, 
            ScrollbarRadius = 3f;

        public RasterShadowSettings? InteractableShadow, 
            ContainerShadow,
            FloatingContainerShadow,
            ScrollbarThumbShadow,
            TitleShadow;

        public Color FocusedColor = new Color(200, 230, 255),
            ActiveColor = new Color(240, 240, 240),
            InactiveColor = new Color(180, 180, 180),
            ContainerOutlineColor = new Color(32, 32, 32),
            ContainerFillColor = Color.Transparent,
            InertOutlineColor = new Color(255, 255, 255) * 0.33f,
            InertFillColor = Color.Transparent,
            ScrollbarThumbColor = new Color(220, 220, 220),
            ScrollbarTrackColor = new Color(32, 32, 32),
            TitleColor = new Color(50, 120, 160),
            TitleTextColor = Color.White,
            TextColor = Color.White;

        public float DisabledTextAlpha = 0.5f;

        public Color? FloatingContainerOutlineColor, 
            FloatingContainerFillColor;

        private void Button_Below (UIOperationContext context, DecorationSettings settings) {
            var state = settings.State;

            float alpha, thickness;
            var baseColor = settings.BackgroundColor ?? (pSRGBColor)(
                state.HasFlag(ControlStates.Focused)
                    ? FocusedColor
                    : InactiveColor
            );

            var hasColor = settings.BackgroundColor.HasValue;

            float pulse = 0;
            if (state.HasFlag(ControlStates.Pressed)) {
                alpha = hasColor ? 1f : 0.9f;
                thickness = PressedOutlineThickness;
                if (hasColor) {
                    // Intensify the color if the button has a custom color
                    baseColor = (settings.BackgroundColor.Value.ToVector4()) * 1.25f;
                    baseColor.Vector4.W = 1;
                } else
                    baseColor = ActiveColor;
            } else if (state.HasFlag(ControlStates.Hovering)) {
                alpha = hasColor ? 0.9f : 0.75f;
                thickness = ActiveOutlineThickness;
                pulse = Arithmetic.PulseSine(context.AnimationTime / 3.33f, 0f, 0.08f);
            } else {
                alpha = hasColor 
                    ? (state.HasFlag(ControlStates.Focused) ? 0.9f : 0.8f)
                    : (state.HasFlag(ControlStates.Focused) ? 0.5f : 0.35f);
                thickness = state.HasFlag(ControlStates.Focused) ? ActiveOutlineThickness : InactiveOutlineThickness;
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
            if (!settings.State.HasFlag(ControlStates.Hovering))
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
                fillOffset: -Arithmetic.PulseSine(context.AnimationTime / 4f, 0f, 0.05f),
                fillAngle: Arithmetic.PulseCyclicExp(context.AnimationTime / 2f, 3),
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
            bool isFocused = settings.State.HasFlag(ControlStates.Focused),
                isHovering = settings.State.HasFlag(ControlStates.Hovering);
            settings.Box.SnapAndInset(out Vector2 a, out Vector2 b, InertCornerRadius);
            context.Renderer.RasterizeRectangle(
                a, b,
                radius: InertCornerRadius,
                outlineRadius: (isFocused || isHovering)
                    ? ActiveOutlineThickness 
                    : InactiveOutlineThickness, 
                outlineColor: isFocused
                    ? FocusedColor
                    : ContainerOutlineColor,
                innerColor: settings.BackgroundColor.Value, 
                outerColor: settings.BackgroundColor.Value
            );
        }

        private void EditableText_Above (UIOperationContext context, DecorationSettings settings) {
            if (!settings.State.HasFlag(ControlStates.Focused))
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
                fillOffset: -Arithmetic.PulseSine(context.AnimationTime / 4f, 0f, 0.05f),
                fillAngle: Arithmetic.PulseCyclicExp(context.AnimationTime / 2f, 3),
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
                innerColor: ScrollbarTrackColor, outerColor: ScrollbarTrackColor,
                fillMode: RasterFillMode.Vertical
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
                innerColor: TitleColor, outerColor: TitleColor,
                shadow: TitleShadow
            );
        }

        public bool GetTextSettings (
            UIOperationContext context, ControlStates state, 
            out Material material, out IGlyphSource font, ref Color? color
        ) {
            if (color == null)
                color = TextColor;

            if (state.HasFlag(ControlStates.Disabled))
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

        public DefaultDecorations () {
            InteractableShadow = new RasterShadowSettings {
                Color = Color.Black * 0.25f,
                Offset = new Vector2(1.5f, 2f),
                Softness = 5f
            };

            ContainerShadow = null;
            FloatingContainerShadow = new RasterShadowSettings {
                Color = Color.Black * 0.33f,
                Offset = new Vector2(2.5f, 3f),
                Softness = 8f
            };
            ScrollbarThumbShadow = null;

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

            EditableText = new DelegateDecorator {
                Margins = new Margins(4),
                Padding = new Margins(6),
                GetTextSettings = GetTextSettings,
                // FIXME
                Below = EditableText_Below,
                // Above = EditableText_Above
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
