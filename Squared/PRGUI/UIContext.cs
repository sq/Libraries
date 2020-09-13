using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Squared.PRGUI.Decorations;
using Squared.PRGUI.Layout;
using Squared.Render;
using Squared.Render.Convenience;
using Squared.Render.Text;

namespace Squared.PRGUI {
    public class UIContext : IDisposable {
        public Vector2 CanvasSize;
        public readonly LayoutContext Layout = new LayoutContext();
        public DecorationProvider Decorations = new DefaultDecorations();
        public IGlyphSource DefaultGlyphSource;

        public List<Control> Controls = new List<Control>();

        public Control Hovering { get; private set; }
        public Control Focused { get; set; }

        public void Update () {
            var context = new UIOperationContext {
                UIContext = this
            };

            Layout.Clear();

            Layout.SetSize(Layout.Root, CanvasSize);
            Layout.SetContainerFlags(Layout.Root, ControlFlags.Container_Row);

            foreach (var control in Controls)
                control.GenerateLayoutTree(context, Layout.Root);

            Layout.Update();
        }

        public void UpdateInput (Vector2 mousePosition) {
            Hovering = HitTest(mousePosition);
        }

        // Position is relative to the top-left corner of the canvas
        public Control HitTest (Vector2 position) {
            foreach (var control in Controls) {
                var result = control.HitTest(Layout, position);
                if (result != null)
                    return result;
            }

            return null;
        }

        private void RasterizePass (UIOperationContext context, RasterizePasses pass) {
            context.Pass = pass;

            foreach (var control in Controls)
                control.Rasterize(context, Vector2.Zero);

            context.Renderer.Layer++;
        }

        public void Rasterize (ref ImperativeRenderer renderer) {
            var context = new UIOperationContext {
                UIContext = this,
                Renderer = renderer
            };

            RasterizePass(context, RasterizePasses.Below);
            RasterizePass(context, RasterizePasses.Content);
            RasterizePass(context, RasterizePasses.Above);

            // HACK
            renderer.Layer = context.Renderer.Layer;
        }

        public void Dispose () {
            Layout.Dispose();
        }
    }
}
