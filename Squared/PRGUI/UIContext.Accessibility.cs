using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Threading.Tasks;
using Squared.PRGUI.Controls;
using Squared.Util;
using System.Speech;
using System.Speech.Synthesis;
using Squared.PRGUI.Accessibility;
using Squared.Util.Text;
using Squared.Util.Event;
using Squared.Render.Convenience;
using Squared.Render.Text;
using Squared.Render;
using Microsoft.Xna.Framework;
using Squared.Game;

namespace Squared.PRGUI.Accessibility {
    public interface IAcceleratorSource {
        IEnumerable<KeyValuePair<Control, string>> Accelerators { get; }
    }

    public interface IReadingTarget {
        AbstractString Text { get; }
        void FormatValueInto (StringBuilder sb);
    }

    public class TTS {
        public readonly UIContext Context;

        public static TimeSpan StopOnTransitionThreshold = TimeSpan.FromSeconds(0.3);

        private readonly List<Prompt> SpeechQueue = new List<Prompt>();
        private SpeechSynthesizer _SpeechSynthesizer;
        public Control CurrentlyReading { get; private set; }

        private long StartedReadingControlWhen;

        public TTS (UIContext context) {
            Context = context;
            Context.EventBus.Subscribe(null, UIEvents.ValueChanged, Control_OnValueChanged);
            Context.EventBus.Subscribe(null, UIEvents.CheckedChanged, Control_OnValueChanged);
            Context.EventBus.Subscribe(null, UIEvents.SelectionChanged, Control_OnSelectionChanged);
            Context.EventBus.Subscribe(null, UIEvents.Shown, Control_OnShown);
        }

        public SpeechSynthesizer SpeechSynthesizer {
            get {
                if (_SpeechSynthesizer == null)
                    InitSpeechSynthesizer();
                return _SpeechSynthesizer;
            }
        }

        private void InitSpeechSynthesizer () {
            _SpeechSynthesizer = new SpeechSynthesizer();
            _SpeechSynthesizer.SelectVoiceByHints(VoiceGender.Female);
            _SpeechSynthesizer.Volume = _Volume;
        }

        public bool IsSpeaking {
            get => SpeechQueue.Any(p => !p.IsCompleted);
        }

        private int _Volume = 100;
        public int Volume {
            get => _Volume;
            set {
                _Volume = Arithmetic.Clamp(value, 0, 100);
                if (_SpeechSynthesizer != null)
                    _SpeechSynthesizer.Volume = _Volume;
            }
        }

        public void Speak (string text, int? rate = null) {
            if (rate != null)
                SpeechSynthesizer.Rate = rate.Value;

            var result = SpeechSynthesizer.SpeakAsync(text);
            SpeechQueue.Add(result);
        }

        public void Stop () {
            if (IsSpeaking)
                SpeechSynthesizer.SpeakAsyncCancelAll();
            SpeechQueue.Clear();
        }

        public void BeginReading (Control control, string prefix = null) {
            if (CurrentlyReading == control)
                return;

            StartedReadingControlWhen = Context.NowL;
            ShouldStopBeforeReadingValue = false;
            Stop();
            CurrentlyReading = control;
            var customTarget = control as IReadingTarget;
            var text = customTarget?.Text.ToString();
            if ((text == null) && control.TooltipContent)
                text = control.TooltipContent.Get(control).ToString();
            if (text == null)
                text = control.ToString();

            if (text != null)
                Speak((prefix ?? "") + text.ToString(), Context.TTSDescriptionReadingSpeed);
        }

        private void Control_OnSelectionChanged (IEventInfo e) {
            var ctl = e.Source as Control;
            // HACK
            if (ctl is EditableText)
                return;
            if (ctl != null)
                NotifyValueChanged(ctl);
        }

        private void Control_OnValueChanged (IEventInfo e) {
            var ctl = e.Source as Control;
            if (ctl != null)
                NotifyValueChanged(ctl);
        }

        private void Control_OnShown (IEventInfo e) {
            var ctl = e.Source as IModal;
            if (ctl != null)
                NotifyModalShown(ctl);
        }

        private StringBuilder ValueStringBuilder = new StringBuilder();
        private bool ShouldStopBeforeReadingValue = false;

        public void NotifyValueChanged (Control target) {
            if (!Context.ReadAloudOnValueChange)
                return;

            if ((target != CurrentlyReading) && (Context.MouseCaptured != target))
                return;

            var irt = target as IReadingTarget;
            if (irt == null)
                return;

            ValueStringBuilder.Clear();
            irt.FormatValueInto(ValueStringBuilder);
            if (ValueStringBuilder.Length > 0) {
                // A ValueChanged event can come immediately after a focus change
                //  if a mouse event both transitioned focus and altered the value.
                // In this case, we want to completely read the new focus target and
                //  THEN read the new value, instead of having the new value immediately
                //  stop the reading of the new focus target, so that it's clear which
                //  control is now focused and having its value modified
                if (
                    ShouldStopBeforeReadingValue || 
                    ((Context.NowL - StartedReadingControlWhen) >= StopOnTransitionThreshold.Ticks)
                )
                    Stop();

                Speak(ValueStringBuilder.ToString(), Context.TTSValueReadingSpeed);
                ShouldStopBeforeReadingValue = true;
            }
        }

        internal void FixatedControlChanged (Control current) {
            if (Context.ReadAloudOnFixation) {
                if (current == null)
                    Stop();
                else
                    BeginReading(current);
            }
        }

        internal void NotifyModalShown (IModal modal) {
            if (Context.ReadAloudOnFocus)
                BeginReading(modal as Control, "Shown: ");
        }

        internal void FocusedControlChanged (Control current) {
            if (Context.ReadAloudOnFocus) {
                if (current == null)
                    Stop();
                else
                    BeginReading(current);
            }
        }

        internal void ControlClicked (Control target) {
            // FIXME: If clicking on a static transferred focus to a neighbor (because it was in
            //  another top-level container) should we read it?
            if (
                !target.AcceptsFocus &&
                Context.ReadAloudOnClickIfNotFocusable &&
                // Top-level containers will transfer focus elsewhere when clicked so don't read them
                !((target is IControlContainer) && Context.Controls.Contains(target)) &&
                // Controls that transfer focus elsewhere shouldn't be read when clicked,
                //  we want to read the new focus target instead
                (target.FocusBeneficiary == null) &&
                ((CurrentlyReading != target) || !IsSpeaking)
            )
                BeginReading(target);
            // HACK: Once we started reading an unfocusable control, we need to detect when the
            //  reading needs to shift back to the focused control
            else if (
                (Context.Focused == target) &&
                Context.ReadAloudOnClickIfNotFocusable &&
                (CurrentlyReading != target) &&
                (CurrentlyReading?.AcceptsFocus == false)
            )
                BeginReading(target);
        }
    }
}

namespace Squared.PRGUI {
    public partial class UIContext : IDisposable {
        public int TTSValueReadingSpeed = 3;
        public int TTSDescriptionReadingSpeed = 0;
        public bool ReadAloudOnFixation = false;
        public bool ReadAloudOnFocus = false;
        public bool ReadAloudOnClickIfNotFocusable = false;
        public bool ReadAloudOnValueChange = false;

        public readonly TTS TTS;

        bool AcceleratorOverlayVisible = false;
        ArraySegment<BitmapDrawCall> AcceleratorOverlayBuffer = new ArraySegment<BitmapDrawCall>(new BitmapDrawCall[256]);

        // HACK
        List<RectF> RasterizedOverlayBoxes = new List<RectF>();

        private void RasterizeAcceleratorOverlay (UIOperationContext context, ref ImperativeRenderer renderer) {
            var activeModal = ActiveModal;
            Control shiftTab = PickRotateFocusTarget(false, -1),
                tab = PickRotateFocusTarget(false, 1),
                ctrlShiftTab = (activeModal?.RetainFocus == true) ? null : PickRotateFocusTarget(true, -1),
                ctrlTab = (activeModal?.RetainFocus == true) ? null : PickRotateFocusTarget(true, 1);

            var targetGroup = renderer.MakeSubgroup();
            var labelGroup = renderer.MakeSubgroup();

            RasterizedOverlayBoxes.Clear();
            if (Focused != null)
                RasterizedOverlayBoxes.Add(Focused.GetRect(context.Layout, contentRect: true));

            // FIXME: This looks confusing
            // RasterizeAcceleratorOverlay(context, ref labelGroup, ref targetGroup, Focused, null);

            var topLevelSource = TopLevelFocused as IAcceleratorSource;
            if (topLevelSource != null) {
                labelGroup = renderer.MakeSubgroup();

                foreach (var kvp in topLevelSource.Accelerators)
                    RasterizeAcceleratorOverlay(context, ref labelGroup, ref targetGroup, kvp.Key, kvp.Value, true);
            }

            RasterizeAcceleratorOverlay(context, ref labelGroup, ref targetGroup, tab, "Tab →");
            if (shiftTab != tab)
                RasterizeAcceleratorOverlay(context, ref labelGroup, ref targetGroup, shiftTab, "← Shift+Tab");

            if ((ctrlTab != TopLevelFocused) || (ctrlShiftTab != TopLevelFocused)) {
                if (ctrlTab != tab)
                    RasterizeAcceleratorOverlay(context, ref labelGroup, ref targetGroup, ctrlTab, "Ctrl+Tab →");
                if (ctrlTab != ctrlShiftTab)
                    RasterizeAcceleratorOverlay(context, ref labelGroup, ref targetGroup, ctrlShiftTab, "← Ctrl+Shift+Tab");
            }

            var focusedSource = Focused as IAcceleratorSource;
            if ((focusedSource != null) && (focusedSource != topLevelSource)) {
                labelGroup = renderer.MakeSubgroup();

                foreach (var kvp in focusedSource.Accelerators)
                    RasterizeAcceleratorOverlay(context, ref labelGroup, ref targetGroup, kvp.Key, kvp.Value, true);
            }
        }

        private bool IsObstructedByAnyPreviousBox (ref RectF box) {
            const float padding = 1;
            var padded = box;
            padded.Left -= padding;
            padded.Top -= padding;
            padded.Width += (padding * 2);
            padded.Height += (padding * 2);

            foreach (var previousRect in RasterizedOverlayBoxes) {
                // Accelerators may point at children of the focused control, in which case
                //  we want to allow their labels to appear as normal
                if (previousRect.Contains(ref box))
                    continue;
                if (previousRect.Intersects(ref padded))
                    return true;
            }

            return false;
        }

        private void RasterizeAcceleratorOverlay (
            UIOperationContext context, ref ImperativeRenderer labelRenderer, ref ImperativeRenderer targetRenderer, 
            Control control, string label, bool showFocused = false
        ) {
            if (control == null)
                return;
            if (!showFocused && (control == Focused) && !string.IsNullOrWhiteSpace(label))
                return;

            var box = control.GetRect(Layout);
            var decorator = Decorations.AcceleratorTarget;
            var settings = new Decorations.DecorationSettings {
                Box = box,
                ContentBox = box
            };
            decorator.Rasterize(context, ref targetRenderer, settings);

            if (!string.IsNullOrWhiteSpace(label)) {
                var outlinePadding = 1f;
                decorator = Decorations.AcceleratorLabel;
                Color? textColor = null;
                decorator.GetTextSettings(context, default(ControlStates), out Material material, out IGlyphSource font, ref textColor);
                var layout = font.LayoutString(label, buffer: AcceleratorOverlayBuffer);

                var labelPosition = box.Position - new Vector2(0, layout.Size.Y + decorator.Padding.Y + outlinePadding);
                if (labelPosition.Y <= 0)
                    labelPosition = box.Position;
                labelPosition.X = Arithmetic.Clamp(labelPosition.X, 0, CanvasSize.X - layout.Size.X);
                labelPosition.Y = Math.Max(0, labelPosition.Y);

                var labelBox = new RectF(
                    labelPosition, 
                    layout.Size + decorator.Padding.Size
                );
                if (IsObstructedByAnyPreviousBox(ref labelBox))
                    labelBox.Left = box.Extent.X - labelBox.Width;
                if (IsObstructedByAnyPreviousBox(ref labelBox)) {
                    labelBox.Left = labelPosition.X;
                    labelBox.Top = box.Extent.Y + 1; // FIXME: Why the +1?
                }
                if (IsObstructedByAnyPreviousBox(ref labelBox))
                    labelBox.Left = box.Extent.X - labelBox.Width;

                var labelContentBox = new RectF(
                    labelBox.Position + new Vector2(decorator.Padding.Left, decorator.Padding.Top),
                    layout.Size
                );
                settings = new Decorations.DecorationSettings {
                    Box = labelBox,
                    ContentBox = box,
                };
                decorator.Rasterize(context, ref labelRenderer, settings);
                labelRenderer.DrawMultiple(layout.DrawCalls, offset: labelContentBox.Position.Floor(), layer: 1);

                RasterizedOverlayBoxes.Add(labelBox);
            }
        }
    }
}
