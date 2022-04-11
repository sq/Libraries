using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using System.Runtime.CompilerServices;
using Squared.PRGUI.Layout;

namespace Squared.PRGUI.NewEngine {
    public partial class LayoutEngine {
       
        private bool IsBreakDimension (LayoutDimensions dim, ControlFlags containerFlags) {
            if (dim == LayoutDimensions.X)
                return containerFlags.HasFlag(ControlFlags.Container_Row);
            else if (dim == LayoutDimensions.Y)
                return containerFlags.HasFlag(ControlFlags.Container_Column);
            else
                throw new ArgumentOutOfRangeException(nameof(dim));
        }

        #region Layout first pass
        private void Pass1_Initialize (ref ControlRecord control, ref ControlLayoutResult result, int depth) {
            result.Rect = result.ContentRect = default;
            result.CompressedSize = result.ExpandedSize = default;
            result.Break = control.Flags.IsFlagged(ControlFlags.Layout_ForceBreak);
            result.RowIndex = 0;
            result.Depth = depth;
        }

        private (Vector2 compressed, Vector2 expanded) Pass1_ComputeRequiredSizes (ref ControlRecord control, ref ControlLayoutResult result, int depth) {
            var x = Pass1_ComputeRequiredSizes_ForDimension(ref control, ref result, LayoutDimensions.X, depth);
            var y = Pass1_ComputeRequiredSizes_ForDimension(ref control, ref result, LayoutDimensions.Y, depth);
            return (new Vector2(x.compressed, y.compressed), new Vector2(x.expanded, y.expanded));
        }

        private void UpdateRun (bool isBreakDim, bool itemBreak, float itemSize, ref float total, ref float run) {
            if (itemBreak) {
                if (isBreakDim) {
                    total = Math.Max(total, run);
                    run = itemSize;
                } else {
                    total += run;
                    run = itemSize;
                }
            } else if (isBreakDim) {
                run += itemSize;
            } else {
                run = Math.Max(run, itemSize);
            }
        }

        private (float compressed, float expanded) Pass1_ComputeRequiredSizes_ForDimension (ref ControlRecord control, ref ControlLayoutResult result, LayoutDimensions dim, int depth) {
            if (dim == LayoutDimensions.X)
                Pass1_Initialize(ref control, ref result, depth);

            var idim = (int)dim;
            ref var sizeConstraints = ref control.Size(dim);

            if (control.FirstChild.IsInvalid) {
                var sz = sizeConstraints.EffectiveMinimum;
                PRGUIExtensions.SetElement(ref result.ContentRect.Size, idim, sz);
                return (sz, sz);
            }

            // If true: We are laying out one row at a time, starting over when we break
            // If false: We are calculating the height of all the rows put together
            var isBreakDim = IsBreakDimension(dim, control.Flags);
            var wrapEnabled = control.Flags.IsFlagged(ControlFlags.Container_Wrap);

            float compressedTotal = 0f, expandedTotal = 0f, 
                compressedRun = 0f, expandedRun = 0f;
            foreach (var ckey in Children(control.Key)) {
                ref var child = ref this[ckey];
                ref var childResult = ref UnsafeResult(ckey);
                // FIXME: Factor in layout modes
                var tup = Pass1_ComputeRequiredSizes_ForDimension(ref child, ref childResult, dim, depth + 1);
                var margin = child.Margins[idim];

                UpdateRun(isBreakDim, childResult.Break || wrapEnabled, tup.compressed + margin, ref compressedTotal, ref compressedRun);
                UpdateRun(isBreakDim, childResult.Break, tup.expanded + margin, ref expandedTotal, ref expandedRun);
            }

            UpdateRun(isBreakDim, true, 0f, ref compressedTotal, ref compressedRun);
            UpdateRun(isBreakDim, true, 0f, ref expandedTotal, ref expandedRun);

            compressedTotal += control.Padding[idim];
            expandedTotal += control.Padding[idim];

            // We separately track the size of our content and the actual size of this control
            //  so that cases where overflow and clipping happen can be factored correctly
            //  into the display of things like scrollbars
            PRGUIExtensions.SetElement(ref result.CompressedSize, idim, compressedTotal);
            PRGUIExtensions.SetElement(ref result.ExpandedSize, idim, expandedTotal);

            sizeConstraints.Constrain(ref compressedTotal, true);
            sizeConstraints.Constrain(ref expandedTotal, true);

            // Set placeholder content size to our absolute minimum on both axes
            PRGUIExtensions.SetElement(ref result.ContentRect.Size, idim, isBreakDim ? compressedTotal : expandedTotal);

            return (compressedTotal, expandedTotal);
        }
        #endregion

        #region Layout second pass
        private void Pass2_ApplyExpansion (ref ControlRecord control, ref ControlLayoutResult result) {
            // FIXME
            bool pcx = control.Flags.IsFlagged(ControlFlags.Container_Prevent_Crush_X),
                pcy = control.Flags.IsFlagged(ControlFlags.Container_Prevent_Crush_Y);

        }
        #endregion

        #region Layout third pass
        private void Pass3_Arrange (ref ControlRecord control, ref ControlLayoutResult result) {
            // FIXME
        }
        #endregion

        private void PerformLayout (ref ControlRecord control) {
            ref var result = ref UnsafeResult(control.Key);
            var rs = Pass1_ComputeRequiredSizes(ref control, ref result, 0);
            Pass2_ApplyExpansion(ref control, ref result);
            Pass3_Arrange(ref control, ref result);
        }
   
    }
}