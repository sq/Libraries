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
using Microsoft.Xna.Framework.Input;
using Squared.PRGUI.Input;

namespace Squared.PRGUI.Accessibility {
    public struct AcceleratorInfo {
        public Control Target;
        public string Text;
        public Keys Key;
        public KeyboardModifiers Modifiers;

        public AcceleratorInfo (Control target, string text) {
            Target = target;
            Text = text;
            Key = default(Keys);
            Modifiers = default(KeyboardModifiers);
        }

        public AcceleratorInfo (Control target, Keys key, bool ctrl = false, bool alt = false, bool shift = false) {
            Target = target;
            Text = null;
            Key = key;
            Modifiers = new KeyboardModifiers { LeftControl = ctrl, LeftAlt = alt, LeftShift = shift };
        }
    }

    public interface IAcceleratorSource {
        IEnumerable<AcceleratorInfo> Accelerators { get; }
    }

    public interface IReadingTarget {
        AbstractString Text { get; }
        void FormatValueInto (StringBuilder sb);
    }

    public sealed class TTS {
        public readonly UIContext Context;

        public static TimeSpan StopOnTransitionThreshold = TimeSpan.FromSeconds(0.3);

        private readonly List<Prompt> SpeechQueue = new List<Prompt>();
        private SpeechSynthesizer _SpeechSynthesizer;
        public Control CurrentlyReading { get; private set; }
        private Control CurrentlyReadingTopLevel;

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

        public void BeginReading (Control control, string prefix = null, bool force = false) {
            var topLevel = Context.FindTopLevelAncestor(control);
            BeginReading(topLevel, control, prefix, force);
        }

        private IReadingTarget FindReadingTarget (Control control) {
            return control?.DelegatedReadingTarget ?? (control as IReadingTarget);
        }

        private void SpeakControl (Control control, string prefix = null) {
            var customTarget = FindReadingTarget(control);
            var text = customTarget?.Text.ToString();
            if ((text == null) && control.TooltipContent)
                text = control.TooltipContent.GetPlainText(control).ToString();
            if (text == null)
                text = control.ToString();

            if (text != null)
                Speak((prefix ?? "") + text.ToString(), Context.TTSDescriptionReadingSpeed);
        }

        private void BeginReading (Control topLevel, Control control, string prefix = null, bool force = false) {
            if ((CurrentlyReading == control) && !force)
                return;

            var prefixed = (topLevel != CurrentlyReadingTopLevel) && (topLevel != control) && 
                // If the control isn't a reading target its ToString probably isn't very helpful
                //  either, so informing the user that it's active is counterproductive
                (topLevel is IReadingTarget);

            CurrentlyReadingTopLevel = topLevel;
            CurrentlyReading = control;
            StartedReadingControlWhen = Context.NowL;
            ShouldStopBeforeReadingValue = false;

            Stop();
            if (prefixed)
                SpeakControl(topLevel, "Inside");
            SpeakControl(control, prefix ?? (prefixed ? ", " : null));
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

            var irt = FindReadingTarget(target);
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
            return;
            // FIXME
            if (Context.ReadAloudOnFocus)
                BeginReading(modal as Control, "Shown: ");
        }

        internal void FocusedControlChanged (Control currentTopLevel, Control current) {
            if (Context.ReadAloudOnFocus) {
                if (current == null)
                    Stop();
                else
                    BeginReading(currentTopLevel, current);
            }
        }

        internal bool AttemptToReadForClick (Control target) {
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
            ) {
                BeginReading(target);
                return true;
            }
            // HACK: Once we started reading an unfocusable control, we need to detect when the
            //  reading needs to shift back to the focused control
            else if (
                (Context.Focused == target) &&
                Context.ReadAloudOnClickIfNotFocusable &&
                (CurrentlyReading != target) &&
                (CurrentlyReading?.AcceptsFocus == false)
            ) {
                BeginReading(target);
                return true;
            }
            // HACK: The user might want narration for a control that transfers focus to a beneficiary
            //  and the default behavior will be to simply read whatever it transferred focus to.
            // However if focus has already been transferred there is nothing to read so we might as well
            //  read the thing they just clicked on instead.
            else if (
                (target.FocusBeneficiary == Context.Focused) &&
                Context.ReadAloudOnClickIfNotFocusable
            ) {
                SpeakControl(target);
                return true;
            }

            return false;
        }

        internal void ControlClicked (Control target, Control mouseOver) {
            var moRt = FindReadingTarget(mouseOver);
            var targetRt = FindReadingTarget(target);

            if ((moRt != null) && AttemptToReadForClick(moRt as Control))
                return;

            if (targetRt != null)
                target = (targetRt as Control) ?? target;

            if (AttemptToReadForClick(target))
                return;
        }
    }
}

namespace Squared.PRGUI {
    public sealed partial class UIContext : IDisposable {
        public int TTSValueReadingSpeed = 3;
        public int TTSDescriptionReadingSpeed = 0;
        public bool ReadAloudOnFixation = false;
        public bool ReadAloudOnFocus = false;
        public bool ReadAloudOnClickIfNotFocusable = false;
        public bool ReadAloudOnValueChange = false;

        public readonly TTS TTS;

        bool AcceleratorOverlayVisible = false;
        ArraySegment<BitmapDrawCall> AcceleratorOverlayBuffer = new ArraySegment<BitmapDrawCall>(new BitmapDrawCall[256]);

        struct RasterizedOverlayBox {
            public Control Control;
            public RectF ControlBox, LabelBox;
        }

        // HACK
        List<RasterizedOverlayBox> RasterizedOverlayBoxes = new List<RasterizedOverlayBox>();

        internal InputID FocusForward, FocusBackward, WindowFocusForward, WindowFocusBackward;

        internal void ClearInputIDButtons () {
            foreach (var iid in InputIDs) {
                iid.GamePadButton = null;
                iid.GamePadLabel = null;
            }
        }

        private InputID CreateInputID (Keys key, string label, bool ctrl = false, bool alt = false, bool shift = false) {
            var mods = new KeyboardModifiers {
                LeftAlt = alt,
                LeftShift = shift,
                LeftControl = ctrl
            };
            var result = GetInputID(key, mods);
            result.Label = label;
            return result;
        }

        private void CreateInputIDs () {
            FocusForward = CreateInputID(Keys.Tab, "{0} →");
            FocusBackward = CreateInputID(Keys.Tab, "← {0}", shift: true);
            WindowFocusForward = CreateInputID(Keys.Tab, "{0} →", ctrl: true);
            WindowFocusBackward = CreateInputID(Keys.Tab, "← {0}", ctrl: true, shift: true);
        }

        private Control ResolveProxies (Control c) {
            if (c is FocusProxy fp)
                return fp.FocusBeneficiary;
            else
                return c;
        }

        private void RasterizeAcceleratorOverlay (ref UIOperationContext context, ref ImperativeRenderer renderer) {
            var activeModal = ActiveModal;
            Control shiftTab = ResolveProxies(PickRotateFocusTarget(false, -1)),
                tab = ResolveProxies(PickRotateFocusTarget(false, 1)),
                ctrlShiftTab = (activeModal?.RetainFocus == true) ? null : ResolveProxies(PickRotateFocusTarget(true, -1)),
                ctrlTab = (activeModal?.RetainFocus == true) ? null : ResolveProxies(PickRotateFocusTarget(true, 1));

            var targetGroup = renderer.MakeSubgroup();
            var labelGroup = renderer.MakeSubgroup();

            RasterizedOverlayBoxes.Clear();
            if (Focused != null)
                RasterizedOverlayBoxes.Add(new RasterizedOverlayBox {
                    Control = Focused,
                    ControlBox = Focused.GetRect(contentRect: true)
                });

            // FIXME: This looks confusing
            // RasterizeAcceleratorOverlay(context, ref labelGroup, ref targetGroup, Focused, null);

            var topLevelSource = TopLevelFocused as IAcceleratorSource;
            if (topLevelSource != null) {
                labelGroup = renderer.MakeSubgroup();

                foreach (var accel in topLevelSource.Accelerators)
                    RasterizeAcceleratorOverlay(context, ref labelGroup, ref targetGroup, accel, true);
            }

            RasterizeAcceleratorOverlay(context, ref labelGroup, ref targetGroup, tab, FocusForward);
            if (shiftTab != tab)
                RasterizeAcceleratorOverlay(context, ref labelGroup, ref targetGroup, shiftTab, FocusBackward);

            if ((ctrlTab != TopLevelFocused) || (ctrlShiftTab != TopLevelFocused)) {
                if (ctrlTab != tab)
                    RasterizeAcceleratorOverlay(context, ref labelGroup, ref targetGroup, ctrlTab, WindowFocusForward);
                if (ctrlTab != ctrlShiftTab)
                    RasterizeAcceleratorOverlay(context, ref labelGroup, ref targetGroup, ctrlShiftTab, WindowFocusBackward);
            }

            var focusedSource = Focused as IAcceleratorSource;
            if ((focusedSource != null) && (focusedSource != topLevelSource)) {
                labelGroup = renderer.MakeSubgroup();

                foreach (var accel in focusedSource.Accelerators)
                    RasterizeAcceleratorOverlay(context, ref labelGroup, ref targetGroup, accel, true);
            }
        }

        private bool IsObstructedByAnyPreviousBox (ref RectF box, Control forControl) {
            const float padding = 0.5f;
            var padded = box;
            padded.Left -= padding;
            padded.Top -= padding;
            padded.Width += (padding * 2);
            padded.Height += (padding * 2);

            foreach (var previousRect in RasterizedOverlayBoxes) {
                // Accelerators may point at children of the focused control, in which case
                //  we want to allow their labels to appear as normal
                var controlBox = previousRect.ControlBox;
                var label = previousRect.LabelBox;
                if (previousRect.Control != forControl) {
                    if (controlBox.Contains(in box))
                        continue;
                }
                if (label.Intersects(in padded))
                    return true;
            }

            return false;
        }

        private StringBuilder OverlayStringBuilder = new StringBuilder();

        private void RasterizeAcceleratorOverlay (
            UIOperationContext context, ref ImperativeRenderer labelRenderer, ref ImperativeRenderer targetRenderer, 
            Control control, InputID id, bool showFocused = false, Control forControl = null
        ) {
            if (id == null)
                throw new ArgumentNullException("id");

            var gamePadMode = InputSources.FirstOrDefault() is GamepadVirtualKeyboardAndCursor;
            OverlayStringBuilder.Clear();
            id.Format(OverlayStringBuilder, gamePadMode);
            RasterizeAcceleratorOverlay(
                context, ref labelRenderer, ref targetRenderer, 
                control, OverlayStringBuilder, showFocused
            );
        }

        private void RasterizeAcceleratorOverlay (
            UIOperationContext context, ref ImperativeRenderer labelRenderer, ref ImperativeRenderer targetRenderer, 
            AcceleratorInfo accel, bool showFocused = false, Control forControl = null
        ) {
            if (accel.Text != null)
                RasterizeAcceleratorOverlay(
                    context, ref labelRenderer, ref targetRenderer,
                    accel.Target, accel.Text, showFocused, forControl
                );
            else
                RasterizeAcceleratorOverlay(
                    context, ref labelRenderer, ref targetRenderer,
                    accel.Target, GetInputID(accel.Key, accel.Modifiers), 
                    showFocused, forControl
                );
        }

        private void RasterizeAcceleratorOverlay (
            UIOperationContext context, ref ImperativeRenderer labelRenderer, ref ImperativeRenderer targetRenderer, 
            Control control, AbstractString label, bool showFocused = false, Control forControl = null
        ) {
            if (control == null)
                return;
            if (!showFocused && (control == Focused) && !label.IsNull && (label.Length > 0))
                return;
            if (label.Length <= 0)
                return;

            var box = control.GetRect();
            if ((box.Width <= 1) || (box.Height <= 1))
                return;

            var decorator = Decorations.AcceleratorTarget;
            var settings = new Decorations.DecorationSettings {
                Box = box,
                ContentBox = box
            };
            decorator.Rasterize(ref context, ref targetRenderer, settings);

            var outlinePadding = 1f;
            decorator = Decorations.AcceleratorLabel;
            Color? textColor = null;
            decorator.GetTextSettings(ref context, default(ControlStates), out Material material, ref textColor, out _);
            var layout = decorator.GlyphSource.LayoutString(label, buffer: AcceleratorOverlayBuffer);
            var textScale = 1f;
            if (layout.Size.X > (box.Width - decorator.Padding.X))
                textScale = Math.Max(0.25f, (box.Width - decorator.Padding.X) / layout.Size.X);
            var scaledSize = layout.Size * textScale;

            var labelTraits = new DenseList<string> { "above" };
            var labelPosition = box.Position - new Vector2(0, scaledSize.Y + decorator.Padding.Y + outlinePadding);
            if (labelPosition.Y <= 0) {
                labelTraits[0] = "inside";
                labelPosition = box.Position;
            }
            labelPosition.X = Arithmetic.Clamp(labelPosition.X, 0, CanvasSize.X - scaledSize.X);
            labelPosition.Y = Math.Max(0, labelPosition.Y);

            var labelBox = new RectF(
                labelPosition, 
                scaledSize + decorator.Padding.Size
            );
            if (IsObstructedByAnyPreviousBox(ref labelBox, forControl))
                labelBox.Left = box.Extent.X - labelBox.Width;
            if (IsObstructedByAnyPreviousBox(ref labelBox, forControl)) {
                labelTraits[0] = "below";
                labelBox.Left = labelPosition.X;
                labelBox.Top = box.Extent.Y + 1; // FIXME: Why the +1?
            }

            while (IsObstructedByAnyPreviousBox(ref labelBox, forControl)) {
                labelTraits[0] = "stacked";
                labelBox.Left = box.Left;
                labelBox.Width = box.Width;
                labelBox.Top = labelBox.Extent.Y + 0.5f;
            }
            // HACK

            var labelContentBox = new RectF(
                labelBox.Position + new Vector2(decorator.Padding.Left, decorator.Padding.Top),
                scaledSize
            );
            settings = new Decorations.DecorationSettings {
                Box = labelBox,
                ContentBox = box,
                Traits = labelTraits
            };
            decorator.Rasterize(ref context, ref labelRenderer, settings);
            labelRenderer.DrawMultiple(layout.DrawCalls, offset: labelContentBox.Position.Floor(), scale: new Vector2(textScale), layer: 1);

            RasterizedOverlayBoxes.Add(new RasterizedOverlayBox {
                Control = forControl,
                ControlBox = box,
                LabelBox = labelBox
            });
        }
    }
}
