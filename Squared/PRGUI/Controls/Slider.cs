using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Squared.Game;
using Squared.PRGUI.Decorations;
using Squared.Render.Convenience;
using Squared.Util;
using Squared.Util.Text;

namespace Squared.PRGUI.Controls {
    public class Slider : Control, ICustomTooltipTarget {
        public const int ControlMinimumHeight = 28, ControlMinimumWidth = 100,
            ThumbMinimumWidth = 12, MaxNotchCount = 128;
        public const float NotchThickness = 0.75f;
        public static readonly Color NotchColor = Color.Black * 0.2f;

        public float Minimum = 0, Maximum = 100;

        private float _Value = 50;

        public float? NotchInterval;
        public float KeyboardSpeed = 1;
        public float NotchMagnetism = 99999;
        public bool SnapToNotch = false;
        public bool Integral = false;

        public float Value {
            get => ClampValue(_Value);
            set {
                value = ClampValue(value);
                if (value == _Value)
                    return;

                _Value = value;
                FireEvent(UIEvents.ValueChanged);
            }
        }

        protected bool HasCustomTooltipContent => TooltipContent.GetText != GetDefaultTooltip;

        float? ICustomTooltipTarget.TooltipDisappearDelay => null;
        float? ICustomTooltipTarget.TooltipAppearanceDelay => HasCustomTooltipContent ? (float?)null : 0f;
        bool ICustomTooltipTarget.ShowTooltipWhileMouseIsHeld => !HasCustomTooltipContent;
        bool ICustomTooltipTarget.ShowTooltipWhileMouseIsNotHeld => HasCustomTooltipContent;
        bool ICustomTooltipTarget.ShowTooltipWhileKeyboardFocus => true;
        bool ICustomTooltipTarget.HideTooltipOnMousePress => HasCustomTooltipContent;

        /// <summary>
        /// A string.Format format string, where {0} is Value, {1} is Minimum, {2} is Maximum, and {3} is Value scaled to [0, 1]
        /// </summary>
        public string TooltipFormat = null;

        bool _WasMouseOverThumb = false;

        public Slider () : base () {
            AcceptsFocus = true;
            AcceptsMouseInput = true;
            TooltipContent = new AbstractTooltipContent(GetDefaultTooltip);
        }

        private static AbstractString GetDefaultTooltip (Control c) {
            var s = (Slider)c;
            if (s.TooltipFormat != null)
                return string.Format(s.TooltipFormat, s.Value, s.Minimum, s.Maximum, (s.Value - s.Minimum) / (s.Maximum - s.Minimum));
            else
                return $"{s.SmartFormat(s.Value)}/{s.SmartFormat(s.Maximum)}";
        }

        private string SmartFormat (float value) {
            if ((int)value == value)
                return value.ToString();
            else
                return value.ToString("F");
        }

        private float ClampValue (float value) {
            if (Integral)
                value = (float)Math.Round(value, MidpointRounding.AwayFromZero);
            return Arithmetic.Clamp(value, Minimum, Maximum);
        }

        protected override IDecorator GetDefaultDecorations (IDecorationProvider provider) {
            return provider.Slider;
        }

        protected override void ComputeSizeConstraints (out float? minimumWidth, out float? minimumHeight, out float? maximumWidth, out float? maximumHeight) {
            var decorations = GetDefaultDecorations(Context.Decorations);
            base.ComputeSizeConstraints(out minimumWidth, out minimumHeight, out maximumWidth, out maximumHeight);
            minimumHeight = Math.Max(minimumHeight ?? 0, ControlMinimumHeight);
            minimumWidth = Math.Max(minimumWidth ?? 0, ControlMinimumWidth);
        }

        private float ApplyNotchMagnetism (float result) {
            var interval = (NotchInterval ?? 0);

            if (SnapToNotch && (interval > float.Epsilon)) {
                var rangeSize = (Maximum - Minimum);
                result -= Minimum;
                float a = Arithmetic.Saturate((float)Math.Floor(result / interval) * interval, rangeSize), 
                    b = Arithmetic.Saturate((float)Math.Ceiling(result / interval) * interval, rangeSize), 
                    distA = Math.Abs(result - a), distB = Math.Abs(result - b);

                if (distA < NotchMagnetism) {
                    if (distB < NotchMagnetism) {
                        if (distA <= distB)
                            result = a;
                        else
                            result = b;
                    } else {
                        result = a;
                    }
                } else if (distB < NotchMagnetism) {
                    result = b;
                }
                result += Minimum;
            }

            return ClampValue(result);
        }

        private float ValueFromPoint (RectF contentBox, Vector2 globalPosition) {
            var thumbSize = ComputeThumbSize();
            var localPosition = globalPosition - contentBox.Position;
            var scaledValue = Arithmetic.Saturate(localPosition.X / contentBox.Width);
            var rangeSize = (Maximum - Minimum);
            var result = (rangeSize * scaledValue) + Minimum;
            result = ApplyNotchMagnetism(result);
            return result;
        }

        private RectF ComputeThumbBox (RectF contentBox, float value) {
            var thumbSize = ComputeThumbSize();
            thumbSize.Y = contentBox.Height;
            var trackSpace = contentBox.Width;
            var scaledValue = (ClampValue(value) - Minimum) / (Maximum - Minimum);
            var thumbPosition = new Vector2(scaledValue * trackSpace - (thumbSize.X * 0.5f), 0) + contentBox.Position;
            return new RectF(thumbPosition.Floor(), thumbSize);
        }

        protected override bool OnEvent<T> (string name, T args) {
            if (args is MouseEventArgs)
                return OnMouseEvent(name, (MouseEventArgs)(object)args);
            else if (args is KeyEventArgs)
                return OnKeyEvent(name, (KeyEventArgs)(object)args);
            else
                return base.OnEvent(name, args);
        }

        private bool OnKeyEvent (string name, KeyEventArgs args) {
            switch (name) {
                case UIEvents.KeyPress:
                    Context.OverrideKeyboardSelection(this);
                    var speed = args.Modifiers.Control
                        ? (NotchInterval ?? 10 * KeyboardSpeed)
                        : KeyboardSpeed;
                    float oldValue = Value, newValue;
                    switch (args.Key) {
                        case Keys.Up:
                            newValue = Minimum;
                            break;
                        case Keys.Down:
                            newValue = Maximum;
                            break;
                        case Keys.Left:
                            newValue = oldValue - speed;
                            break;
                        case Keys.Right:
                            newValue = oldValue + speed;
                            break;
                        default:
                            return false;
                    }

                    var snappedValue = ApplyNotchMagnetism(newValue);
                    var snapDelta = (snappedValue - oldValue);
                    int snapDirection = Math.Sign(snapDelta), moveDirection = Math.Sign(newValue - oldValue);
                    // Ensure we don't get stuck as a result of value snapping
                    if ((Math.Abs(snapDelta) <= float.Epsilon) || (snapDirection != moveDirection))
                        Value = newValue;
                    else
                        Value = snappedValue;
                    return true;
            }

            return false;
        }

        private bool OnMouseEvent (string name, MouseEventArgs args) {
            switch (name) {
                case UIEvents.MouseDown:
                case UIEvents.MouseMove:
                case UIEvents.MouseUp:
                    if ((name == UIEvents.MouseMove) && (args.Buttons == MouseButtons.None))
                        return true;

                    var newValue = ValueFromPoint(GetRect(Context.Layout, contentRect: true), args.RelativeGlobalPosition);
                    Value = newValue;

                    return true;
            }

            return false;
        }

        private Vector2 ComputeThumbSize () {
            var thumb = Context.Decorations.SliderThumb;
            var thumbSize = (thumb.Margins + thumb.Padding).Size;
            thumbSize.X = Math.Max(ThumbMinimumWidth, thumbSize.X);
            return thumbSize;
        }

        protected override Margins ComputePadding (UIOperationContext context, IDecorator decorations) {
            var result = base.ComputePadding(context, decorations);
            var thumbSize = ComputeThumbSize();
            result.Left += thumbSize.X * 0.5f;
            result.Right += thumbSize.X * 0.5f;
            return result;
        }

        protected override void OnRasterize (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings, IDecorator decorations) {
            base.OnRasterize(context, ref renderer, settings, decorations);

            if (context.Pass == RasterizePasses.Below) {
                var rangeSize = Maximum - Minimum;
                var interval = Arithmetic.Saturate(NotchInterval ?? 0, rangeSize);
                var hasInterval = (interval > float.Epsilon) && (interval < rangeSize) && ((rangeSize / interval) < MaxNotchCount);

                if (hasInterval)
                    DrawNotches(context, ref renderer, settings, decorations, interval, rangeSize);
            }

            var thumb = Context.Decorations.SliderThumb;
            var thumbSettings = settings;
            // FIXME: Apply padding
            thumbSettings.Box = ComputeThumbBox(settings.ContentBox, _Value);
            thumbSettings.ContentBox = thumbSettings.Box;
            var hoveringThumb = 
                (settings.State.HasFlag(ControlStates.Hovering))
                    ? thumbSettings.Box.Contains(context.UIContext.CalculateRelativeGlobalPosition(this, context.MousePosition))
                    : false;
            _WasMouseOverThumb = hoveringThumb;
            thumbSettings.State = (thumbSettings.State & ~ControlStates.Hovering);
            if (hoveringThumb)
                thumbSettings.State |= ControlStates.Hovering;
            thumb.Rasterize(context, ref renderer, thumbSettings);
            // renderer.RasterizeRectangle(thumbSettings.Box.Position, thumbSettings.Box.Extent, 1f, Color.Red * 0.5f);
        }

        private void DrawNotches (
            UIOperationContext context, ref ImperativeRenderer renderer, 
            DecorationSettings settings, IDecorator decorations, 
            float interval, float rangeSize
        ) {
            var numSteps = (int)Math.Floor(rangeSize / interval);

            float x = interval, y = settings.ContentBox.Top + 0.5f;
            for (int i = 0; i < numSteps; i++, x += interval) {
                if ((Math.Abs(x - rangeSize) <= float.Epsilon) || (x >= Maximum))
                    break;

                var offset = (x / rangeSize) * settings.ContentBox.Width;
                renderer.RasterizeLineSegment(
                    new Vector2(settings.ContentBox.Left + offset, y),
                    new Vector2(settings.ContentBox.Left + offset, settings.ContentBox.Extent.Y + 1f),
                    NotchThickness, NotchColor
                );
            }
        }
    }
}
