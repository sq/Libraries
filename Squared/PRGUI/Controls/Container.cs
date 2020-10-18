using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Squared.PRGUI.Decorations;
using Squared.PRGUI.Layout;
using Squared.Render.Convenience;
using Squared.Util;

namespace Squared.PRGUI.Controls {
    public class Container : Control, IControlContainer, IScrollableControl, IPostLayoutListener {
        public ControlCollection Children { get; private set; }

        /// <summary>
        /// If set, children will only be rendered within the volume of this container
        /// </summary>
        public bool ClipChildren = false;

        public bool Scrollable = false;
        public bool ShowVerticalScrollbar = true, ShowHorizontalScrollbar = true;

        private Vector2 _ScrollOffset;
        protected Vector2 AbsoluteDisplayOffsetOfChildren;

        protected Vector2 MinScrollOffset;
        protected Vector2? MaxScrollOffset;

        bool IScrollableControl.AllowDragToScroll => true;
        Vector2? IScrollableControl.MinScrollOffset => MinScrollOffset;
        Vector2? IScrollableControl.MaxScrollOffset => MaxScrollOffset;

        public ControlFlags ContainerFlags = ControlFlags.Container_Align_Start | ControlFlags.Container_Row | ControlFlags.Container_Wrap;

        protected ScrollbarState HScrollbar, VScrollbar;
        protected bool HasContentBounds, CanScrollVertically;
        protected RectF ContentBounds;

        public Container () 
            : base () {
            Children = new ControlCollection(this);
            AcceptsMouseInput = true;

            HScrollbar = new ScrollbarState {
                DragInitialPosition = null,
                Horizontal = true
            };
            VScrollbar = new ScrollbarState {
                DragInitialPosition = null,
                Horizontal = false
            };
        }

        public Vector2 ScrollOffset {
            get {
                return _ScrollOffset;
            }
            set {
                if (!CanScrollVertically)
                    value.Y = 0;
                if (!Scrollable)
                    value = Vector2.Zero;
                if (value == _ScrollOffset)
                    return;

                _ScrollOffset = value;
                OnDisplayOffsetChanged();
            }
        }

        // HACK used to properly discard scroll events when at a scroll edge
        private bool CanScrollUp, CanScrollDown;

        protected bool OnScroll (float delta) {
            if (!Scrollable)
                return false;
            if (!ShowVerticalScrollbar || !CanScrollVertically)
                return false;
            var so = ScrollOffset;
            so.Y = so.Y - delta;
            if (MaxScrollOffset.HasValue) {
                so.X = Arithmetic.Clamp(so.X, 0, MaxScrollOffset.Value.X);
                so.Y = Arithmetic.Clamp(so.Y, 0, MaxScrollOffset.Value.Y);
            }
            if (so != ScrollOffset) {
                ScrollOffset = so;
                return true;
            } else {
                return false;
            }
        }

        protected override bool OnEvent<T> (string name, T args) {
            if (name == UIEvents.Scroll)
                return OnScroll(Convert.ToSingle(args));

            return false;
        }

        protected override void OnDisplayOffsetChanged () {
            AbsoluteDisplayOffsetOfChildren = AbsoluteDisplayOffset - _ScrollOffset;

            foreach (var child in Children)
                child.AbsoluteDisplayOffset = AbsoluteDisplayOffsetOfChildren;
        }

        protected override ControlKey OnGenerateLayoutTree (UIOperationContext context, ControlKey parent, ControlKey? existingKey) {
            HasContentBounds = false;
            var result = base.OnGenerateLayoutTree(context, parent, existingKey);
            context.Layout.SetContainerFlags(result, ContainerFlags);
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

        protected override IDecorator GetDefaultDecorations (IDecorationProvider provider) {
            if (LayoutFlags.IsFlagged(ControlFlags.Layout_Floating))
                return provider?.FloatingContainer ?? provider?.Container;
            else
                return provider?.Container;
        }

        protected override bool ShouldClipContent => ClipChildren && (Children.Count > 0);
        // FIXME: Always true?
        protected override bool HasChildren => (Children.Count > 0);

        protected override void OnRasterizeChildren (UIOperationContext context, ref RasterizePassSet passSet, DecorationSettings settings) {
            // FIXME
            int layer1 = passSet.Below.Layer,
                layer2 = passSet.Content.Layer,
                layer3 = passSet.Above.Layer,
                maxLayer1 = layer1,
                maxLayer2 = layer2,
                maxLayer3 = layer3;

            var sequence = Children.InPaintOrder();
            foreach (var item in sequence) {
                passSet.Below.Layer = layer1;
                passSet.Content.Layer = layer2;
                passSet.Above.Layer = layer3;

                item.Rasterize(ref context, ref passSet);

                maxLayer1 = Math.Max(maxLayer1, passSet.Below.Layer);
                maxLayer2 = Math.Max(maxLayer2, passSet.Content.Layer);
                maxLayer3 = Math.Max(maxLayer3, passSet.Above.Layer);
            }

            passSet.Below.Layer = maxLayer1;
            passSet.Content.Layer = maxLayer2;
            passSet.Above.Layer = maxLayer3;
        }

        protected override void OnRasterize (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings, IDecorator decorations) {
            base.OnRasterize(context, ref renderer, settings, decorations);

            if (Scrollable) {
                var scrollbar = context.DecorationProvider?.Scrollbar;
                if (ShowHorizontalScrollbar)
                    scrollbar?.Rasterize(context, ref renderer, settings, ref HScrollbar);
                if (ShowVerticalScrollbar)
                    scrollbar?.Rasterize(context, ref renderer, settings, ref VScrollbar);
            }
        }

        protected override Margins ComputePadding (UIOperationContext context, IDecorator decorations) {
            var result = base.ComputePadding(context, decorations);
            if (!Scrollable)
                return result;
            var scrollbar = context.DecorationProvider?.Scrollbar;
            if (scrollbar == null)
                return result;
            if (ShowVerticalScrollbar)
                result.Right += scrollbar.MinimumSize.X;
            if (ShowHorizontalScrollbar)
                result.Bottom += scrollbar.MinimumSize.Y;
            return result;
        }

        protected bool GetContentBounds (UIContext context, out RectF contentBounds) {
            if (!HasContentBounds)
                HasContentBounds = context.Layout.TryMeasureContent(LayoutKey, out ContentBounds);

            contentBounds = ContentBounds;
            return HasContentBounds;
        }

        protected override void ApplyClipMargins (UIOperationContext context, ref RectF box) {
            var scroll = context.DecorationProvider?.Scrollbar;
            if (scroll != null) {
                if (ShowHorizontalScrollbar)
                    box.Height -= scroll.MinimumSize.Y;
                if (ShowVerticalScrollbar)
                    box.Width -= scroll.MinimumSize.X;
            }
        }

        protected override bool OnHitTest (LayoutContext context, RectF box, Vector2 position, bool acceptsMouseInputOnly, bool acceptsFocusOnly, ref Control result) {
            if (!base.OnHitTest(context, box, position, false, false, ref result))
                return false;

            bool success = AcceptsMouseInput || !acceptsMouseInputOnly;
            // FIXME: Should we only perform the hit test if the position is within our boundaries?
            // This doesn't produce the right outcome when a container's computed size is zero
            var sorted = Children.InPaintOrder();
            for (int i = sorted.Count - 1; i >= 0; i--) {
                var item = sorted[i];
                var newResult = item.HitTest(context, position, acceptsMouseInputOnly, acceptsFocusOnly);
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

        void IPostLayoutListener.OnLayoutComplete (UIOperationContext context, ref bool relayoutRequested) {
            // FIXME: This should be done somewhere else
            if (Scrollable) {
                var box = context.Layout.GetRect(LayoutKey);
                var scrollbar = context.DecorationProvider?.Scrollbar;
                float viewportWidth = box.Width - (scrollbar?.MinimumSize.X ?? 0),
                    viewportHeight = box.Height - (scrollbar?.MinimumSize.Y ?? 0);

                GetContentBounds(context.UIContext, out RectF contentBounds);

                if (HasContentBounds) {
                    float maxScrollX = ContentBounds.Width - viewportWidth, maxScrollY = ContentBounds.Height - viewportHeight;
                    maxScrollX = Math.Max(0, maxScrollX);
                    maxScrollY = Math.Max(0, maxScrollY);
                    MinScrollOffset = Vector2.Zero;
                    MaxScrollOffset = new Vector2(maxScrollX, maxScrollY);
                    ScrollOffset = new Vector2(
                        Arithmetic.Clamp(ScrollOffset.X, 0, maxScrollX),
                        Arithmetic.Clamp(ScrollOffset.Y, 0, maxScrollY)
                    );

                    CanScrollUp = ScrollOffset.Y > 0;
                    CanScrollDown = ScrollOffset.Y < maxScrollY;

                    CanScrollVertically = maxScrollY > 0;
                }

                HScrollbar.ContentSize = ContentBounds.Width;
                HScrollbar.ViewportSize = box.Width;
                HScrollbar.Position = ScrollOffset.X;
                VScrollbar.ContentSize = ContentBounds.Height;
                VScrollbar.ViewportSize = box.Height;
                VScrollbar.Position = ScrollOffset.Y;

                HScrollbar.HasCounterpart = VScrollbar.HasCounterpart = (ShowHorizontalScrollbar && ShowVerticalScrollbar);
            } else {
                ScrollOffset = Vector2.Zero;
            }
        }
    }
}
