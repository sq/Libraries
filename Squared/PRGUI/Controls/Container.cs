using System;
using System.Collections;
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
    public class Container 
        : ContainerBase, IScrollableControl, IPostLayoutListener, 
            IPartiallyIntangibleControl, IEnumerable<Control> {
        new public ControlCollection Children {
            get => base.Children;
        }

        protected override Vector2 AbsoluteDisplayOffsetOfChildren => 
            AbsoluteDisplayOffset - (ScrollOffset + VirtualScrollOffset).Floor();

        /// <summary>
        /// If set, children will only be rendered within the volume of this container
        /// </summary>
        new public bool ClipChildren {
            get => base.ClipChildren;
            set => base.ClipChildren = value;
        }
        public bool Scrollable { get; set; } = false;
        public bool? ShowVerticalScrollbar = null, 
            ShowHorizontalScrollbar = null;

        new public Control DefaultFocusTarget {
            get => base.DefaultFocusTarget;
            set => base.DefaultFocusTarget = value;
        }

        protected bool ShouldShowHorizontalScrollbar =>
            ShowHorizontalScrollbar ?? (HScrollbar.ContentSize > HScrollbar.ViewportSize);
        protected bool ShouldShowVerticalScrollbar =>
            ShowVerticalScrollbar ?? (VScrollbar.ContentSize > VScrollbar.ViewportSize);

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
                return ConstrainNewScrollOffset(_ScrollOffset);
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
            if ((ShowVerticalScrollbar == false) || !CanScrollVertically)
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
            else if (args is MouseEventArgs ma)
                return ProcessMouseEventForScrollbar(name, ma);

            return false;
        }

        private bool HitTestScrollbars (Vector2 position) {
            if (!PrepareScrollbarsForMethodCall(out IWidgetDecorator<ScrollbarState> scroll, out DecorationSettings settings))
                return false;

            var result = scroll.HitTest(settings, ref HScrollbar, position) ||
                scroll.HitTest(settings, ref VScrollbar, position);
            if (result)
                ;
            return result;
        }

        private bool PrepareScrollbarsForMethodCall (out IWidgetDecorator<ScrollbarState> scroll, out DecorationSettings settings) {
            scroll = Context.Decorations?.Scrollbar;
            settings = default(DecorationSettings);
            if (scroll == null)
                return false;

            if ((ShowHorizontalScrollbar == false) && (ShowVerticalScrollbar == false))
                return false;

            GetRects(out RectF box, out RectF contentBox);
            settings = MakeDecorationSettings(ref box, ref contentBox, default(ControlStates), false);

            // Ensure the scrollbar state is up-to-date if someone modified our offset
            HScrollbar.Position = ScrollOffset.X;
            VScrollbar.Position = ScrollOffset.Y;

            return true;
        }

        protected bool ProcessMouseEventForScrollbar (string name, MouseEventArgs args) {
            if (!PrepareScrollbarsForMethodCall(out IWidgetDecorator<ScrollbarState> scroll, out DecorationSettings settings))
                return false;

            var hScrollProcessed = ShouldShowHorizontalScrollbar && scroll.OnMouseEvent(settings, ref HScrollbar, name, args);
            var vScrollProcessed = ShouldShowVerticalScrollbar && scroll.OnMouseEvent(settings, ref VScrollbar, name, args);

            if (hScrollProcessed || vScrollProcessed) {
                var newOffset = new Vector2(HScrollbar.Position, VScrollbar.Position);
                TrySetScrollOffset(newOffset, true);
                HScrollbar.Position = ScrollOffset.X;
                VScrollbar.Position = ScrollOffset.Y;
                return true;
            }
            return false;
        }
        
        protected override ControlKey OnGenerateLayoutTree (ref UIOperationContext context, ControlKey parent, ControlKey? existingKey) {
            HasContentBounds = false;
            return base.OnGenerateLayoutTree(ref context, parent, existingKey);
        }

        protected override IDecorator GetDefaultDecorator (IDecorationProvider provider) {
            if (LayoutFlags.IsFlagged(ControlFlags.Layout_Floating))
                return provider?.FloatingContainer ?? provider?.Container;
            else
                return provider?.Container;
        }

        protected override void OnRasterize (ref UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings, IDecorator decorations) {
            var scrollingActive = Scrollable && (settings.Box.Height > MostRecentHeaderHeight);

            base.OnRasterize(ref context, ref renderer, settings, decorations);

            if (scrollingActive) {
                settings.Box.Top += MostRecentHeaderHeight;
                settings.ContentBox.Top += MostRecentHeaderHeight;
                settings.Box.Height -= MostRecentHeaderHeight;
                settings.ContentBox.Height -= MostRecentHeaderHeight;
                var scrollbar = context.DecorationProvider?.Scrollbar;
                if (ShouldShowHorizontalScrollbar)
                    scrollbar?.Rasterize(ref context, ref renderer, settings, ref HScrollbar);
                if (ShouldShowVerticalScrollbar)
                    scrollbar?.Rasterize(ref context, ref renderer, settings, ref VScrollbar);
            }
        }

        protected override void ApplyClipMargins (ref UIOperationContext context, ref RectF box) {
            base.ApplyClipMargins(ref context, ref box);

            // If we have a title bar, clip it out
            box.Top += MostRecentHeaderHeight;
            box.Height -= MostRecentHeaderHeight;

            if (!Scrollable)
                return;

            var decorationProvider = context.DecorationProvider;
            var scrollbar = decorationProvider?.Scrollbar;
            if (scrollbar == null)
                return;

            // Also push the clip region inward so that content doesn't get drawn behind the scrollbars
            var sizeScale = decorationProvider?.SizeScaleRatio ?? Vector2.One;
            if (ShouldShowHorizontalScrollbar)
                box.Height -= scrollbar.MinimumSize.Y * sizeScale.Y;
            if (ShouldShowVerticalScrollbar)
                box.Width -= scrollbar.MinimumSize.X * sizeScale.X;
        }

        protected override void ComputeUnscaledPadding (ref UIOperationContext context, IDecorator decorations, out Margins result) {
            base.ComputeUnscaledPadding(ref context, decorations, out result);
            if (!Scrollable)
                return;
            var decorationProvider = context.DecorationProvider;
            var scrollbar = decorationProvider?.Scrollbar;
            if (scrollbar == null)
                return;
            var sizeScale = decorationProvider?.SizeScaleRatio ?? Vector2.One;
            // FIXME: Conditionally adjust padding when visibility is auto? Possibly introduces jitter
            if (ShouldShowVerticalScrollbar)
                result.Right += scrollbar.MinimumSize.X * sizeScale.X;
            if (ShouldShowHorizontalScrollbar)
                result.Bottom += scrollbar.MinimumSize.Y * sizeScale.Y;
        }

        protected bool GetContentBounds (UIContext context, out Vector2 contentBounds) {
            contentBounds = default(Vector2);
            // FIXME: Only check for missing key instead?
            if (IsLayoutInvalid)
                return false;

            var contentRect = default(RectF);
            bool ok = false;
            if (ColumnCount > 1) {
                for (int i = 0; i < ColumnCount; i++) {
                    var ckey = ColumnKeys[i];
                    RectF columnBounds;
                    if (UIContext.UseNewEngine)
                        context.Engine.TryMeasureContent(ckey, out columnBounds);
                    else
                        context.Layout.TryMeasureContent(ckey, out columnBounds);
                    if (i == 0) {
                        contentRect = columnBounds;
                    } else {
                        contentRect.Width += columnBounds.Width;
                        contentRect.Height = Math.Max(contentRect.Height, columnBounds.Height);
                    }
                    ok = true;
                }
            } else {
                if (UIContext.UseNewEngine)
                    ok = context.Engine.TryMeasureContent(LayoutKey, out contentRect);
                else
                    ok = context.Layout.TryMeasureContent(LayoutKey, out contentRect);
            }
            if (ok)
                contentBounds = contentRect.Size;
            HasContentBounds = ok;
            return ok;
        }

        protected override bool OnHitTest (RectF box, Vector2 position, bool acceptsMouseInputOnly, bool acceptsFocusOnly, bool rejectIntangible, ref Control result) {
            if (!HitTestShell(box, position, false, false, rejectIntangible, ref result))
                return false;

            bool success = !DisableSelfHitTests && HitTestInterior(box, position, acceptsMouseInputOnly, acceptsFocusOnly, ref result);
            if (MostRecentTitleBox.Contains(position))
                return success;

            success |= HitTestChildren(position, acceptsMouseInputOnly, acceptsFocusOnly, rejectIntangible, ref result);
            return success;
        }

        protected virtual void OnLayoutComplete (ref UIOperationContext context, ref bool relayoutRequested) {
            // FIXME: This should be done somewhere else
            if (Scrollable) {
                context.Layout.TryGetContentRect(LayoutKey, out RectF contentBox);
                var scrollbar = context.DecorationProvider?.Scrollbar;
                float viewportWidth = contentBox.Width,
                    viewportHeight = contentBox.Height;

                GetContentBounds(context.UIContext, out Vector2 contentBounds);

                float maxScrollX = 0, maxScrollY = 0, contentSizeX = 0, contentSizeY = 0;
                if (HasContentBounds) {
                    if (VirtualScrollRegion.X > 0)
                        contentSizeX = VirtualScrollRegion.X;
                    else
                        contentSizeX = contentBounds.X;
                    if (VirtualScrollRegion.Y > 0)
                        contentSizeY = VirtualScrollRegion.Y;
                    else
                        contentSizeY = contentBounds.Y;
                    maxScrollX = contentSizeX - viewportWidth;
                    maxScrollY = contentSizeY - viewportHeight;
                    maxScrollX = Math.Max(0, maxScrollX);
                    maxScrollY = Math.Max(0, maxScrollY);

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

                HScrollbar.ContentSize = contentSizeX;
                HScrollbar.ViewportSize = viewportWidth;
                HScrollbar.Position = ScrollOffset.X;
                VScrollbar.ContentSize = CanScrollVertically ? contentSizeY : contentBox.Height;
                VScrollbar.ViewportSize = viewportHeight;
                VScrollbar.Position = ScrollOffset.Y;

                HScrollbar.HasCounterpart = VScrollbar.HasCounterpart = (ShouldShowHorizontalScrollbar && ShouldShowVerticalScrollbar);
            } else {
                TrySetScrollOffset(Vector2.Zero, false);
            }
        }

        void IPostLayoutListener.OnLayoutComplete (ref UIOperationContext context, ref bool relayoutRequested) {
            OnLayoutComplete(ref context, ref relayoutRequested);
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

        // For simple initializers
        public void Add (Control control) {
            Children.Add(control);
        }

        public IEnumerator<Control> GetEnumerator () {
            return ((IEnumerable<Control>)Children).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator () {
            return ((IEnumerable<Control>)Children).GetEnumerator();
        }
    }
}
