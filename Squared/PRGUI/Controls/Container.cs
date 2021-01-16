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
    public class Container : Control, IControlContainer, IScrollableControl, IPostLayoutListener, IPartiallyIntangibleControl {
        public ControlCollection Children { get; private set; }

        /// <summary>
        /// If set, children will only be rendered within the volume of this container
        /// </summary>
        public bool ClipChildren { get; set; } = false;
        public bool Scrollable { get; set; } = false;
        public bool ShowVerticalScrollbar = true, ShowHorizontalScrollbar = true;

        private Vector2 _ScrollOffset;
        protected Vector2 AbsoluteDisplayOffsetOfChildren;

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

        public ControlFlags ContainerFlags { get; set; } =
            ControlFlags.Container_Align_Start | ControlFlags.Container_Row | 
            ControlFlags.Container_Wrap;

        protected ScrollbarState HScrollbar, VScrollbar;
        protected bool HasContentBounds, CanScrollVertically;
        protected RectF ContentBounds;

        protected float ScrollSpeedMultiplier = 1;

        internal ContainerBuilder DynamicBuilder;
        internal ContainerContentsDelegate _DynamicContents;
        /// <summary>
        /// If set, every update this delegate will be invoked to reconstruct the container's children
        /// </summary>        
        public ContainerContentsDelegate DynamicContents {
            get => _DynamicContents;
            set {
                if (_DynamicContents == value)
                    return;
                _DynamicContents = value;
                DynamicContentIsInvalid = true;
            }
        }
        /// <summary>
        /// If true, dynamic contents will only be updated when this container is invalidated
        /// </summary>
        public bool CacheDynamicContent;

        protected bool DynamicContentIsInvalid = true;
        protected bool FreezeDynamicContent = false;
        protected bool SuppressChildLayout = false;

        public Container () 
            : base () {
            Children = new ControlCollection(this);
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

        public void InvalidateDynamicContent () {
            DynamicContentIsInvalid = true;
        }

        internal override void InvalidateLayout () {
            base.InvalidateLayout();
            foreach (var ch in Children)
                ch.InvalidateLayout();
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

            foreach (var child in Children)
                child.AbsoluteDisplayOffset = AbsoluteDisplayOffsetOfChildren;
        }

        protected void GenerateDynamicContent (bool force) {
            if (DynamicContents == null)
                return;

            if ((FreezeDynamicContent || CacheDynamicContent) && !force)
                return;

            DynamicContentIsInvalid = false;
            if (DynamicBuilder.Container != this)
                DynamicBuilder = new ContainerBuilder(this);
            DynamicBuilder.Reset();
            DynamicContents(ref DynamicBuilder);
            DynamicBuilder.Finish();
        }
        
        protected override ControlKey OnGenerateLayoutTree (UIOperationContext context, ControlKey parent, ControlKey? existingKey) {
            HasContentBounds = false;
            var result = base.OnGenerateLayoutTree(context, parent, existingKey);
            if (result.IsInvalid) {
                foreach (var item in Children)
                    item.InvalidateLayout();

                return result;
            }

            context.Layout.SetContainerFlags(result, ContainerFlags);

            if (SuppressChildLayout) {
                // FIXME: We need to also lock our minimum width in this case
                // HACK
                foreach (var item in Children)
                    item.InvalidateLayout();

                return result;
            } else {
                GenerateDynamicContent(false || DynamicContentIsInvalid);

                foreach (var item in Children) {
                    item.AbsoluteDisplayOffset = AbsoluteDisplayOffsetOfChildren;

                    // If we're performing layout again on an existing layout item, attempt to do the same
                    //  for our children
                    var childExistingKey = (ControlKey?)null;
                    if ((existingKey.HasValue) && !item.LayoutKey.IsInvalid)
                        childExistingKey = item.LayoutKey;

                    item.GenerateLayoutTree(ref context, result, childExistingKey);
                }
                return result;
            }
        }

        protected override IDecorator GetDefaultDecorator (IDecorationProvider provider) {
            if (LayoutFlags.IsFlagged(ControlFlags.Layout_Floating))
                return provider?.FloatingContainer ?? provider?.Container;
            else
                return provider?.Container;
        }

        protected override bool ShouldClipContent => ClipChildren && (Children.Count > 0);
        // FIXME: Always true?
        protected override bool HasChildren => (Children.Count > 0);

        protected virtual bool HideChildren => false;

        protected override void OnRasterizeChildren (UIOperationContext context, ref RasterizePassSet passSet, DecorationSettings settings) {
            if (HideChildren)
                return;

            // FIXME
            int layer1 = passSet.Below.Layer,
                layer2 = passSet.Content.Layer,
                layer3 = passSet.Above.Layer,
                maxLayer1 = layer1,
                maxLayer2 = layer2,
                maxLayer3 = layer3;

            RasterizeChildrenInOrder(
                ref context, ref passSet, 
                layer1, layer2, layer3,
                ref maxLayer1, ref maxLayer2, ref maxLayer3
            );

            passSet.Below.Layer = maxLayer1;
            passSet.Content.Layer = maxLayer2;
            passSet.Above.Layer = maxLayer3;
        }

        protected virtual void RasterizeChildrenInOrder (
            ref UIOperationContext context, ref RasterizePassSet passSet, 
            int layer1, int layer2, int layer3, 
            ref int maxLayer1, ref int maxLayer2, ref int maxLayer3
        ) {
            var sequence = Children.InDisplayOrder(Context.FrameIndex);
            foreach (var item in sequence)
                RasterizeChild(ref context, item, ref passSet, layer1, layer2, layer3, ref maxLayer1, ref maxLayer2, ref maxLayer3);
        }

        /// <summary>
        /// Intelligently rasterize children starting from an automatically selected
        ///  midpoint, instead of rasterizing all of our children.
        /// May draw an unnecessarily large number of children in some cases, but will
        ///  typically only draw slightly more than the number of children currently
        ///  in view.
        /// </summary>
        /// <returns>The number of child controls rasterization was attempted for</returns>
        public static int RasterizeChildrenFromCenter (
            ref UIOperationContext context, ref RasterizePassSet passSet, 
            RectF box, ControlCollection children, Control selectedItem,
            int layer1, int layer2, int layer3, 
            ref int maxLayer1, ref int maxLayer2, ref int maxLayer3,
            ref int lastOffset1, ref int lastOffset2
        ) {
            if (children.Count <= 0)
                return 0;

            RectF childRect =
                (selectedItem != null)
                    ? selectedItem.GetRect()
                    : default(RectF);

            int count = children.Count, 
                selectedIndex = children.IndexOf(selectedItem), 
                startOffset = (
                    (selectedIndex >= 0) &&
                    box.Intersects(ref childRect)
                )
                    // If we have a selected item and the selected item is visible, begin painting
                    //  from its position
                    ? selectedIndex
                    : (
                        (
                            (lastOffset1 >= 0) &&
                            (lastOffset2 >= 0) &&
                            (lastOffset2 < count)
                        )
                            // Otherwise, start painting from the midpoint of the last paint region
                            ? (lastOffset1 + lastOffset2) / 2
                            // And if we don't have a last paint region, start from our midpoint
                            : count / 2
                    );
            bool hasRenderedAny = false;

            int itemsAttempted = 0;
            for (int i = startOffset, j = startOffset; (i >= 0) || (j < count); i--, j++) {
                if (i >= 0) {
                    itemsAttempted++;
                    // Stop searching upward once an item fails to render
                    var item1 = children[i];
                    var ok = RasterizeChild(
                        ref context, item1, ref passSet,
                        layer1, layer2, layer3,
                        ref maxLayer1, ref maxLayer2, ref maxLayer3
                    );
                    if (item1.IsTransparent) {
                        ;
                    } else if (!ok && hasRenderedAny) {
                        lastOffset1 = i;
                        i = -1;
                    } else if (ok) {
                        hasRenderedAny = true;
                    }
                }

                if (j < count) {
                    itemsAttempted++;
                    var item2 = children[j];
                    var ok = RasterizeChild(
                        ref context, item2, ref passSet,
                        layer1, layer2, layer3,
                        ref maxLayer1, ref maxLayer2, ref maxLayer3
                    );
                    if (item2.IsTransparent) {
                        ;
                    } else if (!ok && hasRenderedAny) {
                        lastOffset2 = j;
                        j = count;
                    } else if (ok) {
                        hasRenderedAny = true;
                    }
                }
            }

            return itemsAttempted;
        }

        /// <summary>
        /// Rasterizes a child control and updates the pass layer data
        /// </summary>
        /// <returns>Whether the child was successfully rasterized</returns>
        public static bool RasterizeChild (
            ref UIOperationContext context, Control item, ref RasterizePassSet passSet, 
            int layer1, int layer2, int layer3, ref int maxLayer1, 
            ref int maxLayer2, ref int maxLayer3
        ) {
            passSet.Below.Layer = layer1;
            passSet.Content.Layer = layer2;
            passSet.Above.Layer = layer3;

            var result = item.Rasterize(ref context, ref passSet);

            maxLayer1 = Math.Max(maxLayer1, passSet.Below.Layer);
            maxLayer2 = Math.Max(maxLayer2, passSet.Content.Layer);
            maxLayer3 = Math.Max(maxLayer3, passSet.Above.Layer);

            return result;
        }

        protected override void OnVisibilityChange (bool newValue) {
            base.OnVisibilityChange(newValue);
            DynamicContentIsInvalid = true;

            if (newValue)
                return;

            ReleaseChildFocus();
        }

        private void ReleaseChildFocus () {
            if (IsEqualOrAncestor(Context?.Focused, this))
                Context.NotifyControlBecomingInvalidFocusTarget(Context.Focused, false);
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
            if (!base.OnHitTest(box, position, false, false, ref result))
                return false;

            var ipic = this as IPartiallyIntangibleControl;
            bool success = (AcceptsMouseInput && !ipic.IsIntangibleAtPosition(position)) || !acceptsMouseInputOnly;
            // Don't perform child hit-tests if the mouse is over the header
            if (MostRecentTitleBox.Contains(position))
                return success;

            // FIXME: Should we only perform the hit test if the position is within our boundaries?
            // This doesn't produce the right outcome when a container's computed size is zero
            var sorted = Children.InDisplayOrder(Context.FrameIndex);
            for (int i = sorted.Count - 1; i >= 0; i--) {
                var item = sorted[i];
                var newResult = item.HitTest(position, acceptsMouseInputOnly, acceptsFocusOnly);
                if (newResult != null) {
                    result = newResult;
                    success = true;
                }
            }

            return success;
        }

        public T Child<T> (Func<T, bool> predicate)
            where T : Control {

            foreach (var child in Children) {
                if (!(child is T))
                    continue;
                var t = (T)child;
                if (predicate(t))
                    return t;
            }

            return null;
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

        protected virtual void OnDescendantReceivedFocus (Control control, bool isUserInitiated) {
        }

        void IControlContainer.DescendantReceivedFocus (Control descendant, bool isUserInitiated) {
            OnDescendantReceivedFocus(descendant, isUserInitiated);
        }

        void IPostLayoutListener.OnLayoutComplete (UIOperationContext context, ref bool relayoutRequested) {
            OnLayoutComplete(context, ref relayoutRequested);
        }

        bool IPartiallyIntangibleControl.IsIntangibleAtPosition (Vector2 position) {
            // HACK: Top-level containers should never be partially intangible,
            //  otherwise weird things happen when they cover another control
            if (!HasParent)
                return false;

            var rect = GetRect();

            if (MostRecentTitleBox.Contains(position))
                return false;
            else if (HitTestScrollbars(position))
                return false;

            return true;
        }
    }
}
