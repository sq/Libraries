using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Squared.PRGUI.Decorations;
using Squared.Render.Convenience;
using Squared.Util;
using Squared.Util.Text;

namespace Squared.PRGUI.Controls {
    public class Slider : Control, ICustomTooltipTarget {
        public const int ControlMinimumHeight = 28, ControlMinimumWidth = 100,
            ThumbMinimumWidth = 12;

        public float Minimum = 0, Maximum = 100;

        private float _Value = 50;

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

        private bool HasCustomTooltipContent => TooltipContent.GetText != GetDefaultTooltip;

        public float? TooltipDisappearDelay => null;
        public float? TooltipAppearanceDelay => HasCustomTooltipContent ? (float?)null : 0f;
        public bool ShowTooltipWhileMouseIsHeld => !HasCustomTooltipContent;
        public bool ShowTooltipWhileMouseIsNotHeld => HasCustomTooltipContent;
        public bool HideTooltipOnMousePress => HasCustomTooltipContent;

        public Slider () : base () {
            AcceptsFocus = true;
            AcceptsMouseInput = true;
            TooltipContent = new AbstractTooltipContent(GetDefaultTooltip);
        }

        bool _WasMouseOverThumb = false;

        private static AbstractString GetDefaultTooltip (Control c) {
            var s = (Slider)c;
            return $"{s.Value}/{s.Maximum}";
        }

        private float ClampValue (float value) {
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

        private RectF ComputeThumbBox (RectF contentBox) {
            var thumb = Context.Decorations.SliderThumb;
            var thumbSize = (thumb.Margins + thumb.Padding).Size;
            thumbSize.X = Math.Max(ThumbMinimumWidth, thumbSize.X);
            thumbSize.Y = contentBox.Height;
            var trackSpace = contentBox.Width - thumbSize.X;
            var scaledValue = (ClampValue(_Value) - Minimum) / (Maximum - Minimum);
            var thumbPosition = new Vector2(scaledValue * trackSpace, 0) + contentBox.Position;
            return new RectF(thumbPosition, thumbSize);
        }

        protected override void OnRasterize (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings, IDecorator decorations) {
            base.OnRasterize(context, ref renderer, settings, decorations);
            var thumb = Context.Decorations.SliderThumb;
            var thumbSettings = settings;
            // FIXME: Apply padding
            thumbSettings.Box = ComputeThumbBox(settings.ContentBox);
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
    }
}
