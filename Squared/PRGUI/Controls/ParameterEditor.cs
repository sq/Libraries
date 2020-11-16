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
    public interface IParameterEditor {
        object Minimum { get; set; }
        object Maximum { get; set; }
        object Value { get; set; }
        bool IntegerOnly { get; set; }
        bool DoubleOnly { get; set; }
    }

    public class ParameterEditor<T> : EditableText, IParameterEditor
        where T : struct, IComparable<T> {

        // FIXME: Use a decorator for this?
        public const float ArrowWidth = 10,
            ArrowHeight = 17,
            ArrowPadding = ArrowWidth + 8;

        private bool _HasValue;
        private T _Value;
        private T? _Minimum, _Maximum;

        public T Value {
            get => _Value;
            set {
                value = ClampValue(value);
                if ((_Value.CompareTo(value) == 0) && _HasValue)
                    return;
                _Value = value;
                Text = Convert.ToString(value);
                _HasValue = true;
            }
        }

        public T? Minimum {
            get => _Minimum;
            set {
                _Minimum = value;
                if (_HasValue)
                    Value = Value;
            }
        }
        public T? Maximum {
            get => _Maximum;
            set {
                _Maximum = value;
                if (_HasValue)
                    Value = Value;
            }
        }

        object IParameterEditor.Minimum {
            get => Minimum;
            set => Minimum = (T)value;
        }
        object IParameterEditor.Maximum {
            get => Maximum;
            set => Maximum = (T)value;
        }
        object IParameterEditor.Value {
            get => Value;
            set => Value = (T)value;
        }

        public ParameterEditor ()
            : base () {
            ClampVirtualPositionToTextbox = false;
            var t = typeof(T);
            if (!t.IsValueType)
                throw new ArgumentException("T must be a value type");

            if (t == typeof(double) || t == typeof(float))
                DoubleOnly = true;
            else if (t == typeof(int) || t == typeof(long))
                IntegerOnly = true;
        }

        private T ClampValue (T value) {
            if (_Minimum.HasValue && (value.CompareTo(_Minimum.Value) < 0))
                value = _Minimum.Value;
            if (_Maximum.HasValue && (value.CompareTo(_Maximum.Value) > 0))
                value = _Maximum.Value;
            return value;
        }

        protected override void OnValueChanged () {
            try {
                var newValue = Convert.ChangeType(Text, typeof(T));
                var converted = ClampValue((T)newValue);
                _HasValue = true;
                _Value = converted;
                FireEvent(UIEvents.ValueChanged);
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

            // If there's a gauge, adjust our content box based on its margins so that it doesn't overlap
            //  our text or selection box
            var gauge = context.DecorationProvider.ParameterGauge;
            if (gauge != null) {
                result.Top += (gauge.Margins.Top * 2f);
                result.Bottom += (gauge.Margins.Bottom * 2f);
            }
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

            var gauge = context.DecorationProvider.ParameterGauge;
            if ((Minimum.HasValue && Maximum.HasValue) && (gauge != null)) {
                var gaugeBox = settings.Box;
                gaugeBox.Top += gauge.Margins.Top;
                gaugeBox.Left += gauge.Margins.Left;
                gaugeBox.Width -= gauge.Margins.X;
                gaugeBox.Height -= gauge.Margins.Y;
                // HACK: Good enough for rasterization
                double v = Convert.ToDouble(_Value),
                    min = Convert.ToDouble(_Minimum.Value),
                    max = Convert.ToDouble(_Maximum.Value);
                var fraction = (v - min) / (max - min);
                gaugeBox.Width = Math.Min(Math.Max(settings.Box.Width * (float)fraction, gauge.Padding.X), settings.Box.Width - gauge.Margins.X);
                var tempSettings = settings;
                tempSettings.ContentBox = gaugeBox;
                gauge.Rasterize(context, ref renderer, tempSettings);
            }

            // Draw in the "Above" pass to ensure it is not clipped (better batching)
            if (context.Pass != RasterizePasses.Above)
                return;

            var box = settings.ContentBox;
            // Compensate for padding
            box.Left -= ArrowPadding;
            box.Width -= (ArrowPadding * 2);
            // Shift the arrows up/down if there's a gauge running along the edge of the editor
            if (gauge != null) {
                box.Top += gauge.Padding.Top;
                box.Height -= gauge.Padding.Y;
            }

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
            if (name == UIEvents.LostFocus) {
                FinalizeValue();
                ClearSelection();
            } else if (name == UIEvents.GotFocus)
                SetSelection(new Pair<int>(-9999, 9999), 0);

            return base.OnEvent<T>(name, args);
        }
    }
}
