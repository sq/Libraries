using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Render;

namespace Squared.PRGUI.Decorations {
    [Flags]
    public enum ControlStates : int {
        Disabled = 0b1,
        Hovering = 0b10,
        Focused = 0b100,
        Pressed = 0b1000
    }

    public interface IDecorator {
        Margins Margins { get; }
        Margins Padding { get; }
        Vector2 PressedInset { get; }
        void Rasterize (UIOperationContext context, RectF box, ControlStates state);
        Material GetTextMaterial (UIOperationContext context, ControlStates state);
    }

    public sealed class DelegateDecorator : IDecorator {
        public Margins Margins { get; set; }
        public Margins Padding { get; set; }
        public Vector2 PressedInset { get; set; }
        public Action<UIOperationContext, RectF, ControlStates> Below, Content, Above;
        public Func<UIOperationContext, ControlStates, Material> GetTextMaterial;

        Material IDecorator.GetTextMaterial (UIOperationContext context, ControlStates state) {
            if (GetTextMaterial != null)
                return GetTextMaterial(context, state);
            else
                return null;
        }

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
            }
        }
    }

    public class DefaultDecorations : DecorationProvider {
        public float InteractableCornerRadius = 6f, InertCornerRadius = 3f;
        public float InactiveOutlineThickness = 1f, ActiveOutlineThickness = 1.33f, PressedOutlineThickness = 2f,
            InertOutlineThickness = 1f;

        public Color FocusedColor = new Color(200, 230, 255),
            ActiveColor = Color.White,
            InactiveColor = new Color(180, 180, 180),
            InertOutlineColor = new Color(255, 255, 255) * 0.33f,
            InertFillColor = Color.Transparent;

        public DefaultDecorations () {
            Func<UIOperationContext, ControlStates, Material> getTextMaterial =
                (context, state) => context.Renderer.Materials.Get(
                    context.Renderer.Materials.ScreenSpaceShadowedBitmap, blendState: BlendState.AlphaBlend
                );

            Button = new DelegateDecorator {
                Margins = new Margins(4),
                Padding = new Margins(6),
                PressedInset = new Vector2(0, 1),
                GetTextMaterial = getTextMaterial,
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
                    var offset = new Vector2(InteractableCornerRadius);
                    context.Renderer.RasterizeRectangle(
                        box.Position + offset, box.Extent - offset,
                        radius: InteractableCornerRadius,
                        outlineRadius: thickness, outlineColor: baseColor * alpha,
                        innerColor: baseColor * 0.3f * alpha, outerColor: baseColor * 0.2f * alpha,
                        fillMode: Render.RasterShape.RasterFillMode.Linear
                    );
                },
            };

            Container = new DelegateDecorator {
                Margins = new Margins(4),
                GetTextMaterial = getTextMaterial,
                Below = (context, box, state) => {
                    var offset = new Vector2(InertCornerRadius);
                    context.Renderer.RasterizeRectangle(
                        box.Position + offset, box.Extent - offset,
                        radius: InertCornerRadius,
                        outlineRadius: InertOutlineThickness, outlineColor: InertOutlineColor,
                        innerColor: InertFillColor, outerColor: InertFillColor
                    );
                }
            };
        }
    }

    public abstract class DecorationProvider {
        public DelegateDecorator Button { get; set; }
        public DelegateDecorator Container { get; set; }
    }
}
