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

        #region Layout first pass
        private void InitializeResult (ref ControlRecord control, ref ControlLayoutResult result, int depth) {
            result.Tag = control.Tag;
            result.Rect = result.ContentRect = default;
            result.FirstRunIndex = -1;
            result.Break = control.Flags.IsFlagged(ControlFlags.Layout_ForceBreak);
            result.Depth = depth;
            result.Version = Version;
            _Count = Math.Max(control.Key.ID + 1, _Count);
        }

        private void Pass1_ComputeSizesAndBuildRuns (ref ControlRecord control, ref ControlLayoutResult result, int depth) {
            InitializeResult(ref control, ref result, depth);

            // During this pass, result.Rect contains our minimum size:
            // Size constraints, largest of all our children, etc
            // result.ContentRect will contain the total size of *all* of our children, ignoring forced wrapping
            result.Rect.Width = control.Size(LayoutDimensions.X).EffectiveMinimum;
            result.Rect.Height = control.Size(LayoutDimensions.Y).EffectiveMinimum;
            if (control.FirstChild.IsInvalid)
                return;

            bool vertical = control.Flags.IsFlagged(ControlFlags.Container_Column),
                wrap = control.Flags.IsFlagged(ControlFlags.Container_Break_Auto),
                noExpandX = control.Flags.IsFlagged(ControlFlags.Container_No_Expansion_X),
                noExpandY = control.Flags.IsFlagged(ControlFlags.Container_No_Expansion_Y);
            float padX = control.Padding.X, padY = control.Padding.Y;
            var currentRunIndex = -1;
            foreach (var ckey in Children(control.Key)) {
                ref var child = ref this[ckey];
                ref var childResult = ref UnsafeResult(ckey);

                Pass1_ComputeSizesAndBuildRuns(ref child, ref childResult, depth + 1);
                float w = childResult.Rect.Width + child.Margins.X,
                    h = childResult.Rect.Height + child.Margins.Y;
                ref var run = ref Pass1_UpdateRun(
                    in control, ref result, in child, in childResult, 
                    w, h, ref currentRunIndex
                );

                // At a minimum we should be able to hold all our children if they were stacked on each other
                if (!noExpandX)
                    result.Rect.Width = Math.Max(result.Rect.Width, w + padX);
                if (!noExpandY)
                    result.Rect.Height = Math.Max(result.Rect.Height, h + padY);
                // If we're not in wrapped mode, we will try to expand to hold our largest run
                if (!wrap) {
                    if (vertical && !noExpandY)
                        result.Rect.Height = Math.Max(result.Rect.Height, run.TotalHeight + padY);
                    else if (!noExpandX)
                        result.Rect.Width = Math.Max(result.Rect.Width, run.TotalWidth + padX);
                }
            }

            Pass1_IncreaseContentSizeForCompletedRun(in control, ref result, currentRunIndex);

            // We have our minimum size in result.Rect and the size of all our content in result.ContentRect
            // Now we add padding to the contentrect and pick the biggest of the two
            // This gives us proper autosize for non-forced-wrap
            if (!noExpandX)
                result.Rect.Width = Math.Max(result.Rect.Width, result.ContentRect.Width + padX);
            if (!noExpandY)
                result.Rect.Height = Math.Max(result.Rect.Height, result.ContentRect.Height + padY);

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
            ref ControlLayoutRun run, 
            in ControlRecord control, in ControlLayoutResult result,
            in ControlRecord child, in ControlLayoutResult childResult,
            ref int firstRunIndex, int currentRunIndex,
            float childWidth, float childHeight
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

            run.FlowCount++;
            if (ShouldExpand(in control, in child, LayoutDimensions.X))
                run.ExpandCountX++;
            if (ShouldExpand(in control, in child, LayoutDimensions.Y))
                run.ExpandCountY++;
            run.MaxOuterWidth = Math.Max(run.MaxOuterWidth, childWidth);
            run.MaxOuterHeight = Math.Max(run.MaxOuterHeight, childHeight);
            run.TotalWidth += childWidth;
            run.TotalHeight += childHeight;
        }

        private ref ControlLayoutRun Pass1_UpdateRun (
            // TODO: These aren't necessary, remove them?
            in ControlRecord control, ref ControlLayoutResult result, 
            in ControlRecord child, in ControlLayoutResult childResult, 
            float childWidth, float childHeight, ref int currentRunIndex
        ) {
            bool isBreak = child.Flags.IsFlagged(ControlFlags.Layout_ForceBreak);
            var previousRunIndex = currentRunIndex;

            // We still generate runs even if a control is stacked/floating
            // This ensures that you can enumerate all of a control's children by enumerating its runs
            // We will then skip stacked/floating controls when enumerating runs (as appropriate)
            ref var run = ref SelectRunForBuildingPass(ref currentRunIndex, isBreak);
            if (currentRunIndex != previousRunIndex)
                Pass1_IncreaseContentSizeForCompletedRun(in control, ref result, previousRunIndex);

            UpdateRunCommon(
                ref run, in control, in result,
                in child, in childResult, 
                ref result.FirstRunIndex, currentRunIndex,
                childWidth, childHeight
            );

            return ref run;
        }

        #endregion

        #region Pass 2: wrap and expand
        private void Pass2_ForceWrapAndRebuildRuns (
            ref ControlRecord control, ref ControlLayoutResult result,
            float contentWidth, float contentHeight
        ) {
            if (control.FirstChild.IsInvalid)
                return;

            bool vertical = control.Flags.IsFlagged(ControlFlags.Container_Column);
            float capacity = vertical ? contentHeight : contentWidth, offset = 0, extent = 0;

            // HACK: Unfortunately, we have to build new runs entirely from scratch because modifying the existing ones
            //  in-place would be far too difficult
            // FIXME: Reclaim the existing runs
            int oldFirstRun = result.FirstRunIndex, firstRunIndex = -1, currentRunIndex = -1, numForcedBreaks = 0;
            // HACK
            result.FirstRunIndex = -1;

            foreach (var ckey in Children(control.Key)) {
                ref var child = ref this[ckey];
                ref var childResult = ref Result(ckey);
                float w = childResult.Rect.Width + child.Margins.X,
                    h = childResult.Rect.Height + child.Margins.Y,
                    startMargin = vertical ? child.Margins.Top : child.Margins.Left,
                    endMargin = vertical ? child.Margins.Bottom : child.Margins.Right,
                    size = vertical ? childResult.Rect.Height : childResult.Rect.Width,
                    totalSize = startMargin + size + endMargin;
                var forceBreak = (offset + size + startMargin) > capacity;
                if (forceBreak)
                    numForcedBreaks++;

                var previousRunIndex = currentRunIndex;

                bool isBreak = child.Flags.IsFlagged(ControlFlags.Layout_ForceBreak) || forceBreak;
                // We still generate runs even if a control is stacked/floating
                // This ensures that you can enumerate all of a control's children by enumerating its runs
                // We will then skip stacked/floating controls when enumerating runs (as appropriate)
                ref var run = ref SelectRunForBuildingPass(ref currentRunIndex, isBreak);
                UpdateRunCommon(
                    ref run, in control, in result,
                    in child, in childResult, 
                    ref firstRunIndex, currentRunIndex,
                    w, h
                );

                if (previousRunIndex != currentRunIndex) {
                    if (previousRunIndex >= 0) {
                        ref var previousRun = ref Run(previousRunIndex);
                        extent += vertical ? previousRun.MaxOuterWidth : previousRun.MaxOuterHeight;
                    }
                    offset = totalSize;
                } else {
                    offset += totalSize;
                }
            }

            if (currentRunIndex >= 0) {
                ref var currentRun = ref Run(currentRunIndex);
                extent += vertical ? currentRun.MaxOuterWidth : currentRun.MaxOuterHeight;
            }

            if (numForcedBreaks <= 0) {
                result.FirstRunIndex = oldFirstRun;
                return;
            }

            result.FirstRunIndex = firstRunIndex;
            if (vertical)
                result.Rect.Width = control.Width.Constrain(Math.Max(result.Rect.Width, extent + control.Padding.X), true);
            else
                result.Rect.Height = control.Height.Constrain(Math.Max(result.Rect.Height, extent + control.Padding.Y), true);
            // FIXME: We need to recompute the required size of our control now, because the wrapping may have caused us to become
            //  narrower and taller
        }

        private void Pass2_WrapAndExpand (ref ControlRecord control, ref ControlLayoutResult result) {
            if (control.FirstChild.IsInvalid)
                return;

            bool wrap = control.Flags.IsFlagged(ControlFlags.Container_Break_Auto),
                vertical = control.Flags.IsFlagged(ControlFlags.Container_Column),
                constrain = control.Flags.IsFlagged(ControlFlags.Container_Constrain_Growth);
            float w = result.Rect.Width - control.Padding.X, 
                h = result.Rect.Height - control.Padding.Y;

            if (wrap)
                Pass2_ForceWrapAndRebuildRuns(ref control, ref result, w, h);

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
                float runMaxOuterWidth = constrain && vertical ? Math.Min(w, run.MaxOuterWidth) : run.MaxOuterWidth,
                    runMaxOuterHeight = constrain && !vertical ? Math.Min(h, run.MaxOuterHeight) : run.MaxOuterHeight,
                    // HACK: For the last run in a box we want to expand the run to fill the entire available space
                    //  otherwise listbox items will have a width of 0 :(
                    effectiveRunMaxWidth = isLastRun && vertical ? Math.Max(w, runMaxOuterWidth) : runMaxOuterWidth,
                    effectiveRunMaxHeight = isLastRun && !vertical ? Math.Max(h, runMaxOuterHeight) : runMaxOuterHeight,
                    effectiveRunTotalWidth = run.TotalWidth,
                    effectiveRunTotalHeight = run.TotalHeight,
                    xSpace = vertical ? 0f : w - effectiveRunTotalWidth,
                    ySpace = vertical ? h - effectiveRunTotalHeight : 0,
                    newXSpace = xSpace, newYSpace = ySpace,
                    minOuterWidth = vertical ? effectiveRunMaxWidth : 0,
                    minOuterHeight = vertical ? 0 : effectiveRunMaxHeight;

                /*
                if (vertical)
                    w -= effectiveRunWidth;
                else
                    h -= effectiveRunHeight;
                */

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
                        bool expandX = child.Flags.IsFlagged(ControlFlags.Layout_Fill_Row) && !child.Width.Fixed.HasValue,
                            expandY = child.Flags.IsFlagged(ControlFlags.Layout_Fill_Column) && !child.Height.Fixed.HasValue;
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
                    Pass2_WrapAndExpand(ref child, ref childResult);
                }

                if (vertical)
                    w -= run.MaxOuterWidth;
                else
                    h -= run.MaxOuterHeight;
            }
        }

        private bool ShouldExpand (in ControlRecord parent, in ControlRecord child, LayoutDimensions dim) {
            return (dim == LayoutDimensions.X)
                ? !child.Width.Fixed.HasValue && child.Flags.IsFlagged(ControlFlags.Layout_Fill_Row)
                : !child.Height.Fixed.HasValue && child.Flags.IsFlagged(ControlFlags.Layout_Fill_Column);
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
            bool vertical = control.Flags.IsFlagged(ControlFlags.Container_Column),
                clipAny = control.Flags.IsFlagged(ControlFlags.Container_Clip_Children),
                clipX = clipAny && !control.Flags.IsFlagged(ControlFlags.Container_Prevent_Crush_X),
                clipY = clipAny && !control.Flags.IsFlagged(ControlFlags.Container_Prevent_Crush_Y);
            float w = result.ContentRect.Width, h = result.ContentRect.Height,
                x = 0, y = 0;

            foreach (var runIndex in Runs(control.Key)) {
                ref var run = ref Run(runIndex);
                float rw = vertical ? run.MaxOuterWidth : run.TotalWidth,
                    rh = vertical ? run.TotalHeight : run.MaxOuterHeight,
                    space = Math.Max(vertical ? h - rh : w - rw, 0),
                    baseline = vertical ? run.MaxOuterWidth : run.MaxOuterHeight;

                run.GetAlignmentF(control.Flags, out float xAlign, out float yAlign);

                if (vertical)
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

                        if (vertical) {
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
                    if (clipX) {
                        var rightEdge = result.ContentRect.Right - childMargins.Right;
                        childResult.Rect.Width = Math.Max(0, Math.Min(childResult.Rect.Width, rightEdge - childResult.Rect.Left));
                    }
                    if (clipY) {
                        var bottomEdge = result.ContentRect.Bottom - childMargins.Bottom;
                        childResult.Rect.Height = Math.Max(0, Math.Min(childResult.Rect.Height, bottomEdge - childResult.Rect.Top));
                    }

                    Pass3_Arrange(ref child, ref childResult);
                }

                if (vertical) {
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

        private void PerformLayout (ref ControlRecord control) {
            ref var result = ref UnsafeResult(control.Key);
            _RunCount = 0;
            Pass1_ComputeSizesAndBuildRuns(ref control, ref result, 0);
            Pass2_WrapAndExpand(ref control, ref result);
            Pass3_Arrange(ref control, ref result);
            ;
        }
    }
}