using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Squared.Game;
using Squared.PRGUI.Decorations;
using Squared.PRGUI.Layout;
using Squared.Render;
using Squared.Render.Convenience;
using Squared.Render.Text;
using Squared.Util;

namespace Squared.PRGUI.Controls {
    public class Window : TitledContainer, IPartiallyIntangibleControl {
        private Vector2 _ScreenAlignment = Vector2.One * 0.5f;
        public Vector2 ScreenAlignment {
            get => _ScreenAlignment;
            set {
                _ScreenAlignment = value;
                NeedsAlignment = true;
            }
        }

        private Vector2? _DesiredPosition;
        public Vector2 Position {
            get {
                return new Vector2(Margins.Left, Margins.Top);
            }
            set {
                SetPosition(value, true);
                NeedsAlignment = false;
            }
        }

        private void SetPosition (Vector2 value, bool updateDesiredPosition) {
            if (updateDesiredPosition)
                _DesiredPosition = value;

            Margins.Left = value.X;
            Margins.Top = value.Y;
        }

        private bool _DesiredCollapsible;

        new public bool Collapsible {
            get => _DesiredCollapsible;
            set {
                _DesiredCollapsible = value;
                base.Collapsible = _DesiredCollapsible && !Maximized;
            }
        }

        public bool AllowDrag = true;
        public bool AllowMaximize = false;

        private bool NeedsAlignment = true;
        private bool Dragging, DragStartedMaximized;
        private Vector2 DragStartMousePosition, DragStartWindowPosition;
        private RectF MostRecentUnmaximizedRect;

        private bool _Maximized;

        public bool Maximized {
            get => _Maximized;
            set {
                if (!AllowMaximize)
                    value = false;
                _Maximized = value;
                base.Collapsible = _DesiredCollapsible && !value;
            }
        }

        public Window ()
            : base () {
            AcceptsMouseInput = true;
            LayoutFlags = ControlFlags.Layout_Floating;
        }

        protected override void ComputeFixedSize (out float? fixedWidth, out float? fixedHeight) {
            base.ComputeFixedSize(out fixedWidth, out fixedHeight);
            if (_Maximized) {
                fixedWidth = Context.CanvasSize.X;
                fixedHeight = Context.CanvasSize.Y;
            }
        }

        protected override ControlKey OnGenerateLayoutTree (UIOperationContext context, ControlKey parent, ControlKey? existingKey) {
            try {
                if (Collapsed)
                    context.HiddenCount++;
                ExtraContainerFlags = Scrollable
                    ? ControlFlags.Container_Prevent_Crush
                    : ControlFlags.Container_Constrain_Size;
                return base.OnGenerateLayoutTree(context, parent, existingKey);
            } finally {
                if (Collapsed)
                    context.HiddenCount--;
            }
        }

        protected override IDecorator GetDefaultDecorator (IDecorationProvider provider) {
            return provider?.Window;
        }

        protected override IDecorator GetTitleDecorator (UIOperationContext context) {
            return context.DecorationProvider?.WindowTitle ?? base.GetTitleDecorator(context);
        }

        protected override void OnLayoutComplete (UIOperationContext context, ref bool relayoutRequested) {
            base.OnLayoutComplete(context, ref relayoutRequested);

            var rect = GetRect(applyOffset: false);

            // Handle the corner case where the canvas size has changed since we were last moved and ensure we are still on screen
            if (!Maximized && MostRecentFullSize.HasValue)
                MostRecentUnmaximizedRect = MostRecentFullSize.Value;

            var availableSpace = (context.UIContext.CanvasSize - rect.Size);

            if (!NeedsAlignment) {
                if (UpdatePosition(_DesiredPosition ?? Position, context.UIContext, rect, false))
                    relayoutRequested = true;

                _ScreenAlignment = Position / availableSpace;
            } else if (!CollapsePending && !relayoutRequested) {
                SetPosition(availableSpace * _ScreenAlignment, true);
                NeedsAlignment = false;
                relayoutRequested = true;
            } else {
                SetPosition(availableSpace * _ScreenAlignment, false);
            }
        }

        private bool UpdatePosition (Vector2 newPosition, UIContext context, RectF box, bool updateDesiredPosition) {
            var availableSpaceX = Math.Max(0, context.CanvasSize.X - box.Width);
            var availableSpaceY = Math.Max(0, context.CanvasSize.Y - box.Height);
            newPosition = new Vector2(
                Arithmetic.Saturate(newPosition.X, availableSpaceX),
                Arithmetic.Saturate(newPosition.Y, availableSpaceY)
            ).Floor();

            var changed = Position != newPosition;

            // context.Log($"Window position {Position} -> {newPosition}");
            SetPosition(newPosition, updateDesiredPosition);

            return changed;
        }

        protected override void OnRasterize (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings, IDecorator decorations) {
            // HACK: We don't want collapsing to be enabled the first time a window is clicked
            if (!context.MouseButtonHeld)
                CollapsingEnabled = settings.State.IsFlagged(ControlStates.ContainsFocus);

            base.OnRasterize(context, ref renderer, settings, decorations);
        }

        protected override bool OnMouseEvent (string name, MouseEventArgs args) {
            if (name == UIEvents.MouseDown) {
                Context.TrySetFocus(this);

                if (MostRecentTitleBox.Contains(args.RelativeGlobalPosition) && AllowDrag) {
                    Context.CaptureMouse(this);
                    Dragging = true;
                    DragStartedMaximized = Maximized;
                    DragStartMousePosition = args.GlobalPosition;
                    DragStartWindowPosition = Position;
                    return true;
                } else {
                    Dragging = false;
                }
            } else if (
                (name == UIEvents.MouseMove) ||
                (name == UIEvents.MouseUp)
            ) {
                if (!Dragging)
                    return base.OnMouseEvent(name, args);

                if (name == UIEvents.MouseUp)
                    Dragging = false;

                var delta = args.GlobalPosition - DragStartMousePosition;
                var newPosition = DragStartedMaximized
                    ? new Vector2(args.GlobalPosition.X - (MostRecentUnmaximizedRect.Width / 2f), args.GlobalPosition.Y - 4)
                    : (DragStartWindowPosition + delta);
                var shouldMaximize = (newPosition.Y < -5) && !Maximized && AllowMaximize;
                var shouldUnmaximize = (((delta.Y > 4) || (newPosition.Y > 4)) || !AllowMaximize) && Maximized;
                if (shouldUnmaximize) {
                    // FIXME: Scale the mouse anchor based on the new size vs the old maximized size
                    Maximized = false;
                    UpdatePosition(newPosition, args.Context, MostRecentUnmaximizedRect, true);
                } else if (shouldMaximize || Maximized) {
                    Maximized = true;
                    SetCollapsed(false, instant: true);
                } else {
                    UpdatePosition(newPosition, args.Context, args.Box, Dragging && (delta.Length() >= 2));
                }

                FireEvent(UIEvents.Moved);

                return true;
            } else if (name == UIEvents.Click) {
                if (
                    (Collapsed && CollapsingEnabled) ||
                    DisclosureArrowHitTest(args.RelativeGlobalPosition - args.Box.Position) ||
                    (CollapsingEnabled && (args.SequentialClickCount == 2) && !Collapsed)
                )
                    ToggleCollapsed();
                return true;
            }

            // Don't fallback to TitledContainer's event handler, it does things we don't want
            return false;
        }

        protected override string DescriptionPrefix => (Collapsed ? "Collapsed Window" : "Window");

        bool IPartiallyIntangibleControl.IsIntangibleAtPosition (Vector2 position) => false;
    }
}
