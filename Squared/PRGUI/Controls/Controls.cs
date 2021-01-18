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

        protected override bool OnHitTest (RectF box, Vector2 position, bool acceptsMouseInputOnly, bool acceptsFocusOnly, ref Control result) => false;
        protected override bool OnEvent<T> (string name, T args) => false;
        protected override bool OnEvent (string name) => false;

        protected override void OnRasterize (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings, IDecorator decorations) {
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

        public override string ToString () {
            return $"FocusProxy ({FocusBeneficiary})";
        }

        protected override bool OnHitTest (RectF box, Vector2 position, bool acceptsMouseInputOnly, bool acceptsFocusOnly, ref Control result) => false;
        protected override bool OnEvent<T> (string name, T args) => false;
        protected override bool OnEvent (string name) => false;

        protected override void OnRasterize (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings, IDecorator decorations) {
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

        protected override bool OnEvent<T> (string name, T args) {
            if ((name == UIEvents.Click) && (Menu != null)) {
                Menu.Show(Context, this);
                return true;
            }

            if (args is MouseEventArgs)
                return OnMouseEvent(name, (MouseEventArgs)(object)args);

            return base.OnEvent(name, args);
        }

        private bool OnMouseEvent (string name, MouseEventArgs args) {
            if ((name == UIEvents.MouseDown) && (Menu != null)) {
                var box = GetRect(contentRect: true);
                Menu.Width.Minimum = box.Width;
                Menu.Show(Context, this);
                return true;
            }

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
        public bool Checked;

        new public DynamicStringLayout Content => base.Content;
        new public AbstractString Text {
            get => base.Text;
            set => base.Text = value;
        }
        new public void Invalidate () => base.Invalidate();

        public Checkbox ()
            : base () {
            Content.Alignment = HorizontalAlignment.Left;
            Content.RichText = true;
            AcceptsMouseInput = true;
            AcceptsFocus = true;
            Wrap = false;
        }

        bool IValueControl<bool>.Value {
            get => Checked;
            set => Checked = value;
        }

        protected override IDecorator GetDefaultDecorator (IDecorationProvider provider) {
            return provider?.Checkbox;
        }

        protected override AbstractString GetReadingText () {
            return Text.ToString() + (Checked ? ": Yes" : ": No");
        }

        protected override void FormatValueInto (StringBuilder sb) {
            sb.Append(Checked ? "Yes" : "No");
        }

        protected override void OnRasterize (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings, IDecorator decorations) {
            if (Checked)
                settings.State |= ControlStates.Checked;
            base.OnRasterize(context, ref renderer, settings, decorations);
        }

        protected override bool OnEvent<T> (string name, T args) {
            if (name == UIEvents.Click) {
                Checked = !Checked;
                FireEvent(UIEvents.CheckedChanged);
            }

            return base.OnEvent(name, args);
        }
    }

    public class RadioButton : StaticTextBase, IValueControl<bool> {
        private bool _Checked, _SubscriptionPending;
        public string GroupId;

        private EventSubscription Subscription;

        new public DynamicStringLayout Content => base.Content;
        new public AbstractString Text {
            get => base.Text;
            set => base.Text = value;
        }
        new public void Invalidate () => base.Invalidate();

        public RadioButton ()
            : base () {
            Content.Alignment = HorizontalAlignment.Left;
            Content.RichText = true;
            AcceptsMouseInput = true;
            AcceptsFocus = true;
            Wrap = false;
        }

        public bool Checked {
            get => _Checked;
            set {
                if (value == _Checked)
                    return;

                if (value == false) {
                    Unsubscribe();
                    _Checked = false;
                    FireEvent(UIEvents.CheckedChanged);
                } else {
                    Subscribe();
                    _Checked = true;
                    FireEvent(UIEvents.RadioButtonSelected, GroupId);
                    FireEvent(UIEvents.CheckedChanged);
                }
            }
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

        private void OnRadioButtonSelected (IEventInfo<string> e, string groupId) {
            if (e.Source == this)
                return;
            if (GroupId != groupId)
                return;
            Checked = false;
        }

        private void Subscribe () {
            _SubscriptionPending = false;
            Subscription.Dispose();
            if (Context == null) {
                _SubscriptionPending = true;
                return;
            }
            Subscription = Context.EventBus.Subscribe<string>(null, UIEvents.RadioButtonSelected, OnRadioButtonSelected);
        }

        private void Unsubscribe () {
            Subscription.Dispose();
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

        protected override void OnRasterize (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings, IDecorator decorations) {
            if (Checked)
                settings.State |= ControlStates.Checked;
            base.OnRasterize(context, ref renderer, settings, decorations);
        }

        protected override bool OnEvent<T> (string name, T args) {
            if (name == UIEvents.Click)
                Checked = true;

            return base.OnEvent(name, args);
        }
    }

    public sealed class NullControl : Control {
        internal NullControl () {
            AcceptsMouseInput = AcceptsTextInput = AcceptsFocus = false;
            Intangible = true; Enabled = false;
        }

        protected override bool OnEvent (string name) {
            return false;
        }

        protected override bool OnEvent<T> (string name, T args) {
            return false;
        }

        protected override bool OnHitTest (RectF box, Vector2 position, bool acceptsMouseInputOnly, bool acceptsFocusOnly, ref Control result) {
            return false;
        }

        protected override void OnRasterize (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings, IDecorator decorations) {
            return;
        }

        protected override ControlKey OnGenerateLayoutTree (UIOperationContext context, ControlKey parent, ControlKey? existingKey) {
            return ControlKey.Invalid;
        }
    }
}