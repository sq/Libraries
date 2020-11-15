using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Squared.PRGUI.Decorations;
using Squared.Render.Convenience;
using Squared.Util;

namespace Squared.PRGUI.Controls {
    public class ParameterEditor<T> : EditableText
        where T : struct, IEquatable<T> {

        // FIXME: Use a decorator for this?
        public const float ArrowWidth = 9,
            ArrowHeight = 17,
            ArrowPadding = ArrowWidth + 6;

        private T _Value;

        public T Value {
            get => _Value;
            set {
                if (_Value.Equals(value))
                    return;
                _Value = value;
                Text = Convert.ToString(value);
            }
        }

        public ParameterEditor ()
            : base () {
            ClampVirtualPositionToTextbox = false;
        }

        protected override void OnValueChanged () {
            try {
                var newValue = Convert.ChangeType(Text, typeof(T));
                _Value = (T)newValue;
            } catch {
            }
        }

        private void FinalizeValue () {
            OnValueChanged();
            Text = Convert.ToString(_Value);
        }

        protected override Margins ComputePadding (UIOperationContext context, IDecorator decorations) {
            var result = base.ComputePadding(context, decorations);
            result.Left += ArrowPadding;
            result.Right += ArrowPadding;
            return result;
        }

        void DrawArrow (ref ImperativeRenderer renderer, RectF box, bool facingRight) {
            Vector2 a = !facingRight ? box.Extent : box.Position,
                b = !facingRight 
                    ? new Vector2(box.Position.X, box.Center.Y)
                    : new Vector2(box.Extent.X, box.Center.Y),
                c = !facingRight
                    ? new Vector2(box.Extent.X, box.Position.Y)
                    : new Vector2(box.Position.X, box.Extent.Y);
            renderer.RasterizeTriangle(
                a, b, c, radius: 0f, outlineRadius: 1f,
                innerColor: Color.White, outerColor: Color.White, 
                outlineColor: Color.Black * 0.8f
            );
        }

        protected override void OnRasterize (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings, IDecorator decorations) {
            base.OnRasterize(context, ref renderer, settings, decorations);

            if (context.Pass != RasterizePasses.Content)
                return;

            var box = settings.ContentBox;
            // Compensate for padding
            box.Left -= ArrowPadding;
            box.Width -= (ArrowPadding * 2);

            var space = (box.Height - ArrowHeight) / 2f;
            box.Top += space;
            box.Width = ArrowWidth;
            box.Height = ArrowHeight;
            DrawArrow(ref renderer, box, false);
            box.Left = settings.ContentBox.Extent.X + (ArrowPadding - ArrowWidth);
            DrawArrow(ref renderer, box, true);
        }

        protected override bool OnKeyPress (KeyEventArgs evt) {
            if (evt.Key == Keys.Enter) {
                FinalizeValue();
                return true;
            }

            return base.OnKeyPress(evt);
        }

        protected override bool OnEvent<T> (string name, T args) {
            if (name == UIEvents.LostFocus)
                FinalizeValue();
            else if (name == UIEvents.GotFocus)
                SetSelection(new Pair<int>(-9999, 9999), 0);

            return base.OnEvent<T>(name, args);
        }
    }
}
