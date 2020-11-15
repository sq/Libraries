using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Input;

namespace Squared.PRGUI.Controls {
    public class ParameterEditor<T> : EditableText
        where T : struct, IEquatable<T> {

        private T _Value;

        public T Value {
            get => _Value;
            set {
                if (_Value.Equals(value))
                    return;
                _Value = value;
                Text = Convert.ToString(value);
            }
        }

        public ParameterEditor ()
            : base () {
            ClampVirtualPositionToTextbox = false;
        }

        protected override void OnValueChanged () {
            try {
                var newValue = Convert.ChangeType(Text, typeof(T));
                _Value = (T)newValue;
            } catch {
            }
        }

        private void FinalizeValue () {
            OnValueChanged();
            Text = Convert.ToString(_Value);
        }

        protected override bool OnKeyPress (KeyEventArgs evt) {
            if (evt.Key == Keys.Enter) {
                FinalizeValue();
                return true;
            }

            return base.OnKeyPress(evt);
        }

        protected override bool OnEvent<T> (string name, T args) {
            if (name == UIEvents.LostFocus)
                FinalizeValue();

            return base.OnEvent<T>(name, args);
        }
    }
}
