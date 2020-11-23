using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Squared.Util;
using Squared.Util.Text;

namespace Squared.PRGUI.Controls {
    public class ItemList<T> : List<T> {
        private readonly Dictionary<T, Control> ControlForValue;
        private readonly Dictionary<Control, T> ValueForControl = 
            new Dictionary<Control, T>(new ReferenceComparer<Control>());

        public ItemList (IEqualityComparer<T> comparer) 
            : base () {
            ControlForValue = new Dictionary<T, Control>(comparer);
        }

        public bool GetControlForValue (T value, out Control result) {
            if (value == null) {
                result = null;
                return false;
            }
            return ControlForValue.TryGetValue(value, out result);
        }

        public bool GetControlForValue (ref T value, out Control result) {
            if (value == null) {
                result = null;
                return false;
            }
            return ControlForValue.TryGetValue(value, out result);
        }

        public bool GetValueForControl (Control control, out T result) {
            if (control == null) {
                result = default(T);
                return false;
            }
            return ValueForControl.TryGetValue(control, out result);
        }

        public void GenerateControls (
            ControlCollection output, 
            CreateControlForValueDelegate<T> createControlForValue
        ) {
            for (int i = 0; i < Count; i++) {
                var value = this[i];
                // FIXME
                if (value == null)
                    continue;
                var existingControl = (i < output.Count)
                    ? output[i]
                    : null;

                Control newControl;
                if (createControlForValue != null)
                    newControl = createControlForValue(ref value, existingControl);
                else if (value is Control)
                    newControl = (Control)(object)value;
                else
                    throw new ArgumentNullException("createControlForValue");

                if (value != null)
                    ControlForValue[value] = newControl;

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
                if (ValueForControl.TryGetValue(output[i], out T temp))
                    ControlForValue.Remove(temp);
                ValueForControl.Remove(output[i]);
                output.RemoveAt(i);
            }
        }
    }
}
