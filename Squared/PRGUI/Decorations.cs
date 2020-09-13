using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;

namespace Squared.PRGUI {
    public interface IDecorationRenderer {
        void Rasterize (RasterizeContext context, RectF box);
    }

    public sealed class DelegateDecorationRenderer : IDecorationRenderer {
        public Action<RasterizeContext, RectF> Below, Content, Above;

        void IDecorationRenderer.Rasterize (RasterizeContext context, RectF box) {
            switch (context.Pass) {
                case RasterizePasses.Below:
                    if (Below != null)
                        Below(context, box);
                    return;
                case RasterizePasses.Content:
                    if (Content != null)
                        Content(context, box);
                    return;
                case RasterizePasses.Above:
                    if (Above != null)
                        Above(context, box);
                    return;
            }
        }
    }

    public class DefaultDecorations : DecorationProvider {
        public float CornerRadius = 3f;
        public float OutlineThickness = 1.25f;

        public DefaultDecorations () {
            Button = new DelegateDecorationRenderer {
                Below = (context, box) => {
                    context.Renderer.RasterizeRectangle(
                        box.Position, box.Extent,
                        radius: CornerRadius,
                        outlineRadius: OutlineThickness, outlineColor: Color.White,
                        innerColor: Color.White, outerColor: Color.Black,
                        fillMode: Render.RasterShape.RasterFillMode.Linear
                    );
                }
            };

            Container = new DelegateDecorationRenderer {
                Below = (context, box) => {
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
        public DelegateDecorationRenderer Button { get; set; }
        public DelegateDecorationRenderer Container { get; set; }
    }
}
