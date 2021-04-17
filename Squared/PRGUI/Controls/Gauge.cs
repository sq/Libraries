using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Squared.Game;
using Squared.PRGUI.Decorations;
using Squared.PRGUI.Layout;
using Squared.Render.Convenience;
using Squared.Util;
using Squared.Util.Text;

namespace Squared.PRGUI.Controls {
    public class Gauge : Control, Accessibility.IReadingTarget, IValueControl<float>, IHasDescription {
        public GaugeDirection Direction = GaugeDirection.Auto;

        public const int ControlMinimumHeight = 30, ControlMinimumLength = 125;

        private float _Value = 0.5f, _Limit = 1.0f;
        public float FastAnimationThreshold = 0.33f,
            MinAnimationLength = 0.1f,
            MaxAnimationLength = 0.4f,
            FastAnimationLength = 0.05f;

        private Tween<float> ValueTween = 0.5f;

        public string Description { get; set; }

        public float Value {
            get => _Value;
            set {
                SetValue(value, true);
            }
        }

        public float Limit {
            get => _Limit;
            set {
                _Limit = value;
                SetValue(_Value, false);
            }
        }

        public void SetValue (float value, bool enableAnimation = true) {
            value = Math.Max(Math.Min(value, _Limit), 0f);

            if (_Value == value)
                return;

            if (enableAnimation)
                AnimateTo(value);
            else
                ValueTween = value;
            _Value = value;
            FireEvent(UIEvents.ValueChanged);
        }

        AbstractString Accessibility.IReadingTarget.Text {
            get {
                var sb = new StringBuilder();
                var desc = (Description ?? base.TooltipContent.GetPlainText(this));
                if (desc != null) {
                    sb.Append(desc);
                    sb.Append(": ");
                }
                FormatValue(sb);
                return sb;
            }
        }
        void Accessibility.IReadingTarget.FormatValueInto (StringBuilder sb) => FormatValue(sb);

        public Gauge () : base () {
            AcceptsFocus = false;
            AcceptsMouseInput = false;
        }

        public void FormatValue (StringBuilder sb) {
            SmartAppend(sb, (float)Math.Round(Value * 100, 2, MidpointRounding.AwayFromZero));
            sb.Append("%");
        }

        private void AnimateTo (float to) {
            // HACK
            if (Context == null) {
                ValueTween = to;
                return;
            }

            var from = ValueTween.Get(Context.NowL);
            var distance = Math.Abs(to - from);
            float length;
            if (distance >= FastAnimationThreshold)
                length = FastAnimationLength;
            else
                length = Math.Max(MinAnimationLength, (distance / FastAnimationThreshold) * MaxAnimationLength);
            length *= Context.Animations?.AnimationDurationMultiplier ?? 1;
            ValueTween = Tween.StartNow(from, to, seconds: length, now: Context.NowL);
        }

        private static void SmartAppend (StringBuilder sb, float value) {
            if ((int)value == value)
                sb.Append((int)value);
            else
                sb.AppendFormat("{0:F}", value);
        }

        protected override IDecorator GetDefaultDecorator (IDecorationProvider provider) {
            return provider.Gauge;
        }

        protected bool DetermineIfHorizontal (float? width, float? height) {
            switch (Direction) {
                default:
                case GaugeDirection.Auto:
                    float w = Math.Max(width ?? 0, Width.Fixed ?? Width.Minimum ?? 0);
                    float h = Math.Max(height ?? 0, Height.Fixed ?? Height.Minimum ?? 0);
                    return (w >= h);
                case GaugeDirection.LeftToRight:
                case GaugeDirection.RightToLeft:
                    return true;
                case GaugeDirection.TopToBottom:
                case GaugeDirection.BottomToTop:
                    return false;
            }
        }

        protected override void ComputeSizeConstraints (ref UIOperationContext context, ref ControlDimension width, ref ControlDimension height, Vector2 sizeScale) {
            var decorations = GetDefaultDecorator(Context.Decorations);
            Color? color = null;
            decorations.GetTextSettings(ref UIOperationContext.Default, default(ControlStates), out Render.Material temp, ref color);
            base.ComputeSizeConstraints(ref context, ref width, ref height, sizeScale);
            if ((Direction == GaugeDirection.Clockwise) || (Direction == GaugeDirection.CounterClockwise)) {
                var m = Math.Max(ControlMinimumLength, ControlMinimumHeight);
                height.Minimum = Math.Max(Math.Max(height.Minimum ?? 0, m * Context.Decorations.SizeScaleRatio.Y), (decorations.GlyphSource?.LineSpacing ?? 0) * 0.6f);
                width.Minimum = Math.Max(width.Minimum ?? 0, m * Context.Decorations.SizeScaleRatio.X);
            } else if (DetermineIfHorizontal(width.Minimum, height.Minimum)) {
                height.Minimum = Math.Max(Math.Max(height.Minimum ?? 0, ControlMinimumHeight * Context.Decorations.SizeScaleRatio.Y), (decorations.GlyphSource?.LineSpacing ?? 0) * 0.6f);
                width.Minimum = Math.Max(width.Minimum ?? 0, ControlMinimumLength * Context.Decorations.SizeScaleRatio.X);
            } else {
                height.Minimum = Math.Max(Math.Max(height.Minimum ?? 0, ControlMinimumLength * Context.Decorations.SizeScaleRatio.Y), (decorations.GlyphSource?.LineSpacing ?? 0) * 0.6f);
                width.Minimum = Math.Max(width.Minimum ?? 0, ControlMinimumHeight * Context.Decorations.SizeScaleRatio.X);
            }
        }

        private static readonly string[] DirectionNames = new[] {
            "auto", "ltr", "rtl", "ttb", "btt", "cw", "ccw"
        };

        protected override bool IsPassDisabled (RasterizePasses pass, IDecorator decorations) {
            return decorations.IsPassDisabled(pass);
        }

        private GaugeDirection PickDirection (ref RectF box) {
            return Direction == GaugeDirection.Auto
                ? (DetermineIfHorizontal(box.Width, box.Height) ? GaugeDirection.LeftToRight : GaugeDirection.BottomToTop)
                : Direction;
        }

        protected override DecorationSettings MakeDecorationSettings (ref RectF box, ref RectF contentBox, ControlStates state, bool compositing) {
            var direction = PickDirection(ref box);
            var result = base.MakeDecorationSettings(ref box, ref contentBox, state, compositing);
            result.Traits.Add(DirectionNames[(int)direction]);
            return result;
        }

        private void MakeContentBox (
            GaugeDirection direction, float value1, float value2, ref RectF contentBox
        ) {
            float extent;

            var fillSize = Math.Abs(value2 - value1);
            switch (direction) {
                default:
                case GaugeDirection.LeftToRight:
                case GaugeDirection.RightToLeft:
                    extent = contentBox.Extent.X;
                    break;
                case GaugeDirection.TopToBottom:
                case GaugeDirection.BottomToTop:
                    extent = contentBox.Extent.Y;
                    break;
                case GaugeDirection.Clockwise:
                case GaugeDirection.CounterClockwise:
                    float a1 = (direction == GaugeDirection.Clockwise)
                        ? value1 * 360f - 90f
                        : 360f - (value2 * 360f) - 90f;
                    // FIXME: Shrink when the value is really small so that there isn't a fill dot
                    //  when the gauge's value is 0
                    var fillRadius = (ControlMinimumHeight / 2f) - Padding.Size.Length();
                    var maxRad = Math.Min(contentBox.Width, contentBox.Height) / 2f;
                    contentBox = new RectF(
                        a1, fillSize * 360f,
                        maxRad - fillRadius, fillRadius
                    );
                    return;
            }

            switch (direction) {
                default:
                case GaugeDirection.LeftToRight:
                    contentBox.Left += value1 * contentBox.Width;
                    contentBox.Width *= fillSize;
                    break;
                case GaugeDirection.RightToLeft:
                    contentBox.Left = extent - (value2 * contentBox.Width);
                    contentBox.Width *= fillSize;
                    break;
                case GaugeDirection.TopToBottom:
                    contentBox.Top += value1 * contentBox.Height;
                    contentBox.Height *= fillSize;
                    break;
                case GaugeDirection.BottomToTop:
                    contentBox.Top = extent - (value2 * contentBox.Height);
                    contentBox.Height *= fillSize;
                    break;
            }
        }

        protected override void OnRasterize (ref UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings, IDecorator decorations) {
            var direction = PickDirection(ref settings.Box);
            var fill = context.DecorationProvider.Gauge;
            var originalCbox = settings.ContentBox;
            var value1 = ValueTween.Get(Context.NowL);
            settings.UserData = value1;

            MakeContentBox(direction, 0f, value1, ref settings.ContentBox);
            base.OnRasterize(ref context, ref renderer, settings, decorations);

            if ((_Limit < 1.0f) && (context.Pass == RasterizePasses.Content)) {
                settings.ContentBox = originalCbox;
                settings.UserData = _Limit;
                settings.Traits.Add("limit");
                MakeContentBox(direction, _Limit, 1f, ref settings.ContentBox);
                fill.Rasterize(ref context, ref renderer, settings);
            }
        }
    }

    public enum GaugeDirection : int {
        Auto = 0,
        LeftToRight = 1,
        RightToLeft = 2,
        TopToBottom = 3,
        BottomToTop = 4,
        Clockwise = 5,
        CounterClockwise = 6
    }
}
