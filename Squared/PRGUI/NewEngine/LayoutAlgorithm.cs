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
            result.Tag = control.Tag;
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

        private void UpdateRun (
            bool isBreakDim, bool itemBreak, ControlFlags itemFlags,
            float itemSize, ref float total, ref float run
        ) {
            if (itemFlags.IsFlagged(ControlFlags.Layout_Floating)) {
                return;
            } else if (itemFlags.IsFlagged(ControlFlags.Layout_Stacked)) {
                // FIXME
                total = Math.Max(total, itemSize);
                return;
            }

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
            // TODO: Also disable wrap if prevent crush is on (but that's silly anyway)
            var wrapEnabled = control.Flags.IsFlagged(ControlFlags.Container_Wrap);

            float compressedTotal = 0f, expandedTotal = 0f, 
                compressedRun = 0f, expandedRun = 0f;
            foreach (var ckey in Children(control.Key)) {
                ref var child = ref this[ckey];
                ref var childResult = ref UnsafeResult(ckey);
                // FIXME: Factor in layout modes
                var tup = Pass1_ComputeRequiredSizes_ForDimension(ref child, ref childResult, dim, depth + 1);
                var margin = child.Margins[idim];

                UpdateRun(isBreakDim, childResult.Break || wrapEnabled, child.Flags, tup.compressed + margin, ref compressedTotal, ref compressedRun);
                UpdateRun(isBreakDim, childResult.Break, child.Flags, tup.expanded + margin, ref expandedTotal, ref expandedRun);
            }

            UpdateRun(isBreakDim, true, default, 0f, ref compressedTotal, ref compressedRun);
            UpdateRun(isBreakDim, true, default, 0f, ref expandedTotal, ref expandedRun);

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
        private void Pass2_ComputeForcedWrap (ref ControlRecord control, ref ControlLayoutResult result) {
            if (control.FirstChild.IsInvalid)
                return;

            Pass2_ComputeForcedWrap_ForDimension(ref control, ref result, LayoutDimensions.X);
            Pass2_ComputeForcedWrap_ForDimension(ref control, ref result, LayoutDimensions.Y);

            foreach (var ckey in Children(control.Key)) {
                ref var child = ref UnsafeItem(ckey);
                ref var childResult = ref UnsafeResult(ckey);
                Pass2_ComputeForcedWrap(ref child, ref childResult);
            }
        }

        private void Pass2_ComputeForcedWrap_ForDimension (ref ControlRecord control, ref ControlLayoutResult result, LayoutDimensions dim) {
            // A single item cannot wrap
            if (control.FirstChild == control.LastChild)
                return;

            var isBreakDim = IsBreakDimension(dim, control.Flags);
            var wrapEnabled = control.Flags.IsFlagged(ControlFlags.Container_Wrap);
            var pc = dim == LayoutDimensions.X
                ? control.Flags.IsFlagged(ControlFlags.Container_Prevent_Crush_X)
                : control.Flags.IsFlagged(ControlFlags.Container_Prevent_Crush_Y);
            var fastPath = !isBreakDim || !wrapEnabled || pc;

            if (fastPath)
                return;

            var idim = (int)dim;
            float childSize, newExtent, 
                runTotalSize = 0, maxExtent = result.ContentRect.Size.GetElement(idim);

            foreach (var ckey in Children(control.Key)) {
                ref var child = ref UnsafeItem(ckey);
                ref var childResult = ref UnsafeResult(ckey);
                var margin = child.Margins[idim];
                childSize = childResult.ContentRect.Size.GetElement(idim) + margin;

                if (!childResult.Break) {
                    newExtent = runTotalSize + childSize;
                    if (newExtent > maxExtent)
                        childResult.Break = true;
                }

                if (!childResult.Break) {
                    runTotalSize += childSize;
                    continue;
                }

                runTotalSize = 0;
            }
        }
        #endregion

        #region Layout third pass
        private void Pass3_ApplyExpansion (ref ControlRecord control, ref ControlLayoutResult result) {
            if (control.FirstChild.IsInvalid)
                return;

            bool pcx = control.Flags.IsFlagged(ControlFlags.Container_Prevent_Crush_X),
                pcy = control.Flags.IsFlagged(ControlFlags.Container_Prevent_Crush_Y),
                // FIXME: It feels like this should actually be the default, and there should be a flag
                //  to disable it!
                cs = control.Flags.IsFlagged(ControlFlags.Container_Constrain_Size) || true;

            // FIXME: I'm really not sure how this should work
            var space = cs
                ? result.ContentRect.Size
                : result.ExpandedSize;
            float w = pcx
                ? result.ExpandedSize.X
                : space.X,
                h = pcy
                    ? result.ExpandedSize.Y
                    : space.Y;

            Pass3_ApplyExpansion_ForDimension(ref control, ref result, LayoutDimensions.X, w);
            Pass3_ApplyExpansion_ForDimension(ref control, ref result, LayoutDimensions.Y, h);

            // We could maybe recurse in ForDimension but it feels sketchy
            foreach (var ckey in Children(control.Key)) {
                ref var child = ref UnsafeItem(ckey);
                ref var childResult = ref UnsafeResult(ckey);
                Pass3_ApplyExpansion(ref child, ref childResult);
            }
        }

        private bool IsExpand (ref ControlRecord parent, ref ControlRecord child, LayoutDimensions dim) {
            var fill = (dim == LayoutDimensions.X)
                ? child.Flags.IsFlagged(ControlFlags.Layout_Fill_Row)
                : child.Flags.IsFlagged(ControlFlags.Layout_Fill_Column);
            if (!fill)
                return false;
            var ne = (dim == LayoutDimensions.X)
                ? parent.Flags.IsFlagged(ControlFlags.Container_No_Expansion_X)
                : parent.Flags.IsFlagged(ControlFlags.Container_No_Expansion_Y);
            return !ne;
        }

        private void Pass3_ApplyExpansion_ForDimension (ref ControlRecord control, ref ControlLayoutResult result, LayoutDimensions dim, float space) {
            var idim = (int)dim;
            var isBreakDim = IsBreakDimension(dim, control.Flags);
            // TODO: Also disable wrap if prevent crush is on (but that's silly anyway)
            var wrapEnabled = control.Flags.IsFlagged(ControlFlags.Container_Wrap);
            // Scan forward to locate the start and end of each run, then expand all the controls in it
            ControlKey runStart = control.FirstChild, runEnd = runStart;
            // When laying out a row we compute the total size then compute remaining space
            // When laying out all rows we compute the max size of each row to pick a new max
            float runMaxSize = 0, runTotalSize = 0;
            int expandCount = 0;
            var parentSize = result.ContentRect.Size.GetElement(idim);
            foreach (var ckey in Children(control.Key)) {
                ref var child = ref UnsafeItem(ckey);
                ref var childResult = ref UnsafeResult(ckey);
                float childSize = childResult.ContentRect.Size.GetElement(idim),
                    newW = runTotalSize + childSize;

                if (!childResult.Break && isBreakDim && wrapEnabled) {
                    if (newW > parentSize)
                        childResult.Break = true;
                }

                if (!childResult.Break) {
                    var isExpand = IsExpand(ref control, ref child, dim);
                    if (isExpand)
                        expandCount++;
                    runMaxSize = Math.Max(runMaxSize, childSize);
                    runTotalSize += childSize;
                    runEnd = ckey;
                    continue;
                }

                if (expandCount > 0) {
                    float currentSize = isBreakDim ? runTotalSize : runMaxSize,
                        distributed = (space - currentSize) / expandCount,
                        addSize = isBreakDim ? distributed : 0,
                        newMinSize = isBreakDim ? 0 : runMaxSize + distributed;
                    Pass3_ApplyExpansion_ToRun(
                        ref control, ref result, dim,
                        addSize, newMinSize, runStart, runEnd, false
                    );
                }

                expandCount = 0;
                runMaxSize = runTotalSize = 0;
                runStart = runEnd = ckey;
            }

            if (expandCount > 0) {
                float distributed = space / expandCount,
                    addSize = isBreakDim ? distributed : 0,
                    newMinSize = isBreakDim ? 0 : runMaxSize + distributed;
                Pass3_ApplyExpansion_ToRun(
                    ref control, ref result, dim,
                    addSize, newMinSize, runStart, runEnd, false
                );
            }
        }

        private void Pass3_ApplyExpansion_ToRun (
            ref ControlRecord control, ref ControlLayoutResult result, LayoutDimensions dim, 
            float addSize, float newMinSize, ControlKey runStart, ControlKey runEnd, bool nested
        ) {
            var idim = (int)dim;
            var isBreakDim = IsBreakDimension(dim, control.Flags);
            var wrapEnabled = control.Flags.IsFlagged(ControlFlags.Container_Wrap);
            int uncompressedCount = 0;
            float leftover = 0;

            var ckey = runStart;
            do {
                ref var child = ref UnsafeItem(ckey);
                ref var childResult = ref UnsafeResult(ckey);
                ref var sizeConstraints = ref child.Size(dim);

                if (IsExpand(ref control, ref child, dim)) {
                    var size = childResult.ContentRect.Size.GetElement(idim);
                    size = Math.Max(newMinSize, size + addSize);
                    var newSize = sizeConstraints.Constrain(size, true);
                    if ((addSize > 0) && (newSize < size))
                        leftover += size - newSize;
                    else
                        uncompressedCount++;
                    PRGUIExtensions.SetElement(ref childResult.ContentRect.Size, idim, size);
                }

                ckey = child.NextSibling;
            } while (!ckey.IsInvalid && (ckey != runEnd));

            // If constraints stopped us from fully distributing our available space, 
            //  do another pass to spread the leftover space across any controls that
            //  didn't get stopped by constraints
            // Worst case THOSE might hit constraints but eh, screw it.
            if (!nested && (leftover > 1) && (uncompressedCount > 0)) {
                Pass3_ApplyExpansion_ToRun(
                    ref control, ref result, dim, 
                    leftover / uncompressedCount, newMinSize, 
                    runStart, runEnd, true
                );
            }
        }

        #endregion

        #region Layout fourth pass
        private void Pass4_Arrange (ref ControlRecord control, ref ControlLayoutResult result) {
            // FIXME
        }
        #endregion

        private void PerformLayout (ref ControlRecord control) {
            ref var result = ref UnsafeResult(control.Key);
            var rs = Pass1_ComputeRequiredSizes(ref control, ref result, 0);
            Pass2_ComputeForcedWrap(ref control, ref result);
            Pass3_ApplyExpansion(ref control, ref result);
            Pass4_Arrange(ref control, ref result);
        }
   
    }
}