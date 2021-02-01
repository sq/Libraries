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
    public class Gauge : Control, Accessibility.IReadingTarget, IValueControl<float> {
        public GaugeDirection Direction = GaugeDirection.Auto;

        public const int ControlMinimumHeight = 30, ControlMinimumLength = 125;

        private float _Value = 0.5f;
        public float FastAnimationThreshold = 0.33f,
            MinAnimationLength = 0.1f,
            MaxAnimationLength = 0.4f,
            FastAnimationLength = 0.05f;

        private Tween<float> ValueTween = 0.5f;

        public string Description;

        public float Value {
            get => _Value;
            set {
                SetValue(value, true);
            }
        }

        public void SetValue (float value, bool enableAnimation = true) {
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
                var desc = (Description ?? base.TooltipContent.Get(this));
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
            ValueTween = Tween.StartNow(from, to, length, now: Context.NowL);
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

        protected override void ComputeSizeConstraints (out float? minimumWidth, out float? minimumHeight, out float? maximumWidth, out float? maximumHeight) {
            var decorations = GetDefaultDecorator(Context.Decorations);
            Color? color = null;
            decorations.GetTextSettings(default(UIOperationContext), default(ControlStates), out Render.Material temp, ref color);
            base.ComputeSizeConstraints(out minimumWidth, out minimumHeight, out maximumWidth, out maximumHeight);
            if (DetermineIfHorizontal(minimumWidth, minimumHeight)) {
                minimumHeight = Math.Max(Math.Max(minimumHeight ?? 0, ControlMinimumHeight * Context.Decorations.SizeScaleRatio.Y), (decorations.GlyphSource?.LineSpacing ?? 0) * 0.6f);
                minimumWidth = Math.Max(minimumWidth ?? 0, ControlMinimumLength * Context.Decorations.SizeScaleRatio.X);
            } else {
                minimumHeight = Math.Max(Math.Max(minimumHeight ?? 0, ControlMinimumLength * Context.Decorations.SizeScaleRatio.Y), (decorations.GlyphSource?.LineSpacing ?? 0) * 0.6f);
                minimumWidth = Math.Max(minimumWidth ?? 0, ControlMinimumHeight * Context.Decorations.SizeScaleRatio.X);
            }
        }

        private static readonly string[] DirectionNames = new[] {
            "auto", "ltr", "rtl", "ttb", "btt"
        };

        protected override bool IsPassDisabled (RasterizePasses pass, IDecorator decorations) {
            return decorations.IsPassDisabled(pass);
        }

        private GaugeDirection PickDirection (ref RectF box) {
            return Direction == GaugeDirection.Auto
                ? (DetermineIfHorizontal(box.Width, box.Height) ? GaugeDirection.LeftToRight : GaugeDirection.BottomToTop)
                : Direction;
        }

        protected override DecorationSettings MakeDecorationSettings (ref RectF box, ref RectF contentBox, ControlStates state) {
            var direction = PickDirection(ref box);
            var result = base.MakeDecorationSettings(ref box, ref contentBox, state);
            result.Traits.Add(DirectionNames[(int)direction]);
            return result;
        }

        protected override void OnRasterize (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings, IDecorator decorations) {
            var direction = PickDirection(ref settings.Box);
            var fill = context.DecorationProvider.Gauge;
            var fillWidth = Arithmetic.Saturate(ValueTween.Get(context.NowL));
            float extent;
            switch (direction) {
                default:
                case GaugeDirection.LeftToRight:
                    settings.ContentBox.Width *= fillWidth;
                    break;
                case GaugeDirection.RightToLeft:
                    extent = settings.ContentBox.Extent.X;
                    settings.ContentBox.Width *= fillWidth;
                    settings.ContentBox.Left = extent - settings.ContentBox.Width;
                    break;
                case GaugeDirection.TopToBottom:
                    settings.ContentBox.Height *= fillWidth;
                    break;
                case GaugeDirection.BottomToTop:
                    extent = settings.ContentBox.Extent.Y;
                    settings.ContentBox.Height *= fillWidth;
                    settings.ContentBox.Top = extent - settings.ContentBox.Height;
                    break;
            }
            base.OnRasterize(context, ref renderer, settings, decorations);
        }
    }

    public enum GaugeDirection : int {
        Auto = 0,
        LeftToRight = 1,
        RightToLeft = 2,
        TopToBottom = 3,
        BottomToTop = 4
    }
}
