﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Squared.PRGUI.Decorations;
using Squared.Util;

namespace Squared.PRGUI {
    public interface IAlignedControl {
        Control AlignmentAnchor { get; }
        Vector2? AlignedPosition { get; }
        void EnsureAligned (ref UIOperationContext context, ref bool relayoutRequested);
    }

    /// <summary>
    /// Manages position and alignment for controls that are anchored/aligned to another control.
    /// Also manages cascading alignment (where control C is aligned to control B which is aligned to control A).
    /// Will prevent the aligned control from extending outside its parent region or from covering its anchor.
    /// </summary>
    public sealed class ControlAlignmentHelper<TControl>
        where TControl : Control
    {
        public delegate bool UpdatePositionHandler (in Vector2 newPosition, in RectF parentRect, in RectF rect, bool updateDesiredPosition);

        public bool Enabled = true, UseTransformedAnchor = false, UseContentRect = false;
        private bool IsNewInstance = true;

        public UpdatePositionHandler UpdatePosition;
        public Func<bool> IsAnimating, IsLocked;

        private Vector2 _AlignmentPoint = new Vector2(0.5f, 0.5f), _AnchorPoint = new Vector2(0.5f, 0.5f);

        public bool IsInvalid { get; private set; } = true;

        /// <summary>
        /// Configures what point on the control [0 - 1] is aligned onto the anchor point
        /// </summary>
        public Vector2 ControlAlignmentPoint {
            get => _AlignmentPoint;
            set {
                if ((_AlignmentPoint == value) && !IsNewInstance)
                    return;
                IsNewInstance = false;
                _AlignmentPoint = value;
                AlignmentPending = true;
            }
        }

        /// <summary>
        /// Configures what point on the anchor [0 - 1] is used as the center for alignment
        /// </summary>
        public Vector2 AnchorPoint {
            get => _AnchorPoint;
            set {
                if (_AnchorPoint == value)
                    return;
                _AnchorPoint = value;
                AlignmentPending = true;
            }
        }

        /// <summary>
        /// If set, alignment will be relative to this control. Otherwise, the screen will be used.
        /// </summary>
        public Control Anchor {
            get {
                if (_WeakAnchor.IsAllocated)
                    return _WeakAnchor.Target as Control;
                else
                    return null;
            }
            set {
                var oldAnchor = Anchor;
                if (value == null) {
                    if (_WeakAnchor.IsAllocated)
                        _WeakAnchor.Free();
                    _WeakAnchor = default;
                    AlignmentPending = true;
                } else if (oldAnchor != value) {
                    if (_WeakAnchor.IsAllocated)
                        _WeakAnchor.Free();
                    _WeakAnchor = GCHandle.Alloc(value, GCHandleType.Weak);
                    AlignmentPending = true;
                }
                _LastAnchorRect = default;
                _LastParentRect = default;
                _LastSize = default;
            }
        }

        GCHandle _WeakAnchor;
        Vector2 _LastSize;
        RectF _LastAnchorRect, _LastParentRect;

        /// <summary>
        /// If false, the aligner will attempt to prevent the control from covering its anchor.
        /// </summary>
        public bool AllowOverlap = true;
        public bool ConstrainToParentInsteadOfScreen = false;
        public bool HideIfNotInsideParent = false;
        public bool WasPositionSetByUser;
        public bool AlignmentPending = false;
        public Vector2? MostRecentAlignedPosition = null;
        public bool ComputeNewAlignment = false;

        public Vector2? DesiredPosition;

        public TControl Control { get; private set; }

        public ControlAlignmentHelper (TControl host) {
            Control = host;
        }

        ~ControlAlignmentHelper () {
            if (_WeakAnchor.IsAllocated)
                _WeakAnchor.Free();
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

        private void ClampToConstraintArea (ref UIOperationContext context, ref Vector2 position, in RectF rect) {
            // FIXME
            var area = context.UIContext.CanvasRect;
            var availableSpace = area.Size - rect.Size;
            if (availableSpace.X > 0)
                position.X = Arithmetic.Clamp(position.X, area.Left, availableSpace.X);
            if (availableSpace.Y > 0)
                position.Y = Arithmetic.Clamp(position.Y, area.Top, availableSpace.Y);

            if (HideIfNotInsideParent) {
                if (!area.Intersects(in rect))
                    Control.Visible = false;
            }
        }

        private bool Align (ref UIOperationContext context, RectF parentRect, RectF rect, bool updateDesiredPosition) {
            PRGUI.Control.ComputeEffectiveSpacing(Control, ref context, out _, out var margins);

            RectF actualRect = rect, anchorRect;
            Vector2 trialAnchorPoint = AnchorPoint, trialAlignmentPoint = ControlAlignmentPoint;
            var allowOverlap = AllowOverlap || (Anchor == null);

            if (Anchor != null) {
                // HACK: Expand our rect so we don't rub up against the screen edges
                rect.Size += margins.Size;

                // FIXME: Should we set contentRect: UseTransformedAnchor to cancel out the effect of the anchor's margins?
                anchorRect = Anchor.GetRect(displayRect: UseTransformedAnchor, contentRect: UseContentRect);
                if (anchorRect == default(RectF))
                    return false;
                
                // HACK: Apply *our* margins to the anchor's rect so that our margins apply to our relative positioning
                anchorRect.Left -= margins.Right;
                anchorRect.Top -= margins.Bottom;
                anchorRect.Size += margins.Size;
            } else {
                // HACK
                parentRect.Left += margins.Left;
                parentRect.Top += margins.Top;
                parentRect.Size -= margins.Size;
                anchorRect = parentRect;
            }

            var a = AlignmentTrial(ref context, in parentRect, in rect, margins, trialAnchorPoint, trialAlignmentPoint, anchorRect, out Vector2 mrapA);
            var isectA = GetIntersectionFactor(in anchorRect, in actualRect, a);
            // HACK: If the specified alignment causes the control to overlap its anchor, attempt to flip the alignment vertically to find it a better spot
            if (!allowOverlap && (isectA > 4f)) {
                trialAnchorPoint.Y = 1 - trialAnchorPoint.Y;
                trialAlignmentPoint.Y = 1 - trialAlignmentPoint.Y;
                var b = AlignmentTrial(ref context, in parentRect, in rect, margins, trialAnchorPoint, trialAlignmentPoint, anchorRect, out Vector2 mrapB);
                var isectB = GetIntersectionFactor(in anchorRect, in actualRect, b);
                if (isectB < isectA) {
                    MostRecentAlignedPosition = mrapB;
                    return SetPosition(b, updateDesiredPosition);
                }
            }
            MostRecentAlignedPosition = mrapA;
            return SetPosition(a, updateDesiredPosition);
        }

        private float GetIntersectionFactor (in RectF anchorRect, in RectF originalRect, Vector2 newPosition) {
            var rect = originalRect;
            rect.Position = newPosition;
            if (!anchorRect.Intersection(in rect, out RectF intersection))
                return 0f;
            return intersection.Size.Length();
        }

        private Vector2 AlignmentTrial (
            ref UIOperationContext context, in RectF parentRect, in RectF rect, 
            Margins margins, Vector2 trialAnchorPoint, Vector2 trialAlignmentPoint, 
            RectF anchorRect, out Vector2 mostRecentAlignedPosition
        ) {
            // We use the anchor's display rect for most calculations if it's transformed, but in this case we can't
            //  do the special logic based on the anchor's aligned position because it isn't transformed
            var evaluatedAnchorPosition = UseTransformedAnchor
                ? anchorRect.Position
                : ((Anchor as IAlignedControl)?.AlignedPosition);
            if (evaluatedAnchorPosition.HasValue) {
                var clampedAp = evaluatedAnchorPosition.Value;
                // HACK: The anchor may be hanging off the edges of the screen, so account for that when computing its real rectangle
                ClampToConstraintArea(ref context, ref clampedAp, in anchorRect);
                anchorRect.Position = clampedAp;
            }
            // We also need to clamp the final anchor rectangle to the screen when deciding where to place the control
            anchorRect.Intersection(in parentRect, out RectF clampedAnchorRect);
            // FIXME
            var anchorCenter = clampedAnchorRect.Position + (clampedAnchorRect.Size * trialAnchorPoint);
            var offset = (rect.Size * trialAlignmentPoint);
            var result = anchorCenter - offset - parentRect.Position;
            ClampToConstraintArea(ref context, ref result, in rect);
            mostRecentAlignedPosition = anchorCenter - offset;
            return result;
        }

        /// <summary>
        /// Adds 'aligned-(corner)' traits based on the control's position relative to its anchor,
        ///  so you can render notched corners or such
        /// </summary>
        public void AddDecorationTraits (ref DecorationSettings settings) {
            if (Anchor == null)
                return;
            RectF anchorRect = Anchor.GetRect(displayRect: UseTransformedAnchor), 
                myRect = Control.GetRect();
            anchorRect.SnapAndInset(out Vector2 anchorTl, out Vector2 anchorBr);
            myRect.SnapAndInset(out Vector2 myTl, out Vector2 myBr);
            const float bias = 4f;

            if (
                (myTl.X > anchorTl.X - bias) &&
                (myTl.Y > anchorTl.Y - bias)
            )
                settings.Traits.Add("aligned-tl");
            if (
                (myTl.X < anchorBr.X + bias) &&
                (myTl.Y > anchorTl.Y - bias)
            )
                settings.Traits.Add("aligned-tr");
            if (
                (myTl.X > anchorTl.X - bias) &&
                (myTl.Y < anchorBr.Y + bias)
            )
                settings.Traits.Add("aligned-bl");
            if (
                (myTl.X < anchorBr.X + bias) &&
                (myTl.Y < anchorBr.Y + bias)
            )
                settings.Traits.Add("aligned-br");
        }

        public void EnsureAligned (ref UIOperationContext context, ref bool relayoutRequested) {
            if (!Enabled)
                return;

            AlignmentPending = false;

            GetParentContentRect(out RectF parentRect);
            var rect = Control.GetRect(applyOffset: false);
            bool needRelayout = false;

            if (rect == default(RectF)) {
                IsInvalid = true;
                relayoutRequested = true;
                return;
            }

            if (Anchor != null) {
                if (Anchor is IAlignedControl iac)
                    iac.EnsureAligned(ref context, ref needRelayout);

                var anchorRect = Anchor.GetRect(displayRect: UseTransformedAnchor);
                if (anchorRect != _LastAnchorRect) {
                    _LastAnchorRect = anchorRect;
                    needRelayout = true;
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
                needRelayout = true;
            }
            _LastSize = rect.Size;
            _LastParentRect = parentRect;

            if (WasPositionSetByUser) {
                MostRecentAlignedPosition = null;

                if (DoUpdatePosition(
                    DesiredPosition ?? Control.Layout.FloatingPosition ?? Vector2.Zero, 
                    in parentRect, in rect, 
                    false, !context.UIContext.IsPerformingRelayout
                )) {
                    needRelayout = true;
                }

                var availableSpace = (parentRect.Size - rect.Size);
                if (ComputeNewAlignment)
                    ControlAlignmentPoint = ((Control.Layout.FloatingPosition ?? Vector2.Zero) - parentRect.Position) / availableSpace;
            } else if (((IsAnimating == null) || !IsAnimating()) && (!needRelayout || (Anchor != null))) {
                needRelayout |= Align(ref context, parentRect, rect, true);
            } else if ((IsLocked == null) || !IsLocked()) {
                needRelayout |= Align(ref context, parentRect, rect, false);
            } else {
                MostRecentAlignedPosition = null;
            }

            if (needRelayout && context.UIContext.IsPerformingRelayout)
                IsInvalid = true;
            else
                IsInvalid = false;

            relayoutRequested = needRelayout || relayoutRequested;
        }

        private bool DoUpdatePosition (
            Vector2 newPosition, in RectF parentRect, in RectF rect, 
            bool updateDesiredPosition, bool updateFloatingPosition
        ) {
            if (UpdatePosition != null)
                return UpdatePosition(newPosition, in parentRect, in rect, updateDesiredPosition);

            if (Control.Layout.FloatingPosition == newPosition)
                // FIXME
                return false;

            if (updateFloatingPosition)
                Control.Layout.FloatingPosition = newPosition;

            return true;
        }
    }
}
