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
    public interface IBaseDecorator {
        Margins Margins { get; }
        Margins Padding { get; }
        Vector2 PressedInset { get; }
        Material GetTextMaterial (UIOperationContext context, ControlStates state);
    }

    public interface IWidgetDecorator<TData> : IBaseDecorator {
        Vector2 MinimumSize { get; }
        void Rasterize (UIOperationContext context, RectF box, ControlStates state, ref TData data);
    }

    public interface IDecorator : IBaseDecorator {
        void Rasterize (UIOperationContext context, RectF box, ControlStates state);
    }

    public interface IDecorationProvider {
        IDecorator Container { get; }
        IDecorator StaticText { get; }
        IDecorator Button { get; }
        IWidgetDecorator<ScrollbarState> HorizontalScrollbar { get; }
        IWidgetDecorator<ScrollbarState> VerticalScrollbar { get; }
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
        public Action<UIOperationContext, RectF, ControlStates> Below, Content, Above, ContentClip;

        void IDecorator.Rasterize (UIOperationContext context, RectF box, ControlStates state) {
            switch (context.Pass) {
                case RasterizePasses.Below:
                    if (Below != null)
                        Below(context, box, state);
                    return;
                case RasterizePasses.Content:
                    if (Content != null)
                        Content(context, box, state);
                    return;
                case RasterizePasses.Above:
                    if (Above != null)
                        Above(context, box, state);
                    return;
                case RasterizePasses.ContentClip:
                    if (ContentClip != null)
                        ContentClip(context, box, state);
                    return;
            }
        }
    }

    public delegate void WidgetDecoratorRasterizer<TData> (UIOperationContext context, RectF box, ControlStates state, ref TData data);

    public sealed class DelegateWidgetDecorator<TData> : DelegateBaseDecorator, IWidgetDecorator<TData> {
        public Vector2 MinimumSize { get; set; }
        public WidgetDecoratorRasterizer<TData> Below, Content, Above, ContentClip;

        void IWidgetDecorator<TData>.Rasterize (UIOperationContext context, RectF box, ControlStates state, ref TData data) {
            switch (context.Pass) {
                case RasterizePasses.Below:
                    if (Below != null)
                        Below(context, box, state, ref data);
                    return;
                case RasterizePasses.Content:
                    if (Content != null)
                        Content(context, box, state, ref data);
                    return;
                case RasterizePasses.Above:
                    if (Above != null)
                        Above(context, box, state, ref data);
                    return;
                case RasterizePasses.ContentClip:
                    if (ContentClip != null)
                        ContentClip(context, box, state, ref data);
                    return;
            }
        }
    }

    public class DefaultDecorations : IDecorationProvider {
        public IDecorator Button { get; set; }
        public IDecorator Container { get; set; }
        public IDecorator StaticText { get; set; }
        public IWidgetDecorator<ScrollbarState> HorizontalScrollbar { get; set; }
        public IWidgetDecorator<ScrollbarState> VerticalScrollbar { get; set; }

        public float InteractableCornerRadius = 6f, InertCornerRadius = 3f, ContainerCornerRadius = 3f;
        public float InactiveOutlineThickness = 1f, ActiveOutlineThickness = 1.33f, PressedOutlineThickness = 2f,
            InertOutlineThickness = 1f;

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

        public DefaultDecorations () {
            Button = new DelegateDecorator {
                Margins = new Margins(4),
                Padding = new Margins(6),
                PressedInset = new Vector2(0, 1),
                GetTextMaterial = GetTextMaterial,
                Below = (context, box, state) => {
                    float alpha, thickness;
                    var baseColor = state.HasFlag(ControlStates.Focused)
                        ? FocusedColor
                        : InactiveColor;

                    if (state.HasFlag(ControlStates.Pressed)) {
                        alpha = 1f;
                        thickness = PressedOutlineThickness;
                        baseColor = ActiveColor;
                    } else if (state.HasFlag(ControlStates.Hovering)) {
                        alpha = 0.85f;
                        thickness = ActiveOutlineThickness;
                    } else {
                        alpha = state.HasFlag(ControlStates.Focused) ? 0.7f : 0.6f;
                        thickness = state.HasFlag(ControlStates.Focused) ? ActiveOutlineThickness : InactiveOutlineThickness;
                    }

                    box.SnapAndInset(out Vector2 a, out Vector2 b, InteractableCornerRadius);
                    context.Renderer.RasterizeRectangle(
                        a, b,
                        radius: InteractableCornerRadius,
                        outlineRadius: thickness, outlineColor: baseColor * alpha,
                        innerColor: baseColor * 0.3f * alpha, outerColor: baseColor * 0.2f * alpha,
                        fillMode: Render.RasterShape.RasterFillMode.Linear
                    );
                },
            };

            Container = new DelegateDecorator {
                Margins = new Margins(4),
                GetTextMaterial = GetTextMaterial,
                Below = (context, box, state) => {
                    box.SnapAndInset(out Vector2 a, out Vector2 b, ContainerCornerRadius);
                    // FIXME: Should we draw the outline in Above?
                    context.Renderer.RasterizeRectangle(
                        a, b,
                        radius: ContainerCornerRadius,
                        outlineRadius: InertOutlineThickness, outlineColor: ContainerOutlineColor,
                        innerColor: ContainerFillColor, outerColor: ContainerFillColor
                    );
                },
                ContentClip = (context, box, state) => {
                    box.SnapAndInset(out Vector2 a, out Vector2 b, ContainerCornerRadius);
                    context.Renderer.RasterizeRectangle(
                        a, b,
                        radius: ContainerCornerRadius,
                        outlineRadius: 0, outlineColor: Color.Transparent,
                        innerColor: Color.White, outerColor: Color.White,
                        blendState: RenderStates.DrawNone
                    );
                },
            };

            StaticText = new DelegateDecorator {
                Margins = new Margins(2, 4),
                Padding = new Margins(6),
                GetTextMaterial = GetTextMaterial,
                Below = (context, box, state) => {
                    box.SnapAndInset(out Vector2 a, out Vector2 b, InertCornerRadius);
                    context.Renderer.RasterizeRectangle(
                        a, b,
                        radius: InertCornerRadius,
                        outlineRadius: InertOutlineThickness, outlineColor: InertOutlineColor,
                        innerColor: InertFillColor, outerColor: InertFillColor
                    );
                }
            };

            const float scrollbarSize = 12;
            const float scrollbarRadius = 3;

            VerticalScrollbar = new DelegateWidgetDecorator<ScrollbarState> {
                MinimumSize = new Vector2(scrollbarSize, 0),
                Above = (UIOperationContext context, RectF box, ControlStates state, ref ScrollbarState data) => {
                    var vRadius = new Vector2(scrollbarRadius);
                    var totalOverflow = Math.Max(data.ContentSize - data.ViewportSize, 0.1);
                    var min = data.Position / data.ContentSize;
                    var size = data.ViewportSize / Math.Max(data.ContentSize, 0.1);

                    var height = box.Height - 1;
                    var a = new Vector2(box.Extent.X - scrollbarSize, box.Top + 1);
                    var b = box.Extent;

                    context.Renderer.RasterizeRectangle(
                        a + vRadius, b - vRadius,
                        radius: scrollbarRadius,
                        outlineRadius: 0, outlineColor: Color.Transparent,
                        innerColor: ScrollbarTrackColor, outerColor: ScrollbarTrackColor,
                        fillMode: RasterFillMode.Vertical
                    );

                    a.Y += (height * (float)min);
                    b.Y = box.Top + (height * (float)(min + size));

                    context.Renderer.RasterizeRectangle(
                        a + vRadius, b - vRadius,
                        radius: scrollbarRadius,
                        outlineRadius: 0, outlineColor: Color.Transparent,
                        innerColor: ScrollbarThumbColor, outerColor: ScrollbarThumbColor * 0.8f,
                        fillMode: RasterFillMode.Radial
                    );
                }
            };
        }
    }

    public struct ScrollbarState {
        public float Position;
        public float ContentSize, ViewportSize;
        public float? DragInitialPosition;
    }
}
