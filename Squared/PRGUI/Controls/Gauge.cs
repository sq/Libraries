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
        public const int ControlMinimumHeight = 28, ControlMinimumWidth = 100,
            ThumbMinimumWidth = 13;

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

        protected override void ComputeSizeConstraints (out float? minimumWidth, out float? minimumHeight, out float? maximumWidth, out float? maximumHeight) {
            var decorations = GetDefaultDecorator(Context.Decorations);
            Color? color = null;
            decorations.GetTextSettings(default(UIOperationContext), default(ControlStates), out Render.Material temp, ref color);
            base.ComputeSizeConstraints(out minimumWidth, out minimumHeight, out maximumWidth, out maximumHeight);
            minimumHeight = Math.Max(Math.Max(minimumHeight ?? 0, ControlMinimumHeight * Context.Decorations.SizeScaleRatio.Y), (decorations.GlyphSource?.LineSpacing ?? 0) * 0.6f);
            minimumWidth = Math.Max(minimumWidth ?? 0, ControlMinimumWidth * Context.Decorations.SizeScaleRatio.X);
        }
        
        protected override void OnRasterize (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings, IDecorator decorations) {
            var fill = context.DecorationProvider.Gauge;
            var fillWidth = Arithmetic.Saturate(ValueTween.Get(context.NowL));
            settings.ContentBox.Width *= fillWidth;
            base.OnRasterize(context, ref renderer, settings, decorations);
        }
    }
}
