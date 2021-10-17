using System;
using System.Collections.Generic;
using System.Linq;
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
    public class ControlAlignmentHelper<TControl>
        where TControl : Control
    {
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
                var actualRect = rect;
                var trialAnchorPoint = AnchorPoint;
                var trialAlignmentPoint = ControlAlignmentPoint;
                // FIXME: Adjust on appropriate sides
                rect.Size += margins.Size;
                var anchorRect = Anchor.GetRect();
                if (anchorRect == default(RectF))
                    return false;
                var a = AlignmentTrial(ref context, ref parentRect, ref rect, margins, trialAnchorPoint, trialAlignmentPoint, anchorRect, out Vector2 mrapA);
                var isectA = GetIntersectionFactor(ref anchorRect, ref actualRect, a);
                // HACK: If the specified alignment causes the control to overlap its anchor, attempt to flip the alignment vertically to find it a better spot
                if (!AllowOverlap && (isectA > 4f)) {
                    trialAnchorPoint.Y = 1 - trialAnchorPoint.Y;
                    trialAlignmentPoint.Y = 1 - trialAlignmentPoint.Y;
                    var b = AlignmentTrial(ref context, ref parentRect, ref rect, margins, trialAnchorPoint, trialAlignmentPoint, anchorRect, out Vector2 mrapB);
                    var isectB = GetIntersectionFactor(ref anchorRect, ref actualRect, b);
                    if (isectB < isectA) {
                        MostRecentAlignedPosition = mrapB;
                        return SetPosition(b, updateDesiredPosition);
                    }
                }
                MostRecentAlignedPosition = mrapA;
                return SetPosition(a, updateDesiredPosition);
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

        private float GetIntersectionFactor (ref RectF anchorRect, ref RectF originalRect, Vector2 newPosition) {
            var rect = originalRect;
            rect.Position = newPosition;
            if (!anchorRect.Intersection(ref rect, out RectF intersection))
                return 0f;
            return intersection.Size.Length();
        }

        private Vector2 AlignmentTrial (
            ref UIOperationContext context, ref RectF parentRect, ref RectF rect, 
            Margins margins, Vector2 trialAnchorPoint, Vector2 trialAlignmentPoint, 
            RectF anchorRect, out Vector2 mostRecentAlignedPosition
        ) {
            var evaluatedAnchorPosition = ((Anchor as IAlignedControl)?.AlignedPosition);
            if (evaluatedAnchorPosition.HasValue) {
                var clampedAp = evaluatedAnchorPosition.Value;
                // HACK: The anchor may be hanging off the edges of the screen, so account for that when computing its real rectangle
                ClampToConstraintArea(ref context, ref clampedAp, ref anchorRect);
                anchorRect.Position = clampedAp;
            }
            anchorRect.Left -= margins.Left;
            anchorRect.Top -= margins.Top;
            anchorRect.Size += margins.Size;
            // We also need to clamp the final anchor rectangle to the screen when deciding where to place the control
            anchorRect.Intersection(ref parentRect, out RectF clampedAnchorRect);
            // FIXME
            var anchorCenter = clampedAnchorRect.Position + (clampedAnchorRect.Size * trialAnchorPoint);
            var offset = (rect.Size * trialAlignmentPoint);
            var result = anchorCenter - offset - parentRect.Position;
            ClampToConstraintArea(ref context, ref result, ref rect);
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
            RectF anchorRect = Anchor.GetRect(), myRect = Control.GetRect();
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
            if (rect == default(RectF)) {
                relayoutRequested = true;
                return;
            }

            if (Anchor != null) {
                if (Anchor is IAlignedControl iac)
                    iac.EnsureAligned(ref context, ref relayoutRequested);

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
}
