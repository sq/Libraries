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
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ref ControlLayoutRun Run (int index) {
            if ((index < 0) || (index >= _RunCount))
                throw new ArgumentOutOfRangeException(nameof(index));
            return ref RunBuffer[index];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ref ControlLayoutRun PushRun (out int index) {
            return ref InsertRun(out index, -1);
        }

        private ref ControlLayoutRun InsertRun (out int index, int afterIndex) {
            index = _RunCount++;
            if (index >= RunBuffer.Length)
                throw new Exception("Run buffer full");
            ref var result = ref RunBuffer[index];
            if (afterIndex >= 0) {
                ref var after = ref Run(afterIndex);
                var beforeIndex = after.NextRunIndex;
                after.NextRunIndex = index;
                result.NextRunIndex = beforeIndex;
            } else
                result.NextRunIndex = -1;
            return ref result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ref ControlLayoutRun GetOrPushRun (ref int index) {
            if (index < 0)
                return ref PushRun(out index);
            else
                return ref Run(index);
        }

        private struct ControlTraits {
            public bool vertical, wrap, noExpandX, noExpandY,
                constrain, clip, clipX, clipY,
                inX, inY, fillX, fillY;

            public ControlTraits (in ControlRecord control) {
                vertical = control.Flags.IsFlagged(ControlFlags.Container_Column);
                wrap = control.Flags.IsFlagged(ControlFlags.Container_Break_Auto);
                constrain = control.Flags.IsFlagged(ControlFlags.Container_Constrain_Growth);
                clip = control.Flags.IsFlagged(ControlFlags.Container_Clip_Children);
                clipX = clip && !control.Flags.IsFlagged(ControlFlags.Container_Prevent_Crush_X);
                clipY = clip && !control.Flags.IsFlagged(ControlFlags.Container_Prevent_Crush_Y);
                noExpandX = control.Flags.IsFlagged(ControlFlags.Container_No_Expansion_X);
                noExpandY = control.Flags.IsFlagged(ControlFlags.Container_No_Expansion_Y);
                // Size on this axis is determined by our parent
                inX = wrap && !vertical;
                inY = wrap && vertical;
                fillX = (control.Flags.IsFlagged(ControlFlags.Layout_Fill_Row) || inX) && !control.Width.Fixed.HasValue;
                fillY = (control.Flags.IsFlagged(ControlFlags.Layout_Fill_Column) || inY) && !control.Height.Fixed.HasValue;
            }
        }

        private void InitializeResult (ref ControlRecord control, ref ControlLayoutResult result, int depth) {
            result.Tag = control.Tag;
            result.Rect = result.ContentRect = default;
            result.FirstRunIndex = -1;
#if DEBUG
            result.Break = control.Flags.IsFlagged(ControlFlags.Layout_ForceBreak);
            result.Depth = depth;
#endif
            result.Version = Version;
            _Count = Math.Max(control.Key.ID + 1, _Count);
        }

        #region Pass 1
        private void Pass1_ComputeSizesAndBuildRuns (ref ControlRecord control, ref ControlLayoutResult result, int depth) {
            InitializeResult(ref control, ref result, depth);

            // During this pass, result.Rect contains our minimum size:
            // Size constraints, largest of all our children, etc
            // result.ContentRect will contain the total size of *all* of our children, ignoring forced wrapping
            result.Rect.Width = control.Size(LayoutDimensions.X).EffectiveMinimum;
            result.Rect.Height = control.Size(LayoutDimensions.Y).EffectiveMinimum;
            if (control.FirstChild.IsInvalid)
                return;

            var t = new ControlTraits(control);
            // HACK: Don't attempt to do clipping in this pass since we have incomplete size information
            // It will happen in the next pass and in the arrange pass
            t.clip = t.clipX = t.clipY = false;

            float padX = control.Padding.X, padY = control.Padding.Y, p = 0;
            var currentRunIndex = -1;
            foreach (var ckey in Children(control.Key)) {
                ref var child = ref this[ckey];
                ref var childResult = ref UnsafeResult(ckey);
                var ct = new ControlTraits(child);

                Pass1_ComputeSizesAndBuildRuns(ref child, ref childResult, depth + 1);
                float w = childResult.Rect.Width + child.Margins.X,
                    h = childResult.Rect.Height + child.Margins.Y;
                ref var run = ref Pass1_UpdateRun(
                    in t, in control, ref result, 
                    in child, ref childResult, 
                    in ct, ref currentRunIndex
                );

                childResult.PositionInRun = p;
                p += t.vertical ? w : h;

                // At a minimum we should be able to hold all our children if they were stacked on each other
                if (!t.noExpandX)
                    result.Rect.Width = Math.Max(result.Rect.Width, w + padX);
                if (!t.noExpandY)
                    result.Rect.Height = Math.Max(result.Rect.Height, h + padY);
                // If we're not in wrapped mode, we will try to expand to hold our largest run
                if (!t.wrap) {
                    if (t.vertical && !t.noExpandY)
                        result.Rect.Height = Math.Max(result.Rect.Height, run.TotalHeight + padY);
                    if (!t.vertical && !t.noExpandX)
                        result.Rect.Width = Math.Max(result.Rect.Width, run.TotalWidth + padX);
                }
            }

            Pass1_IncreaseContentSizeForCompletedRun(in control, ref result, currentRunIndex);

            // We have our minimum size in result.Rect and the size of all our content in result.ContentRect
            // Now we add padding to the contentrect and pick the biggest of the two
            // This gives us proper autosize for non-forced-wrap
            // if (!t.wrap) {
                if (!t.noExpandX)
                    result.Rect.Width = Math.Max(result.Rect.Width, result.ContentRect.Width + padX);
                if (!t.noExpandY)
                    result.Rect.Height = Math.Max(result.Rect.Height, result.ContentRect.Height + padY);
            // }

            control.Width.Constrain(ref result.Rect.Width, true);
            control.Height.Constrain(ref result.Rect.Height, true);
        }

        private ref ControlLayoutRun SelectRunForBuildingPass (ref int currentRunIndex, bool isBreak) {
            if (isBreak)
                return ref InsertRun(out currentRunIndex, currentRunIndex);
            else
                return ref GetOrPushRun(ref currentRunIndex);
        }

        private void Pass1_IncreaseContentSizeForCompletedRun (
            in ControlRecord control, ref ControlLayoutResult result, int runIndex
        ) {
            if (runIndex < 0)
                return;

            // If we are ending the current run we want to expand our control on the secondary axis
            //  to account for the size of the current run
            ref var completedRun = ref Run(runIndex);

            if (control.Flags.IsFlagged(ControlFlags.Container_Column))
                result.ContentRect.Width += completedRun.MaxOuterWidth;
            else
                result.ContentRect.Height += completedRun.MaxOuterHeight;
        }

        private void UpdateRunCommon (
            ref ControlLayoutRun run, in ControlTraits t,
            in ControlRecord control, ref ControlLayoutResult result,
            in ControlRecord child, ref ControlLayoutResult childResult,
            in ControlTraits ct, ref int firstRunIndex, int currentRunIndex
        ) {
            if (firstRunIndex < 0)
                firstRunIndex = currentRunIndex;
            if (run.First.IsInvalid)
                run.First = child.Key;
            run.Last = child.Key;

            if (child.Flags.IsStackedOrFloating())
                return;

            var xAnchor = child.Flags & ControlFlags.Layout_Fill_Row;
            var yAnchor = child.Flags & ControlFlags.Layout_Fill_Column;
            if ((xAnchor != ControlFlags.Layout_Fill_Row) && (xAnchor != default)) {
                if (run.XAnchor == null)
                    run.XAnchor = xAnchor;
                else if (run.XAnchor != xAnchor)
                    run.XAnchor = default;
            }
            if ((yAnchor != ControlFlags.Layout_Fill_Column) && (yAnchor != default)) {
                if (run.YAnchor == null)
                    run.YAnchor = yAnchor;
                else if (run.YAnchor != yAnchor)
                    run.YAnchor = default;
            }

            float childOuterWidth = childResult.Rect.Width + child.Margins.X,
                childOuterHeight = childResult.Rect.Height + child.Margins.Y;

            ApplyClip(t, in control, ref result, in child, ref childResult, in run);

            run.FlowCount++;
            if (ct.fillX)
                run.ExpandCountX++;
            if (ct.fillY)
                run.ExpandCountY++;
            run.MaxOuterWidth = Math.Max(run.MaxOuterWidth, childOuterWidth);
            run.MaxOuterHeight = Math.Max(run.MaxOuterHeight, childOuterHeight);
            run.TotalWidth += childOuterWidth;
            run.TotalHeight += childOuterHeight;
        }

        private void ApplyClip (
            in ControlTraits t,
            in ControlRecord control, ref ControlLayoutResult result,
            in ControlRecord child, ref ControlLayoutResult childResult,
            in ControlLayoutRun run
        ) {
            if (!t.clipX && !t.clipY)
                return;

            // FIXME: Unfortunately this is all completely broken
            return;

            // HACK: While clipping in the arrange pass is good enough to make the result of a layout
            //  look right in most cases, we need to clip earlier on in the layout process to ensure
            //  that the grow pass does not cause boxes to grow beyond their parent's bounds if
            //  the parent has clipping turned on.
            // For example if a box has two columns and one of the columns is huge and overflows at
            //  the bottom, we want to prevent the other column from expanding to overflow. For that
            //  to work right we have to clip it before expanding
            // FIXME: This won't work if we're clipping a run after the first.
            // In that case it will still get clipped correctly during arrange, but its children
            //  may auto-size too big since it didn't get clipped here.
            float xSpace = result.Rect.Width - control.Padding.X,
                ySpace = result.Rect.Height - control.Padding.Y;

            // FIXME: The space eaten by previous runs is not taken into account here
            // We probably need to scan backwards to calculate it, or keep a running tally as we go
            if (t.vertical) {
                ySpace -= run.TotalHeight;
            } else {
                xSpace -= run.TotalWidth;
            }

            if (xSpace > 0)
                childResult.Rect.Width = Math.Min(childResult.Rect.Width, xSpace);
            if (ySpace > 0)
                childResult.Rect.Height = Math.Min(childResult.Rect.Height, ySpace);
        }

        private ref ControlLayoutRun Pass1_UpdateRun (
            in ControlTraits t,
            in ControlRecord control, ref ControlLayoutResult result, 
            in ControlRecord child, ref ControlLayoutResult childResult, 
            in ControlTraits ct, ref int currentRunIndex
        ) {
            bool isBreak = !child.Flags.IsStackedOrFloating() && child.Flags.IsFlagged(ControlFlags.Layout_ForceBreak);
            var previousRunIndex = currentRunIndex;

            // We still generate runs even if a control is stacked/floating
            // This ensures that you can enumerate all of a control's children by enumerating its runs
            // We will then skip stacked/floating controls when enumerating runs (as appropriate)
            ref var run = ref SelectRunForBuildingPass(ref currentRunIndex, isBreak);
            if (currentRunIndex != previousRunIndex)
                Pass1_IncreaseContentSizeForCompletedRun(in control, ref result, previousRunIndex);

            UpdateRunCommon(
                ref run, in t, in control, ref result,
                in child, ref childResult, in ct,
                ref result.FirstRunIndex, currentRunIndex
            );

            return ref run;
        }

        #endregion

        #region Pass 2: wrap and expand
        private Vector2 Pass2a_ForceWrapAndRebuildRuns (
            ref ControlRecord control, ref ControlLayoutResult result
        ) {
            if (control.FirstChild.IsInvalid)
                return default;
            if (!control.Flags.IsFlagged(ControlFlags.Container_Break_Auto))
                return default;

            var t = new ControlTraits(control);
            float contentWidth = result.Rect.Width - control.Padding.X,
                contentHeight = result.Rect.Height - control.Padding.Y,
                capacity = t.vertical ? contentHeight : contentWidth,
                offset = 0, extent = 0;

            // HACK: Unfortunately, we have to build new runs entirely from scratch because modifying the existing ones
            //  in-place would be far too difficult
            // FIXME: Reclaim the existing runs
            int oldFirstRun = result.FirstRunIndex, firstRunIndex = -1, currentRunIndex = -1, numForcedBreaks = 0;
            // HACK
            result.FirstRunIndex = -1;

            foreach (var ckey in Children(control.Key)) {
                ref var child = ref this[ckey];
                ref var childResult = ref Result(ckey);
                var ct = new ControlTraits(child);
                float startMargin = t.vertical ? child.Margins.Top : child.Margins.Left,
                    endMargin = t.vertical ? child.Margins.Bottom : child.Margins.Right,
                    size = t.vertical ? childResult.Rect.Height : childResult.Rect.Width,
                    totalSize = startMargin + size + endMargin;
                var forceBreak = (offset + startMargin + size) > capacity;
                if (forceBreak)
                    numForcedBreaks++;

                var previousRunIndex = currentRunIndex;

                bool isBreak = child.Flags.IsFlagged(ControlFlags.Layout_ForceBreak) || forceBreak;
                // We still generate runs even if a control is stacked/floating
                // This ensures that you can enumerate all of a control's children by enumerating its runs
                // We will then skip stacked/floating controls when enumerating runs (as appropriate)
                ref var run = ref SelectRunForBuildingPass(ref currentRunIndex, isBreak);
                UpdateRunCommon(
                    ref run, in t, in control, ref result,
                    in child, ref childResult, in ct,
                    ref firstRunIndex, currentRunIndex
                );

                if (previousRunIndex != currentRunIndex) {
                    if (previousRunIndex >= 0) {
                        ref var previousRun = ref Run(previousRunIndex);
                        extent += t.vertical ? previousRun.MaxOuterWidth : previousRun.MaxOuterHeight;
                    }
                    offset = totalSize;
                } else {
                    offset += totalSize;
                }
            }

            if (currentRunIndex >= 0) {
                ref var currentRun = ref Run(currentRunIndex);
                extent += t.vertical ? currentRun.MaxOuterWidth : currentRun.MaxOuterHeight;
            }

            if (numForcedBreaks <= 0)
                result.FirstRunIndex = oldFirstRun;
            else
                result.FirstRunIndex = firstRunIndex;

            var oldSize = result.Rect.Size;
            if (t.vertical)
                result.Rect.Width = control.Width.Constrain(Math.Max(result.Rect.Width, extent + control.Padding.X), true);
            else
                result.Rect.Height = control.Height.Constrain(Math.Max(result.Rect.Height, extent + control.Padding.Y), true);
            // Return the amount our size changed so that our caller can update the run we're in
            return result.Rect.Size - oldSize;
        }

        private Vector2 Pass2b_WrapAndAdjustSizes (ref ControlRecord control, ref ControlLayoutResult result) {
            if (control.FirstChild.IsInvalid)
                return default;

            var oldSize = result.Rect.Size;
            var t = new ControlTraits(control);
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
                float runMaxOuterWidth = t.constrain && t.vertical ? Math.Min(w, run.MaxOuterWidth) : run.MaxOuterWidth,
                    runMaxOuterHeight = t.constrain && !t.vertical ? Math.Min(h, run.MaxOuterHeight) : run.MaxOuterHeight,
                    // HACK: For the last run in a box we want to expand the run to fill the entire available space
                    //  otherwise listbox items will have a width of 0 :(
                    effectiveRunMaxWidth = isLastRun && t.vertical ? Math.Max(w, runMaxOuterWidth) : runMaxOuterWidth,
                    effectiveRunMaxHeight = isLastRun && !t.vertical ? Math.Max(h, runMaxOuterHeight) : runMaxOuterHeight,
                    effectiveRunTotalWidth = run.TotalWidth,
                    effectiveRunTotalHeight = run.TotalHeight,
                    // HACK: In the old engine the last run would be expanded to fill on both axes, and we rely on this
                    //  right now for various things. This should be improved on
                    xSpace = t.vertical 
                        ? (isLastRun ? w - effectiveRunMaxWidth : 0)
                        : w - effectiveRunTotalWidth,
                    ySpace = t.vertical 
                        ? h - effectiveRunTotalHeight 
                        : (isLastRun ? h - effectiveRunMaxHeight : 0),
                    newXSpace = xSpace, newYSpace = ySpace,
                    minOuterWidth = t.vertical ? effectiveRunMaxWidth : 0,
                    minOuterHeight = t.vertical ? 0 : effectiveRunMaxHeight;

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
                        var ct = new ControlTraits(child);
                        var margins = child.Margins;
                        bool expandX = ct.fillX && !child.Width.Fixed.HasValue,
                            expandY = ct.fillY && !child.Height.Fixed.HasValue;
                        float amountX = countX > 0 ? xSpace / countX : 0, amountY = countY > 0 ? ySpace / countY : 0;

                        if (child.Flags.IsFlagged(ControlFlags.Layout_Floating)) {
                        } else if (child.Flags.IsFlagged(ControlFlags.Layout_Stacked)) {
                            if (expandX)
                                childResult.Rect.Width = child.Width.Constrain(w - child.Margins.X, true);
                            if (expandY)
                                childResult.Rect.Height = child.Height.Constrain(h - child.Margins.Y, true);
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

                            if (child.Key.ID == 149)
                                ;

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

                // HACK: It would be ideal if we could do this in the previous loop
                foreach (var ckey in Enumerate(run.First.Key, run.Last.Key)) {
                    ref var child = ref this[ckey];
                    ref var childResult = ref Result(ckey);

                    ApplyClip(in t, in control, ref result, in child, ref childResult, in run);

                    // Process wrapping (if necessary) and then if wrapping changed the size of the child,
                    //  update the run that contains it
                    var growth = Pass2a_ForceWrapAndRebuildRuns(ref child, ref childResult) + 
                        Pass2b_WrapAndAdjustSizes(ref child, ref childResult);

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

                if (t.vertical)
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
                    if (t.vertical) {
                        result.Rect.Width += run.MaxOuterWidth;
                        result.Rect.Height = Math.Max(result.Rect.Height, run.TotalHeight);
                    } else {
                        result.Rect.Width = Math.Max(result.Rect.Width, run.TotalWidth);
                        result.Rect.Height += run.MaxOuterHeight;
                    }
                }

                // TODO: Figure out whether these should actually be enabled, they seem right but they also don't seem to fix anything?
                if (false && control.Flags.IsFlagged(ControlFlags.Container_No_Expansion_X))
                    result.Rect.Width = oldSize.X;
                else
                    control.Width.Constrain(ref result.Rect.Width, true);

                if (false && control.Flags.IsFlagged(ControlFlags.Container_No_Expansion_Y))
                    result.Rect.Height = oldSize.Y;
                else
                    control.Height.Constrain(ref result.Rect.Height, true);
            }

            return result.Rect.Size - oldSize;
        }

        #endregion

        #region Pass 3: arrange
        private void Pass3_Arrange (ref ControlRecord control, ref ControlLayoutResult result) {
            result.ContentRect = result.Rect;
            result.ContentRect.Left += control.Padding.Left;
            result.ContentRect.Top += control.Padding.Top;
            result.ContentRect.Width = Math.Max(0, result.ContentRect.Width - control.Padding.X);
            result.ContentRect.Height = Math.Max(0, result.ContentRect.Height - control.Padding.Y);

            ControlKey firstProcessed = ControlKey.Invalid,
                lastProcessed = ControlKey.Invalid;
            var t = new ControlTraits(control);
            float w = result.ContentRect.Width, h = result.ContentRect.Height,
                x = 0, y = 0;

            foreach (var runIndex in Runs(control.Key)) {
                ref var run = ref Run(runIndex);
                bool isLastRun = run.NextRunIndex < 0;
                float rw = t.vertical ? run.MaxOuterWidth : run.TotalWidth,
                    rh = t.vertical ? run.TotalHeight : run.MaxOuterHeight,
                    space = Math.Max(t.vertical ? h - rh : w - rw, 0),
                    baseline = t.vertical 
                        // HACK: The last run needs to have its baseline expanded to our outer edge
                        //  so that anchor bottom/right will hit the edges of our content rect
                        ? (isLastRun ? result.ContentRect.Size.X - x : run.MaxOuterWidth)
                        : (isLastRun ? result.ContentRect.Size.Y - y : run.MaxOuterHeight);

                run.GetAlignmentF(control.Flags, out float xAlign, out float yAlign);

                if (t.vertical)
                    y = space * yAlign;
                else
                    x = space * xAlign;

                foreach (var ckey in Enumerate(run.First.Key, run.Last.Key)) {
                    if (firstProcessed.IsInvalid)
                        firstProcessed = ckey;
                    lastProcessed = ckey;
                    ref var child = ref this[ckey];
                    ref var childResult = ref Result(ckey);
                    var childMargins = child.Margins;
                    var childOuterSize = childResult.Rect.Size + childMargins.Size;

                    child.Flags.GetAlignmentF(out float xChildAlign, out float yChildAlign);

                    if (child.Flags.IsStackedOrFloating()) {
                        if (child.Flags.IsFlagged(ControlFlags.Layout_Floating)) {
                            // TODO: Margins?
                            childResult.Rect.Position = result.ContentRect.Position + child.FloatingPosition;
                        } else {
                            var stackSpace = result.ContentRect.Size - childOuterSize;
                            // If the control is stacked and aligned but did not fill the container (size constraints, etc)
                            //  then try to align it
                            stackSpace.X = Math.Max(stackSpace.X, 0f) * xChildAlign;
                            stackSpace.Y = Math.Max(stackSpace.Y, 0f) * yChildAlign;
                            childResult.Rect.Position = result.ContentRect.Position +
                                new Vector2(stackSpace.X + childMargins.Left, stackSpace.Y + childMargins.Top);
                        }
                    } else {
                        childResult.Rect.Left = result.ContentRect.Left + childMargins.Left + x;
                        childResult.Rect.Top = result.ContentRect.Top + childMargins.Top + y;

                        if (child.Key.ID == 9)
                            ;

                        if (t.vertical) {
                            var alignment = (xChildAlign * Math.Max(0, baseline - childOuterSize.X));
                            if (alignment != 0)
                                childResult.Rect.Left += alignment;
                            y += childOuterSize.Y;
                        } else {
                            var alignment = (yChildAlign * Math.Max(0, baseline - childOuterSize.Y));
                            if (alignment != 0)
                                childResult.Rect.Top += alignment;
                            x += childOuterSize.X;
                        }
                    }

                    // TODO: Clip left/top edges as well?
                    if (t.clipX) {
                        var rightEdge = result.ContentRect.Right - childMargins.Right;
                        childResult.Rect.Width = Math.Max(0, Math.Min(childResult.Rect.Width, rightEdge - childResult.Rect.Left));
                    }
                    if (t.clipY) {
                        var bottomEdge = result.ContentRect.Bottom - childMargins.Bottom;
                        childResult.Rect.Height = Math.Max(0, Math.Min(childResult.Rect.Height, bottomEdge - childResult.Rect.Top));
                    }

                    Pass3_Arrange(ref child, ref childResult);
                }

                if (t.vertical) {
                    x += run.MaxOuterWidth;
                    y = 0;
                } else {
                    x = 0;
                    y += run.MaxOuterHeight;
                }
            }

            Assert(firstProcessed == control.FirstChild);
            Assert(lastProcessed == control.LastChild);
        }
        #endregion

        public void UpdateSubtree (ControlKey control) {
            PerformLayout(ref this[control]);
        }

        internal void PerformLayout (ref ControlRecord control) {
            ref var result = ref UnsafeResult(control.Key);
            Pass1_ComputeSizesAndBuildRuns(ref control, ref result, 0);
            Pass2a_ForceWrapAndRebuildRuns(ref control, ref result);
            Pass2b_WrapAndAdjustSizes(ref control, ref result);
            Pass3_Arrange(ref control, ref result);
            ;
        }
    }
}