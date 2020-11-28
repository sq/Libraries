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

        private Control CreateControlForValue (
            ref T value, Control existingControl,
            CreateControlForValueDelegate<T> createControlForValue
        ) {
            Control newControl;
            if (value is Control)
                newControl = (Control)(object)value;
            else if (createControlForValue != null)
                newControl = createControlForValue(ref value, existingControl);
            else
                throw new ArgumentNullException("createControlForValue");

            if (value != null)
                ControlForValue[value] = newControl;

            ValueForControl[newControl] = value;
            return newControl;
        }

        public void GenerateControls (
            ControlCollection output, 
            CreateControlForValueDelegate<T> createControlForValue,
            int offset = 0, int count = int.MaxValue
        ) {
            // FIXME: This is inefficient, it would be cool to reuse existing controls
            //  even if the order of values changes
            ControlForValue.Clear();

            count = Math.Min(Count, count);
            if (offset < 0) {
                count += offset;
                offset = 0;
            }

            count = Math.Max(Math.Min(Count - offset, count), 0);

            while (output.Count > count) {
                var ctl = output[output.Count - 1];
                if (ValueForControl.TryGetValue(ctl, out T temp))
                    ControlForValue.Remove(temp);
                ValueForControl.Remove(ctl);
                output.RemoveAt(output.Count - 1);
            }

            for (int i = 0; i < count; i++) {
                var value = this[i + offset];
                // FIXME
                if (value == null)
                    continue;

                var existingControl = (i < output.Count)
                    ? output[i]
                    : null;

                var newControl = CreateControlForValue(ref value, existingControl, createControlForValue);

                if (newControl != existingControl) {
                    if (i < output.Count)
                        output[i] = newControl;
                    else
                        output.Add(newControl);
                }
            }
        }
    }
}
