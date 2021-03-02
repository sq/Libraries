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
    public interface IAlignedControl {
        Control AlignmentAnchor { get; }
        Vector2? AlignedPosition { get; }
        void EnsureAligned (UIOperationContext context, ref bool relayoutRequested);
    }

    public class ControlAlignmentHelper {
        public delegate bool UpdatePositionHandler (Vector2 newPosition, ref RectF parentRect, ref RectF rect, bool updateDesiredPosition);

        public bool Enabled = true;

        public UpdatePositionHandler UpdatePosition;
        public Func<bool> IsAnimating, IsLocked;

        public Margins ExtraMargins;

        /// <summary>
        /// Configures what point on the control [0 - 1] is aligned onto the anchor point
        /// </summary>
        public Vector2 ControlAlignmentPoint { get; set; } = new Vector2(0.5f, 0.5f);

        /// <summary>
        /// Configures what point on the anchor [0 - 1] is used as the center for alignment
        /// </summary>
        public Vector2 AnchorPoint { get; set; } = new Vector2(0.5f, 0.5f);

        /// <summary>
        /// If set, alignment will be relative to this control. Otherwise, the screen will be used.
        /// </summary>
        public Control Anchor { get; set; } = null;

        Vector2 _LastSize;
        RectF _LastAnchorRect, _LastParentRect;

        public bool ConstrainToParentInsteadOfScreen = false;
        public bool HideIfNotInsideParent = false;
        public bool WasPositionSetByUser;
        public bool AlignmentPending = false;
        public Vector2? MostRecentAlignedPosition = null;
        public bool ComputeNewAlignment = false;

        public Vector2? DesiredPosition;

        public Control Control { get; private set; }

        public ControlAlignmentHelper (Control host) {
            Control = host;
        }

        public bool SetPosition (Vector2 value, bool updateDesiredPosition) {
            if (updateDesiredPosition)
                DesiredPosition = value;

            if (Control.Layout.FloatingPosition != value) {
                Control.Layout.FloatingPosition = value;
                return true;
            }

            return false;
        }

        public void GetParentContentRect (out RectF result) {
            if (!Control.TryGetParent(out Control parent))
                result = Control.Context.CanvasRect;
            else
                result = parent.GetRect(contentRect: true);
        }

        private void ClampToConstraintArea (ref UIOperationContext context, ref Vector2 position, ref RectF rect) {
            // FIXME
            var area = context.UIContext.CanvasRect;
            var availableSpace = area.Size - rect.Size;
            if (availableSpace.X > 0)
                position.X = Arithmetic.Clamp(position.X, area.Left, availableSpace.X);
            if (availableSpace.Y > 0)
                position.Y = Arithmetic.Clamp(position.Y, area.Top, availableSpace.Y);

            if (HideIfNotInsideParent) {
                if (!area.Intersects(ref rect))
                    Control.Visible = false;
            }
        }

        private bool Align (ref UIOperationContext context, RectF parentRect, RectF rect, bool updateDesiredPosition) {
            // Computed?
            var margins = Control.Margins + ExtraMargins;

            if (Anchor != null) {
                // FIXME: Adjust on appropriate sides
                rect.Size += margins.Size;
                var anchorRect = Anchor.GetRect();
                var anchorPosition = ((Anchor as IAlignedControl)?.AlignedPosition) ?? anchorRect.Position;
                var anchorCenter = anchorPosition + (anchorRect.Size * AnchorPoint);
                anchorRect.Size -= margins.Size;
                var offset = (rect.Size * ControlAlignmentPoint);
                var result = anchorCenter - offset - parentRect.Position;
                ClampToConstraintArea(ref context, ref result, ref rect);
                MostRecentAlignedPosition = anchorCenter - offset;
                return SetPosition(result, updateDesiredPosition);
            } else {
                // HACK
                parentRect.Left += margins.Left;
                parentRect.Top += margins.Top;
                parentRect.Size -= margins.Size;

                var availableSpace = (parentRect.Size - rect.Size);
                if (availableSpace.X < 0)
                    availableSpace.X = 0;
                if (availableSpace.Y < 0)
                    availableSpace.Y = 0;
                var result = availableSpace * ControlAlignmentPoint;
                ClampToConstraintArea(ref context, ref result, ref rect);
                MostRecentAlignedPosition = result + parentRect.Position;
                return SetPosition(result, updateDesiredPosition);
            }
        }

        public void EnsureAligned (UIOperationContext context, ref bool relayoutRequested) {
            if (!Enabled)
                return;

            AlignmentPending = false;

            GetParentContentRect(out RectF parentRect);
            var rect = Control.GetRect(applyOffset: false);

            if (Anchor != null) {
                if (Anchor is IAlignedControl iac)
                    iac.EnsureAligned(context, ref relayoutRequested);

                var anchorRect = Anchor.GetRect();
                if (anchorRect != _LastAnchorRect) {
                    _LastAnchorRect = anchorRect;
                    relayoutRequested = true;
                }
            }

            // Handle the cases where our parent's size or our size have changed
            if (
                // We only want to realign in the event of a size change if our current position
                //  is based on alignment and not user drags, otherwise expanding a collapsed window
                //  will cause it to move out from under the mouse
                ((_LastSize != rect.Size) && !WasPositionSetByUser) || 
                (_LastParentRect != parentRect)
            ) {
                relayoutRequested = true;
            }
            _LastSize = rect.Size;
            _LastParentRect = parentRect;

            if (WasPositionSetByUser) {
                MostRecentAlignedPosition = null;

                if (DoUpdatePosition(DesiredPosition ?? Control.Layout.FloatingPosition, ref parentRect, ref rect, false))
                    relayoutRequested = true;

                var availableSpace = (parentRect.Size - rect.Size);
                if (ComputeNewAlignment)
                    ControlAlignmentPoint = (Control.Layout.FloatingPosition - parentRect.Position) / availableSpace;
            } else if (((IsAnimating == null) || !IsAnimating()) && (!relayoutRequested || (Anchor != null))) {
                relayoutRequested |= Align(ref context, parentRect, rect, true);
            } else if ((IsLocked == null) || !IsLocked()) {
                relayoutRequested |= Align(ref context, parentRect, rect, false);
            } else {
                MostRecentAlignedPosition = null;
            }
        }

        private bool DoUpdatePosition (Vector2 newPosition, ref RectF parentRect, ref RectF rect, bool updateDesiredPosition) {
            if (UpdatePosition != null)
                return UpdatePosition(newPosition, ref parentRect, ref rect, updateDesiredPosition);

            if (Control.Layout.FloatingPosition == newPosition)
                // FIXME
                return false;

            Control.Layout.FloatingPosition = newPosition;
            return true;
        }
    }

    public class Window : TitledContainer, IPartiallyIntangibleControl, IAlignedControl {
        protected ControlAlignmentHelper Aligner;

        /// <summary>
        /// Configures what point on the screen (or the anchor, if set) [0 - 1] is used as the center for this window
        /// </summary>
        public Vector2 Alignment {
            get => Aligner.ControlAlignmentPoint;
            set => Aligner.ControlAlignmentPoint = value;
        }

        /// <summary>
        /// Configures what point on the anchor [0 - 1] is used as the center for alignment
        /// </summary>
        public Vector2 AlignmentAnchorPoint {
            get => Aligner.AnchorPoint;
            set => Aligner.AnchorPoint = value;
        }

        /// <summary>
        /// If set, alignment will be relative to this control
        /// </summary>
        public Control AlignmentAnchor {
            get => Aligner.Anchor;
            set => Aligner.Anchor = value;
        }

        public Vector2 Position {
            get => base.Layout.FloatingPosition;
            set {
                Aligner.SetPosition(value, true);
            }
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
            Aligner = new ControlAlignmentHelper(this) {
                UpdatePosition = UpdatePosition,
                IsAnimating = () => CollapsePending,
                IsLocked = () => _Maximized
            };
            AcceptsMouseInput = true;
            LayoutFlags = default(ControlFlags);
            Layout.Floating = true;
        }

        protected override void ComputeSizeConstraints (ref UIOperationContext context, ref ControlDimension width, ref ControlDimension height, Vector2 sizeScale) {
            base.ComputeSizeConstraints(ref context, ref width, ref height, sizeScale);
            if (_Maximized) {
                width.Fixed = Context.CanvasSize.X;
                height.Fixed = Context.CanvasSize.Y;
            }
        }

        protected override ControlKey OnGenerateLayoutTree (ref UIOperationContext context, ControlKey parent, ControlKey? existingKey) {
            try {
                if (Collapsed)
                    context.HiddenCount++;
                ExtraContainerFlags = Scrollable
                    ? default(ControlFlags)
                    : ControlFlags.Container_Constrain_Size;
                Aligner.AlignmentPending = true;
                return base.OnGenerateLayoutTree(ref context, parent, existingKey);
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

            Aligner.EnsureAligned(context, ref relayoutRequested);

            // Handle the corner case where the canvas size has changed since we were last moved and ensure we are still on screen
            if (!Maximized && MostRecentFullSize.HasValue)
                MostRecentUnmaximizedRect = MostRecentFullSize.Value;
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
            Aligner.SetPosition(newPosition, updateDesiredPosition);

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
                var isPullingBackAway = (newPosition.Y > 4) && !DragStartedMaximized;
                var shouldUnmaximize = (((delta.Y > 4) || isPullingBackAway) || !AllowMaximize) && Maximized;
                if (shouldUnmaximize) {
                    // FIXME: Scale the mouse anchor based on the new size vs the old maximized size
                    Maximized = false;
                    Aligner.GetParentContentRect(out RectF parentRect);
                    UpdatePosition(newPosition, ref parentRect, ref MostRecentUnmaximizedRect, true);
                    Aligner.ComputeNewAlignment = true;
                } else if (shouldMaximize || Maximized) {
                    Maximized = true;
                    SetCollapsed(false, instant: true);
                } else {
                    Aligner.GetParentContentRect(out RectF parentRect);
                    var didDrag = Dragging && (delta.Length() >= 2);
                    UpdatePosition(newPosition, ref parentRect, ref args.Box, didDrag);
                    if (didDrag)
                        Aligner.WasPositionSetByUser = true;
                    Aligner.ComputeNewAlignment = didDrag;
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

        Vector2? IAlignedControl.AlignedPosition => Aligner.MostRecentAlignedPosition;

        void IAlignedControl.EnsureAligned (UIOperationContext context, ref bool relayoutRequested) {
            if (!Aligner.AlignmentPending) {
                return;
            } else {
                Aligner.EnsureAligned(context, ref relayoutRequested);
            }
        }
    }
}
