using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Squared.Game;
using Squared.PRGUI.Decorations;
using Squared.PRGUI.Layout;
using Squared.Render;
using Squared.Render.Convenience;
using Squared.Render.Text;
using Squared.Util;
using Squared.Util.Text;

namespace Squared.PRGUI.Controls {
    public class StaticText : Control {
        public const bool DiagnosticText = false;

        public Tween<Color>? TextColor = null;
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

            var decorations = GetDecorations(context);
            UpdateFont(context, decorations);

            // HACK: If we know that our size is going to be constrained by layout settings, apply that in advance
            //  when computing auto-size to reduce the odds that our layout will be changed once full UI layout happens
            var textWidthLimit = ComputeTextWidthLimit(context, decorations);
            if (textWidthLimit.HasValue)
                Content.LineBreakAtX = textWidthLimit;

            var computedPadding = ComputePadding(context, decorations);
            var layout = Content.Get();
            if (AutoSizeWidth)
                AutoSizeComputedWidth = layout.UnconstrainedSize.X + computedPadding.Size.X;
            if (AutoSizeHeight)
                AutoSizeComputedHeight = layout.Size.Y + computedPadding.Size.Y;
        }

        public void Invalidate () {
            Content.LineBreakAtX = null;
            Content.Invalidate();
        }

        protected override ControlKey OnGenerateLayoutTree (UIOperationContext context, ControlKey parent, ControlKey? existingKey) {
            ComputeAutoSize(context);
            var result = base.OnGenerateLayoutTree(context, parent, existingKey);
            return result;
        }

        protected override IDecorator GetDefaultDecorations (UIOperationContext context) {
            return context.DecorationProvider?.StaticText;
        }

        protected float? ComputeTextWidthLimit (UIOperationContext context, IDecorator decorations) {
            var limit = FixedWidth ?? MaximumWidth;
            var computedPadding = ComputePadding(context, decorations);
            if (limit.HasValue)
                return limit.Value - computedPadding.X;
            else
                return null;
        }

        protected override void OnRasterize (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings, IDecorator decorations) {
            base.OnRasterize(context, ref renderer, settings, decorations);

            if (context.Pass != RasterizePasses.Content)
                return;

            if (!AutoSizeWidth || Wrap) {
                // If auto-size is disabled or wrapping is enabled, we need to enable wrapping/breaking at
                //  our rightmost edge to ensure that our text doesn't overflow outside of our boundaries
                // If wrapping is disabled entirely, the overflowing text will be suppressed by the text
                //  layout engine, otherwise it will be wrapped (potentially changing our layout, oops)
                var textWidthLimit = ComputeTextWidthLimit(context, decorations) ?? settings.ContentBox.Width;
                Content.LineBreakAtX = textWidthLimit;
            }

            Color? overrideColor = TextColor?.Get(context.Now);
            Material material;
            GetTextSettings(context, decorations, settings.State, out material, ref overrideColor);

            settings.Box.SnapAndInset(out Vector2 a, out Vector2 b);

            var computedPadding = ComputePadding(context, decorations);
            var textOffset = a + new Vector2(computedPadding.Left, computedPadding.Top);
            if (settings.State.IsFlagged(ControlStates.Pressed))
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

            renderer.DrawMultiple(
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
}
