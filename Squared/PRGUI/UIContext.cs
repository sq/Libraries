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
using Squared.Util;

namespace Squared.PRGUI {
    public class UIContext : IDisposable {
        public Vector2 CanvasSize;
        public readonly LayoutContext Layout = new LayoutContext();
        public IDecorationProvider Decorations = new DefaultDecorations();
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
                UIContext = this,
                AnimationTime = (float)Time.Seconds
            };

            Layout.Clear();

            Layout.SetFixedSize(Layout.Root, CanvasSize);
            Layout.SetContainerFlags(Layout.Root, ControlFlags.Container_Row | ControlFlags.Container_Constrain_Size);

            foreach (var control in Controls)
                control.GenerateLayoutTree(context, Layout.Root);

            Layout.Update();
        }

        public void UpdateInput (
            Vector2 mousePosition, 
            bool isButtonPressed, bool wasButtonPressed,
            float mouseWheelDelta = 0
        ) {
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

            if (mouseWheelDelta != 0)
                HandleScroll(MouseCaptured ?? Hovering, mouseWheelDelta);
        }

        private void HandleScroll (Control control, float delta) {
            while (control != null) {
                // FIXME
                var container = control as Container;
                if (container == null) {
                    control.TryGetParent(out control);
                    continue;
                }

                container.ScrollOffset.Y -= delta;
                return;
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
            for (var i = Controls.Count - 1; i >= 0; i--) {
                var control = Controls[i];
                var result = control.HitTest(Layout, position);
                if (result != null)
                    return result;
            }

            return null;
        }

        private void RasterizePass (UIOperationContext context, Control control, RasterizePasses pass) {
            var passContext = context.Clone();
            passContext.Renderer = context.Renderer.MakeSubgroup();
            passContext.Pass = pass;
            control.Rasterize(passContext, Vector2.Zero);
        }

        public void Rasterize (ref ImperativeRenderer renderer) {
            var context = new UIOperationContext {
                UIContext = this,
                AnimationTime = (float)Time.Seconds
            };

            // Ensure each control is rasterized in its own group of passes, so that top level controls can
            //  properly overlap each other
            foreach (var control in Controls) {
                context.Renderer = renderer.MakeSubgroup();
                context.Renderer.DepthStencilState = DepthStencilState.None;
                RasterizePass(context, control, RasterizePasses.Below);
                RasterizePass(context, control, RasterizePasses.Content);
                RasterizePass(context, control, RasterizePasses.Above);
            }
        }

        public void Dispose () {
            Layout.Dispose();
        }
    }

    public class UIOperationContext {
        public UIContext UIContext;
        public IDecorationProvider DecorationProvider => UIContext.Decorations;
        public LayoutContext Layout => UIContext.Layout;
        public ImperativeRenderer Renderer;
        public RasterizePasses Pass;
        public float AnimationTime;

        public UIOperationContext Clone () {
            return new UIOperationContext {
                UIContext = UIContext,
                Renderer = Renderer,
                Pass = Pass,
                AnimationTime = AnimationTime
            };
        }
    }
}
