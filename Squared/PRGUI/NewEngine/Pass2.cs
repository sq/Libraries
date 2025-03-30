﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Squared.PRGUI.Layout;
using Squared.PRGUI.NewEngine.Enums;
using Squared.Util;

namespace Squared.PRGUI.NewEngine {
    public partial class LayoutEngine {
        private UnorderedList<ControlKey> RecalcSizeQueue = new UnorderedList<ControlKey>(Capacity);

        private bool Pass2 (
            ref BoxRecord control, ref BoxLayoutResult result, int depth
        ) {
            if (result.Pass2Processed)
                throw new Exception("Already processed phase 2");
            result.Pass2Processed = true;

            ref readonly var config = ref control.Config;

            if (config.GridColumnCount > 0) {
                return Pass2_Grid(ref control, ref result, depth);
            } else {
                var oldSize = result.Rect.Size;

                var needRecalc = false;
                if (config.IsWrap)
                    needRecalc = Pass2a_PerformWrapping(ref control, ref result, depth);

                Pass2b_ExpandChildren(ref control, ref result, depth);

                foreach (var ckey in Children(ref control)) {
                    ref var child = ref this[ckey];
                    ref var childResult = ref UnsafeResult(ckey);
                    if (Pass2(ref child, ref childResult, depth + 1))
                        needRecalc = true;
                }

                if (needRecalc)
                    RecalcSizeQueue.Add(control.Key);
                return needRecalc;
            }
        }

        private unsafe bool Pass2_Grid (
            ref BoxRecord control, ref BoxLayoutResult result, int depth
        ) {
            ref readonly var config = ref control.Config;
            var normalizeHeight = (control.Config.ChildFlags & ContainerFlags.GridNoNormalization) == default;

            var oldSize = result.Rect.Size;

            float cw = result.Rect.Width - control.Padding.X,
                ch = result.Rect.Height - control.Padding.Y,
                columnWidth = cw / config.GridColumnCount;

            int columnIndex = 0;
            int* columns = stackalloc int[config.GridColumnCount];
            foreach (var run in Runs(ref result)) {
                if (columnIndex >= config.GridColumnCount)
                    throw new Exception("Too many columns");
                columns[columnIndex++] = run;
            }

            columnIndex = 0;

            // FIXME: SkipInit
            ControlDimension scratch1 = default, scratch2 = default;

            foreach (var ckey in Children(ref control)) {
                ref var child = ref this[ckey];
                ref var childResult = ref Result(ckey);

                ref var childWidth = ref ControlDimension.ConvertPercentage(ref child.Width, cw, ref scratch1);
                ref var childHeight = ref ControlDimension.ConvertPercentage(ref child.Height, ch, ref scratch2);

                var margins = child.Margins;
                ref readonly var childConfig = ref child.Config;
                bool expandChildX = childConfig.FillRow && !childWidth.HasFixed,
                    expandChildY = childConfig.FillColumn && !childHeight.HasFixed;
                float childOuterW = childResult.Rect.Width + margins.X,
                    childOuterH = childResult.Rect.Height + margins.Y;

                ref var run = ref Run(columns[columnIndex]);

                childResult.Rect.Width = columnWidth;
                childWidth.Constrain(ref childResult.Rect.Width, true);
                if (normalizeHeight) {
                    childResult.Rect.Height = run.MaxOuterHeight;
                    childHeight.Constrain(ref childResult.Rect.Height, true);
                }

                columnIndex = (columnIndex + 1) % config.GridColumnCount;
            }                

            var needRecalc = false;
            foreach (var ckey in Children(ref control)) {
                ref var child = ref this[ckey];
                ref var childResult = ref UnsafeResult(ckey);
                if (Pass2(ref child, ref childResult, depth + 1))
                    needRecalc = true;
            }
            return needRecalc;
        }

        private bool Pass2a_PerformWrapping (
            ref BoxRecord control, ref BoxLayoutResult result, int depth
        ) {
            ref readonly var config = ref control.Config;
            var isVertical = config.IsVertical;
            if (!config.IsWrap)
                throw new Exception("Wrapping not enabled");

            float contentSpaceX = result.Rect.Width - control.Padding.X,
                contentSpaceY = result.Rect.Height - control.Padding.Y;
            if (contentSpaceX <= 0)
                contentSpaceX = control.Width.Constrain(999999f, true);
            if (contentSpaceY <= 0)
                contentSpaceY = control.Height.Constrain(999999f, true);
            float capacity = isVertical ? contentSpaceY : contentSpaceX,
                offset = 0, extent = 0;

            // HACK: Unfortunately, we have to build new runs entirely from scratch because modifying the existing ones
            //  in-place would be far too difficult
            // FIXME: Reclaim the existing runs
            int currentRunIndex = -1, numForcedWraps = 0;
            result.FirstRunIndex = -1;
            result.FloatingRunIndex = -1;

            // Scan through all our children and wrap them if necessary now that we know our size
            foreach (var ckey in Children(ref control)) {
                ref var child = ref this[ckey];
                ref var childResult = ref UnsafeResult(ckey);
                childResult.ParentRunIndex = -1;
                ref readonly var childConfig = ref child.Config;

                float startMargin = isVertical ? child.Margins.Top : child.Margins.Left,
                    endMargin = isVertical ? child.Margins.Bottom : child.Margins.Right,
                    size = isVertical ? childResult.Rect.Height : childResult.Rect.Width;
                
                // Containers that wrap will auto-expand to available space so we don't count the size of their content
                if (isVertical) {
                    if (childConfig.IsVertical && childConfig.IsWrap)
                        size = child.Height.Constrain(0, true);
                } else {
                    if (!childConfig.IsVertical && childConfig.IsWrap)
                        size = child.Width.Constrain(0, true);
                }
                float totalSize = startMargin + size + endMargin,
                    wrappingExtent = (childConfig._BoxFlags & BoxFlag.CollapseMargins) == BoxFlag.CollapseMargins
                        ? size
                        : totalSize;

                var forcedWrap = !childConfig.IsStackedOrFloating && ((offset + wrappingExtent) > capacity);
                if (forcedWrap)
                    numForcedWraps++;

                var previousRunIndex = currentRunIndex;

                bool isBreak = childConfig.ForceBreak || forcedWrap;
                // We still generate runs even if a control is stacked/floating
                // This ensures that you can enumerate all of a control's children by enumerating its runs
                // We will then skip stacked/floating controls when enumerating runs (as appropriate)
                ref var run = ref SelectRunForBuildingPass(
                    ref result.FloatingRunIndex, ref result.FirstRunIndex,
                    ref currentRunIndex, childConfig.IsStackedOrFloating, isBreak
                );
                if (UpdateRunCommon(
                    ref run, ref control, ref result,
                    ref child, ref childResult,
                    ref result.FirstRunIndex, currentRunIndex
                )) {
                    if (result.FloatingRunIndex >= 0)
                        Run(result.FloatingRunIndex).NextRunIndex = currentRunIndex;
                }

                if (previousRunIndex != currentRunIndex) {
                    if (previousRunIndex >= 0) {
                        ref var previousRun = ref Run(previousRunIndex);
                        extent += isVertical ? previousRun.MaxOuterWidth : previousRun.MaxOuterHeight;
                    }
                    offset = totalSize;
                } else {
                    offset += totalSize;
                }

                childResult.SizeSetByParent = true;
            }

            if (currentRunIndex >= 0) {
                ref var currentRun = ref Run(currentRunIndex);
                extent += isVertical ? currentRun.MaxOuterWidth : currentRun.MaxOuterHeight;
            }

            // Now that we computed our new runs with wrapping applied we can expand all our children appropriately
            Pass2b_ExpandChildren(ref control, ref result, depth);

            var oldSize = result.Rect.Size;
            if (isVertical)
                result.Rect.Width = control.Width.Constrain(Math.Max(result.Rect.Width, extent + control.Padding.X), true);
            else
                result.Rect.Height = control.Height.Constrain(Math.Max(result.Rect.Height, extent + control.Padding.Y), true);

            return numForcedWraps > 0;
        }

        private void Pass2b_ExpandChildren (ref BoxRecord control, ref BoxLayoutResult result, int depth) {
            ref readonly var config = ref control.Config;
            var isVertical = config.IsVertical;

            var oldSize = result.Rect.Size;
            float cw = result.Rect.Width - control.Padding.X,
                ch = result.Rect.Height - control.Padding.Y;

            // HACK: Find the last run that contains an expandable child and mark that as the one to perform expansion for
            // This allows having things like status bars at the bottom of a box without them eating all the expansion space
            var lastExpandableRun = -1;
            var spaceAfterLastExpandableRun = 0f;
            foreach (var runIndex in Runs(ref result)) {
                ref var run = ref Run(runIndex);
                var expand = isVertical ? run.ExpandCountX > 0 : run.ExpandCountY > 0;
                if (expand) {
                    lastExpandableRun = runIndex;
                    spaceAfterLastExpandableRun = 0;
                } else {
                    spaceAfterLastExpandableRun += isVertical ? run.MaxOuterWidth : run.MaxOuterHeight;
                }
            }

            // FIXME: SkipInit
            ControlDimension scratch1 = default, scratch2 = default;

            foreach (var runIndex in Runs(ref result)) {
                ref var run = ref Run(runIndex);
                var expandThisRun = (lastExpandableRun == runIndex) || (run.NextRunIndex < 0) || (runIndex == result.FloatingRunIndex);

                // We track our own count here so that when expansion hits a constraint, we
                //  can reduce the count to evenly distribute the leftovers to non-constrained controls
                int countX = run.ExpandCountX, countY = run.ExpandCountY,
                    newCountX = countX, newCountY = countY;
                // HACK: If a menu contains one item that is like 2000px wide, normally we would expand any other items
                //  to ALSO be 2000px wide. This ensures that if Constrain_Size is set we will only expand other items
                //  to the size of the menu itself
                // In the demo this is necessary to ensure that the 'item a ... item b' menu item is laid out correctly
                float runMaxOuterWidth = config.Clip && isVertical ? Math.Min(cw, run.MaxOuterWidth) : run.MaxOuterWidth,
                    runMaxOuterHeight = config.Clip && !isVertical ? Math.Min(ch, run.MaxOuterHeight) : run.MaxOuterHeight,
                    // The last run in a box might not contain any expandable controls, in which case the pass up above determined
                    //  that an earlier run should be expanded instead. We want to ensure that when we expand a run other than
                    //  the last one, we leave enough space for the non-expandable runs that follow it, like a status bar.
                    unexpandableBuffer = (runIndex == result.FloatingRunIndex) ? 0 : spaceAfterLastExpandableRun,
                    // HACK: For the last run in a box we want to expand the run to fill the entire available space
                    //  otherwise listbox items will have a width of 0 :(
                    effectiveRunMaxWidth = expandThisRun && isVertical ? Math.Max(cw - unexpandableBuffer, runMaxOuterWidth) : runMaxOuterWidth,
                    effectiveRunMaxHeight = expandThisRun && !isVertical ? Math.Max(ch - unexpandableBuffer, runMaxOuterHeight) : runMaxOuterHeight,
                    effectiveRunTotalWidth = run.TotalWidth,
                    effectiveRunTotalHeight = run.TotalHeight,
                    // HACK: In the old engine the last run would be expanded to fill on both axes, and we rely on this
                    //  right now for various things. This should be improved on
                    // We need unexpandableBuffer here too for cases where xSpace/ySpace are doing the expanding
                    xSpace = isVertical
                        ? (expandThisRun ? cw - effectiveRunMaxWidth - unexpandableBuffer : 0)
                        : cw - effectiveRunTotalWidth,
                    ySpace = isVertical
                        ? ch - effectiveRunTotalHeight 
                        : (expandThisRun ? ch - effectiveRunMaxHeight - unexpandableBuffer : 0),
                    newXSpace = xSpace, newYSpace = ySpace,
                    minOuterWidth = isVertical ? effectiveRunMaxWidth : 0,
                    minOuterHeight = isVertical ? 0 : effectiveRunMaxHeight;

                for (int pass = 0; pass < 10; pass++) {
                    if (countX < 1)
                        xSpace = 0;
                    if (countY < 1)
                        ySpace = 0;

                    // Always run a single pass so we can expand controls along the secondary axis to fit their run
                    if ((xSpace <= 1) && (ySpace <= 1) && (pass > 0))
                        break;

                    float p = 0;
                    ref var child = ref FirstItemInRun(ref run);
                    var stopAt = run.Last.Key;
                    while (!child.IsInvalid) {
                        // HACK: The floating run and non-floating run potentially walk us through the same controls,
                        //  so skip anything we shouldn't be processing in this run. Yuck.
                        if (child.Config.IsStackedOrFloating != run.IsFloating) {
                            child = ref NextSibling(ref child, stopAt);
                            continue;
                        }

                        ref var childResult = ref Result(child.Key);

                        ref var childWidth = ref ControlDimension.ConvertPercentage(ref child.Width, cw, ref scratch1);
                        ref var childHeight = ref ControlDimension.ConvertPercentage(ref child.Height, ch, ref scratch2);
#if DEBUG
                        if (childResult.ParentRunIndex != runIndex)
                            throw new Exception();
#endif

                        var margins = child.Margins;
                        ref readonly var childConfig = ref child.Config;
                        bool expandChildX = childConfig.FillRow && !childWidth.HasFixed,
                            expandChildY = childConfig.FillColumn && !childHeight.HasFixed;
                        float amountX = countX > 0 ? xSpace / countX : 0, amountY = countY > 0 ? ySpace / countY : 0;

                        // If a control's parent has a size constraint set, it may be too small to hold all of its children,
                        //  which will produce a negative expansion amount and try to crush the children.
                        // If the child has preventcrush set, we shouldn't crush it, so we need to set the amount to 0
                        if ((childConfig.ChildFlags & ContainerFlags.PreventCrush_X) == ContainerFlags.PreventCrush_X)
                            amountX = Math.Max(0, amountX);
                        if ((childConfig.ChildFlags & ContainerFlags.PreventCrush_Y) == ContainerFlags.PreventCrush_Y)
                            amountY = Math.Max(0, amountY);

                        if (childConfig.IsStackedOrFloating) {
                            // Floating = Stacked, but don't expand to fill parent
                            // Maybe I should rethink this :)
                            // if ((childConfig._BoxFlags & BoxFlag.Floating) != BoxFlag.Floating) {
                            if (expandChildX || childWidth.HasFixed)
                                childResult.Rect.Width = childWidth.Constrain(cw - child.Margins.X, true);
                            if (expandChildY || childHeight.HasFixed)
                                childResult.Rect.Height = childHeight.Constrain(ch - child.Margins.Y, true);
                            // }
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

                            if (expandChildX) {
                                run.TotalWidth -= childOuterW;
                                var newChildW = Math.Max(childOuterW + amountX, minOuterWidth);
                                childResult.AvailableSpace.X = Math.Max(childResult.AvailableSpace.X, newChildW);
                                newChildW = childWidth.Constrain(newChildW - margins.X, true) + margins.X;
                                if (config.Clip || (config.ConstrainGrowth && (newChildW > childOuterW)))
                                    newChildW = Math.Min(newChildW, isVertical ? cw : cw - p);
                                float expanded = newChildW - childOuterW;
                                if (expanded < Math.Max(0, amountX - 1))
                                    newCountX--;
                                newXSpace -= expanded;
                                childResult.Rect.Width = newChildW - margins.X;
                                run.TotalWidth += newChildW;
                                run.MaxOuterWidth = Math.Max(run.MaxOuterWidth, newChildW);
                            }

                            if (expandChildY) {
                                run.TotalHeight -= childOuterH;
                                var newChildH = Math.Max(childOuterH + amountY, minOuterHeight);
                                if (config.Clip || (config.ConstrainGrowth && (newChildH > childOuterH)))
                                    newChildH = Math.Min(newChildH, isVertical ? ch - p : ch); 
                                childResult.AvailableSpace.Y = Math.Max(childResult.AvailableSpace.Y, newChildH);
                                newChildH = childHeight.Constrain(newChildH - margins.Y, true) + margins.Y;
                                float expanded = childOuterH - childResult.Rect.Height;
                                if (expanded < Math.Max(0, amountY - 1))
                                    newCountY--;
                                newYSpace -= expanded;
                                childResult.Rect.Height = newChildH - margins.Y;
                                run.TotalHeight += newChildH;
                                run.MaxOuterHeight = Math.Max(run.MaxOuterHeight, newChildH);
                            }

                            if (isVertical)
                                p += childOuterH;
                            else
                                p += childOuterW;
                        }

                        child = ref NextSibling(ref child, stopAt);
                    }

                    countX = newCountX;
                    countY = newCountY;
                    xSpace = newXSpace;
                    ySpace = newYSpace;
                }

                if (isVertical) {
                    cw -= run.MaxOuterWidth;
                } else {
                    ch -= run.MaxOuterHeight;
                }
            }
        }

        private void Pass2c_Recalculate (ref BoxRecord control, ref BoxLayoutResult result) {
            ref readonly var config = ref control.Config;
            var isVertical = config.IsVertical;
            if (control.FirstChild.IsInvalid)
                return;

            float cw = 0, ch = 0;

            // HACK: We do another pass to recalculate our size if any of our children's sizes 
            //  changed during the wrap/expand pass, since that probably also changed our size
            foreach (var runIndex in Runs(ref result)) {
                ref var run = ref Run(runIndex);

                // FIXME: Collapse margins
                if (runIndex == result.FloatingRunIndex) {
                    cw = Math.Max(cw, run.MaxOuterWidth);
                    ch = Math.Max(ch, run.MaxOuterHeight);
                } else if (isVertical) {
                    cw += run.MaxOuterWidth;
                    ch = Math.Max(ch, run.TotalHeight);
                } else {
                    ch += run.MaxOuterHeight;
                    cw = Math.Max(cw, run.TotalWidth);
                }
            }

            float width = cw + control.Padding.X, height = ch + control.Padding.Y;
            // TODO: Figure out whether these should actually be enabled, they seem right but they also don't seem to fix anything?
            control.Width.Constrain(ref width, true);
            control.Height.Constrain(ref height, true);
            // HACK: Never shrink after recalculation (is this correct?)
            var oldSize = result.Rect.Size;
            result.Rect.Width = Math.Max(width, result.Rect.Width);
            result.Rect.Height = Math.Max(height, result.Rect.Height);
            result.ContentSize = new Vector2(cw, ch);

            // If we have a parent, resize the run that contains us. This is essential for the parent's size calculations
            //  to end up being correct.
            if (result.ParentRunIndex >= 0) {
                ref var parentRun = ref Run(result.ParentRunIndex);
                var sizeDelta = result.Rect.Size - oldSize;
                if (parentRun.IsVertical) {
                    parentRun.TotalWidth += sizeDelta.X;
                } else {
                    parentRun.TotalHeight += sizeDelta.Y;
                }
                parentRun.MaxOuterWidth = Math.Max(parentRun.MaxOuterWidth, result.Rect.Width + control.Margins.X);
                parentRun.MaxOuterHeight = Math.Max(parentRun.MaxOuterHeight, result.Rect.Height + control.Margins.Y);
            }
        }
    }
}
