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

    public class Dropdown<T> : StaticTextBase, Accessibility.IReadingTarget {
        public IEqualityComparer<T> Comparer = EqualityComparer<T>.Default;
        public readonly List<T> Items = new List<T>();
        private readonly Menu ItemsMenu = new Menu();

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
            }
        }

        AbstractString Accessibility.IReadingTarget.Text {
            get {
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

        public Dropdown () : base () {
            AcceptsFocus = true;
            AcceptsMouseInput = true;
        }

        protected override IDecorator GetDefaultDecorations (IDecorationProvider provider) {
            // FIXME
            return provider.Button;
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

            for (int i = 0; i < Items.Count; i++) {
                var value = Items[i];
                var existingControl = (i < ItemsMenu.Count)
                    ? ItemsMenu[i]
                    : null;

                Control newControl;
                if (CreateControlForValue != null)
                    newControl = CreateControlForValue(ref value, existingControl);
                else {
                    AbstractString text = FormatValue != null
                        ? FormatValue(value)
                        : value.ToString();
                    newControl = new StaticText { Text = text, Data = { { null, value } } };
                }

                if (newControl != existingControl) {
                    if (i < ItemsMenu.Count)
                        ItemsMenu[i] = newControl;
                    else
                        ItemsMenu.Add(newControl);
                }
            }

            while (ItemsMenu.Count > Items.Count)
                ItemsMenu.RemoveAt(ItemsMenu.Count - 1);

            return result;
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
            switch (name) {
                case UIEvents.MouseDown:
                case UIEvents.MouseMove:
                case UIEvents.MouseUp:
                    if ((name == UIEvents.MouseMove) && (args.Buttons == MouseButtons.None))
                        return true;

                    // FIXME

                    return true;
            }

            return false;
        }

        protected override void OnRasterize (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings, IDecorator decorations) {
            base.OnRasterize(context, ref renderer, settings, decorations);
        }

        public override string ToString () {
            return $"Dropdown #{GetHashCode():X8} '{GetTrimmedText(GetValueText().ToString())}'";
        }
    }
}
