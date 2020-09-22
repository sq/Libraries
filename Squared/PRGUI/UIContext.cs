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
using Squared.Util.Event;

namespace Squared.PRGUI {
    public class UIContext : IDisposable {
        public static class Events {
            public static readonly string LostFocus = string.Intern("LostFocus"),
                GotFocus = string.Intern("GotFocus"),
                MouseDown = string.Intern("MouseDown"),
                // Mouse moved with no button(s) held
                MouseMove = string.Intern("MouseMove"),
                // Mouse moved with button(s) held
                MouseDrag = string.Intern("MouseDrag"),
                MouseUp = string.Intern("MouseUp"),
                MouseEnter = string.Intern("MouseEnter"),
                MouseLeave = string.Intern("MouseLeave"),
                Click = string.Intern("Click"),
                Scroll = string.Intern("Scroll");
        }

        // Double-clicks will only be tracked if this far apart or less
        public double DoubleClickWindowSize = 0.66;
        // If the mouse is only moved less than this far, it will be treated as no movement
        public float MinimumMovementDistance = 4;

        public Vector2 CanvasSize;
        public EventBus EventBus = new EventBus();
        public readonly LayoutContext Layout = new LayoutContext();
        public IDecorationProvider Decorations;

        private Vector2 LastMousePosition;
        private bool LastMouseButtonState = false;
        private Vector2? MouseDownPosition;

        private Control LastClickTarget;
        private double LastClickTime;
        private int SequentialClickCount;

        public List<Control> Controls = new List<Control>();

        /// <summary>
        /// The control that currently has the mouse captured (if a button is pressed)
        /// </summary>
        public Control MouseCaptured { get; private set; }
        /// <summary>
        /// The control currently underneath the mouse cursor, as long as the mouse is not captured by another control
        /// </summary>
        public Control Hovering { get; private set; }
        /// <summary>
        /// The control currently underneath the mouse cursor
        /// </summary>
        public Control MouseOver { get; private set; }

        private Control _Focused;
        public Control Focused {
            get => _Focused;
            set {
                if (value != null && (!value.AcceptsFocus || !value.Enabled))
                    throw new InvalidOperationException();
                var previous = _Focused;
                _Focused = value;
                if (previous != null)
                    FireEvent(Events.LostFocus, previous, _Focused);
                if (_Focused != null)
                    FireEvent(Events.GotFocus, _Focused, previous);
            }
        }

        public UIContext (IGlyphSource font = null)
            : this (
                new DefaultDecorations {
                    DefaultFont = font
                }
            ) {
        }

        public UIContext (IDecorationProvider decorations) {
            Decorations = decorations;
        }

        internal bool FireEvent<T> (string name, Control target, T args) {
            // FIXME: Is this right?
            if (target == null)
                return false;
            if (EventBus == null)
                return true;
            if (EventBus.Broadcast(target, name, args))
                return true;
            return target.HandleEvent(name, args);
        }

        internal bool FireEvent (string name, Control target) {
            // FIXME: Is this right?
            if (target == null)
                return false;
            if (EventBus == null)
                return true;
            if (EventBus.Broadcast<object>(target, name, null))
                return true;
            return target.HandleEvent(name);
        }

        public void UpdateLayout () {
            var context = new UIOperationContext {
                UIContext = this,
                AnimationTime = (float)Time.Seconds,
                MousePosition = LastMousePosition
            };

            Layout.Clear();

            Layout.SetFixedSize(Layout.Root, CanvasSize);
            Layout.SetContainerFlags(Layout.Root, ControlFlags.Container_Row | ControlFlags.Container_Constrain_Size);

            foreach (var control in Controls)
                control.GenerateLayoutTree(context, Layout.Root);

            Layout.Update();
        }

        public void UpdateInput (
            Vector2 mousePosition, bool leftButtonPressed,
            float mouseWheelDelta = 0
        ) {
            var previouslyHovering = Hovering;
            MouseOver = HitTest(mousePosition);

            if ((Focused != null) && !Focused.Enabled)
                Focused = null;

            if ((MouseOver != MouseCaptured) && (MouseCaptured != null))
                Hovering = null;
            else
                Hovering = MouseOver;

            if (Hovering != previouslyHovering)
                HandleHoverTransition(previouslyHovering, Hovering);

            var mouseEventTarget = MouseCaptured ?? Hovering;

            if (LastMousePosition != mousePosition) {
                if (leftButtonPressed)
                    HandleMouseDrag(mouseEventTarget, mousePosition);
                else
                    HandleMouseMove(mouseEventTarget, mousePosition);
            }

            if (!LastMouseButtonState && leftButtonPressed) {
                // FIXME: This one should probably always be Hovering
                HandleMouseDown(mouseEventTarget, mousePosition);
            } else if (LastMouseButtonState && !leftButtonPressed) {
                if (Hovering != null)
                    HandleMouseUp(mouseEventTarget, mousePosition);

                if (MouseCaptured != null) {
                    if (Hovering == MouseCaptured)
                        HandleClick(MouseCaptured);
                    else
                        HandleDrag(MouseCaptured, Hovering);
                }

                MouseCaptured = null;
            } else if (!leftButtonPressed) {
                // Shouldn't be necessary but whatever
                MouseCaptured = null;
            }

            if (mouseWheelDelta != 0)
                HandleScroll(MouseCaptured ?? Hovering, mouseWheelDelta);

            LastMouseButtonState = leftButtonPressed;
            LastMousePosition = mousePosition;
        }

        private MouseEventArgs MakeArgs (Control target, Vector2 globalPosition) {
            if (target == null)
                return default(MouseEventArgs);

            var box = target.GetRect(Layout, contentRect: false);
            var contentBox = target.GetRect(Layout, contentRect: true);
            var mdp = MouseDownPosition ?? globalPosition;
            var travelDistance = (globalPosition - mdp).Length();
            return new MouseEventArgs {
                Focused = Focused,
                MouseOver = MouseOver,
                Hovering = Hovering,
                MouseCaptured = MouseCaptured,
                GlobalPosition = globalPosition,
                LocalPosition = globalPosition - contentBox.Position,
                Box = box,
                ContentBox = contentBox,
                MouseDownPosition = mdp,
                MovedSinceMouseDown = travelDistance >= MinimumMovementDistance
            };
        }

        private void HandleMouseDown (Control target, Vector2 globalPosition) {
            MouseDownPosition = globalPosition;
            if (target != null && (target.AcceptsCapture && target.Enabled))
                MouseCaptured = target;
            if (target == null || (target.AcceptsFocus && target.Enabled))
                Focused = target;
            // FIXME: Suppress if disabled?
            FireEvent(Events.MouseDown, target, MakeArgs(target, globalPosition));
        }

        private void HandleMouseUp (Control target, Vector2 globalPosition) {
            MouseDownPosition = null;
            // FIXME: Suppress if disabled?
            FireEvent(Events.MouseUp, target, MakeArgs(target, globalPosition));
        }

        private void HandleMouseMove (Control target, Vector2 globalPosition) {
            FireEvent(Events.MouseMove, target, MakeArgs(target, globalPosition));
        }

        private void HandleMouseDrag (Control target, Vector2 globalPosition) {
            // FIXME: Suppress if disabled?
            FireEvent(Events.MouseDrag, target, MakeArgs(target, globalPosition));
        }

        private void HandleScroll (Control control, float delta) {
            while (control != null) {
                if (FireEvent(Events.Scroll, control, delta))
                    return;

                if (control.TryGetParent(out control))
                    continue;
            }
        }

        private void HandleHoverTransition (Control previous, Control current) {
            if (previous != null)
                FireEvent(Events.MouseLeave, previous, current);
            if (current != null)
                FireEvent(Events.MouseEnter, current, previous);
        }

        private void HandleClick (Control target) {
            if (!target.Enabled)
                return;

            var now = Time.Seconds;
            if (LastClickTarget == target) {
                var elapsed = now - LastClickTime;
                if (elapsed < DoubleClickWindowSize)
                    SequentialClickCount++;
            } else {
                SequentialClickCount = 1;
            }

            LastClickTarget = target;
            LastClickTime = now;
            FireEvent(Events.Click, target, SequentialClickCount);
        }

        private void HandleDrag (Control originalTarget, Control finalTarget) {
            // FIXME
        }

        // Position is relative to the top-left corner of the canvas
        public Control HitTest (Vector2 position, bool acceptsCaptureOnly = false, bool acceptsFocusOnly = false) {
            for (var i = Controls.Count - 1; i >= 0; i--) {
                var control = Controls[i];
                var result = control.HitTest(Layout, position, acceptsCaptureOnly, acceptsFocusOnly);
                if (result != null)
                    return result;
            }

            return null;
        }

        private void RasterizePass (UIOperationContext context, Control control, RasterizePasses pass) {
            var passContext = context.Clone();
            passContext.Renderer = context.Renderer.MakeSubgroup();
            passContext.Pass = pass;
            control.Rasterize(passContext);
        }

        public void Rasterize (ref ImperativeRenderer renderer) {
            var context = new UIOperationContext {
                UIContext = this,
                AnimationTime = (float)Time.Seconds,
                MousePosition = LastMousePosition
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
        public Vector2 MousePosition;

        public UIOperationContext Clone () {
            return new UIOperationContext {
                UIContext = UIContext,
                Renderer = Renderer,
                Pass = Pass,
                AnimationTime = AnimationTime,
                MousePosition = MousePosition
            };
        }
    }

    public struct MouseEventArgs {
        public Control MouseOver, MouseCaptured, Hovering, Focused;
        public Vector2 GlobalPosition, LocalPosition;
        public Vector2 MouseDownPosition;
        public RectF Box, ContentBox;
        public bool MovedSinceMouseDown;
    }
}
