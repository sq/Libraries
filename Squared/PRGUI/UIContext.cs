﻿using System;
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
                MouseUp = string.Intern("MouseUp"),
                Click = string.Intern("Click"),
                Scroll = string.Intern("Scroll");
        }

        public Vector2 CanvasSize;
        public EventBus EventBus = new EventBus();
        public readonly LayoutContext Layout = new LayoutContext();
        public IDecorationProvider Decorations = new DefaultDecorations();
        public IGlyphSource DefaultGlyphSource;

        private bool LastMouseButtonState = false;

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

        internal void FireEvent<T> (string name, object target, T args) {
            EventBus?.Broadcast(target, name, args);
        }

        internal void FireEvent (string name, object target) {
            EventBus?.Broadcast<object>(target, name, null);
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

            if (!LastMouseButtonState && leftButtonPressed) {
                HandleMouseDown(Hovering);
            } else if (LastMouseButtonState && !leftButtonPressed) {
                if (MouseCaptured != null) {
                    if (Hovering == MouseCaptured)
                        HandleClick(MouseCaptured);
                    else
                        HandleDrag(MouseCaptured, Hovering);
                }

                if (Hovering != null)
                    HandleMouseUp(Hovering);

                MouseCaptured = null;
            } else if (!leftButtonPressed) {
                // Shouldn't be necessary but whatever
                MouseCaptured = null;
            }

            if (mouseWheelDelta != 0)
                HandleScroll(MouseCaptured ?? Hovering, mouseWheelDelta);

            LastMouseButtonState = leftButtonPressed;
        }

        private void HandleScroll (Control control, float delta) {
            while (control != null) {
                FireEvent(Events.Scroll, control, delta);

                if (!control.AcceptsScroll || !control.HandleScroll(delta)) {
                    control.TryGetParent(out control);
                    continue;
                }

                break;
            }
        }

        private void HandleHoverTransition (Control previous, Control current) {
            // FIXME
        }

        private void HandleMouseDown (Control target) {
            // FIXME: Position
            if (target != null)
                FireEvent(Events.MouseDown, target);
            if (target != null && (target.AcceptsCapture && target.Enabled))
                MouseCaptured = target;
            if (target == null || (target.AcceptsFocus && target.Enabled))
                Focused = target;
        }

        private void HandleMouseUp (Control target) {
            FireEvent(Events.MouseUp, target);
        }

        private void HandleClick (Control target) {
            if (target.Enabled)
                FireEvent(Events.Click, target);
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
