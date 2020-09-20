using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Game;
using Squared.PRGUI.Decorations;
using Squared.PRGUI.Layout;
using Squared.Render;
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
        public Color? BackgroundColor;

        // Accumulates scroll offset(s) from parent controls
        private Vector2 _AbsoluteDisplayOffset;

        public ControlStates State;

        internal ControlKey LayoutKey;

        public bool Enabled { get; set; } = true;
        public bool AcceptsCapture { get; protected set; }
        public bool AcceptsFocus { get; protected set; }
        public virtual bool AcceptsScroll => false;
        protected virtual bool HasNestedContent => false;
        protected virtual bool ShouldClipContent => false;
        protected virtual bool HasFixedWidth => FixedWidth.HasValue;
        protected virtual bool HasFixedHeight => FixedHeight.HasValue;

        protected WeakReference<Control> WeakParent = null;

        public Vector2 AbsoluteDisplayOffset {
            get {
                return _AbsoluteDisplayOffset;
            }
            set {
                if (value == _AbsoluteDisplayOffset)
                    return;
                _AbsoluteDisplayOffset = value;
                OnDisplayOffsetChanged();
            }
        }

        protected virtual void OnDisplayOffsetChanged () {
        }

        internal bool HandleScroll (float delta) {
            return OnScroll(delta);
        }

        protected virtual bool OnScroll (float delta) {
            return false;
        }

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

        protected virtual bool OnHitTest (LayoutContext context, RectF box, Vector2 position, bool acceptsCaptureOnly, bool acceptsFocusOnly, ref Control result) {
            if (!AcceptsCapture && acceptsCaptureOnly)
                return false;
            if (!AcceptsFocus && acceptsFocusOnly)
                return false;
            if ((acceptsFocusOnly || acceptsCaptureOnly) && !Enabled)
                return false;

            if (box.Contains(position)) {
                result = this;
                return true;
            }

            return false;
        }

        public RectF GetRect (LayoutContext context, bool includeOffset = true, bool contentRect = false) {
            var result = contentRect 
                ? context.GetContentRect(LayoutKey) 
                : context.GetRect(LayoutKey);
            result.Left += _AbsoluteDisplayOffset.X;
            result.Top += _AbsoluteDisplayOffset.Y;

            // HACK
            if (FixedWidth.HasValue)
                result.Width = FixedWidth.Value;
            if (FixedHeight.HasValue)
                result.Height = FixedHeight.Value;

            if (MinimumWidth.HasValue)
                result.Width = Math.Max(MinimumWidth.Value, result.Width);
            if (MinimumHeight.HasValue)
                result.Height = Math.Max(MinimumHeight.Value, result.Height);
            
            return result;
        }

        public Control HitTest (LayoutContext context, Vector2 position, bool acceptsCaptureOnly, bool acceptsFocusOnly) {
            var result = this;
            var box = GetRect(context);
            if (OnHitTest(context, box, position, acceptsCaptureOnly, acceptsFocusOnly, ref result))
                return result;

            return null;
        }

        protected virtual Margins ComputeMargins (UIOperationContext context, IDecorator decorations) {
            var result = Margins;
            if (decorations != null)
                result += decorations.Margins;
            return result;
        }

        protected virtual Margins ComputePadding (UIOperationContext context, IDecorator decorations) {
            var result = Padding;
            if (decorations != null)
                result += decorations.Padding;
            return result;
        }

        protected virtual ControlKey OnGenerateLayoutTree (UIOperationContext context, ControlKey parent) {
            var result = context.Layout.CreateItem();

            var decorations = GetDecorations(context);
            var computedMargins = ComputeMargins(context, decorations);
            var computedPadding = ComputePadding(context, decorations);

            var actualLayoutFlags = LayoutFlags;
            if (HasFixedWidth)
                actualLayoutFlags &= ~ControlFlags.Layout_Fill_Row;
            if (HasFixedHeight)
                actualLayoutFlags &= ~ControlFlags.Layout_Fill_Column;

            context.Layout.SetLayoutFlags(result, actualLayoutFlags);
            context.Layout.SetMargins(result, computedMargins);
            context.Layout.SetPadding(result, computedPadding);
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

            if (!Enabled) {
                result |= ControlStates.Disabled;
            } else {
                if (context.UIContext.Hovering == this)
                    result |= ControlStates.Hovering;
                if (context.UIContext.Focused == this)
                    result |= ControlStates.Focused;
            }

            if (context.UIContext.MouseCaptured == this)
                result |= ControlStates.Pressed;

            return result;
        }

        protected virtual void OnRasterize (UIOperationContext context, DecorationSettings settings, IDecorator decorations) {
            decorations?.Rasterize(context, settings);
        }

        protected virtual void ApplyClipMargins (UIOperationContext context, ref RectF box) {
        }

        protected virtual DecorationSettings MakeDecorationSettings (ref RectF box, ref RectF contentBox, ControlStates state) {
            return new DecorationSettings {
                Box = box,
                ContentBox = contentBox,
                State = state,
                BackgroundColor = BackgroundColor
            };
        }

        public void Rasterize (UIOperationContext context) {
            var box = GetRect(context.Layout);
            var contentBox = GetRect(context.Layout, contentRect: true);
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

            var settings = MakeDecorationSettings(ref box, ref contentBox, state);
            OnRasterize(contentContext, settings, decorations);

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
                    settings.State = default(ControlStates);
                    decorations.Rasterize(contentContext, settings);
                }

                context.Renderer.Layer += 1;
            }
        }

        public bool TryGetParent (out Control parent) {
            if (WeakParent == null) {
                parent = null;
                return false;
            }

            return WeakParent.TryGetTarget(out parent);
        }

        internal void SetParent (Control parent) {
            Control actualParent;
            if ((WeakParent != null) && WeakParent.TryGetTarget(out actualParent)) {
                if (actualParent != parent)
                    throw new Exception("This control already has a parent");
                else
                    return;
            }

            WeakParent = new WeakReference<Control>(parent, false);
        }

        internal void UnsetParent (Control oldParent) {
            if (WeakParent == null)
                return;

            Control actualParent;
            if (!WeakParent.TryGetTarget(out actualParent))
                return;

            if (actualParent != oldParent)
                throw new Exception("Parent mismatch");

            WeakParent = null;
        }

        public override string ToString () {
            return $"{GetType().Name} #{GetHashCode():X8}";
        }
    }

    public class StaticText : Control {
        public const bool DiagnosticText = false;

        public Color? TextColor = null;
        public Material TextMaterial = null;
        public DynamicStringLayout Content = new DynamicStringLayout();
        public bool AutoSizeWidth = true, AutoSizeHeight = true;

        public StaticText ()
            : base () {
            Content.LineLimit = 1;
        }

        public bool Multiline {
            get {
                return Content.LineLimit > 1;
            }
            set {
                Content.LineLimit = value ? int.MaxValue : 1;
            }
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

        public AbstractString Text {
            get {
                return Content.Text;
            }
            set {
                Content.Text = value;
            }
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

                var decorations = GetDecorations(context);
                UpdateFont(context, decorations);

                var computedPadding = ComputePadding(context, decorations);
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

        protected override void OnRasterize (UIOperationContext context, DecorationSettings settings, IDecorator decorations) {
            base.OnRasterize(context, settings, decorations);

            if (context.Pass != RasterizePasses.Content)
                return;

            Content.LineBreakAtX = settings.Box.Width;

            Color? overrideColor = TextColor;
            Material material;
            GetTextSettings(context, decorations, settings.State, out material, ref overrideColor);

            settings.Box.SnapAndInset(out Vector2 a, out Vector2 b);

            var computedPadding = ComputePadding(context, decorations);
            var textOffset = a + new Vector2(computedPadding.Left, computedPadding.Top);
            if (settings.State.HasFlag(ControlStates.Pressed))
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
                material: material, samplerState: RenderStates.Text, multiplyColor: overrideColor
            );
        }

        protected void UpdateFont (UIOperationContext context, IDecorator decorations) {
            Color? temp2 = null;
            GetTextSettings(context, decorations, default(ControlStates), out Material temp, ref temp2);
        }

        protected void GetTextSettings (UIOperationContext context, IDecorator decorations, ControlStates state, out Material material, ref Color? color) {
            decorations.GetTextSettings(context, state, out material, out IGlyphSource font, ref color);
            if (Content.GlyphSource == null)
                Content.GlyphSource = font;
            if (TextMaterial != null)
                material = TextMaterial;
        }

        private string GetTrimmedText () {
            var s = Text.ToString() ?? "";
            if (s.Length > 16)
                return s.Substring(0, 16) + "...";
            else
                return s;
        }

        public override string ToString () {
            return $"{GetType().Name} #{GetHashCode():X8} '{GetTrimmedText()}'";
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

        /*
        protected override void OnRasterize (UIOperationContext context, DecorationSettings settings, IDecorator decorations) {
            base.OnRasterize(context, settings, decorations);
        }
        */
    }

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
            AcceptsCapture = true;

            HScrollbar = new ScrollbarState {
                DragInitialPosition = null,
                Horizontal = true
            };
            VScrollbar = new ScrollbarState {
                DragInitialPosition = null,
                Horizontal = false
            };
        }

        public override bool AcceptsScroll => Scrollable;

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

        protected override bool OnScroll (float delta) {
            ScrollOffset = new Vector2(ScrollOffset.X, ScrollOffset.Y - delta);
            return true;
        }

        protected override void OnDisplayOffsetChanged () {
            AbsoluteDisplayOffsetOfChildren = AbsoluteDisplayOffset - _ScrollOffset;

            foreach (var child in Children)
                child.AbsoluteDisplayOffset = AbsoluteDisplayOffsetOfChildren;
        }

        protected override ControlKey OnGenerateLayoutTree (UIOperationContext context, ControlKey parent) {
            HasContentBounds = false;
            var result = base.OnGenerateLayoutTree(context, parent);
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

        private void RasterizeChildren (UIOperationContext context, RasterizePasses pass) {
            context.Pass = pass;
            // FIXME
            int layer = context.Renderer.Layer, maxLayer = layer;

            foreach (var item in Children) {
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

        protected override void OnRasterize (UIOperationContext context, DecorationSettings settings, IDecorator decorations) {
            base.OnRasterize(context, settings, decorations);

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

            RasterizeChildren(context, RasterizePasses.Below);
            RasterizeChildren(context, RasterizePasses.Content);
            RasterizeChildren(context, RasterizePasses.Above);
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

        protected override bool OnHitTest (LayoutContext context, RectF box, Vector2 position, bool acceptsCaptureOnly, bool acceptsFocusOnly, ref Control result) {
            if (!base.OnHitTest(context, box, position, false, false, ref result))
                return false;

            bool success = AcceptsCapture || !acceptsCaptureOnly;
            // FIXME: Should we only perform the hit test if the position is within our boundaries?
            // This doesn't produce the right outcome when a container's computed size is zero
            for (int i = Children.Count - 1; i >= 0; i--) {
                var item = Children[i];
                var newResult = item.HitTest(context, position, acceptsCaptureOnly, acceptsFocusOnly);
                if (newResult != null) {
                    result = newResult;
                    success = true;
                }
            }

            return success;
        }
    }

    public class Window : Container {
        public Vector2 Position {
            get {
                return new Vector2(Margins.Left, Margins.Top);
            }
            set {
                Margins.Left = value.X;
                Margins.Top = value.Y;
            }
        }

        public string Title;

        protected DynamicStringLayout TitleLayout = new DynamicStringLayout {
            LineLimit = 1
        };

        public Window ()
            : base () {
            ContainerFlags |= ControlFlags.Container_Constrain_Size;
            LayoutFlags |= ControlFlags.Layout_Floating;
        }

        protected IDecorator UpdateTitle (UIOperationContext context, DecorationSettings settings, out Material material, ref Color? color) {
            var decorations = context.DecorationProvider?.WindowTitle;
            if (decorations == null) {
                material = null;
                return null;
            }
            decorations.GetTextSettings(context, settings.State, out material, out IGlyphSource font, ref color);
            TitleLayout.Text = Title;
            TitleLayout.GlyphSource = font;
            TitleLayout.Color = color ?? Color.White;
            TitleLayout.LineBreakAtX = settings.ContentBox.Width;
            return decorations;
        }

        protected override Margins ComputePadding (UIOperationContext context, IDecorator decorations) {
            var result = base.ComputePadding(context, decorations);
            var titleDecorations = context.DecorationProvider?.WindowTitle;
            if (titleDecorations == null)
                return result;

            Color? color = null;
            titleDecorations.GetTextSettings(context, default(ControlStates), out Material temp, out IGlyphSource font, ref color);
            result.Top += titleDecorations.Margins.Bottom;
            result.Top += titleDecorations.Padding.Top;
            result.Top += titleDecorations.Padding.Bottom;
            result.Top += font.LineSpacing;
            return result;
        }

        protected override void OnRasterize (UIOperationContext context, DecorationSettings settings, IDecorator decorations) {
            base.OnRasterize(context, settings, decorations);

            IDecorator titleDecorator;
            Color? titleColor = null;
            if (
                (context.Pass == RasterizePasses.Below) && 
                (titleDecorator = UpdateTitle(context, settings, out Material titleMaterial, ref titleColor)) != null
            ) {
                var layout = TitleLayout.Get();
                var titleBox = settings.Box;
                titleBox.Height = titleDecorator.Padding.Top + titleDecorator.Padding.Bottom + TitleLayout.GlyphSource.LineSpacing;
                var titleContentBox = titleBox;
                titleContentBox.Left += titleDecorator.Padding.Left;
                titleContentBox.Top += titleDecorator.Padding.Top;
                titleContentBox.Width -= (titleDecorator.Padding.Left + titleDecorator.Padding.Right);

                var offsetX = (titleContentBox.Width - layout.Size.X) / 2f;

                var subSettings = settings;
                subSettings.Box = titleBox;
                subSettings.ContentBox = titleContentBox;

                titleDecorator.Rasterize(context, subSettings);
                context.Renderer.DrawMultiple(
                    layout.DrawCalls, new Vector2(titleContentBox.Left + offsetX, titleContentBox.Top),
                    multiplyColor: titleColor.Value
                );
            }
        }

        public override string ToString () {
            return $"{GetType().Name} #{GetHashCode():X8} '{Title}'";
        }
    }

    public class ControlCollection : IEnumerable<Control> {
        private List<Control> Items = new List<Control>();

        public int Count => Items.Count;
        public Control Parent { get; private set; }

        public ControlCollection (Control parent) {
            Parent = parent;
        }

        public void Add (Control control) {
            if (Items.Contains(control))
                throw new InvalidOperationException("Control already in collection");

            Items.Add(control);
            control.SetParent(Parent);
        }

        public void Remove (Control control) {
            control.UnsetParent(Parent);
            Items.Remove(control);
        }

        public void Clear () {
            foreach (var control in Items)
                control.UnsetParent(Parent);

            Items.Clear();
        }

        public List<Control>.Enumerator GetEnumerator () {
            return Items.GetEnumerator();
        }

        public Control this[int index] {
            get {
                return Items[index];
            }
            set {
                Items[index] = value;
            }
        }

        IEnumerator<Control> IEnumerable<Control>.GetEnumerator () {
            return ((IEnumerable<Control>)Items).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator () {
            return ((IEnumerable)Items).GetEnumerator();
        }
    }
}
