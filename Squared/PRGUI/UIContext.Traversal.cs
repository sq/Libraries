using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Squared.PRGUI.Controls;
using Squared.PRGUI.Input;
using Squared.Render.Text;
using Squared.Util;

namespace Squared.PRGUI {
    public sealed partial class UIContext : IDisposable {
        public record struct FocusMapEntry (int globalIndex, int depth, IControlContainer parent, int indexInParent, Control control, bool validTarget) {
            public sealed class Comparer : IComparer<FocusMapEntry>, IEqualityComparer<FocusMapEntry> {
                public static readonly Comparer Instance = new ();

                public int Compare (FocusMapEntry x, FocusMapEntry y) =>
                    x.GetSortingTuple().CompareTo(y.GetSortingTuple());

                public bool Equals (FocusMapEntry x, FocusMapEntry y) =>
                    x.Equals(y);

                public int GetHashCode (FocusMapEntry obj) =>
                    obj.GetHashCode();
            }

            // FIXME: Generate a full chain of tab order values
            public (int parentTabIndex, int tabIndex, int globalIndex) GetSortingTuple () =>
                ((parent as Control)?.TabOrder ?? 0, control.TabOrder, globalIndex);
        }

        public sealed class FocusMap : List<FocusMapEntry> {
            public int IndexOf (Control control) {
                for (int i = 0, c = Count; i < c; i++)
                    if (this[i].control == control)
                        return i;

                return -1;
            }
        }

        public FocusMap BuildFocusMap (Control scope = null, Func<Control, bool> predicate = null) {
            var result = new FocusMap();
            BuildFocusMap(result, scope, predicate);
            return result;
        }

        public void BuildFocusMap (FocusMap result, Control scope = null, Func<Control, bool> predicate = null) {
            if (scope == null) {
                for (int i = 0, c = Controls.Count; i < c; i++)
                    PopulateUnorderedFocusMap(result, null, i, Controls[i], 0, true, predicate);
            } else {
                PopulateUnorderedFocusMap(result, null, 0, scope, 0, true, predicate);
            }
            result.Sort(FocusMapEntry.Comparer.Instance);
        }

        private void PopulateUnorderedFocusMap (
            FocusMap result, IControlContainer parent, int indexInParent, 
            Control control, int currentDepth, bool validTarget, Func<Control, bool> predicate
        ) {
            {
                var _validTarget = validTarget;
                if (predicate != null)
                    _validTarget = _validTarget && predicate(control);

                result.Add(new FocusMapEntry(result.Count, currentDepth, parent, indexInParent, control, _validTarget));
            }

            if (control is IControlContainer container) {
                var _validTarget = validTarget && control.Enabled && 
                    // FIXME: IsValidFocusTarget already computes this
                    !Control.IsRecursivelyTransparent(control, true, ignoreFadeIn: true);
                var cc = container.Children;
                var depth = currentDepth + 1;
                for (int i = 0, c = cc.Count; i < c; i++)
                    PopulateUnorderedFocusMap(result, container, i, cc[i], depth, _validTarget, predicate);
            }
        }

        private bool _FocusablePredicate (Control control) => control.IsValidFocusTarget;
        private bool _RotatablePredicate (Control control) => control.EligibleForFocusRotation;

        Func<Control, bool> FocusablePredicate, RotatablePredicate;

        FocusMap FocusMap_PFC = new (),
            FocusMap_FC = new (),
            FocusMap_PFSFR = new ();

        public Control FindChild (Control container, Func<Control, bool> predicate) {
            lock (FocusMap_FC) {
                FocusMap_FC.Clear();
                BuildFocusMap(FocusMap_FC, container, predicate);
                foreach (var tup in FocusMap_FC)
                    if (tup.validTarget)
                        return tup.control;
                return null;
            }
        }

        public Control PickFocusableChild (Control container) {
            lock (FocusMap_PFC) {
                FocusMap_PFC.Clear();
                BuildFocusMap(FocusMap_PFC, container, FocusablePredicate ?? (FocusablePredicate = _FocusablePredicate));
                foreach (var tup in FocusMap_PFC)
                    if (tup.validTarget)
                        return tup.control;
                return null;
            }
        }

        public Control PickFocusableSiblingForRotation (Control child, int direction, bool? allowLoop, out bool didFollowProxy) {
            didFollowProxy = false;

            lock (FocusMap_PFSFR) {
                FocusMap_PFSFR.Clear();
                var scope = FindTopLevelAncestor(child);
                if (scope == null) {
                    for (int i = 0, c = Controls.Count; i < c; i++) {
                        var control = Controls[i];
                        FocusMap_PFSFR.Add(new FocusMapEntry(i, 0, null, i, control, _RotatablePredicate(control)));
                    }
                } else {
                    BuildFocusMap(FocusMap_PFSFR, scope, RotatablePredicate ?? (RotatablePredicate = _RotatablePredicate));
                }
                if (FocusMap_PFSFR.Count == 0)
                    return null;

                int index = FocusMap_PFSFR.IndexOf(child),
                    initialIndex = index;

                if (index < 0)
                    return null;

                do {
                    index += direction;
                    if (allowLoop != false)
                        index = Arithmetic.Wrap(index, 0, FocusMap_PFSFR.Count - 1);
                    else if (index < 0)
                        return null;
                    else if (index >= FocusMap_PFSFR.Count)
                        return null;
                } while (!FocusMap_PFSFR[index].validTarget && (index != initialIndex));

                var result = FocusMap_PFSFR[index].control;
                // FIXME: Detect cycles
                while (result is FocusProxy fp) {
                    didFollowProxy = true;
                    result = fp.FocusBeneficiary;
                }
                return result;
            }
        }
    }
}
