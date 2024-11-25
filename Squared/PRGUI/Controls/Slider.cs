﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Squared.Game;
using Squared.PRGUI.Decorations;
using Squared.PRGUI.Layout;
using Squared.Render;
using Squared.Render.Convenience;
using Squared.Util;
using Squared.Util.Text;

namespace Squared.PRGUI.Controls {
    public class Slider : Control, ICustomTooltipTarget, Accessibility.IReadingTarget, IValueControl<float>, ISelectionBearer, IScrollableControl {
        public const int ControlMinimumHeight = 32, ControlMinimumWidth = 100,
            ThumbMinimumWidth = 13, MaxNotchCount = 128;
        public const float NotchThickness = 0.75f;
        public static readonly pSRGBColor NotchColor = pSRGBColor.Black(0.2f),
            CenterMarkColor = pSRGBColor.White(0.3f);

        /// <summary>
        /// If set, the slider will increase its minimum size to match that of the sibling to produce consistent row layouts in a 2-column view
        /// </summary>
        public Control RowSibling;

        // HACK: Track whether this is a default-initialized slider so we can respond appropriately to min/max changes
        private bool _HasValue;
        private float _Minimum = 0, _Maximum = 100;
        private float _Value = 0;

        private RectF? _LastThumbBox;
        bool _WasMouseOverThumb = false;

        public float? NotchInterval;
        public float KeyboardSpeed = 1;
        public float NotchMagnetism = 99999;
        public bool SnapToNotch = false;
        public bool Integral = false;

        public float Minimum {
            get => _Minimum;
            set {
                if (_Minimum == value)
                    return;
                _Minimum = value;
                _Value = ClampValue(_Value);
            }
        }

        public float Maximum {
            get => _Maximum;
            set {
                if (_Maximum == value)
                    return;
                _Maximum = value;
                _Value = ClampValue(_Value);
            }
        }

        public float Value {
            get => 
                _HasValue 
                    ? ClampValue(_Value)
                    : ClampValue((Minimum + Maximum) / 2);
            set {
                SetValue(value, false);
            }
        }

        public void SetValue (float value, bool forUserInput) {
            _HasValue = true;

            value = ClampValue(value);
            if (value == _Value)
                return;

            _Value = value;
            InvalidateTooltip();
            ScrollInputValue = null;
            FireEvent(UIEvents.ValueChanged);
            if (forUserInput)
                FireEvent(UIEvents.ValueChangedByUser);
        }

        AbstractString Accessibility.IReadingTarget.Text => base.TooltipContent.GetPlainText(this);
        void Accessibility.IReadingTarget.FormatValueInto (StringBuilder sb) => FormatValue(sb);

        protected bool HasCustomTooltipContent => !TooltipContent.Equals(default(AbstractTooltipContent));

        AbstractTooltipContent _GetDefaultTooltip = new AbstractTooltipContent(GetDefaultTooltip);
        AbstractTooltipContent ICustomTooltipTarget.GetContent () => _GetDefaultTooltip;
        TooltipTargetSettings _TooltipSettings = new TooltipTargetSettings {
            ShowWhileMouseIsHeld = true,
            ShowWhileKeyboardFocused = true,
            HideOnMousePress = false
        };
        TooltipTargetSettings ICustomTooltipTarget.TooltipSettings {
            get {
                _TooltipSettings.AppearDelay = HasCustomTooltipContent ? (float?)null : 0f;
                _TooltipSettings.ShowWhileMouseIsNotHeld = HasCustomTooltipContent;
                return _TooltipSettings;
            }
        }
        Control ICustomTooltipTarget.Anchor => null;

        private string _TooltipFormat = null;
        /// <summary>
        /// A string.Format format string, where {0} is Value, {1} is Minimum, {2} is Maximum, and {3} is Value scaled to [0, 1]
        /// </summary>
        public string TooltipFormat {
            get => _TooltipFormat;
            set {
                if (_TooltipFormat == value)
                    return;
                _TooltipFormat = value;
                InvalidateTooltip();
            }
        }

        public Slider () : base () {
            AcceptsFocus = true;
            AcceptsMouseInput = true;
        }

        private readonly StringBuilder TooltipBuilder = new StringBuilder();
        private readonly object[] TooltipFormatArgs = new object[4];

        public void FormatValue (StringBuilder sb) {
            var value = (float)Math.Round(Value, 2, MidpointRounding.AwayFromZero);
            if (TooltipFormat != null) {
                TooltipFormatArgs[0] = value;
                TooltipFormatArgs[1] = Minimum;
                TooltipFormatArgs[2] = Maximum;
                if (Maximum != Minimum)
                    TooltipFormatArgs[3] = (value - Minimum) / (Maximum - Minimum);
                else
                    TooltipFormatArgs[3] = 0;
                sb.AppendFormat(TooltipFormat, TooltipFormatArgs);
            } else {
                SmartAppend(sb, value);
                sb.Append('/');
                SmartAppend(sb, Maximum);
            }
        }

        private static AbstractString GetDefaultTooltip (Control c) {
            var s = (Slider)c;
            s.TooltipBuilder.Clear();
            if (!s.TooltipContent.Equals(default(AbstractTooltipContent))) {
                // FIXME: UserData
                var ct = s.TooltipContent.Get(s, out _);
                s.TooltipBuilder.Append(ct.ToString());
                s.TooltipBuilder.Append(": ");
            }
            s.FormatValue(s.TooltipBuilder);
            return s.TooltipBuilder;
        }

        private static void SmartAppend (StringBuilder sb, float value) {
            if ((int)value == value)
                sb.Append((int)value);
            else
                sb.AppendFormat("{0:F}", value);
        }

        private float ClampValue (float value) {
            if (Integral)
                value = (float)Math.Round(value, MidpointRounding.AwayFromZero);
            return Arithmetic.Clamp(value, Minimum, Maximum);
        }

        protected override IDecorator GetDefaultDecorator (IDecorationProvider provider) {
            return provider.Slider;
        }

        protected override void ComputeSizeConstraints (ref UIOperationContext context, ref ControlDimension width, ref ControlDimension height, Vector2 sizeScale) {
            var decorations = GetDefaultDecorator(context.DecorationProvider);
            var glyphSource = GetGlyphSource(ref context, decorations);
            base.ComputeSizeConstraints(ref context, ref width, ref height, sizeScale);
            height.Minimum = Math.Max(Math.Max(height.Minimum ?? 0, ControlMinimumHeight * sizeScale.Y), (glyphSource?.LineSpacing ?? 0) * 0.6f);
            width.Minimum = Math.Max(width.Minimum ?? 0, ControlMinimumWidth * sizeScale.X);

            if ((RowSibling != null) && (RowSibling != this) && !(RowSibling is Slider s)) {
                GetSizeConstraints(RowSibling, ref context, out _, out var siblingHeight);
                height = height.Intersection(ref siblingHeight);
            }
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
            if (args is MouseEventArgs ma)
                return OnMouseEvent(name, ma);
            else if (KeyEventArgs.From(ref args, out var ka))
                return OnKeyEvent(name, ka);
            else
                return base.OnEvent(name, args);
        }

        private bool OnKeyEvent (string name, KeyEventArgs args) {
            switch (name) {
                case UIEvents.KeyPress:
                    Context.OverrideKeyboardSelection(this, true);
                    var speed = args.Modifiers.Control
                        ? Math.Max(10 * KeyboardSpeed, (NotchInterval ?? 0))
                        : KeyboardSpeed;
                    float oldValue = Value, newValue;
                    switch (args.Key) {
                        case Keys.Left:
                            newValue = oldValue - speed;
                            break;
                        case Keys.Right:
                            newValue = oldValue + speed;
                            break;
                        case Keys.Home:
                            newValue = (oldValue > 0) ? 0 : Minimum;
                            break;
                        case Keys.End:
                            newValue = (oldValue < 0) ? 0 : Maximum;
                            break;
                        default:
                            return false;
                    }

                    var snappedValue = ApplyNotchMagnetism(newValue);
                    var snapDelta = (snappedValue - oldValue);
                    int snapDirection = Math.Sign(snapDelta), moveDirection = Math.Sign(newValue - oldValue);
                    // Ensure we don't get stuck as a result of value snapping
                    if ((Math.Abs(snapDelta) <= float.Epsilon) || (snapDirection != moveDirection))
                        SetValue(newValue, true);
                    else
                        SetValue(snappedValue, true);
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
                    if ((args.Buttons != MouseButtons.Left) && (args.PreviousButtons != MouseButtons.Left))
                        return true;
                    if (args.MouseCaptured != this)
                        return false;

                    var newValue = ValueFromPoint(GetRect(contentRect: true), args.RelativeGlobalPosition);
                    SetValue(newValue, true);

                    return true;
            }

            return false;
        }

        private Vector2 ComputeThumbSize () {
            var thumb = Context.Decorations.SliderThumb;
            var thumbSize = (thumb.Margins + thumb.Padding).Size * Context.Decorations.SizeScaleRatio;
            thumbSize.X = Math.Max(ThumbMinimumWidth, thumbSize.X);
            return thumbSize;
        }

        protected override void ComputeAppearanceSpacing (
            ref UIOperationContext context, IDecorator decorations, 
            out Margins scaledMargins, out Margins scaledPadding, out Margins unscaledPadding
        ) {
            base.ComputeAppearanceSpacing(ref context, decorations, out scaledMargins, out scaledPadding, out unscaledPadding);
            var thumbSize = ComputeThumbSize();
            unscaledPadding.Left += thumbSize.X * 0.5f;
            unscaledPadding.Right += thumbSize.X * 0.5f;

            if ((RowSibling != null) && (RowSibling != this)) {
                ComputeAppearanceSpacing(RowSibling, ref context, out var sm, out var sp, out var usp);
                scaledMargins = scaledMargins.Max(ref sm);
                scaledPadding = scaledPadding.Max(ref sp);
                unscaledPadding = unscaledPadding.Max(ref usp);
            }
        }

        protected override void OnRasterize (ref UIOperationContext context, ref RasterizePassSet passSet, DecorationSettings settings, IDecorator decorations) {
            base.OnRasterize(ref context, ref passSet, settings, decorations);

            passSet.AdjustAllLayers(1);

            var rangeSize = Maximum - Minimum;
            var interval = Arithmetic.Saturate(NotchInterval ?? 0, rangeSize);
            var hasInterval = (interval > float.Epsilon) && (interval < rangeSize) && ((rangeSize / interval) < MaxNotchCount);

            if (hasInterval)
                DrawNotches(ref context, ref passSet.Below, settings, decorations, interval, rangeSize);

            if ((Maximum > 0) && (Minimum < 0))
                DrawCenterMark(ref context, ref passSet.Below, settings, decorations, rangeSize);

            passSet.Below.Layer += 1;

            var thumb = Context.Decorations.SliderThumb;
            var thumbSettings = settings;
            // FIXME: Apply padding
            _LastThumbBox = thumbSettings.Box = ComputeThumbBox(settings.ContentBox, Value);
            thumbSettings.ContentBox = thumbSettings.Box;
            var hoveringThumb = 
                (settings.HasStateFlag(ControlStates.Hovering))
                    ? thumbSettings.Box.Contains(context.UIContext.CalculateRelativeGlobalPosition(this, context.MousePosition))
                    : false;
            _WasMouseOverThumb = hoveringThumb;
            thumbSettings.State = (thumbSettings.State & ~ControlStates.Hovering);
            if (hoveringThumb)
                thumbSettings.State |= ControlStates.Hovering;
            thumb.Rasterize(ref context, ref passSet, ref thumbSettings);
            // renderer.RasterizeRectangle(thumbSettings.Box.Position, thumbSettings.Box.Extent, 1f, Color.Red * 0.5f);
        }

        private void DrawNotches (
            ref UIOperationContext context, ref ImperativeRenderer renderer, 
            DecorationSettings settings, IDecorator decorations, 
            float interval, float rangeSize
        ) {
            var numSteps = (int)Math.Ceiling(rangeSize / interval) + 1;

            float x = 0, y = settings.ContentBox.Top + 0.5f;
            for (int i = 0; i < numSteps; i++, x += interval) {
                var offset = (x / rangeSize) * settings.ContentBox.Width;
                renderer.RasterizeLineSegment(
                    new Vector2(settings.ContentBox.Left + offset, y),
                    new Vector2(settings.ContentBox.Left + offset, settings.ContentBox.Extent.Y + 1f),
                    NotchThickness, NotchColor
                );
            }
        }

        private void DrawCenterMark (
            ref UIOperationContext context, ref ImperativeRenderer renderer, 
            DecorationSettings settings, IDecorator decorations, 
            float rangeSize
        ) {
            float offset = (-Minimum / rangeSize) * settings.ContentBox.Width, y = settings.ContentBox.Top + 0.5f;
            renderer.RasterizeLineSegment(
                new Vector2(settings.ContentBox.Left + offset, y),
                new Vector2(settings.ContentBox.Left + offset, settings.ContentBox.Extent.Y + 1f),
                NotchThickness, CenterMarkColor
            );
        }

        bool ISelectionBearer.HasSelection => true;
        RectF? ISelectionBearer.SelectionRect => _LastThumbBox;
        Control ISelectionBearer.SelectedControl => null;

        float? ScrollInputValue;
        const float ScrollScale = 1000f;

        Vector2 IScrollableControl.ScrollOffset => new Vector2(
            ScrollInputValue ?? ((_Value - _Minimum) / (_Maximum - _Minimum) * ScrollScale), 
            0f
        );
        bool IScrollableControl.Scrollable {
            get => true;
            set {
            }
        }

        bool IScrollableControl.AllowDragToScroll => false;
        Vector2? IScrollableControl.MinScrollOffset => new Vector2(0f, 0f);
        Vector2? IScrollableControl.MaxScrollOffset => new Vector2(ScrollScale, 0f);

        bool IScrollableControl.TrySetScrollOffset (Vector2 value, bool forUser) {
            var newValue = _Minimum + ((value.X / ScrollScale) * (_Maximum - _Minimum));
            SetValue(newValue, forUser);
            ScrollInputValue = value.X;
            return true;
        }
    }
}
