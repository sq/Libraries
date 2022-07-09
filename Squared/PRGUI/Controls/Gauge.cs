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
using Squared.Render;
using Squared.Render.Convenience;
using Squared.Util;
using Squared.Util.Text;

namespace Squared.PRGUI.Controls {
    public class Gauge : Control, Accessibility.IReadingTarget, IValueControl<float>, IHasDescription {
        public struct FillSettings {
            public Tween<float> Offset;
            public Tween<float>? Thickness;
            public float? Size;
            public bool? Repeat;
        }

        public struct MarkedRange {
            public string Name;
            /// <summary>
            /// If set, the range is drawn below the gauge's fill and limit, otherwise above
            /// </summary>
            public bool DrawBelow;
            /// <summary>
            /// If set, disables the default gradient applied to gauge fills
            /// </summary>
            public bool StaticColor;
            public pSRGBColor? Color;
            public Tween<float> Start, End;
            public IDecorator Decorator;
            public DenseList<string> Traits;
            public FillSettings Fill;
        }

        public GaugeDirection Direction = GaugeDirection.Auto;

        public FillSettings Fill;
        public float CircularFillThicknessFactor = 0.15f;

        public const int ControlMinimumHeight = 30, ControlMinimumLength = 125;

        private float _Value = 0.5f, _Limit = 1.0f;
        public float FastAnimationThreshold = 0.33f,
            MinAnimationLength = 0.1f,
            MaxAnimationLength = 0.4f,
            FastAnimationLength = 0.05f;

        private Tween<float> ValueTween = 0.5f;

        public readonly List<MarkedRange> MarkedRanges = new List<MarkedRange>();

        public string Description { get; set; }

        public float Value {
            get => _Value;
            set => SetValue(value, true);
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

        public virtual void FormatValue (StringBuilder sb) {
            SmartAppend(sb, (float)Math.Round(Value * 100, 2, MidpointRounding.AwayFromZero));
            sb.Append("%");
        }

        public bool IsAnimating => !ValueTween.IsConstant && !ValueTween.IsOver(Context.NowL);

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

        public bool GetMarkedRange (string name, out MarkedRange value) {
            for (int i = 0; i < MarkedRanges.Count; i++) {
                if (MarkedRanges[i].Name == name) {
                    value = MarkedRanges[i];
                    return true;
                }
            }

            value = default(MarkedRange);
            return false;
        }

        public void RemoveMarkedRange (string name) {
            for (int i = 0; i < MarkedRanges.Count; i++) {
                if (MarkedRanges[i].Name == name) {
                    MarkedRanges.RemoveAt(i);
                    return;
                }
            }
        }

        public void SetMarkedRange (string name, ref MarkedRange value) {
            if (value.Name != name)
                value.Name = name;

            for (int i = 0; i < MarkedRanges.Count; i++) {
                if (MarkedRanges[i].Name == name) {
                    MarkedRanges[i] = value;
                    return;
                }
            }

            MarkedRanges.Add(value);
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
            decorations.GetTextSettings(ref UIOperationContext.Default, default(ControlStates), out Render.Material temp, ref color, out _);
            base.ComputeSizeConstraints(ref context, ref width, ref height, sizeScale);
            var temp2 = default(DecorationSettings);
            var gs = decorations.GetGlyphSource(ref temp2);
            var lineSpacing = (gs?.LineSpacing ?? 0) * 0.6f;
            if ((Direction == GaugeDirection.Clockwise) || (Direction == GaugeDirection.CounterClockwise)) {
                var m = Math.Max(ControlMinimumLength, ControlMinimumHeight);
                height.Minimum = Math.Max(Math.Max(height.Minimum ?? 0, m * sizeScale.Y), lineSpacing);
                width.Minimum = Math.Max(width.Minimum ?? 0, m * sizeScale.X);
            } else if (DetermineIfHorizontal(width.Minimum, height.Minimum)) {
                height.Minimum = Math.Max(Math.Max(height.Minimum ?? 0, ControlMinimumHeight * sizeScale.Y), lineSpacing);
                width.Minimum = Math.Max(width.Minimum ?? 0, ControlMinimumLength * sizeScale.X);
            } else {
                height.Minimum = Math.Max(Math.Max(height.Minimum ?? 0, ControlMinimumLength * sizeScale.Y), lineSpacing);
                width.Minimum = Math.Max(width.Minimum ?? 0, ControlMinimumHeight * sizeScale.X);
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
            GaugeDirection direction, float value1, float value2, ref RectF contentBox, float thickness
        ) {
            float extent;
            float thicknessMultiplier = Math.Min(Math.Abs(thickness), 1f);
            float thicknessOffset = thickness < 0
                ? 1.0f - Math.Abs(thickness)
                : 0f;

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
                    var fillRadius = Math.Min(
                        (ControlMinimumHeight / 2f) - Padding.Size.Length(),
                        (Math.Min(contentBox.Width, contentBox.Height) * CircularFillThicknessFactor)
                    ) * thicknessMultiplier;
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
                    contentBox.Top += contentBox.Height * thicknessOffset;
                    contentBox.Height *= thicknessMultiplier;
                    break;
                case GaugeDirection.RightToLeft:
                    contentBox.Left = extent - (value2 * contentBox.Width);
                    contentBox.Width *= fillSize;
                    contentBox.Top += contentBox.Height * thicknessOffset;
                    contentBox.Height *= thicknessMultiplier;
                    break;
                case GaugeDirection.TopToBottom:
                    contentBox.Top += value1 * contentBox.Height;
                    contentBox.Height *= fillSize;
                    contentBox.Left += contentBox.Width * thicknessOffset;
                    contentBox.Width *= thicknessMultiplier;
                    break;
                case GaugeDirection.BottomToTop:
                    contentBox.Top = extent - (value2 * contentBox.Height);
                    contentBox.Height *= fillSize;
                    contentBox.Left += contentBox.Width * thicknessOffset;
                    contentBox.Width *= thicknessMultiplier;
                    break;
            }
        }

        protected override void OnRasterize (ref UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings, IDecorator decorations) {
            var direction = PickDirection(ref settings.Box);
            var fill = context.DecorationProvider.Gauge;
            var originalCbox = settings.ContentBox;
            var value1 = ValueTween.Get(Context.NowL);
            settings.UserData = MakeUserData(0, value1, ref Fill, context.NowL);

            bool needBumpLayer = false;
            foreach (var mr in MarkedRanges) {
                if (!mr.DrawBelow)
                    continue;
                if (context.Pass != RasterizePasses.Content)
                    continue;

                if (DrawMarkedRange(ref context, ref renderer, settings, ref originalCbox, direction, fill, mr))
                    needBumpLayer = true;
            }

            var thickness = Fill.Thickness.HasValue
                ? Fill.Thickness.Value.Get(context.NowL)
                : 1.0f;

            MakeContentBox(direction, 0f, value1, ref settings.ContentBox, thickness);
            // HACK
            if ((value1 > 0) || (context.Pass != RasterizePasses.Content)) {
                // FIXME: Do we need to do this?
                if (needBumpLayer)
                    // renderer.Layer += 1;
                    ;

                base.OnRasterize(ref context, ref renderer, settings, decorations);
            }

            if (context.Pass != RasterizePasses.Content)
                return;

            if (_Limit < 1.0f) {
                settings.ContentBox = originalCbox;
                settings.UserData = new Vector4(_Limit, 1f, 0, 0);
                settings.Traits.Add("limit");
                MakeContentBox(direction, _Limit, 1f, ref settings.ContentBox, thickness);
                fill.Rasterize(ref context, ref renderer, ref settings);
            }

            needBumpLayer = true;
            foreach (var mr in MarkedRanges) {
                if (mr.DrawBelow)
                    continue;

                if (needBumpLayer) {
                    // renderer.Layer += 1;
                    needBumpLayer = false;
                }

                DrawMarkedRange(ref context, ref renderer, settings, ref originalCbox, direction, fill, mr);
            }
        }

        private Vector4 MakeUserData (float value1, float value2, ref FillSettings fill, long nowL) {
            var repeatFactor = (fill.Repeat ?? false) ? -1 : 1;
            return new Vector4(value1, value2, fill.Offset.Get(nowL), (fill.Size ?? 1) * repeatFactor);
        }

        private bool DrawMarkedRange (
            ref UIOperationContext context, ref ImperativeRenderer renderer, 
            DecorationSettings settings, ref RectF originalCbox, GaugeDirection direction,
            IDecorator fill, MarkedRange mr
        ) {
            float value1 = Arithmetic.Saturate(mr.Start.Get(context.NowL)),
                value2 = Arithmetic.Saturate(mr.End.Get(context.NowL)),
                thickness = mr.Fill.Thickness.HasValue
                    ? mr.Fill.Thickness.Value.Get(context.NowL)
                    : 1.0f;
            if (value2 <= value1)
                return false;
            settings.ContentBox = originalCbox;
            settings.TextColor = mr.Color ?? settings.TextColor;
            settings.UserData = MakeUserData(value1, value2, ref mr.Fill, context.NowL);
            foreach (var trait in mr.Traits)
                settings.Traits.Add(trait);
            if (mr.StaticColor)
                settings.Traits.Add("static");
            MakeContentBox(direction, value1, value2, ref settings.ContentBox, thickness);
            (mr.Decorator ?? fill).Rasterize(ref context, ref renderer, ref settings);
            return true;
        }

        /// <summary>
        /// Overrides the tween used for display animations without changing the gauge's value.
        /// </summary>
        public void OverrideValueTween (in Tween<float> tween) {
            ValueTween = tween;
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
