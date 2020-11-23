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
        public readonly ItemList<T> Items = new ItemList<T>();
        private readonly Menu ItemsMenu = new Menu {
            DeselectOnMouseLeave = false
        };

        public string Description;

        public CreateControlForValueDelegate<T> CreateControlForValue = null;
        public Func<T, AbstractString> FormatValue = null;
        public AbstractString Label;

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

        public Dropdown (IEqualityComparer<T> comparer = null) : base () {
            AcceptsFocus = true;
            AcceptsMouseInput = true;
            Comparer = comparer ?? EqualityComparer<T>.Default;
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
                ItemsMenu.Children, CreateControlForValue,
                FormatValue
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

    public class ItemList<T> : List<T> {
        private readonly Dictionary<Control, T> ValueForControl = 
            new Dictionary<Control, T>(new ReferenceComparer<Control>());

        public bool GetValueForControl (Control control, out T result) {
            return ValueForControl.TryGetValue(control, out result);
        }

        public void GenerateControls (
            ControlCollection output, 
            CreateControlForValueDelegate<T> createControlForValue, 
            Func<T, AbstractString> formatValue
        ) {
            for (int i = 0; i < Count; i++) {
                var value = this[i];
                var existingControl = (i < output.Count)
                    ? output[i]
                    : null;

                Control newControl;
                if (createControlForValue != null)
                    newControl = createControlForValue(ref value, existingControl);
                else if (value is Control)
                    newControl = (Control)(object)value;
                else {
                    AbstractString text = formatValue != null
                        ? formatValue(value)
                        : value.ToString();
                    newControl = new StaticText {
                        Text = text,
                        Data = {
                            { null, value }
                        }
                    };
                }

                if (newControl != existingControl) {
                    ValueForControl[newControl] = value;
                    if (i < output.Count)
                        output[i] = newControl;
                    else
                        output.Add(newControl);
                }
            }

            while (output.Count > Count) {
                var i = output.Count - 1;
                ValueForControl.Remove(output[i]);
                output.RemoveAt(i);
            }
        }
    }
}
