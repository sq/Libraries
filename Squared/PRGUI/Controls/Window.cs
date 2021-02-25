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
        private Vector2 _Alignment = Vector2.One * 0.5f;
        /// <summary>
        /// Configures what point on the screen (or the anchor, if set) [0 - 1] is used as the center for this window
        /// </summary>
        public Vector2 Alignment {
            get => _Alignment;
            set {
                _Alignment = value;
                NeedsAlignment = true;
            }
        }

        private Vector2 _AlignmentAnchorPoint = Vector2.One * 0.5f;
        /// <summary>
        /// Configures what point on the anchor [0 - 1] is used as the center for alignment
        /// </summary>
        public Vector2 AlignmentAnchorPoint {
            get => _AlignmentAnchorPoint;
            set {
                _AlignmentAnchorPoint = value;
                NeedsAlignment = true;
            }
        }

        private Control _AlignmentAnchor = null;
        /// <summary>
        /// If set, alignment will be relative to this control
        /// </summary>
        public Control AlignmentAnchor {
            get => _AlignmentAnchor;
            set {
                if (_AlignmentAnchor == value)
                    return;
                _AlignmentAnchor = value;
                NeedsAlignment = true;
            }
        }

        private Vector2? _DesiredPosition;
        public Vector2 Position {
            get => base.Layout.FloatingPosition;
            set {
                _WasPositionSetByUser = false;
                SetPosition(value, true);
                NeedsAlignment = false;
            }
        }

        private void SetPosition (Vector2 value, bool updateDesiredPosition) {
            if (updateDesiredPosition)
                _DesiredPosition = value;

            base.Layout.FloatingPosition = value;
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

        private bool NeedsAlignment = true, ComputeNewAlignment = false;
        private bool Dragging, DragStartedMaximized;
        private Vector2 DragStartMousePosition, DragStartWindowPosition;
        private RectF MostRecentUnmaximizedRect;

        private bool _Maximized;

        Vector2 _LastSize;
        RectF _LastAnchorRect, _LastParentRect;
        bool _WasPositionSetByUser;

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
            LayoutFlags = default(ControlFlags);
            Layout.Floating = true;
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
                    ? default(ControlFlags)
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

        private void Align (ref UIOperationContext context, ref RectF parentRect, ref RectF rect, bool updateDesiredPosition) {
            // Computed?
            var margins = Margins;
            // HACK
            parentRect.Left += margins.Left;
            parentRect.Top += margins.Top;
            parentRect.Size -= margins.Size;

            if (_AlignmentAnchor != null) {
                var anchorRect = _AlignmentAnchor.GetRect();
                var anchorCenter = anchorRect.Position + (anchorRect.Extent * _AlignmentAnchorPoint);
                // FIXME: Not all four sides?
                rect.Size += margins.Size;
                // FIXME: Right-alignment is busted
                var offset = (rect.Size * _Alignment);
                SetPosition(anchorCenter - offset - parentRect.Position, updateDesiredPosition);
            } else {
                var availableSpace = (parentRect.Size - rect.Size);
                if (availableSpace.X < 0)
                    availableSpace.X = 0;
                if (availableSpace.Y < 0)
                    availableSpace.Y = 0;
                SetPosition(availableSpace * _Alignment, updateDesiredPosition);
            }
        }

        protected override void OnLayoutComplete (UIOperationContext context, ref bool relayoutRequested) {
            base.OnLayoutComplete(context, ref relayoutRequested);

            var parentRect = GetParentContentRect();
            var rect = GetRect(applyOffset: false);

            if (_AlignmentAnchor != null) {
                var anchorRect = _AlignmentAnchor.GetRect();
                if (anchorRect != _LastAnchorRect) {
                    _LastAnchorRect = anchorRect;
                    NeedsAlignment = true;
                }
            }

            // Handle the cases where our parent's size or our size have changed
            if (
                // We only want to realign in the event of a size change if our current position
                //  is based on alignment and not user drags, otherwise expanding a collapsed window
                //  will cause it to move out from under the mouse
                ((_LastSize != rect.Size) && !_WasPositionSetByUser) || 
                (_LastParentRect != parentRect)
            )
                NeedsAlignment = true;
            _LastSize = rect.Size;
            _LastParentRect = parentRect;

            // Handle the corner case where the canvas size has changed since we were last moved and ensure we are still on screen
            if (!Maximized && MostRecentFullSize.HasValue)
                MostRecentUnmaximizedRect = MostRecentFullSize.Value;

            if (!NeedsAlignment) {
                if (UpdatePosition(_DesiredPosition ?? Position, ref parentRect, ref rect, false))
                    relayoutRequested = true;

                var availableSpace = (parentRect.Size - rect.Size);
                if (ComputeNewAlignment)
                    _Alignment = (Position - parentRect.Position) / availableSpace;
            } else if (!CollapsePending && !relayoutRequested) {
                Align(ref context, ref parentRect, ref rect, true);
                NeedsAlignment = false;
                relayoutRequested = true;
            } else {
                Align(ref context, ref parentRect, ref rect, false);
            }
        }

        private RectF GetParentContentRect () {
            RectF parentRect;
            if (!TryGetParent(out Control parent))
                parentRect = Context.CanvasRect;
            else
                parentRect = parent.GetRect(contentRect: true);
            return parentRect;
        }

        private bool UpdatePosition (Vector2 newPosition, ref RectF parentRect, ref RectF box, bool updateDesiredPosition) {
            var effectiveSize = box.Size + Margins.Size;
            var availableSpaceX = Math.Max(0, parentRect.Width - effectiveSize.X);
            var availableSpaceY = Math.Max(0, parentRect.Height - effectiveSize.Y);
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
                    var parentRect = GetParentContentRect();
                    UpdatePosition(newPosition, ref parentRect, ref MostRecentUnmaximizedRect, true);
                    ComputeNewAlignment = true;
                } else if (shouldMaximize || Maximized) {
                    Maximized = true;
                    SetCollapsed(false, instant: true);
                } else {
                    var parentRect = GetParentContentRect();
                    var didDrag = Dragging && (delta.Length() >= 2);
                    UpdatePosition(newPosition, ref parentRect, ref args.Box, didDrag);
                    if (didDrag)
                        _WasPositionSetByUser = true;
                    ComputeNewAlignment = didDrag;
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
