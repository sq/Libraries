using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
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

        public Control MouseCaptured { get; private set; }
        public Control Hovering { get; private set; }

        private Control _Focused;
        public Control Focused {
            get => _Focused;
            set {
                if (value != null && !value.AcceptsFocus)
                    throw new InvalidOperationException();
                _Focused = value;
            }
        }

        public void UpdateLayout () {
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

        public void UpdateInput (Vector2 mousePosition, bool isButtonPressed, bool wasButtonPressed) {
            var previouslyHovering = Hovering;
            Hovering = HitTest(mousePosition);

            if (Hovering != previouslyHovering)
                HandleHoverTransition(previouslyHovering, Hovering);

            if (!wasButtonPressed && isButtonPressed) {
                HandlePress(Hovering);
            } else if (wasButtonPressed && !isButtonPressed) {
                if (MouseCaptured != null) {
                    if (Hovering == MouseCaptured)
                        HandleClick(MouseCaptured);
                    else
                        HandleDrag(MouseCaptured, Hovering);
                }

                HandleRelease(Hovering);

                MouseCaptured = null;
            } else if (!isButtonPressed) {
                // Shouldn't be necessary but whatever
                MouseCaptured = null;
            }
        }

        private void HandleHoverTransition (Control previous, Control current) {
        }

        private void HandlePress (Control target) {
            if (target != null && target.AcceptsCapture)
                MouseCaptured = target;
            if (target == null || target.AcceptsFocus)
                Focused = target;
        }

        private void HandleRelease (Control target) {
        }

        private void HandleClick (Control target) {
        }

        private void HandleDrag (Control originalTarget, Control finalTarget) {
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
            context.Renderer.Layer++;

            var subContext = context.Clone();
            subContext.Renderer = context.Renderer.MakeSubgroup();
            subContext.Pass = pass;

            foreach (var control in Controls)
                control.Rasterize(subContext, Vector2.Zero);

            context.Renderer.Layer++;
        }

        public void Rasterize (ref ImperativeRenderer renderer) {
            var context = new UIOperationContext {
                UIContext = this,
                Renderer = renderer
            };

            context.Renderer.DepthStencilState = DepthStencilState.None;

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

    public class UIOperationContext {
        public UIContext UIContext;
        public DecorationProvider DecorationProvider => UIContext.Decorations;
        public LayoutContext Layout => UIContext.Layout;
        public ImperativeRenderer Renderer;
        public RasterizePasses Pass;

        public UIOperationContext Clone () {
            return new UIOperationContext {
                UIContext = UIContext,
                Renderer = Renderer,
                Pass = Pass
            };
        }
    }
}
