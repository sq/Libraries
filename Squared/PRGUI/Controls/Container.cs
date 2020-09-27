using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
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

        public Tween<float> Opacity = 1;

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

        protected override IDecorator GetDefaultDecorations (UIOperationContext context) {
            if (LayoutFlags.IsFlagged(ControlFlags.Layout_Floating))
                return context.DecorationProvider?.FloatingContainer ?? context.DecorationProvider?.Container;
            else
                return context.DecorationProvider?.Container;
        }

        protected override bool ShouldClipContent => ClipChildren && (Children.Count > 0);
        // FIXME: Always true?
        protected override bool HasNestedContent => (Children.Count > 0);

        private void RasterizeChildren (UIOperationContext context, RasterizePasses pass, UnorderedList<Control> sequence) {
            context.Pass = pass;
            int layer = context.Renderer.Layer, maxLayer = layer;

            foreach (var item in sequence) {
                context.Renderer.Layer = layer;
                item.Rasterize(context);
                maxLayer = Math.Max(maxLayer, context.Renderer.Layer);
            }

            context.Renderer.Layer = maxLayer;
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

        protected virtual void OnRasterizeDecorations (UIOperationContext context, DecorationSettings settings, IDecorator decorations) {
        }

        protected override void OnRasterize (UIOperationContext context, DecorationSettings settings, IDecorator decorations) {
            var opacity = Opacity.Get(context.Now);

            if (opacity <= 0)
                return;

            if (opacity >= 1) {
                base.OnRasterize(context, settings, decorations);
                RasterizeContent(context, settings);
                OnRasterizeDecorations(context, settings, decorations);
            } else {
                var rt = context.UIContext.GetScratchRenderTarget(context.Renderer.Container.Coordinator);
                try {
                    var tempContext = context.Clone();
                    tempContext.Renderer = context.Renderer.ForRenderTarget(rt);
                    tempContext.Renderer.Clear(color: Color.Red);
                    base.OnRasterize(tempContext, settings, decorations);
                    RasterizeContent(tempContext, settings);
                    OnRasterizeDecorations(tempContext, settings, decorations);
                    // FIXME: BlendState
                    context.Renderer.Draw(rt, position: Vector2.Zero, blendState: BlendState.Opaque);
                    context.Renderer.Layer += 1;
                } finally {
                    context.UIContext.ReleaseScratchRenderTarget(rt);
                }
            }
        }

        private void RasterizeContent (UIOperationContext context, DecorationSettings settings) {
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
                    scrollbar?.Rasterize(context, settings, ref HScrollbar);
                if (ShowVerticalScrollbar)
                    scrollbar?.Rasterize(context, settings, ref VScrollbar);
            } else {
                ScrollOffset = Vector2.Zero;
            }

            if (context.Pass != RasterizePasses.Content)
                return;

            if (Children.Count == 0)
                return;

            var seq = Children.InOrder(PaintOrderComparer.Instance);
            RasterizeChildren(context, RasterizePasses.Below, seq);
            RasterizeChildren(context, RasterizePasses.Content, seq);
            RasterizeChildren(context, RasterizePasses.Above, seq);
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
