using System;
using System.Collections.Generic;
using System.Globalization;
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

    public class ParameterEditor<T> : EditableText, IScrollableControl, IParameterEditor, IValueControl<T>
        where T : struct, IComparable<T> {

        public const double NormalAccelerationMultiplier = 1.0,
            NormalRepeatSpeed = 0.75,
            FastAccelerationMultiplier = 0.85,
            FastRepeatSpeed = 0.55;

        // FIXME: Use a decorator for this?
        public const float ArrowWidth = 10,
            ArrowHeight = 18,
            ArrowPadding = ArrowWidth + 4;

        public bool HasValue { get; private set; }
        private T _Value;
        private T? _Minimum, _Maximum;

        private bool IsDraggingGauge = false;

        private double LastRepeatTimestamp;

        // FIXME: If for some reason your FastIncrementRate is very large, pgup/pgdn will be slow
        public int FastIncrementRate = 10;
        public T? Increment;

        public double? Exponent;

        public Func<T, T?> ValueFilter;
        public Func<T, string> ValueEncoder;
        public Func<string, T> ValueDecoder;

        bool IsSettingValue;
        public T Value {
            get => _Value;
            set {
                try {
                    IsSettingValue = true;
                    var clamped = ClampValue(value);
                    if (clamped == null)
                        return;

                    value = clamped.Value;
                    if ((_Value.CompareTo(value) == 0) && HasValue)
                        return;

                    CachedFractionD = null;
                    HasValue = true;
                    _Value = value;

                    var newText = ValueEncoder(value);
                    if (Text != newText) {
                        SetText(newText, true);
                        SelectNone();
                    }
                } finally {
                    IsSettingValue = false;
                }
            }
        }

        public T? Minimum {
            get => _Minimum;
            set {
                if (
                    (value.HasValue == _Minimum.HasValue) &&
                    (
                        (value.HasValue && (value.Value.CompareTo(_Minimum.Value) == 0)) ||
                        !value.HasValue
                    )
                )
                    return;
                CachedFractionD = null;
                _Minimum = value;
                if (HasValue)
                    Value = Value;
            }
        }
        public T? Maximum {
            get => _Maximum;
            set {
                if (
                    (value.HasValue == _Maximum.HasValue) &&
                    (
                        (value.HasValue && (value.Value.CompareTo(_Maximum.Value) == 0)) ||
                        !value.HasValue
                    )
                )
                    return;
                CachedFractionD = null;
                _Maximum = value;
                if (HasValue)
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

            ValueDecoder = (s) => (T)Convert.ChangeType(s, typeof(T));
            var fp = NumberFormatInfo.CurrentInfo;
            ValueEncoder = (v) => Convert.ToString(v, fp);
        }

        private T? ClampValue (T value) {
            if (_Minimum.HasValue && (value.CompareTo(_Minimum.Value) < 0))
                value = _Minimum.Value;
            if (_Maximum.HasValue && (value.CompareTo(_Maximum.Value) > 0))
                value = _Maximum.Value;

            if (ValueFilter != null)
                return ValueFilter(value);
            else
                return value;
        }

        protected override void OnValueChanged () {
            try {
                if (!IsSettingValue) {
                    var newValue = ValueDecoder(Text);
                    var converted = ClampValue((T)newValue);
                    if (converted == null)
                        return;
                    HasValue = true;
                    _Value = converted.Value;
                }
                FireEvent(UIEvents.ValueChanged);
            } catch {
            }
        }

        private void FinalizeValue () {
            OnValueChanged();
            SetText(ValueEncoder(_Value), true);
        }

        protected override void ComputePadding (UIOperationContext context, IDecorator decorations, out Margins result) {
            base.ComputePadding(context, decorations, out result);

            if (Increment.HasValue) {
                result.Left += ArrowPadding;
                result.Right += ArrowPadding;
            }

            // If there's a gauge, adjust our content box based on its padding so that it doesn't overlap
            //  our text or selection box
            var gauge = context.DecorationProvider.ParameterGauge;
            var y1 = gauge.Padding.Top * context.DecorationProvider.SizeScaleRatio.Y;
            var y2 = gauge.Padding.Bottom * context.DecorationProvider.SizeScaleRatio.Y;
            if ((gauge != null) && Minimum.HasValue && Maximum.HasValue) {
                if (gauge.Padding.Top > 0)
                    result.Top += y1;
                if (gauge.Padding.Bottom > 0)
                    result.Bottom += y2;
            }
        }

        void RasterizeArrow (ref ImperativeRenderer renderer, RectF box, bool facingRight) {
            Vector2 a = !facingRight ? box.Extent : box.Position,
                b = !facingRight 
                    ? new Vector2(box.Position.X, box.Center.Y)
                    : new Vector2(box.Extent.X, box.Center.Y),
                c = !facingRight
                    ? new Vector2(box.Extent.X, box.Position.Y)
                    : new Vector2(box.Position.X, box.Extent.Y);

            float alpha = 1;
            if (_Minimum.HasValue && !facingRight)
                if (_Minimum.Value.CompareTo(_Value) >= 0)
                    alpha = 0.6f;
            if (_Maximum.HasValue && facingRight)
                if (_Maximum.Value.CompareTo(_Value) <= 0)
                    alpha = 0.6f;

            renderer.RasterizeTriangle(
                a, b, c, radius: 0f, outlineRadius: 1f,
                innerColor: Color.White * alpha, outerColor: Color.White * alpha, 
                outlineColor: Color.Black
            );
        }

        private RectF ComputeGaugeBox (IDecorator decorations, RectF box) {
            if (decorations == null)
                return default(RectF);
            if (!Minimum.HasValue || !Maximum.HasValue)
                return default(RectF);
            var gaugeBox = box;
            gaugeBox.Top += decorations.Margins.Top;
            gaugeBox.Left += decorations.Margins.Left;
            gaugeBox.Width -= decorations.Margins.X;
            gaugeBox.Height -= decorations.Margins.Y;
            gaugeBox.Top = gaugeBox.Extent.Y - (decorations.Padding.Y * Context.Decorations.SizeScaleRatio.Y);
            gaugeBox.Height = box.Extent.Y - gaugeBox.Top - decorations.Margins.Bottom;
            return gaugeBox;
        }

        private RectF ComputeArrowBox (RectF contentBox, bool facingRight) {
            if (Increment == null)
                return default(RectF);

            var box = contentBox;
            // Compensate for padding
            box.Left -= ArrowPadding;
            box.Width -= (ArrowPadding * 2);

            var space = (box.Height - ArrowHeight) / 2f;
            box.Top += space;
            box.Width = ArrowWidth;
            box.Height = ArrowHeight;
            if (!facingRight)
                return box;
            box.Left = contentBox.Extent.X + (ArrowPadding - ArrowWidth);
            return box;
        }

        protected T CachedFractionValue;
        protected double? CachedFractionD;
        protected double FractionD {
            get {
                if (!_Maximum.HasValue)
                    return double.NaN;
                if (CachedFractionD.HasValue && (CachedFractionValue.CompareTo(_Value) == 0))
                    return CachedFractionD.Value;

                // FIXME: Handle omitted minimum?
                var sub = Arithmetic.GetOperator<T, T>(Arithmetic.Operators.Subtract);
                var range = sub(_Maximum.Value, _Minimum.Value);
                var result = Convert.ToDouble(sub(_Value, _Minimum.Value)) / Convert.ToDouble(range);
                CachedFractionValue = _Value;
                CachedFractionD = result;
                return result;
            }
        }

        protected override void OnRasterize (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings, IDecorator decorations) {
            base.OnRasterize(context, ref renderer, settings, decorations);

            var gauge = context.DecorationProvider.ParameterGauge;
            if ((Minimum.HasValue && Maximum.HasValue) && (gauge != null)) {
                var gaugeBox = ComputeGaugeBox(gauge, settings.Box);
                var fraction = FractionD;
                if (Exponent.HasValue)
                    fraction = 1 - Math.Pow(1 - fraction, Exponent.Value);
                var tempSettings = settings;
                tempSettings.State = settings.State & ~ControlStates.Hovering;
                if (gaugeBox.Contains(context.MousePosition) || IsDraggingGauge)
                    tempSettings.State |= ControlStates.Hovering;
                gaugeBox.Width = Math.Min(Math.Max(gaugeBox.Width * (float)fraction, gauge.Padding.X), settings.Box.Width - gauge.Margins.X);
                // HACK: Compensate for the hitbox being too small for some reason
                gaugeBox.Top += 1;
                gaugeBox.Height -= 1;
                tempSettings.Box = settings.ContentBox;
                tempSettings.ContentBox = gaugeBox;
                gauge.Rasterize(context, ref renderer, tempSettings);
            }

            // Draw in the "Above" pass to ensure it is not clipped (better batching)
            if (context.Pass != RasterizePasses.Above)
                return;

            if (!Increment.HasValue)
                return;

            RasterizeArrow(ref renderer, ComputeArrowBox(settings.ContentBox, false), false);
            RasterizeArrow(ref renderer, ComputeArrowBox(settings.ContentBox, true), true);
        }

        private bool Adjust (bool positive, bool fast) {
            if (!Increment.HasValue)
                return false;

            var increment = Increment.Value;
            if (fast) {
                var value = _Value;
                for (int i = 0; i < FastIncrementRate; i++) {
                    if (positive)
                        value = Arithmetic.OperatorCache<T>.Add(value, increment);
                    else
                        value = Arithmetic.OperatorCache<T>.Subtract(value, increment);
                }
                Value = value;
            } else {
                if (positive)
                    Value = Arithmetic.OperatorCache<T>.Add(_Value, increment);
                else
                    Value = Arithmetic.OperatorCache<T>.Subtract(_Value, increment);
            }

            return true;
        }

        protected override bool OnKeyPress (KeyEventArgs evt) {
            switch (evt.Key) {
                case Keys.Enter:
                    FinalizeValue();
                    return true;
                case Keys.Up:
                    if (evt.IsVirtualInput)
                        return false;
                    else
                        return Adjust(true, evt.Modifiers.Control);
                case Keys.Down:
                    if (evt.IsVirtualInput)
                        return false;
                    else
                        return Adjust(false, evt.Modifiers.Control);
                case Keys.Left:
                    if (evt.IsVirtualInput)
                        return Adjust(false, evt.Modifiers.Control);
                    break;
                case Keys.Right:
                    if (evt.IsVirtualInput)
                        return Adjust(true, evt.Modifiers.Control);
                    break;
                case Keys.PageUp:
                    return Adjust(true, true);
                case Keys.PageDown:
                    return Adjust(false, true);
                case Keys.Home:
                    if (Minimum.HasValue && evt.Modifiers.Control) {
                        Value = Minimum.Value;
                        SelectNone();
                        return true;
                    }
                    break;
                case Keys.End:
                    if (Maximum.HasValue && evt.Modifiers.Control) {
                        Value = Maximum.Value;
                        SelectNone();
                        return true;
                    }
                    break;
            }

            return base.OnKeyPress(evt);
        }

        protected override bool OnEvent<TArgs> (string name, TArgs args) {
            // Don't respond to focus changes if they involve a context menu
            if (args is Menu)
                return base.OnEvent(name, args);

            if (name == UIEvents.LostFocus) {
                FinalizeValue();
                SelectNone();
            } else if (name == UIEvents.GotFocus) {
                SelectAll();
            }

            return base.OnEvent(name, args);
        }

        protected override bool OnClick (int clickCount) {
            if (clickCount > 1)
                return true;

            return base.OnClick(clickCount);
        }

        protected override void OnTick (MouseEventArgs args) {
            base.OnTick(args);
            if (args.Buttons == MouseButtons.None)
                return;

            var leftArrow = ComputeArrowBox(args.ContentBox, false);
            var rightArrow = ComputeArrowBox(args.ContentBox, true);
            var leftHeld = leftArrow.Contains(args.RelativeGlobalPosition) && leftArrow.Contains(args.MouseDownPosition);
            var rightHeld = rightArrow.Contains(args.RelativeGlobalPosition) && rightArrow.Contains(args.MouseDownPosition);
            var fast = args.Buttons == MouseButtons.Right;

            if ((leftHeld || rightHeld) && Context.UpdateRepeat(
                args.Now, args.MouseDownTimestamp, ref LastRepeatTimestamp, 
                speedMultiplier: fast ? FastRepeatSpeed : NormalRepeatSpeed, 
                accelerationMultiplier: fast ? FastAccelerationMultiplier : NormalAccelerationMultiplier
            ))
                Adjust(rightHeld, fast);
        }

        private bool TrySetNewFractionalValue (double fraction) {
            if (!_Maximum.HasValue || !_Minimum.HasValue)
                return false;
            var sub = Arithmetic.GetOperator<T, T>(Arithmetic.Operators.Subtract);
            var add = Arithmetic.GetOperator<T, T>(Arithmetic.Operators.Add);
            var range = sub(_Maximum.Value, _Minimum.Value);
            var offset = Convert.ToDouble(range) * fraction;
            var result = add(_Minimum.Value, (T)Convert.ChangeType(offset, typeof(T)));
            Value = result;
            SelectNone();
            return true;
        }

        protected override bool OnMouseEvent (string name, MouseEventArgs args) {
            var gauge = Context.Decorations.ParameterGauge;
            if (gauge != null) {
                var gaugeBox = ComputeGaugeBox(gauge, args.Box);
                if (gaugeBox.Contains(args.MouseDownPosition)) {
                    IsDraggingGauge = (args.Buttons == MouseButtons.Left);
                    double fraction = Arithmetic.Saturate((args.RelativeGlobalPosition.X - args.Box.Left) / args.Box.Width);
                    if (Exponent.HasValue)
                        fraction = 1 - Math.Pow(1 - fraction, 1.0 / Exponent.Value);
                    if ((args.Buttons == MouseButtons.Left) || (args.PreviousButtons == MouseButtons.Left))
                        return TrySetNewFractionalValue(fraction);
                    else
                        return true;
                } else {
                    IsDraggingGauge = false;
                }
            }

            var leftArrow = ComputeArrowBox(args.ContentBox, false);
            var rightArrow = ComputeArrowBox(args.ContentBox, true);
            if (leftArrow.Contains(args.RelativeGlobalPosition) || rightArrow.Contains(args.RelativeGlobalPosition)) {
                if (name == UIEvents.MouseDown) {
                    Adjust(rightArrow.Contains(args.RelativeGlobalPosition), (args.Buttons == MouseButtons.Right));
                    SelectNone();
                }

                return true;
            }

            if (leftArrow.Contains(args.MouseDownPosition) || rightArrow.Contains(args.MouseDownPosition))
                return true;

            return base.OnMouseEvent(name, args);
        }

        // HACK: Attach scrolling to our value instead of our text viewport so that the user can
        //  adjust our value the way they would normally scroll the view (with mousewheel, etc)
        const float VirtualScrollScale = 1500f;

        Vector2 IScrollableControl.ScrollOffset => new Vector2((float)FractionD * VirtualScrollScale, 0f);
        bool IScrollableControl.Scrollable {
            get => true;
            set {
            }
        }

        bool IScrollableControl.AllowDragToScroll => false;
        Vector2? IScrollableControl.MinScrollOffset => new Vector2(0f, 0f);
        Vector2? IScrollableControl.MaxScrollOffset => new Vector2(VirtualScrollScale, 0f);

        bool IScrollableControl.TrySetScrollOffset (Vector2 value, bool forUser) {
            return TrySetNewFractionalValue(Arithmetic.Saturate(value.X / VirtualScrollScale));
        }
    }
}
