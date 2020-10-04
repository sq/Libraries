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
    public class Container : Control {
        public readonly ControlCollection Children;

        /// <summary>
        /// If set, children will only be rendered within the volume of this container
        /// </summary>
        public bool ClipChildren = false;

        public bool Scrollable = false;
        public bool ShowVerticalScrollbar = true, ShowHorizontalScrollbar = true;

        private Vector2 _ScrollOffset;
        protected Vector2 AbsoluteDisplayOffsetOfChildren;

        public ControlFlags ContainerFlags = ControlFlags.Container_Row;

        protected ScrollbarState HScrollbar, VScrollbar;
        protected bool HasContentBounds;
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
                if (value == _ScrollOffset)
                    return;

                _ScrollOffset = value;
                OnDisplayOffsetChanged();
            }
        }

        protected void OnScroll (float delta) {
            ScrollOffset = new Vector2(ScrollOffset.X, ScrollOffset.Y - delta);
        }

        protected override bool OnEvent<T> (string name, T args) {
            if (name == UIEvents.Scroll) {
                OnScroll(Convert.ToSingle(args));
                return true;
            }

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
                item.GenerateLayoutTree(context, result);
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

            var sequence = Children.InOrder(PaintOrderComparer.Instance);
            foreach (var item in sequence) {
                passSet.Below.Layer = layer1;
                passSet.Content.Layer = layer2;
                passSet.Above.Layer = layer3;

                item.Rasterize(context, ref passSet);

                maxLayer1 = Math.Max(maxLayer1, passSet.Below.Layer);
                maxLayer2 = Math.Max(maxLayer2, passSet.Content.Layer);
                maxLayer3 = Math.Max(maxLayer3, passSet.Above.Layer);
            }

            passSet.Below.Layer = maxLayer1;
            passSet.Content.Layer = maxLayer2;
            passSet.Above.Layer = maxLayer3;
        }

        protected override Margins ComputePadding (UIOperationContext context, IDecorator decorations) {
            var result = base.ComputePadding(context, decorations);
            if (!Scrollable)
                return result;
            var scrollbar = context.DecorationProvider?.Scrollbar;
            if (scrollbar == null)
                return result;
            result.Right += scrollbar.MinimumSize.X;
            result.Bottom += scrollbar.MinimumSize.Y;
            return result;
        }

        protected bool GetContentBounds (UIOperationContext context, out RectF contentBounds) {
            if (!HasContentBounds)
                HasContentBounds = context.Layout.TryMeasureContent(LayoutKey, out ContentBounds);

            contentBounds = ContentBounds;
            return HasContentBounds;
        }

        protected void UpdateScrollbars (UIOperationContext context, DecorationSettings settings) {
        }

        protected override void OnRasterize (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings, IDecorator decorations) {
            base.OnRasterize(context, ref renderer, settings, decorations);

            // FIXME: This should be done somewhere else
            if (Scrollable) {
                var box = settings.Box;
                var scrollbar = context.DecorationProvider?.Scrollbar;
                float viewportWidth = box.Width - (scrollbar?.MinimumSize.X ?? 0),
                    viewportHeight = box.Height - (scrollbar?.MinimumSize.Y ?? 0);

                GetContentBounds(context, out RectF contentBounds);

                if (HasContentBounds) {
                    float maxScrollX = ContentBounds.Width - viewportWidth, maxScrollY = ContentBounds.Height - viewportHeight;
                    maxScrollX = Math.Max(0, maxScrollX);
                    maxScrollY = Math.Max(0, maxScrollY);
                    ScrollOffset = new Vector2(
                        Arithmetic.Clamp(ScrollOffset.X, 0, maxScrollX),
                        Arithmetic.Clamp(ScrollOffset.Y, 0, maxScrollY)
                    );
                }

                HScrollbar.ContentSize = ContentBounds.Width;
                HScrollbar.ViewportSize = box.Width;
                HScrollbar.Position = ScrollOffset.X;
                VScrollbar.ContentSize = ContentBounds.Height;
                VScrollbar.ViewportSize = box.Height;
                VScrollbar.Position = ScrollOffset.Y;

                HScrollbar.HasCounterpart = VScrollbar.HasCounterpart = (ShowHorizontalScrollbar && ShowVerticalScrollbar);

                if (ShowHorizontalScrollbar)
                    scrollbar?.Rasterize(context, ref renderer, settings, ref HScrollbar);
                if (ShowVerticalScrollbar)
                    scrollbar?.Rasterize(context, ref renderer, settings, ref VScrollbar);
            } else {
                ScrollOffset = Vector2.Zero;
            }
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
            var sorted = Children.InOrder(PaintOrderComparer.Instance);
            for (int i = sorted.Count - 1; i >= 0; i--) {
                var item = sorted.DangerousGetItem(i);
                var newResult = item.HitTest(context, position, acceptsMouseInputOnly, acceptsFocusOnly);
                if (newResult != null) {
                    result = newResult;
                    success = true;
                }
            }

            return success;
        }
    }
}
