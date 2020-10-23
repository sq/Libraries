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
    public class Button : StaticText {
        public Menu Menu;

        public Button ()
            : base () {
            Content.Alignment = HorizontalAlignment.Center;
            AcceptsMouseInput = true;
            AcceptsFocus = true;
            Wrap = false;
        }

        protected override bool OnEvent<T> (string name, T args) {
            if (args is MouseEventArgs) {
                return OnMouseEvent(name, (MouseEventArgs)(object)args);
            } else if (name == UIEvents.Click) {
                if (Menu != null) {
                    Menu.Show(Context, this);
                    return true;
                }
            }

            return base.OnEvent(name, args);
        }

        private bool OnMouseEvent (string name, MouseEventArgs args) {
            if ((name == UIEvents.MouseDown) && (Menu != null)) {
                Menu.Show(Context, this);
                return true;
            }

            return false;
        }

        protected override IDecorator GetDefaultDecorations (IDecorationProvider provider) {
            return provider?.Button;
        }
    }

    public class Checkbox : StaticText {
        public bool Checked;

        public Checkbox ()
            : base () {
            Content.Alignment = HorizontalAlignment.Left;
            AcceptsMouseInput = true;
            AcceptsFocus = true;
        }

        protected override IDecorator GetDefaultDecorations (IDecorationProvider provider) {
            return provider?.Checkbox;
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

    public class RadioButton : StaticText {
        private bool _Checked, _SubscriptionPending;
        public string GroupId;

        private EventSubscription Subscription;

        public RadioButton ()
            : base () {
            Content.Alignment = HorizontalAlignment.Left;
            AcceptsMouseInput = true;
            AcceptsFocus = true;
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

        protected override void Initialize () {
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

        protected override IDecorator GetDefaultDecorations (IDecorationProvider provider) {
            return provider?.RadioButton;
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
}