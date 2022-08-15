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
    public class Window : TitledContainer, IPartiallyIntangibleControl, IAlignedControl, IControlContainer {
        protected ControlAlignmentHelper<Window> Aligner;

        /// <summary>
        /// Configures what point on the screen is used as the center for this window, OR
        ///  if an alignment anchor is set, configures what point on this control is aligned to it
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
            get => base.Layout.FloatingPosition.Value;
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

        public bool ChildrenAcceptFocus { get; set; } = true;

        public bool AllowDrag = true;
        public bool AllowMaximize = false;

        private bool Dragging, DragStartedMaximized;
        private Vector2 DragStartMousePosition, DragStartWindowPosition;
        private RectF MostRecentUnmaximizedRect;

        private bool _Maximized;

        new public bool AcceptsFocus {
            get => base.AcceptsFocus;
            set => base.AcceptsFocus = value;
        }

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
            Layout.FloatingPosition = Vector2.Zero;
            Aligner = new ControlAlignmentHelper<Window>(this) {
                UpdatePosition = UpdatePosition,
                IsAnimating = () => CollapsePending,
                IsLocked = () => _Maximized
            };
            AcceptsMouseInput = true;
            LayoutFlags = default(ControlFlags);
            Layout.Floating = true;
            LayoutChildrenWhenCollapsed = false;
            LockWidthWhileCollapsed = true;
        }

        protected void CancelDrag () {
            Dragging = false;
        }

        protected override void ComputeSizeConstraints (ref UIOperationContext context, ref ControlDimension width, ref ControlDimension height, Vector2 sizeScale) {
            base.ComputeSizeConstraints(ref context, ref width, ref height, sizeScale);
            if (_Maximized) {
                width.Fixed = context.UIContext.CanvasSize.X;
                height.Fixed = context.UIContext.CanvasSize.Y;
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
                var result = base.OnGenerateLayoutTree(ref context, parent, existingKey);
                context.Layout.SetTag(result, LayoutTags.Window);
                return result;
            } finally {
                if (Collapsed)
                    context.HiddenCount--;
            }
        }

        protected override IDecorator GetDefaultDecorator (IDecorationProvider provider) {
            return provider?.Window;
        }

        protected override IDecorator GetTitleDecorator (IDecorationProvider provider) {
            return provider?.WindowTitle ?? base.GetTitleDecorator(provider);
        }
        
        protected override void OnLayoutComplete (ref UIOperationContext context, ref bool relayoutRequested) {
            base.OnLayoutComplete(ref context, ref relayoutRequested);

            Aligner.EnsureAligned(ref context, ref relayoutRequested);

            // Handle the corner case where the canvas size has changed since we were last moved and ensure we are still on screen
            if (!Maximized && MostRecentFullSize.HasValue)
                MostRecentUnmaximizedRect = MostRecentFullSize.Value;
        }

        private bool UpdatePosition (in Vector2 _newPosition, in RectF parentRect, in RectF box, bool updateDesiredPosition) {
            var effectiveSize = box.Size + Margins.Size;
            var availableSpaceX = Math.Max(0, parentRect.Width - effectiveSize.X);
            var availableSpaceY = Math.Max(0, parentRect.Height - effectiveSize.Y);
            var newPosition = new Vector2(
                Arithmetic.Saturate(_newPosition.X, availableSpaceX),
                Arithmetic.Saturate(_newPosition.Y, availableSpaceY)
            ).Floor();

            var changed = Position != newPosition;

            // context.Log($"Window position {Position} -> {newPosition}");
            Aligner.SetPosition(newPosition, updateDesiredPosition);

            return changed;
        }

        protected override void OnRasterize (ref UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings, IDecorator decorations) {
            // HACK: We don't want collapsing to be enabled the first time a window is clicked
            if (!context.MouseButtonHeld)
                CollapsingEnabled = settings.HasStateFlag(ControlStates.ContainsFocus);

            base.OnRasterize(ref context, ref renderer, settings, decorations);
        }

        protected override bool OnMouseEvent (string name, MouseEventArgs args) {
            if (name == UIEvents.MouseDown) {
                Context.TrySetFocus(this);

                if (MostRecentTitleBox.Contains(args.RelativeGlobalPosition) && AllowDrag && (args.Buttons == MouseButtons.Left)) {
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
                var shouldMaximize = (newPosition.Y < -5) && !Maximized && AllowMaximize && !Collapsed;
                var isPullingBackAway = (newPosition.Y > 4) && !DragStartedMaximized;
                var shouldUnmaximize = (((delta.Y > 4) || isPullingBackAway) || !AllowMaximize) && Maximized;
                if (shouldUnmaximize) {
                    // FIXME: Scale the mouse anchor based on the new size vs the old maximized size
                    Maximized = false;
                    Aligner.GetParentContentRect(out RectF parentRect);
                    UpdatePosition(in newPosition, in parentRect, in MostRecentUnmaximizedRect, true);
                    Aligner.ComputeNewAlignment = true;
                } else if (shouldMaximize || Maximized) {
                    Maximized = true;
                    SetCollapsed(false, instant: true);
                } else {
                    Aligner.GetParentContentRect(out RectF parentRect);
                    var didDrag = Dragging && (delta.Length() >= 2);
                    UpdatePosition(in newPosition, in parentRect, in args.Box, didDrag);
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

        void IAlignedControl.EnsureAligned (ref UIOperationContext context, ref bool relayoutRequested) {
            if (!Aligner.AlignmentPending) {
                return;
            } else {
                Aligner.EnsureAligned(ref context, ref relayoutRequested);
            }
        }
    }
}
