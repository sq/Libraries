using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Squared.Game;
using Squared.PRGUI.Controls;
using Squared.PRGUI.Decorations;
using Squared.PRGUI.Layout;
using Squared.PRGUI.NewEngine;
using Squared.Render;
using Squared.Render.Convenience;
using Squared.Render.Text;
using Squared.Util;
using Squared.Util.Event;
using Squared.Util.Text;

namespace Squared.PRGUI.Controls {
    public class Spacer : Control {
        public Spacer ()
            : base () {
            AcceptsFocus = AcceptsMouseInput = AcceptsTextInput = false;
            Intangible = true;
        }

        protected override ref BoxRecord OnGenerateLayoutTree (ref UIOperationContext context, ControlKey parent, ControlKey? existingKey) {
            ref var result = ref base.OnGenerateLayoutTree(ref context, parent, existingKey);
            result.Tag = LayoutTags.Spacer;
            result.Config.NoMeasurement = true;
            return ref result;
        }

        protected override bool OnHitTest (RectF box, Vector2 position, ref HitTestState state) => false;
        protected override bool OnEvent<T> (string name, T args) => false;

        protected override bool IsPassDisabled (RasterizePasses pass, IDecorator decorations) {
            return true;
        }

        protected override void OnRasterize (ref UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings, IDecorator decorations) {
        }
    }

    public class FocusProxy : Control {
        public FocusProxy (Control target)
            : base () {
            if (target == null)
                throw new ArgumentNullException("target");
            FocusBeneficiary = target;
            AcceptsFocus = AcceptsMouseInput = AcceptsTextInput = false;
            Intangible = true;
            Width.Maximum = 0;
            Height.Maximum = 0;
        }

        protected override ref BoxRecord OnGenerateLayoutTree (ref UIOperationContext context, ControlKey parent, ControlKey? existingKey) {
            return ref InvalidValues.Record;
        }

        public override string ToString () {
            return $"FocusProxy ({FocusBeneficiary})";
        }

        protected override bool OnHitTest (RectF box, Vector2 position, ref HitTestState state) => false;
        protected override bool OnEvent<T> (string name, T args) => false;

        protected override bool IsPassDisabled (RasterizePasses pass, IDecorator decorations) {
            return true;
        }

        protected override void OnRasterize (ref UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings, IDecorator decorations) {
        }
    }

    public class Button : StaticTextBase {
        public Menu Menu;

        new public DynamicStringLayout Content => base.Content;
        new public AbstractString Text {
            get => base.Text;
            set => base.Text = value;
        }
        new public void Invalidate () => base.Invalidate();
        new public bool AcceptsFocus {
            get => base.AcceptsFocus;
            set => base.AcceptsFocus = value;
        }
        new public bool ScaleToFit {
            get => base.ScaleToFit;
            set => base.ScaleToFit = value;
        }

        public bool SetText (AbstractString value, bool onlyIfTextChanged = false) => base.SetText(value, onlyIfTextChanged);

        /// <summary>
        /// If set, the button will produce additional periodic Click events while pressed
        /// </summary>
        public bool EnableRepeat;

        private double LastRepeatTimestamp;

        public Button ()
            : base () {
            Content.Alignment = HorizontalAlignment.Center;
            Content.RichText = true;
            AcceptsMouseInput = true;
            AcceptsFocus = true;
            Wrap = false;
        }

        private bool AutoShowMenu () {
            if (Menu == null)
                return false;
            // HACK: Detect whether the menu is open
            if (!Menu.Intangible)
                return true;

            var box = GetRect(contentRect: true);
            Menu.Width.Minimum = box.Width;
            Menu.Show(Context, this);
            return true;
        }

        protected override bool OnEvent<T> (string name, T args) {
            if ((name == UIEvents.Click) && AutoShowMenu())
                return true;

            if (args is MouseEventArgs ma)
                return OnMouseEvent(name, ma);

            return base.OnEvent(name, args);
        }

        private bool OnMouseEvent (string name, MouseEventArgs args) {
            if ((name == UIEvents.MouseDown) && AutoShowMenu())
                return true;

            return false;
        }

        protected override void OnTick (MouseEventArgs args) {
            base.OnTick(args);
            if ((args.Buttons != MouseButtons.Left) || !EnableRepeat)
                return;

            if (Context.UpdateRepeat(
                args.Now, args.MouseDownTimestamp, ref LastRepeatTimestamp,
                speedMultiplier: 1f, accelerationMultiplier: 1f
            ))
                FireEvent(UIEvents.Click, args);
        }

        protected override IDecorator GetDefaultDecorator (IDecorationProvider provider) {
            return provider?.Button;
        }

        protected override AbstractString GetReadingText () {
            return $"Button {base.GetReadingText()}";
        }
    }

    public class Checkbox : StaticTextBase, IValueControl<bool> {
        new public DynamicStringLayout Content => base.Content;
        new public AbstractString Text {
            get => base.Text;
            set => base.Text = value;
        }
        new public bool ScaleToFit {
            get => base.ScaleToFit;
            set => base.ScaleToFit = value;
        }
        new public void Invalidate () => base.Invalidate();

        protected bool _Checked;

        public bool SetText (AbstractString value, bool onlyIfTextChanged = false) => base.SetText(value, onlyIfTextChanged);

        public Checkbox ()
            : base () {
            Content.Alignment = HorizontalAlignment.Left;
            Content.RichText = true;
            AcceptsMouseInput = true;
            AcceptsFocus = true;
            Wrap = false;
        }

        public void SetValue (bool value, bool forUserInput) {
            if (value == _Checked)
                return;

            _Checked = value;
            if (!FireEvent(UIEvents.CheckedChanged))
                FireEvent(UIEvents.ValueChanged);
            if (forUserInput)
                FireEvent(UIEvents.ValueChangedByUser);
        }

        public bool Checked {
            get => _Checked;
            set => SetValue(value, false);
        }

        bool IValueControl<bool>.Value {
            get => Checked;
            set => Checked = value;
        }

        protected override IDecorator GetDefaultDecorator (IDecorationProvider provider) {
            return provider?.Checkbox;
        }

        protected override AbstractString GetReadingText () {
            var useTooltipByDefault = Text.IsNullOrWhiteSpace || (Text.Length <= 2);
            return (
                (UseTooltipForReading ?? useTooltipByDefault)
                    ? (TooltipContent.Get(this).ToString() ?? Text.ToString())
                    : Text.ToString()
            ) + (Checked ? ": Yes" : ": No");
        }

        protected override void FormatValueInto (StringBuilder sb) {
            sb.Append(Checked ? "Yes" : "No");
        }

        protected override ControlStates GetCurrentState (ref UIOperationContext context) {
            var result = base.GetCurrentState(ref context);
            if (Checked)
                result |= ControlStates.Checked;
            return result;
        }

        protected override bool OnEvent<T> (string name, T args) {
            if (name == UIEvents.Click)
                SetValue(!Checked, true);

            return base.OnEvent(name, args);
        }
    }

    public class RadioButton : StaticTextBase, IValueControl<bool> {
        private bool _Checked, _SubscriptionPending;
        public string GroupId;

        private TypedEventSubscriber<string> OnRadioButtonSelected;

        new public DynamicStringLayout Content => base.Content;
        new public AbstractString Text {
            get => base.Text;
            set => base.Text = value;
        }
        new public bool ScaleToFit {
            get => base.ScaleToFit;
            set => base.ScaleToFit = value;
        }
        new public void Invalidate () => base.Invalidate();

        public bool SetText (AbstractString value, bool onlyIfTextChanged = false) => base.SetText(value, onlyIfTextChanged);

        public RadioButton ()
            : base () {
            Content.Alignment = HorizontalAlignment.Left;
            Content.RichText = true;
            AcceptsMouseInput = true;
            AcceptsFocus = true;
            Wrap = false;
            OnRadioButtonSelected = _OnRadioButtonSelected;
        }

        public void SetValue (bool value, bool forUserInput) {
            if (value == _Checked)
                return;

            if (value == false) {
                Unsubscribe();
                _Checked = false;
            } else {
                Subscribe();
                _Checked = true;
            }

            if (value)
                FireEvent(UIEvents.RadioButtonSelected, GroupId);
            if (!FireEvent(UIEvents.CheckedChanged))
                FireEvent(UIEvents.ValueChanged);
            if (forUserInput)
                FireEvent(UIEvents.ValueChangedByUser);
        }

        public bool Checked {
            get => _Checked;
            set => SetValue(value, false);
        }

        bool IValueControl<bool>.Value {
            get => Checked;
            set => Checked = value;
        }

        protected override void InitializeForContext () {
            if (_SubscriptionPending) {
                Subscribe();
                if (_Checked)
                    FireEvent(UIEvents.RadioButtonSelected, GroupId);
            }
        }

        private void _OnRadioButtonSelected (IEventInfo<string> e, string groupId) {
            if (e.Source == this)
                return;
            if (GroupId != groupId)
                return;
            Checked = false;
        }

        private void Subscribe () {
            _SubscriptionPending = false;
            if (Context == null) {
                _SubscriptionPending = true;
                return;
            }
            Context.EventBus.Unsubscribe(null, UIEvents.RadioButtonSelected, OnRadioButtonSelected);
            Context.EventBus.Subscribe(null, UIEvents.RadioButtonSelected, OnRadioButtonSelected, weak: true);
        }

        private void Unsubscribe () {
            if (Context == null)
                return;
            Context.EventBus.Unsubscribe(null, UIEvents.RadioButtonSelected, OnRadioButtonSelected);
        }

        protected override IDecorator GetDefaultDecorator (IDecorationProvider provider) {
            return provider?.RadioButton;
        }

        protected override AbstractString GetReadingText () {
            if (Checked)
                return Text.ToString() + ": Selected";
            else
                return Text;
        }

        protected override void FormatValueInto (StringBuilder sb) {
            if (Checked)
                sb.Append("Selected");
        }

        protected override ControlStates GetCurrentState (ref UIOperationContext context) {
            var result = base.GetCurrentState(ref context);
            if (Checked)
                result |= ControlStates.Checked;
            return result;
        }

        protected override bool OnEvent<T> (string name, T args) {
            if (name == UIEvents.Click)
                SetValue(true, true);

            return base.OnEvent(name, args);
        }
    }

    public sealed class NullControl : Control {
        internal NullControl () {
            AcceptsMouseInput = AcceptsTextInput = AcceptsFocus = false;
            Intangible = true; Enabled = false;
        }

        protected override bool OnEvent<T> (string name, T args) {
            return false;
        }

        protected override bool OnHitTest (RectF box, Vector2 position, ref HitTestState state) {
            return false;
        }

        protected override void OnRasterize (ref UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings, IDecorator decorations) {
            return;
        }

        protected override ref BoxRecord OnGenerateLayoutTree (ref UIOperationContext context, ControlKey parent, ControlKey? existingKey) {
            return ref InvalidValues.Record;
        }
    }
}