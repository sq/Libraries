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

namespace Squared.PRGUI.Accessibility {
    public interface IReadingTarget {
        AbstractString Text { get; }
        void FormatValueInto (StringBuilder sb);
    }

    public class TTS {
        public readonly UIContext Context;

        public static TimeSpan StopOnTransitionThreshold = TimeSpan.FromSeconds(0.1);

        private readonly List<Prompt> SpeechQueue = new List<Prompt>();
        private SpeechSynthesizer _SpeechSynthesizer;
        private Control CurrentlyReading;

        private long StartedReadingControlWhen;

        public TTS (UIContext context) {
            Context = context;
            Context.EventBus.Subscribe(null, UIEvents.ValueChanged, Control_OnValueChanged);
            Context.EventBus.Subscribe(null, UIEvents.CheckedChanged, Control_OnValueChanged);
            Context.EventBus.Subscribe(null, UIEvents.SelectionChanged, Control_OnValueChanged);
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

        public void BeginReading (Control control) {
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
                Speak(text.ToString(), Context.TTSDescriptionReadingSpeed);
        }

        private void Control_OnValueChanged (IEventInfo e) {
            var ctl = e.Source as Control;
            if (ctl != null)
                NotifyValueChanged(ctl);
        }

        private StringBuilder ValueStringBuilder = new StringBuilder();
        private bool ShouldStopBeforeReadingValue = false;

        public void NotifyValueChanged (Control target) {
            if (!Context.ReadAloudOnValueChange)
                return;

            if (target != CurrentlyReading)
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

        internal void FocusedControlChanged (Control current) {
            if (Context.ReadAloudOnFocus) {
                if (current == null)
                    Stop();
                else
                    BeginReading(current);
            }
        }
    }
}

namespace Squared.PRGUI {
    public partial class UIContext : IDisposable {
        public int TTSValueReadingSpeed = 3;
        public int TTSDescriptionReadingSpeed = 0;
        public bool ReadAloudOnFixation = false;
        public bool ReadAloudOnFocus = false;
        public bool ReadAloudOnValueChange = true;

        public readonly TTS TTS;
    }
}
