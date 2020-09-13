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
        void Rasterize (UIOperationContext context, RectF box, ControlStates state);
        Material GetTextMaterial (UIOperationContext context, ControlStates state);
    }

    public sealed class DelegateDecorator : IDecorator {
        public Margins Margins { get; set; }
        public Margins Padding { get; set; }
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
        public float CornerRadius = 4f;
        public float InactiveOutlineThickness = 1f, ActiveOutlineThickness = 1.33f;

        public DefaultDecorations () {
            Func<UIOperationContext, ControlStates, Material> getTextMaterial =
                (context, state) => context.Renderer.Materials.Get(
                    context.Renderer.Materials.ScreenSpaceShadowedBitmap, blendState: BlendState.AlphaBlend
                );

            Button = new DelegateDecorator {
                Margins = new Margins(4),
                Padding = new Margins(6),
                GetTextMaterial = getTextMaterial,
                Below = (context, box, state) => {
                    var alpha = state.HasFlag(ControlStates.Hovering) ? 1.0f : 0.66f;
                    var thickness = state.HasFlag(ControlStates.Hovering) ? ActiveOutlineThickness : InactiveOutlineThickness;
                    var offset = new Vector2(CornerRadius);
                    context.Renderer.RasterizeRectangle(
                        box.Position + offset, box.Extent - offset,
                        radius: CornerRadius,
                        outlineRadius: thickness, outlineColor: Color.White * alpha,
                        innerColor: Color.White * 0.3f * alpha, outerColor: Color.White * 0.2f * alpha,
                        fillMode: Render.RasterShape.RasterFillMode.Linear
                    );
                },
            };

            Container = new DelegateDecorator {
                Margins = new Margins(4),
                GetTextMaterial = getTextMaterial,
                Below = (context, box, state) => {
                    var offset = new Vector2(CornerRadius);
                    context.Renderer.RasterizeRectangle(
                        box.Position + offset, box.Extent - offset,
                        radius: CornerRadius,
                        outlineRadius: InactiveOutlineThickness, outlineColor: Color.White * 0.2f,
                        innerColor: Color.Transparent, outerColor: Color.Transparent
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
