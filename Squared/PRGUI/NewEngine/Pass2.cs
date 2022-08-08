using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Squared.PRGUI.Layout;

namespace Squared.PRGUI.NewEngine {
    public partial class LayoutEngine {
        private Vector2 Pass2_ExpandAndProcessMesses (
            ref BoxRecord control, ref BoxLayoutResult result, int depth, bool processWrap
        ) {
            if (control.FirstChild.IsInvalid)
                return default;

            ref readonly var config = ref control.Config;

            if (config.IsWrap && !processWrap)
                return default;

            float contentWidth = result.Rect.Width - control.Padding.X,
                contentHeight = result.Rect.Height - control.Padding.Y,
                capacity = config.IsVertical ? contentHeight : contentWidth,
                offset = 0, extent = 0;

            // HACK: Unfortunately, we have to build new runs entirely from scratch because modifying the existing ones
            //  in-place would be far too difficult
            // FIXME: Reclaim the existing runs
            int oldFirstRun = result.FirstRunIndex, firstRunIndex = -1, currentRunIndex = -1, numForcedBreaks = 0;
            // HACK
            result.FirstRunIndex = -1;
            bool encounteredWrapChild = false;

            // Scan through all our children and wrap them if necessary now that we know our size
            // For wrap boxes this happens in a second pass over the subtree, otherwise it happens in the first pass
            //  since in the first pass we know everything that needs to be wrapped (it has Break set)
            foreach (var ckey in Children(control.Key)) {
                ref var child = ref this[ckey];
                ref var childResult = ref Result(ckey);
                ref readonly var childConfig = ref child.Config;
                if (childConfig.IsWrap) {
                    encounteredWrapChild = true;
                    continue;
                }

                // If we already processed this control in the initial non-wrap pass, skip it
                if (childResult.Pass2Complete)
                    continue;

                float startMargin = config.IsVertical ? child.Margins.Top : child.Margins.Left,
                    endMargin = config.IsVertical ? child.Margins.Bottom : child.Margins.Right,
                    size = config.IsVertical ? childResult.Rect.Height : childResult.Rect.Width,
                    totalSize = startMargin + size + endMargin;
                var forceBreak = (offset + startMargin + size) > capacity;
                if (forceBreak)
                    numForcedBreaks++;

                var previousRunIndex = currentRunIndex;

                bool isBreak = childConfig.ForceBreak || forceBreak;
                // We still generate runs even if a control is stacked/floating
                // This ensures that you can enumerate all of a control's children by enumerating its runs
                // We will then skip stacked/floating controls when enumerating runs (as appropriate)
                ref var run = ref SelectRunForBuildingPass(ref currentRunIndex, isBreak);
                UpdateRunCommon(
                    ref run, in control, ref result,
                    in child, ref childResult,
                    ref firstRunIndex, currentRunIndex
                );

                if (previousRunIndex != currentRunIndex) {
                    if (previousRunIndex >= 0) {
                        ref var previousRun = ref Run(previousRunIndex);
                        extent += config.IsVertical ? previousRun.MaxOuterWidth : previousRun.MaxOuterHeight;
                    }
                    offset = totalSize;
                } else {
                    offset += totalSize;
                }

                Pass2_ExpandAndProcessMesses(ref child, ref childResult, depth + 1, false);
                childResult.Pass2Complete = true;
            }

            // Now that we computed our new runs with wrapping applied we can expand all our children appropriately
            Pass2b_ExpandChildren(ref control, ref result);

            // And once they're expanded any children with wrapping enabled know their size and can wrap their own children
            if (encounteredWrapChild) {
                foreach (var ckey in Children(control.Key)) {
                    ref var child = ref this[ckey];
                    ref var childResult = ref Result(ckey);
                    Pass1_ComputeSizesAndBuildRuns(ref child, ref childResult, depth + 1, true);
                    Pass2_ExpandAndProcessMesses(ref child, ref childResult, depth + 1, true);
                }
            }

            if (currentRunIndex >= 0) {
                ref var currentRun = ref Run(currentRunIndex);
                extent += config.IsVertical ? currentRun.MaxOuterWidth : currentRun.MaxOuterHeight;
            }

            if (numForcedBreaks <= 0)
                result.FirstRunIndex = oldFirstRun;
            else
                result.FirstRunIndex = firstRunIndex;

            var oldSize = result.Rect.Size;
            if (config.IsVertical)
                result.Rect.Width = control.Width.Constrain(Math.Max(result.Rect.Width, extent + control.Padding.X), true);
            else
                result.Rect.Height = control.Height.Constrain(Math.Max(result.Rect.Height, extent + control.Padding.Y), true);
            // Return the amount our size changed so that our caller can update the run we're in
            return result.Rect.Size - oldSize;
        }

        private Vector2 Pass2b_ExpandChildren (ref BoxRecord control, ref BoxLayoutResult result) {
            if (control.FirstChild.IsInvalid)
                return default;

            ref readonly var config = ref control.Config;

            var oldSize = result.Rect.Size;
            bool needRecalcX = false, needRecalcY = false;
            float w = result.Rect.Width - control.Padding.X, 
                h = result.Rect.Height - control.Padding.Y;

            foreach (var runIndex in Runs(control.Key)) {
                ref var run = ref Run(runIndex);
                var isLastRun = run.NextRunIndex < 0;

                // We track our own count here so that when expansion hits a constraint, we
                //  can reduce the count to evenly distribute the leftovers to non-constrained controls
                int countX = run.ExpandCountX, countY = run.ExpandCountY,
                    newCountX = countX, newCountY = countY;
                // HACK: If a menu contains one item that is like 2000px wide, normally we would expand any other items
                //  to ALSO be 2000px wide. This ensures that if Constrain_Size is set we will only expand other items
                //  to the size of the menu itself
                // In the demo this is necessary to ensure that the 'item a ... item b' menu item is laid out correctly
                float runMaxOuterWidth = config.ConstrainChildren && config.IsVertical ? Math.Min(w, run.MaxOuterWidth) : run.MaxOuterWidth,
                    runMaxOuterHeight = config.ConstrainChildren && !config.IsVertical ? Math.Min(h, run.MaxOuterHeight) : run.MaxOuterHeight,
                    // HACK: For the last run in a box we want to expand the run to fill the entire available space
                    //  otherwise listbox items will have a width of 0 :(
                    effectiveRunMaxWidth = isLastRun && config.IsVertical ? Math.Max(w, runMaxOuterWidth) : runMaxOuterWidth,
                    effectiveRunMaxHeight = isLastRun && !config.IsVertical ? Math.Max(h, runMaxOuterHeight) : runMaxOuterHeight,
                    effectiveRunTotalWidth = run.TotalWidth,
                    effectiveRunTotalHeight = run.TotalHeight,
                    // HACK: In the old engine the last run would be expanded to fill on both axes, and we rely on this
                    //  right now for various things. This should be improved on
                    xSpace = config.IsVertical
                        ? (isLastRun ? w - effectiveRunMaxWidth : 0)
                        : w - effectiveRunTotalWidth,
                    ySpace = config.IsVertical
                        ? h - effectiveRunTotalHeight 
                        : (isLastRun ? h - effectiveRunMaxHeight : 0),
                    newXSpace = xSpace, newYSpace = ySpace,
                    minOuterWidth = config.IsVertical ? effectiveRunMaxWidth : 0,
                    minOuterHeight = config.IsVertical ? 0 : effectiveRunMaxHeight;

                for (int pass = 0; pass < 3; pass++) {
                    if (countX < 1)
                        xSpace = 0;
                    if (countY < 1)
                        ySpace = 0;

                    // Always run a single pass so we can expand controls along the secondary axis to fit their run
                    if ((xSpace <= 1) && (ySpace <= 1) && (pass > 0))
                        break;

                    foreach (var ckey in Enumerate(run.First.Key, run.Last.Key)) {
                        ref var child = ref this[ckey];
                        ref var childResult = ref Result(ckey);
                        var margins = child.Margins;
                        ref readonly var childConfig = ref child.Config;
                        bool expandX = childConfig.FillRow && !child.Width.Fixed.HasValue,
                            expandY = childConfig.FillColumn && !child.Height.Fixed.HasValue;
                        float amountX = countX > 0 ? xSpace / countX : 0, amountY = countY > 0 ? ySpace / countY : 0;

                        if (childConfig.IsStackedOrFloating) {
                            // Floating = Stacked, but don't expand to fill parent
                            // Maybe I should rethink this :)
                            if ((childConfig._BoxFlags & Enums.BoxFlag.Floating) != Enums.BoxFlag.Floating) {
                                if (expandX)
                                    childResult.Rect.Width = child.Width.Constrain(w - child.Margins.X, true);
                                if (expandY)
                                    childResult.Rect.Height = child.Height.Constrain(h - child.Margins.Y, true);
                            }
                        } else {
                            float childOuterW = childResult.Rect.Width + margins.X,
                                childOuterH = childResult.Rect.Height + margins.Y;

                            // When expanding an axis, we will either have an expansion amount or a minimum
                            //  size. In the former case we want to make sure that any remaining expansion
                            //  (due to hitting a size constraint) is redistributed to the remaining controls
                            // In the latter case we want to just expand to hit the minimum (if possible).
                            // This would normally reduce the remaining space but we won't have any space
                            //  in minimum mode since expansion amount and minimum are mutually exclusive
                            // When expanding to hit a minimum we need to subtract our control's margins
                            //  from the minimum, since each control has different margins (the size of a
                            //  run includes the margins of the controls)

                            if (expandX) {
                                run.TotalWidth -= childOuterW;
                                var newChildW = Math.Max(childOuterW + amountX, minOuterWidth);
                                newChildW = child.Width.Constrain(newChildW - margins.X, true) + margins.X;
                                float expanded = newChildW - childOuterW;
                                if (expanded < amountX)
                                    newCountX--;
                                newXSpace -= expanded;
                                childResult.Rect.Width = newChildW - margins.X;
                                run.TotalWidth += newChildW;
                                run.MaxOuterWidth = Math.Max(run.MaxOuterWidth, newChildW);
                            }

                            if (expandY) {
                                run.TotalHeight -= childOuterH;
                                var newChildH = Math.Max(childOuterH + amountY, minOuterHeight);
                                newChildH = child.Height.Constrain(newChildH - margins.Y, true) + margins.Y;
                                float expanded = childOuterH - childResult.Rect.Height;
                                if (expanded < amountY)
                                    newCountY--;
                                newYSpace -= expanded;
                                childResult.Rect.Height = newChildH - margins.Y;
                                run.TotalHeight += newChildH;
                                run.MaxOuterHeight = Math.Max(run.MaxOuterHeight, newChildH);
                            }
                        }
                    }

                    countX = newCountX;
                    countY = newCountY;
                    xSpace = newXSpace;
                    ySpace = newYSpace;
                }

                // I think the outer recursion in Pass2 handles this now
                /*
                // HACK: It would be ideal if we could do this in the previous loop
                foreach (var ckey in Enumerate(run.First.Key, run.Last.Key)) {
                    ref var child = ref this[ckey];
                    ref var childResult = ref Result(ckey);

                    // FIXME
                    // ApplyClip(in control, ref result, in child, ref childResult, in run);
                    // var growth = Pass2_ExpandChildren(ref child, ref childResult, depth);

                    if (growth.X != 0) {
                        run.MaxOuterWidth = Math.Max(run.MaxOuterWidth, childResult.Rect.Width + child.Margins.X);
                        run.TotalWidth += growth.X;
                        needRecalcX = true;
                    }

                    if (growth.Y != 0) {
                        run.MaxOuterHeight = Math.Max(run.MaxOuterHeight, childResult.Rect.Height + child.Margins.Y);
                        run.TotalHeight += growth.Y;
                        needRecalcY = true;
                    }
                }
                */

                if (config.IsVertical)
                    w -= run.MaxOuterWidth;
                else
                    h -= run.MaxOuterHeight;
            }

            if (needRecalcX || needRecalcY) {
                // HACK: We do another pass to recalculate our size if any of our children's sizes 
                //  changed during this wrap/expand pass, since that probably also changed our size
                // FIXME: I hate this duplication
                result.Rect.Size = default;
                foreach (var runIndex in Runs(control.Key)) {
                    ref var run = ref Run(runIndex);
                    if (config.IsVertical) {
                        result.Rect.Width += run.MaxOuterWidth;
                        result.Rect.Height = Math.Max(result.Rect.Height, run.TotalHeight);
                    } else {
                        result.Rect.Width = Math.Max(result.Rect.Width, run.TotalWidth);
                        result.Rect.Height += run.MaxOuterHeight;
                    }
                }

                // TODO: Figure out whether these should actually be enabled, they seem right but they also don't seem to fix anything?
                if (false) //if (PRGUIExtensions.HasFlag(control.OldFlags, ControlFlags.Container_No_Expansion_X))
                    result.Rect.Width = oldSize.X;
                else
                    control.Width.Constrain(ref result.Rect.Width, true);

                if (false) // if (PRGUIExtensions.HasFlag(control.OldFlags, ControlFlags.Container_No_Expansion_Y))
                    result.Rect.Height = oldSize.Y;
                else
                    control.Height.Constrain(ref result.Rect.Height, true);
            }

            return result.Rect.Size - oldSize;
        }
    }
}
