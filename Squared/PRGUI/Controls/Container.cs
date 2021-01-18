using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Squared.Game;
using Squared.PRGUI.Decorations;
using Squared.PRGUI.Imperative;
using Squared.PRGUI.Layout;
using Squared.Render.Convenience;
using Squared.Util;

namespace Squared.PRGUI.Controls {
    public class Container : ContainerBase, IScrollableControl, IPostLayoutListener, IPartiallyIntangibleControl {
        /// <summary>
        /// If set, children will only be rendered within the volume of this container
        /// </summary>
        new public bool ClipChildren {
            get => base.ClipChildren;
            set => base.ClipChildren = value;
        }
        public bool Scrollable { get; set; } = false;
        public bool ShowVerticalScrollbar = true, ShowHorizontalScrollbar = true;

        private Vector2 _ScrollOffset;

        protected float MostRecentHeaderHeight = 0;
        protected RectF MostRecentTitleBox;
        protected RectF? MostRecentFullSize = null;
        protected Vector2 MinScrollOffset;
        protected Vector2? MaxScrollOffset;
        /// <summary>
        /// A value added to the actual scroll offset when computing the display position
        ///  of child controls
        /// </summary>
        protected Vector2 VirtualScrollOffset;
        /// <summary>
        /// The total scroll region is increased to this virtual region if necessary in
        ///  order to allow scrolling to reveal hidden (culled) content
        /// </summary>
        protected Vector2 VirtualScrollRegion;

        bool IScrollableControl.AllowDragToScroll => true;
        Vector2? IScrollableControl.MinScrollOffset => MinScrollOffset;
        Vector2? IScrollableControl.MaxScrollOffset => MaxScrollOffset;

        protected ScrollbarState HScrollbar, VScrollbar;
        protected bool HasContentBounds, CanScrollVertically;
        protected RectF ContentBounds;

        protected float ScrollSpeedMultiplier = 1;

        public Container () 
            : base () {
            AcceptsMouseInput = true;

            HScrollbar = new ScrollbarState {
                DragInitialMousePosition = null,
                Horizontal = true
            };
            VScrollbar = new ScrollbarState {
                DragInitialMousePosition = null,
                Horizontal = false
            };
        }

        protected Vector2? DesiredScrollOffset;

        public bool TrySetScrollOffset (Vector2 value, bool forUserInput) {
            if (!Scrollable)
                value = Vector2.Zero;
            if (forUserInput && (value != DesiredScrollOffset))
                DesiredScrollOffset = value;
            if (!CanScrollVertically)
                value.Y = 0;
            value = ConstrainNewScrollOffset(value);
            if (value == _ScrollOffset)
                return false;

            // Console.WriteLine("ScrollOffset {0} -> {1}", _ScrollOffset, value);
            _ScrollOffset = value;
            OnDisplayOffsetChanged();
            return true;
        }

        public Vector2 ScrollOffset {
            get {
                return _ScrollOffset;
            }
            set {
                TrySetScrollOffset(value, false);
            }
        }

        private Vector2 ConstrainNewScrollOffset (Vector2 so) {
            if (!Scrollable)
                return so;

            if (MaxScrollOffset.HasValue) {
                so.X = Arithmetic.Clamp(so.X, MinScrollOffset.X, MaxScrollOffset.Value.X);
                so.Y = Arithmetic.Clamp(so.Y, MinScrollOffset.Y, MaxScrollOffset.Value.Y);
            } else {
                so.X = Math.Max(MinScrollOffset.X, so.X);
                so.Y = Math.Max(MinScrollOffset.Y, so.Y);
            }
            return so;
        }

        protected bool OnScroll (float delta) {
            if (!Scrollable)
                return false;
            if (!ShowVerticalScrollbar || !CanScrollVertically)
                return false;
            var so = ScrollOffset;
            so.Y = so.Y - delta * ScrollSpeedMultiplier;
            so = ConstrainNewScrollOffset(so);
            if (so != ScrollOffset) {
                TrySetScrollOffset(so, true);
                return true;
            } else {
                return false;
            }
        }

        protected override bool OnEvent<T> (string name, T args) {
            if (name == UIEvents.Scroll)
                return OnScroll(Convert.ToSingle(args));
            else if (args is MouseEventArgs)
                return ProcessMouseEventForScrollbar(name, (MouseEventArgs)(object)args);

            return false;
        }

        private bool HitTestScrollbars (Vector2 position) {
            if (!PrepareScrollbarsForMethodCall(out IWidgetDecorator<ScrollbarState> scroll, out DecorationSettings settings))
                return false;

            return scroll.HitTest(settings, ref HScrollbar, position) ||
                scroll.HitTest(settings, ref VScrollbar, position);
        }

        private bool PrepareScrollbarsForMethodCall (out IWidgetDecorator<ScrollbarState> scroll, out DecorationSettings settings) {
            scroll = Context.Decorations?.Scrollbar;
            settings = default(DecorationSettings);
            if (scroll == null)
                return false;

            if (!ShowHorizontalScrollbar && !ShowVerticalScrollbar)
                return false;

            var box = GetRect();
            var contentBox = GetRect(contentRect: true);
            settings = MakeDecorationSettings(ref box, ref contentBox, default(ControlStates));

            // Ensure the scrollbar state is up-to-date if someone modified our offset
            HScrollbar.Position = ScrollOffset.X;
            VScrollbar.Position = ScrollOffset.Y;

            return true;
        }

        protected bool ProcessMouseEventForScrollbar (string name, MouseEventArgs args) {
            if (!PrepareScrollbarsForMethodCall(out IWidgetDecorator<ScrollbarState> scroll, out DecorationSettings settings))
                return false;

            var hScrollProcessed = ShowHorizontalScrollbar && scroll.OnMouseEvent(settings, ref HScrollbar, name, args);
            var vScrollProcessed = ShowVerticalScrollbar && scroll.OnMouseEvent(settings, ref VScrollbar, name, args);

            if (hScrollProcessed || vScrollProcessed) {
                var newOffset = new Vector2(HScrollbar.Position, VScrollbar.Position);
                TrySetScrollOffset(newOffset, true);
                HScrollbar.Position = ScrollOffset.X;
                VScrollbar.Position = ScrollOffset.Y;
                return true;
            }
            return false;
        }

        protected override void OnDisplayOffsetChanged () {
            AbsoluteDisplayOffsetOfChildren = AbsoluteDisplayOffset - (_ScrollOffset + VirtualScrollOffset).Floor();

            foreach (var child in _Children)
                child.AbsoluteDisplayOffset = AbsoluteDisplayOffsetOfChildren;
        }
        
        protected override ControlKey OnGenerateLayoutTree (UIOperationContext context, ControlKey parent, ControlKey? existingKey) {
            HasContentBounds = false;
            return base.OnGenerateLayoutTree(context, parent, existingKey);
        }

        protected override IDecorator GetDefaultDecorator (IDecorationProvider provider) {
            if (LayoutFlags.IsFlagged(ControlFlags.Layout_Floating))
                return provider?.FloatingContainer ?? provider?.Container;
            else
                return provider?.Container;
        }

        protected override void OnRasterize (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings, IDecorator decorations) {
            base.OnRasterize(context, ref renderer, settings, decorations);

            if (Scrollable && (settings.Box.Height > MostRecentHeaderHeight)) {
                settings.Box.Top += MostRecentHeaderHeight;
                settings.ContentBox.Top += MostRecentHeaderHeight;
                settings.Box.Height -= MostRecentHeaderHeight;
                settings.ContentBox.Height -= MostRecentHeaderHeight;

                var scrollbar = context.DecorationProvider?.Scrollbar;
                if (ShowHorizontalScrollbar)
                    scrollbar?.Rasterize(context, ref renderer, settings, ref HScrollbar);
                if (ShowVerticalScrollbar)
                    scrollbar?.Rasterize(context, ref renderer, settings, ref VScrollbar);
            }
        }

        protected override void ComputePadding (UIOperationContext context, IDecorator decorations, out Margins result) {
            base.ComputePadding(context, decorations, out result);
            if (!Scrollable)
                return;
            var scrollbar = context.DecorationProvider?.Scrollbar;
            if (scrollbar == null)
                return;
            // FIXME: Automate this?
            var scale = context.DecorationProvider.SizeScaleRatio;
            if (ShowVerticalScrollbar)
                result.Right += scrollbar.MinimumSize.X * scale.X;
            if (ShowHorizontalScrollbar)
                result.Bottom += scrollbar.MinimumSize.Y * scale.Y;
        }

        protected bool GetContentBounds (UIContext context, out RectF contentBounds) {
            if (LayoutKey.IsInvalid) {
                contentBounds = default(RectF);
                return false;
            }

            var ok = context.Layout.TryMeasureContent(LayoutKey, out contentBounds);
            if (ok)
                ContentBounds = contentBounds;
            HasContentBounds = ok;
            return ok;
        }

        protected override void ApplyClipMargins (UIOperationContext context, ref RectF box) {
            // Scrollbars are already part of computed padding, so just clip out the header (if any)
            box.Top += MostRecentHeaderHeight;
            box.Height -= MostRecentHeaderHeight;
        }

        protected override bool OnHitTest (RectF box, Vector2 position, bool acceptsMouseInputOnly, bool acceptsFocusOnly, ref Control result) {
            if (!HitTestShell(box, position, false, false, ref result))
                return false;

            if (!HitTestInterior(box, position, acceptsMouseInputOnly, acceptsFocusOnly, ref result))
                return false;

            if (MostRecentTitleBox.Contains(position)) {
                result = this;
                return true;
            }

            return HitTestChildren(position, acceptsMouseInputOnly, acceptsFocusOnly, ref result);
        }

        protected virtual void OnLayoutComplete (UIOperationContext context, ref bool relayoutRequested) {
            // FIXME: This should be done somewhere else
            if (Scrollable) {
                var contentBox = context.Layout.GetContentRect(LayoutKey);
                var scrollbar = context.DecorationProvider?.Scrollbar;
                float viewportWidth = contentBox.Width,
                    viewportHeight = contentBox.Height;

                GetContentBounds(context.UIContext, out RectF contentBounds);

                contentBounds.Width = Math.Max(contentBounds.Width, VirtualScrollRegion.X);
                contentBounds.Height = Math.Max(contentBounds.Height, VirtualScrollRegion.Y);

                if (HasContentBounds) {
                    float maxScrollX = ContentBounds.Width - viewportWidth, maxScrollY = ContentBounds.Height - viewportHeight;
                    maxScrollX = Math.Max(Math.Max(0, maxScrollX), VirtualScrollRegion.X);
                    maxScrollY = Math.Max(Math.Max(0, maxScrollY), VirtualScrollRegion.Y);

                    // HACK: Suppress flickering during size transitions
                    if (maxScrollX <= 1)
                        maxScrollX = 0;
                    if (maxScrollY <= 1)
                        maxScrollY = 0;

                    MinScrollOffset = Vector2.Zero;
                    MaxScrollOffset = new Vector2(maxScrollX, maxScrollY);
                    if (DesiredScrollOffset.HasValue)
                        TrySetScrollOffset(DesiredScrollOffset.Value, false);

                    CanScrollVertically = maxScrollY > 0;
                }

                HScrollbar.ContentSize = contentBounds.Width;
                HScrollbar.ViewportSize = contentBox.Width;
                HScrollbar.Position = ScrollOffset.X;
                VScrollbar.ContentSize = CanScrollVertically ? contentBounds.Height : contentBox.Height;
                VScrollbar.ViewportSize = contentBox.Height;
                VScrollbar.Position = ScrollOffset.Y;

                HScrollbar.HasCounterpart = VScrollbar.HasCounterpart = (ShowHorizontalScrollbar && ShowVerticalScrollbar);
            } else {
                TrySetScrollOffset(Vector2.Zero, false);
            }
        }

        void IPostLayoutListener.OnLayoutComplete (UIOperationContext context, ref bool relayoutRequested) {
            OnLayoutComplete(context, ref relayoutRequested);
        }

        bool IPartiallyIntangibleControl.IsIntangibleAtPosition (Vector2 position) {
            // HACK: Top-level containers should never be partially intangible,
            //  otherwise weird things happen when they cover another control
            if (!HasParent)
                return false;

            if (MostRecentTitleBox.Contains(position))
                return false;
            else if (HitTestScrollbars(position))
                return false;

            return true;
        }
    }
}
