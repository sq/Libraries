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
    public delegate Control CreateControlForValueDelegate<T> (ref T value, Control existingControl);

    public class Dropdown<T> : StaticTextBase, Accessibility.IReadingTarget, IMenuListener {
        public IEqualityComparer<T> Comparer;
        public readonly List<T> Items = new List<T>();
        private readonly Menu ItemsMenu = new Menu {
            DeselectOnMouseLeave = false
        };

        public string Description;

        private readonly Dictionary<Control, T> ValueForControl = new Dictionary<Control, T>(new ReferenceComparer<Control>());
        public CreateControlForValueDelegate<T> CreateControlForValue = null;
        public Func<T, AbstractString> FormatValue = null;
        public AbstractString Label;

        private bool NeedsUpdate = true;

        private T _SelectedItem = default(T);
        public T SelectedItem {
            get => _SelectedItem;
            set {
                if (Comparer.Equals(value, _SelectedItem))
                    return;
                if (!Items.Contains(value))
                    throw new ArgumentException("Value not in items list");
                _SelectedItem = value;
                NeedsUpdate = true;
                Invalidate();
                FireEvent(UIEvents.ValueChanged, _SelectedItem);
            }
        }

        AbstractString Accessibility.IReadingTarget.Text {
            get {
                if (Description != null)
                    return $"{Description}: {GetValueText()}";
                else if (TooltipContent)
                    return TooltipContent.Get(this);

                var irt = _SelectedItem as Accessibility.IReadingTarget;
                if (irt != null)
                    return irt.Text;
                else
                    return GetValueText();
            }
        }

        void Accessibility.IReadingTarget.FormatValueInto (StringBuilder sb) {
            var irt = _SelectedItem as Accessibility.IReadingTarget;
            if (irt != null)
                irt.FormatValueInto(sb);
            else
                sb.Append(GetValueText());
        }

        AbstractString GetValueText () {
            var stb = _SelectedItem as StaticTextBase;
            if (FormatValue != null)
                return FormatValue(_SelectedItem);
            else if (stb != null)
                return stb.Text;
            else
                return _SelectedItem?.ToString();
        }

        public Dropdown (IEqualityComparer<T> comparer = null) : base () {
            AcceptsFocus = true;
            AcceptsMouseInput = true;
            Comparer = comparer ?? EqualityComparer<T>.Default;
        }

        protected override IDecorator GetDefaultDecorations (IDecorationProvider provider) {
            return provider.Dropdown;
        }

        protected override ControlKey OnGenerateLayoutTree (UIOperationContext context, ControlKey parent, ControlKey? existingKey) {
            // if (NeedsUpdate)
                Update();

            return base.OnGenerateLayoutTree(context, parent, existingKey);
        }

        protected void Update () {
            NeedsUpdate = false;
            if (Comparer.Equals(_SelectedItem, default(T)) && (Items.Count > 0))
                SelectedItem = Items[0];

            if (Label != default(AbstractString))
                Text = Label;
            else
                Text = GetValueText();
        }

        protected Control UpdateMenu () {
            Control result = null;

            if (Description != null)
                ItemsMenu.Description = $"Menu {Description}";

            for (int i = 0; i < Items.Count; i++) {
                var value = Items[i];
                var existingControl = (i < ItemsMenu.Count)
                    ? ItemsMenu[i]
                    : null;

                Control newControl;
                if (CreateControlForValue != null)
                    newControl = CreateControlForValue(ref value, existingControl);
                else if (value is Control)
                    newControl = (Control)(object)value;
                else {
                    AbstractString text = FormatValue != null
                        ? FormatValue(value)
                        : value.ToString();
                    newControl = new StaticText { Text = text, Data = { { null, value } } };
                }

                if (newControl != existingControl) {
                    ValueForControl[newControl] = value;
                    if (i < ItemsMenu.Count)
                        ItemsMenu[i] = newControl;
                    else
                        ItemsMenu.Add(newControl);
                }
            }

            while (ItemsMenu.Count > Items.Count) {
                var i = ItemsMenu.Count - 1;
                ValueForControl.Remove(ItemsMenu[i]);
                ItemsMenu.RemoveAt(i);
            }

            return result;
        }

        private void ShowMenu () {
            UpdateMenu();

            if (ItemsMenu.IsActive)
                return;

            var box = GetRect(Context.Layout, contentRect: false);
            ItemsMenu.MinimumWidth = box.Width;
            var selectedIndex = Items.IndexOf(_SelectedItem);
            ItemsMenu.Show(Context, this, selectedIndex >= 0 ? ItemsMenu[selectedIndex] : null);
        }

        protected override bool OnEvent<TArgs> (string name, TArgs args) {
            if (args is MouseEventArgs)
                return OnMouseEvent(name, (MouseEventArgs)(object)args);
            else if (args is KeyEventArgs)
                return OnKeyEvent(name, (KeyEventArgs)(object)args);
            else if (name == UIEvents.Click) {
                ShowMenu();
                return true;
            } else
                return base.OnEvent(name, args);
        }

        private bool OnKeyEvent (string name, KeyEventArgs args) {
            switch (name) {
                case UIEvents.KeyPress:
                    Context.OverrideKeyboardSelection(this);
                    var oldSelection = _SelectedItem;
                    switch (args.Key) {
                        case Keys.Up:
                            // FIXME
                            return true;
                        case Keys.Down:
                            return true;
                        default:
                            return false;
                    }
            }

            return false;
        }

        private bool OnMouseEvent (string name, MouseEventArgs args) {
            if (name == UIEvents.MouseDown) {
                ShowMenu();
                return true;
            }

            return false;
        }

        protected override void OnRasterize (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings, IDecorator decorations) {
            if (ItemsMenu.IsActive)
                settings.State |= ControlStates.Pressed;
            base.OnRasterize(context, ref renderer, settings, decorations);
        }

        public override string ToString () {
            return $"Dropdown #{GetHashCode():X8} '{GetTrimmedText(GetValueText().ToString())}'";
        }

        void IMenuListener.Shown (Menu menu) {
        }

        void IMenuListener.Closed (Menu menu) {
        }

        void IMenuListener.ItemSelected (Menu menu, Control item) {
        }

        void IMenuListener.ItemChosen (Menu menu, Control item) {
            var value = ValueForControl[item];
            SelectedItem = value;
        }
    }
}
