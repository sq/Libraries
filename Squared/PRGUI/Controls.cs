using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Game;
using Squared.PRGUI.Decorations;
using Squared.PRGUI.Layout;
using Squared.Render.Convenience;
using Squared.Render.Text;
using Squared.Util;

namespace Squared.PRGUI {
    public abstract class Control {
        public IDecorator CustomDecorations;
        public Margins Margins, Padding;
        public ControlFlags LayoutFlags = ControlFlags.Layout_Fill_Row;
        public float? FixedWidth, FixedHeight;
        public float? MinimumWidth, MinimumHeight;
        public float? MaximumWidth, MaximumHeight;

        public ControlStates State;

        internal ControlKey LayoutKey;

        public bool AcceptsCapture { get; protected set; }
        public bool AcceptsFocus { get; protected set; }
        protected virtual bool HasNestedContent => false;
        protected virtual bool ShouldClipContent => false;
        protected virtual bool HasFixedWidth => FixedWidth.HasValue;
        protected virtual bool HasFixedHeight => FixedHeight.HasValue;

        public void GenerateLayoutTree (UIOperationContext context, ControlKey parent) {
            LayoutKey = OnGenerateLayoutTree(context, parent);
        }

        protected Vector2 GetFixedInteriorSpace () {
            return new Vector2(
                FixedWidth.HasValue
                    ? Math.Max(0, FixedWidth.Value - Margins.Left - Margins.Right)
                    : -1,
                FixedHeight.HasValue
                    ? Math.Max(0, FixedHeight.Value - Margins.Top - Margins.Bottom)
                    : -1
            );
        }

        protected virtual Control OnHitTest (LayoutContext context, RectF box, Vector2 position) {
            if (box.Contains(position))
                return this;

            return null;
        }

        public RectF GetRect (LayoutContext context) {
            var result = context.GetRect(LayoutKey);
            if (MinimumWidth.HasValue)
                result.Width = Math.Max(MinimumWidth.Value, result.Width);
            if (MinimumHeight.HasValue)
                result.Height = Math.Max(MinimumHeight.Value, result.Height);

            return result;
        }

        public Control HitTest (LayoutContext context, Vector2 position) {
            var box = GetRect(context);
            return OnHitTest(context, box, position);
        }

        protected virtual ControlKey OnGenerateLayoutTree (UIOperationContext context, ControlKey parent) {
            var result = context.Layout.CreateItem();

            var decorations = GetDecorations(context);
            var computedMargins = Margins;
            if (decorations != null)
                computedMargins += decorations.Margins;

            var actualLayoutFlags = LayoutFlags;
            if (HasFixedWidth)
                actualLayoutFlags &= ~ControlFlags.Layout_Fill_Row;
            if (HasFixedHeight)
                actualLayoutFlags &= ~ControlFlags.Layout_Fill_Column;

            context.Layout.SetLayoutFlags(result, actualLayoutFlags);
            context.Layout.SetMargins(result, computedMargins);
            context.Layout.SetFixedSize(result, FixedWidth ?? -1, FixedHeight ?? -1);
            context.Layout.SetSizeConstraints(result, MinimumWidth, MinimumHeight, MaximumWidth, MaximumHeight);

            if (!parent.IsInvalid)
                context.Layout.InsertAtEnd(parent, result);

            return result;
        }

        protected virtual IDecorator GetDefaultDecorations (UIOperationContext context) {
            return null;
        }

        protected IDecorator GetDecorations (UIOperationContext context) {
            return CustomDecorations ?? GetDefaultDecorations(context);
        }

        protected ControlStates GetCurrentState (UIOperationContext context) {
            var result = State;
            if (context.UIContext.Hovering == this)
                result |= ControlStates.Hovering;
            if (context.UIContext.Focused == this)
                result |= ControlStates.Focused;
            if (context.UIContext.MouseCaptured == this)
                result |= ControlStates.Pressed;
            return result;
        }

        protected virtual void OnRasterize (UIOperationContext context, RectF box, ControlStates state, IDecorator decorations) {
            decorations?.Rasterize(context, box, state);
        }

        protected virtual void ApplyClipMargins (UIOperationContext context, ref RectF box) {
        }

        public void Rasterize (UIOperationContext context, Vector2 offset) {
            var box = GetRect(context.Layout);
            box.Left += offset.X;
            box.Top += offset.Y;
            var decorations = GetDecorations(context);
            var state = GetCurrentState(context);

            var contentContext = context;
            var hasNestedContext = (context.Pass == RasterizePasses.Content) && (ShouldClipContent || HasNestedContent);

            // For clipping we need to create a separate batch group that contains all the rasterization work
            //  for our children. At the start of it we'll generate the stencil mask that will be used for our
            //  rendering operation(s).
            if (hasNestedContext) {
                context.Renderer.Layer += 1;
                contentContext = context.Clone();
                contentContext.Renderer = context.Renderer.MakeSubgroup();
                contentContext.Renderer.Layer = 0;

                if (ShouldClipContent)
                    contentContext.Renderer.DepthStencilState = RenderStates.StencilTest;
            }

            OnRasterize(contentContext, box, state, decorations);

            if (hasNestedContext) {
                // GROSS OPTIMIZATION HACK: Detect that any rendering operation(s) occurred inside the
                //  group and if so, set up the stencil mask so that they will be clipped.
                if (ShouldClipContent && !contentContext.Renderer.Container.IsEmpty) {
                    contentContext.Renderer.DepthStencilState = RenderStates.StencilWrite;

                    // FIXME: Because we're doing Write here and clearing first, nested clips won't work right.
                    // The solution is probably a combination of test-and-increment when entering the clip,
                    //  and then a test-and-decrement when exiting to restore the previous clip region.
                    contentContext.Renderer.Clear(stencil: 0, layer: -9999);

                    // FIXME: Separate context?
                    contentContext.Pass = RasterizePasses.ContentClip;

                    ApplyClipMargins(context, ref box);

                    contentContext.Renderer.Layer = -999;
                    decorations.Rasterize(contentContext, box, default(ControlStates));
                }

                context.Renderer.Layer += 1;
            }
        }
    }

    public class StaticText : Control {
        public const bool DiagnosticText = false;

        public DynamicStringLayout Content = new DynamicStringLayout();
        public bool AutoSizeWidth = true, AutoSizeHeight = true;

        public StaticText ()
            : base () {
        }

        public bool AutoSize {
            set {
                AutoSizeWidth = AutoSizeHeight = value;
            }
        }

        public HorizontalAlignment TextAlignment {
            get {
                return Content.Alignment;
            }
            set {
                Content.Alignment = value;
            }
        }

        public string Text {
            set {
                Content.Text = value;
            }
        }

        protected Margins ComputePadding (IDecorator decorations) {
            var computedPadding = Padding;
            if (decorations != null)
                computedPadding += decorations.Padding;
            return computedPadding;
        }

        protected override bool HasFixedWidth => base.HasFixedWidth || AutoSizeWidth;
        protected override bool HasFixedHeight => base.HasFixedHeight || AutoSizeHeight;

        protected override ControlKey OnGenerateLayoutTree (UIOperationContext context, ControlKey parent) {
            var result = base.OnGenerateLayoutTree(context, parent);
            if (AutoSizeWidth || AutoSizeHeight) {
                var interiorSpace = GetFixedInteriorSpace();
                if (interiorSpace.X > 0)
                    Content.LineBreakAtX = interiorSpace.X;
                else
                    Content.LineBreakAtX = null;

                if (Content.GlyphSource == null)
                    Content.GlyphSource = context.UIContext.DefaultGlyphSource;

                var decorations = GetDecorations(context);
                var computedPadding = ComputePadding(decorations);
                var layoutSize = Content.Get().Size;
                var computedWidth = layoutSize.X + computedPadding.Left + computedPadding.Right;
                var computedHeight = layoutSize.Y + computedPadding.Top + computedPadding.Bottom;
                if (MinimumWidth.HasValue)
                    computedWidth = Math.Max(MinimumWidth.Value, computedWidth);
                if (MinimumHeight.HasValue)
                    computedHeight = Math.Max(MinimumHeight.Value, computedHeight);

                context.Layout.SetFixedSize(
                    result, 
                    FixedWidth ?? (AutoSizeWidth ? computedWidth : -1), 
                    FixedHeight ?? (AutoSizeHeight ? computedHeight : -1)
                );
            }

            if (DiagnosticText)
                Content.Text = $"#{result.ID} size {context.Layout.GetFixedSize(result)}";

            return result;
        }

        protected override IDecorator GetDefaultDecorations (UIOperationContext context) {
            return context.DecorationProvider?.StaticText;
        }

        protected override void OnRasterize (UIOperationContext context, RectF box, ControlStates state, IDecorator decorations) {
            base.OnRasterize(context, box, state, decorations);

            if (context.Pass != RasterizePasses.Content)
                return;

            var interiorSpace = GetFixedInteriorSpace();
            if (interiorSpace.X > 0)
                Content.LineBreakAtX = interiorSpace.X;
            else
                Content.LineBreakAtX = null;

            if (Content.GlyphSource == null)
                Content.GlyphSource = context.UIContext.DefaultGlyphSource;

            box.SnapAndInset(out Vector2 a, out Vector2 b);

            var computedPadding = ComputePadding(decorations);
            var textOffset = a + new Vector2(computedPadding.Left, computedPadding.Top);
            if (state.HasFlag(ControlStates.Pressed))
                textOffset += decorations.PressedInset;

            var layout = Content.Get();
            var xSpace = (b.X - a.X) - layout.Size.X - computedPadding.Left - computedPadding.Right;
            switch (Content.Alignment) {
                case HorizontalAlignment.Left:
                    break;
                case HorizontalAlignment.Center:
                    textOffset.X += (xSpace / 2f);
                    break;
                case HorizontalAlignment.Right:
                    textOffset.X += xSpace;
                    break;
            }

            context.Renderer.DrawMultiple(
                layout.DrawCalls, offset: textOffset.Floor(),
                material: decorations?.GetTextMaterial(context, state)
            );
        }
    }

    public class Button : StaticText {
        public Button ()
            : base () {
            Content.Alignment = HorizontalAlignment.Center;
            AcceptsCapture = true;
            AcceptsFocus = true;
        }

        protected override IDecorator GetDefaultDecorations (UIOperationContext context) {
            return context.DecorationProvider?.Button;
        }

        protected override void OnRasterize (UIOperationContext context, RectF box, ControlStates state, IDecorator decorations) {
            base.OnRasterize(context, box, state, decorations);
        }
    }

    public class Container : Control {
        /// <summary>
        /// If set, children will only be rendered within the volume of this container
        /// </summary>
        public bool ClipChildren = false;

        public bool ShowVerticalScrollbar, ShowHorizontalScrollbar;
        public Vector2 ScrollOffset = Vector2.Zero;

        public ControlFlags ContainerFlags = ControlFlags.Container_Row;

        public readonly List<Control> Children = new List<Control>();

        protected override ControlKey OnGenerateLayoutTree (UIOperationContext context, ControlKey parent) {
            var result = base.OnGenerateLayoutTree(context, parent);
            context.Layout.SetContainerFlags(result, ContainerFlags);
            foreach (var item in Children)
                item.GenerateLayoutTree(context, result);
            return result;
        }

        protected override IDecorator GetDefaultDecorations (UIOperationContext context) {
            return context.DecorationProvider?.Container;
        }

        protected override bool ShouldClipContent => ClipChildren && (Children.Count > 0);
        // FIXME: Always true?
        protected override bool HasNestedContent => (Children.Count > 0);

        private void RasterizeChildren (UIOperationContext context, RasterizePasses pass) {
            context.Pass = pass;
            // FIXME
            int layer = context.Renderer.Layer, maxLayer = layer;

            foreach (var item in Children) {
                context.Renderer.Layer = layer;
                item.Rasterize(context, -ScrollOffset);
                maxLayer = Math.Max(maxLayer, context.Renderer.Layer);
            }

            context.Renderer.Layer = maxLayer;
        }

        protected override void OnRasterize (UIOperationContext context, RectF box, ControlStates state, IDecorator decorations) {
            base.OnRasterize(context, box, state, decorations);

            // FIXME
            var hstate = new ScrollbarState {
                ContentSize = 1024,
                ViewportSize = box.Width,
                Position = ScrollOffset.X,
                DragInitialPosition = null
            };
            var vstate = new ScrollbarState {
                ContentSize = 1024,
                ViewportSize = box.Height,
                Position = ScrollOffset.Y,
                DragInitialPosition = null
            };

            if (ShowHorizontalScrollbar)
                context.DecorationProvider?.HorizontalScrollbar?.Rasterize(context, box, state, ref hstate);
            if (ShowVerticalScrollbar)
                context.DecorationProvider?.VerticalScrollbar?.Rasterize(context, box, state, ref vstate);

            if (context.Pass != RasterizePasses.Content)
                return;

            if (Children.Count == 0)
                return;

            RasterizeChildren(context, RasterizePasses.Below);
            RasterizeChildren(context, RasterizePasses.Content);
            RasterizeChildren(context, RasterizePasses.Above);
        }

        protected override void ApplyClipMargins (UIOperationContext context, ref RectF box) {
            var vscroll = context.DecorationProvider?.VerticalScrollbar;
            var hscroll = context.DecorationProvider?.HorizontalScrollbar;
            if (ShowHorizontalScrollbar && (hscroll != null))
                box.Height -= hscroll.MinimumSize.Y;
            if (ShowVerticalScrollbar && (vscroll != null))
                box.Width -= vscroll.MinimumSize.X;
        }

        protected override Control OnHitTest (LayoutContext context, RectF box, Vector2 position) {
            var result = base.OnHitTest(context, box, position);
            if (result != this)
                return result;

            // FIXME: Should we only perform the hit test if the position is within our boundaries?
            // This doesn't produce the right outcome when a container's computed size is zero
            foreach (var item in Children) {
                result = item.HitTest(context, position + ScrollOffset);
                if (result != null)
                    return result;
            }

            return this;
        }
    }
}
