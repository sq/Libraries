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
        void Rasterize (RasterizeContext context, RectF box, ControlStates state);
        Material GetTextMaterial (RasterizeContext context, ControlStates state);
    }

    public sealed class DelegateDecorator : IDecorator {
        public Margins Margins { get; set; }
        public Action<RasterizeContext, RectF, ControlStates> Below, Content, Above;
        public Func<RasterizeContext, ControlStates, Material> GetTextMaterial;

        Material IDecorator.GetTextMaterial (RasterizeContext context, ControlStates state) {
            if (GetTextMaterial != null)
                return GetTextMaterial(context, state);
            else
                return null;
        }

        void IDecorator.Rasterize (RasterizeContext context, RectF box, ControlStates state) {
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
        public float CornerRadius = 3f;
        public float OutlineThickness = 1.25f;

        public DefaultDecorations () {
            Func<RasterizeContext, ControlStates, Material> getTextMaterial =
                (context, state) => context.Renderer.Materials.Get(
                    context.Renderer.Materials.ScreenSpaceShadowedBitmap, blendState: BlendState.AlphaBlend
                );

            Button = new DelegateDecorator {
                Margins = new Margins(2),
                GetTextMaterial = getTextMaterial,
                Below = (context, box, state) => {
                    float alpha = state.HasFlag(ControlStates.Hovering) ? 1.0f : 0.66f;
                    context.Renderer.RasterizeRectangle(
                        box.Position, box.Extent,
                        radius: CornerRadius,
                        outlineRadius: OutlineThickness, outlineColor: Color.White * alpha,
                        innerColor: Color.White * 0.3f * alpha, outerColor: Color.White * 0.2f * alpha,
                        fillMode: Render.RasterShape.RasterFillMode.Linear
                    );
                },
            };

            Container = new DelegateDecorator {
                Margins = new Margins(2),
                GetTextMaterial = getTextMaterial,
                Below = (context, box, state) => {
                    context.Renderer.RasterizeRectangle(
                        box.Position, box.Extent,
                        radius: CornerRadius,
                        outlineRadius: OutlineThickness, outlineColor: Color.White * 0.2f,
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
