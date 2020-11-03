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
    public class Dropdown<T> : StaticTextBase, Accessibility.IReadingTarget {
        public IEqualityComparer<T> Comparer = EqualityComparer<T>.Default;
        public readonly List<T> Items = new List<T>();
        private readonly Menu ItemsMenu = new Menu();

        private T _SelectedItem = default(T);
        public T SelectedItem {
            get => _SelectedItem;
            set {
                if (Comparer.Equals(value, _SelectedItem))
                    return;
                if (!Items.Contains(value))
                    throw new ArgumentException("Value not in items list");
                // FIXME
            }
        }

        AbstractString Accessibility.IReadingTarget.Text => (string)null;
        void Accessibility.IReadingTarget.FormatValueInto (StringBuilder sb) => FormatValue(sb);

        public Dropdown () : base () {
            AcceptsFocus = true;
            AcceptsMouseInput = true;
        }

        public void FormatValue (StringBuilder sb) {
            // FIXME
        }

        protected override IDecorator GetDefaultDecorations (IDecorationProvider provider) {
            // FIXME
            return provider.Button;
        }

        protected override bool OnEvent<T> (string name, T args) {
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
    }
}
