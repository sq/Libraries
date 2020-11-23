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

    public class Dropdown<T> : StaticTextBase, Accessibility.IReadingTarget, IMenuListener, IValueControl<T> {
        public IEqualityComparer<T> Comparer;
        public ItemList<T> Items { get; private set; }
        private readonly Menu ItemsMenu = new Menu {
            DeselectOnMouseLeave = false
        };

        public string Description;

        public AbstractString Label;
        public CreateControlForValueDelegate<T> CreateControlForValue = null;
        public Func<T, AbstractString> FormatValue = null;
        private CreateControlForValueDelegate<T> DefaultCreateControlForValue;

        // private bool NeedsUpdate = true;
        private bool MenuJustClosed = false;

        private T _SelectedItem = default(T);
        public T SelectedItem {
            get => _SelectedItem;
            set {
                if (Comparer.Equals(value, _SelectedItem))
                    return;
                if (!Items.Contains(value))
                    throw new ArgumentException("Value not in items list");
                _SelectedItem = value;
                // NeedsUpdate = true;
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

        T IValueControl<T>.Value {
            get => SelectedItem;
            set => SelectedItem = value;
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

        public Dropdown ()
            : this (null) {
        }

        public Dropdown (IEqualityComparer<T> comparer = null) 
            : base () {
            AcceptsFocus = true;
            AcceptsMouseInput = true;
            Comparer = comparer ?? EqualityComparer<T>.Default;
            // FIXME
            Items = new ItemList<T>(Comparer);
            DefaultCreateControlForValue = _DefaultCreateControlForValue;
        }

        private Control _DefaultCreateControlForValue (ref T value, Control existingControl) {
            var st = (existingControl as StaticText) ?? new StaticText();
            var text =
                (FormatValue != null)
                    ? FormatValue(value)
                    : value.ToString();
            st.Text = text;
            st.Data.Set<T>(ref value);
            return st;
        }

        protected override IDecorator GetDefaultDecorator (IDecorationProvider provider) {
            return provider.Dropdown;
        }

        protected override ControlKey OnGenerateLayoutTree (UIOperationContext context, ControlKey parent, ControlKey? existingKey) {
            // if (NeedsUpdate)
                Update();

            return base.OnGenerateLayoutTree(context, parent, existingKey);
        }

        protected void Update () {
            // NeedsUpdate = false;
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

            Items.GenerateControls(
                ItemsMenu.Children, CreateControlForValue ?? DefaultCreateControlForValue
            );

            return result;
        }

        private void ShowMenu () {
            // When the menu closes it will set this flag. If it was closed because a click occurred
            //  on us (outside of the menu), we will hit this point with the flag set and know not
            //  to reopen the menu
            // FIXME: Maybe we should clear it here?
            if (MenuJustClosed)
                return;

            UpdateMenu();

            // FIXME: The ideal behavior is for another click when open to close the dropdown
            if (ItemsMenu.IsActive)
                return;

            var box = GetRect(Context.Layout, contentRect: false);
            ItemsMenu.MinimumWidth = box.Width;
            var selectedIndex = Items.IndexOf(_SelectedItem);
            ItemsMenu.Show(Context, this, selectedIndex >= 0 ? ItemsMenu[selectedIndex] : null);
            MenuJustClosed = false;
        }

        protected override bool OnEvent<TArgs> (string name, TArgs args) {
            if (args is MouseEventArgs)
                return OnMouseEvent(name, (MouseEventArgs)(object)args);
            else if (args is KeyEventArgs)
                return OnKeyEvent(name, (KeyEventArgs)(object)args);
            else
                return base.OnEvent(name, args);
        }

        private bool OnKeyEvent (string name, KeyEventArgs args) {
            switch (name) {
                case UIEvents.KeyPress:
                    Context.OverrideKeyboardSelection(this);
                    var oldSelection = _SelectedItem;
                    var oldIndex = Items.IndexOf(oldSelection);
                    switch (args.Key) {
                        case Keys.Up:
                        case Keys.Down:
                            var newIndex = Arithmetic.Clamp(
                                oldIndex + ((args.Key == Keys.Up) ? -1 : 1),
                                0, Items.Count - 1
                            );
                            SelectedItem = Items[newIndex];
                            return true;
                        case Keys.Space:
                            ShowMenu();
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

            // FIXME: There is probably a better place to clear this flag
            MenuJustClosed = false;
        }

        public override string ToString () {
            return $"Dropdown #{GetHashCode():X8} '{GetTrimmedText(GetValueText().ToString())}'";
        }

        void IMenuListener.Shown (Menu menu) {
        }

        void IMenuListener.Closed (Menu menu) {
            MenuJustClosed = true;
        }

        void IMenuListener.ItemSelected (Menu menu, Control item) {
        }

        void IMenuListener.ItemChosen (Menu menu, Control item) {
            if (Items.GetValueForControl(item, out T value))
                SelectedItem = value;
        }
    }
}
