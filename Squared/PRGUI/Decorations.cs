using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Render;
using Squared.Render.Convenience;
using Squared.Render.RasterShape;
using Squared.Util;

namespace Squared.PRGUI.Decorations {
    public struct DecorationSettings {
        public RectF Box;
        public ControlStates State;
        public pSRGBColor? BackgroundColor;
    }

    public interface IBaseDecorator {
        Margins Margins { get; }
        Margins Padding { get; }
        Vector2 PressedInset { get; }
        Material GetTextMaterial (UIOperationContext context, ControlStates state);
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
        IDecorator StaticText { get; }
        IDecorator Button { get; }
        IWidgetDecorator<ScrollbarState> Scrollbar { get; }
    }

    public abstract class DelegateBaseDecorator : IBaseDecorator {
        public Margins Margins { get; set; }
        public Margins Padding { get; set; }
        public Vector2 PressedInset { get; set; }
        public Func<UIOperationContext, ControlStates, Material> GetTextMaterial;

        Material IBaseDecorator.GetTextMaterial (UIOperationContext context, ControlStates state) {
            if (GetTextMaterial != null)
                return GetTextMaterial(context, state);
            else
                return null;
        }
    }

    public sealed class DelegateDecorator : DelegateBaseDecorator, IDecorator {
        public Action<UIOperationContext, DecorationSettings> Below, Content, Above, ContentClip;

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
        public IDecorator StaticText { get; set; }
        public IWidgetDecorator<ScrollbarState> Scrollbar { get; set; }

        public float InteractableCornerRadius = 6f, InertCornerRadius = 3f, ContainerCornerRadius = 3f;
        public float InactiveOutlineThickness = 1f, ActiveOutlineThickness = 1.3f, PressedOutlineThickness = 2f,
            InertOutlineThickness = 1f;
        public float ScrollbarSize = 14f, ScrollbarRadius = 3f;

        public RasterShadowSettings? InteractableShadow, 
            ContainerShadow,
            ScrollbarThumbShadow;

        public Color FocusedColor = new Color(200, 230, 255),
            ActiveColor = Color.White,
            InactiveColor = new Color(180, 180, 180),
            ContainerOutlineColor = new Color(32, 32, 32),
            ContainerFillColor = new Color(48, 48, 48),
            InertOutlineColor = new Color(255, 255, 255) * 0.33f,
            InertFillColor = Color.Transparent,
            ScrollbarThumbColor = new Color(220, 220, 220),
            ScrollbarTrackColor = new Color(64, 64, 64);

        public Material GetTextMaterial (UIOperationContext context, ControlStates state) {
            return context.Renderer.Materials.Get(
                context.Renderer.Materials.ScreenSpaceShadowedBitmap, blendState: BlendState.AlphaBlend
            );
        }

        private void Button_Below (UIOperationContext context, DecorationSettings settings) {
            var state = settings.State;

            float alpha, thickness;
            var baseColor = settings.BackgroundColor ?? (pSRGBColor)(
                state.HasFlag(ControlStates.Focused)
                    ? FocusedColor
                    : InactiveColor
            );

            float pulse = 0;
            if (state.HasFlag(ControlStates.Pressed)) {
                alpha = 1f;
                thickness = PressedOutlineThickness;
                // FIXME: Should this override the background color property? Maybe?
                baseColor = ActiveColor;
            } else if (state.HasFlag(ControlStates.Hovering)) {
                alpha = 0.85f;
                thickness = ActiveOutlineThickness;
                pulse = Arithmetic.PulseSine(context.AnimationTime / 3.33f, 0f, 0.05f);
            } else {
                alpha = state.HasFlag(ControlStates.Focused) ? 0.75f : 0.6f;
                thickness = state.HasFlag(ControlStates.Focused) ? ActiveOutlineThickness : InactiveOutlineThickness;
            }

            settings.Box.SnapAndInset(out Vector2 a, out Vector2 b, InteractableCornerRadius);
            context.Renderer.RasterizeRectangle(
                a, b,
                radius: InteractableCornerRadius,
                outlineRadius: thickness, outlineColor: baseColor * alpha,
                innerColor: baseColor * ((0.3f + pulse) * alpha), outerColor: baseColor * ((0.1f + pulse) * alpha),
                fillMode: RasterFillMode.RadialEnclosing,
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
                ? new Vector2(box.Left + 1, box.Extent.Y - ScrollbarSize)
                : new Vector2(box.Extent.X - ScrollbarSize, box.Top + 1);
            var b = box.Extent;

            context.Renderer.RasterizeRectangle(
                a + vRadius, b - vRadius,
                radius: ScrollbarRadius,
                outlineRadius: 0, outlineColor: Color.Transparent,
                innerColor: ScrollbarTrackColor, outerColor: ScrollbarTrackColor,
                fillMode: RasterFillMode.Vertical
            );

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

        public DefaultDecorations () {
            InteractableShadow = new RasterShadowSettings {
                Color = Color.Black * 0.25f,
                Offset = new Vector2(1.5f, 2f),
                Softness = 5f
            };

            ContainerShadow = null;
            ScrollbarThumbShadow = null;

            Button = new DelegateDecorator {
                Margins = new Margins(4),
                Padding = new Margins(6),
                PressedInset = new Vector2(0, 1),
                GetTextMaterial = GetTextMaterial,
                Below = Button_Below,
                Above = Button_Above
            };

            Container = new DelegateDecorator {
                Margins = new Margins(4),
                GetTextMaterial = GetTextMaterial,
                Below = Container_Below,
                ContentClip = Container_ContentClip,
            };

            StaticText = new DelegateDecorator {
                Margins = new Margins(2, 4),
                Padding = new Margins(6),
                GetTextMaterial = GetTextMaterial,
                Below = StaticText_Below,
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
