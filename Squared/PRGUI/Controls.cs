using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Squared.Game;
using Squared.PRGUI.Controls;
using Squared.PRGUI.Decorations;
using Squared.PRGUI.Layout;
using Squared.Render;
using Squared.Render.Convenience;
using Squared.Render.Text;
using Squared.Util;
using Squared.Util.Text;

namespace Squared.PRGUI {
    public class StaticText : Control {
        public const bool DiagnosticText = false;

        public Color? TextColor = null;
        public Material TextMaterial = null;
        public DynamicStringLayout Content = new DynamicStringLayout();
        private bool _AutoSizeWidth = true, _AutoSizeHeight = true;

        private float? AutoSizeComputedWidth, AutoSizeComputedHeight;

        public StaticText ()
            : base () {
            Multiline = false;
        }

        public bool Multiline {
            get => Content.LineLimit != 1;
            set {
                Content.LineLimit = value ? int.MaxValue : 1;
            }
        }

        public bool AutoSizeWidth {
            get => _AutoSizeWidth;
            set {
                if (_AutoSizeWidth == value)
                    return;
                _AutoSizeWidth = value;
                Content.Invalidate();
            }
        }

        public bool AutoSizeHeight {
            get => _AutoSizeHeight;
            set {
                if (_AutoSizeHeight == value)
                    return;
                _AutoSizeHeight = value;
                Content.Invalidate();
            }
        }

        public bool AutoSize {
            set {
                AutoSizeWidth = AutoSizeHeight = value;
            }
        }

        public bool Wrap {
            get {
                return Content.WordWrap;
            }
            set {
                Content.CharacterWrap = value;
                Content.WordWrap = value;
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

        protected override void ComputeFixedSize (out float? fixedWidth, out float? fixedHeight) {
            base.ComputeFixedSize(out fixedWidth, out fixedHeight);
            if (AutoSizeWidth && !FixedWidth.HasValue)
                fixedWidth = AutoSizeComputedWidth ?? fixedWidth;
            if (AutoSizeHeight && !FixedHeight.HasValue)
                fixedHeight = AutoSizeComputedHeight ?? fixedHeight;
        }

        private void ComputeAutoSize (UIOperationContext context) {
            AutoSizeComputedHeight = AutoSizeComputedWidth = null;
            if (!AutoSizeWidth && !AutoSizeHeight)
                return;

            /*
            var interiorSpace = GetFixedInteriorSpace();
            if (interiorSpace.X > 0)
                Content.LineBreakAtX = interiorSpace.X;
            */

            var decorations = GetDecorations(context);
            UpdateFont(context, decorations);

            var computedPadding = ComputePadding(context, decorations);
            var layout = Content.Get();
            if (AutoSizeWidth)
                AutoSizeComputedWidth = layout.UnconstrainedSize.X + computedPadding.Size.X;
            if (AutoSizeHeight)
                AutoSizeComputedHeight = layout.Size.Y + computedPadding.Size.Y;
        }

        protected override ControlKey OnGenerateLayoutTree (UIOperationContext context, ControlKey parent) {
            ComputeAutoSize(context);
            var result = base.OnGenerateLayoutTree(context, parent);
            return result;
        }

        protected override IDecorator GetDefaultDecorations (UIOperationContext context) {
            return context.DecorationProvider?.StaticText;
        }

        protected override void OnRasterize (UIOperationContext context, DecorationSettings settings, IDecorator decorations) {
            base.OnRasterize(context, settings, decorations);

            if (context.Pass != RasterizePasses.Content)
                return;

            if (!AutoSizeWidth)
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
            var xSpace = (b.X - a.X) - layout.Size.X - computedPadding.X;
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
        public bool Movable;

        private bool Dragging;
        private Vector2 DragStartMousePosition, DragStartWindowPosition;
        private RectF MostRecentTitleBox;

        protected DynamicStringLayout TitleLayout = new DynamicStringLayout {
            LineLimit = 1
        };

        public Window ()
            : base () {
            AcceptsCapture = true;
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
                MostRecentTitleBox = titleBox;

                var titleContentBox = titleBox;
                titleContentBox.Left += titleDecorator.Padding.Left;
                titleContentBox.Top += titleDecorator.Padding.Top;
                titleContentBox.Width -= titleDecorator.Padding.X;

                var offsetX = (titleContentBox.Width - layout.Size.X) / 2f;

                var subSettings = settings;
                subSettings.Box = titleBox;
                subSettings.ContentBox = titleContentBox;

                titleDecorator.Rasterize(context, subSettings);
                context.Renderer.DrawMultiple(
                    layout.DrawCalls, new Vector2(titleContentBox.Left + offsetX, titleContentBox.Top),
                    samplerState: RenderStates.Text, multiplyColor: titleColor.Value
                );
            }
        }

        protected override bool OnEvent<T> (string name, T args) {
            if (args is MouseEventArgs)
                return OnMouseEvent(name, (MouseEventArgs)(object)args);
            return false;
        }

        private bool OnMouseEvent (string name, MouseEventArgs args) {
            if (name == UIEvents.MouseDown) {
                if (MostRecentTitleBox.Contains(args.GlobalPosition)) {
                    Dragging = true;
                    DragStartMousePosition = args.GlobalPosition;
                    DragStartWindowPosition = Position;
                    return true;
                } else {
                    Dragging = false;
                    return false;
                }
            } else if (
                (name == UIEvents.MouseDrag) ||
                (name == UIEvents.MouseUp)
            ) {
                if (!Dragging)
                    return false;

                var delta = args.GlobalPosition - DragStartMousePosition;
                var newPosition = DragStartWindowPosition + delta;

                newPosition = new Vector2(
                    Arithmetic.Clamp(newPosition.X, 0, args.Context.CanvasSize.X - args.Box.Width),
                    Arithmetic.Clamp(newPosition.Y, 0, args.Context.CanvasSize.Y - args.Box.Height)
                );

                Position = newPosition;

                // args.Context.Invalidate();

                if (name == UIEvents.MouseUp)
                    Dragging = false;

                return true;
            } else
                return false;
        }

        public override string ToString () {
            return $"{GetType().Name} #{GetHashCode():X8} '{Title}'";
        }
    }

    public class ControlCollection : IEnumerable<Control> {
        private UnorderedList<Control> SortBuffer = new UnorderedList<Control>();
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

        internal UnorderedList<Control> InOrder<TComparer> (TComparer comparer)
            where TComparer : IComparer<Control>
        {
            SortBuffer.Clear();
            SortBuffer.EnsureCapacity(Items.Count);
            SortBuffer.AddRange(Items);
            SortBuffer.Sort(comparer);
            return SortBuffer;
        }

        IEnumerator<Control> IEnumerable<Control>.GetEnumerator () {
            return ((IEnumerable<Control>)Items).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator () {
            return ((IEnumerable)Items).GetEnumerator();
        }
    }
}
