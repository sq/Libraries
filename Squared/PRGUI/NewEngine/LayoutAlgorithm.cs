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
        private ref ControlLayoutRun Run (int index) {
            if ((index < 0) || (index >= _RunCount))
                throw new ArgumentOutOfRangeException(nameof(index));
            return ref RunBuffer[index];
        }

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
        }

        private void Pass1_ComputeSizesAndBuildRuns (ref ControlRecord control, ref ControlLayoutResult result, int depth) {
            InitializeResult(ref control, ref result, depth);

            result.Rect.Width = control.Size(LayoutDimensions.X).EffectiveMinimum;
            result.Rect.Height = control.Size(LayoutDimensions.Y).EffectiveMinimum;

            if (control.FirstChild.IsInvalid)
                return;

            bool vertical = control.Flags.IsFlagged(ControlFlags.Container_Column),
                wrap = control.Flags.IsFlagged(ControlFlags.Container_Wrap);
            var currentRunIndex = result.FirstRunIndex;
            foreach (var ckey in Children(control.Key)) {
                ref var child = ref this[ckey];
                ref var childResult = ref UnsafeResult(ckey);

                Pass1_ComputeSizesAndBuildRuns(ref child, ref childResult, depth + 1);
                float w = childResult.Rect.Width + child.Margins.X,
                    h = childResult.Rect.Height + child.Margins.Y;
                ref var run = ref Pass1_UpdateRun(
                    ref control, ref result, ref child, ref childResult, 
                    w, h, ref currentRunIndex
                );

                // At a minimum we should be able to hold all our children if they were stacked on each other
                result.Rect.Width = Math.Max(result.Rect.Width, w);
                result.Rect.Height = Math.Max(result.Rect.Height, h);
                // If we're not in wrapped mode, we will try to expand to hold our largest run
                if (!wrap) {
                    if (vertical)
                        result.Rect.Height = Math.Max(result.Rect.Height, run.TotalHeight);
                    else
                        result.Rect.Width = Math.Max(result.Rect.Width, run.TotalWidth);
                }
            }

            control.Width.Constrain(ref result.Rect.Width, true);
            control.Height.Constrain(ref result.Rect.Height, true);
        }

        private ref ControlLayoutRun Pass1_SelectRun (ref int currentRunIndex, bool isBreak) {
            if (isBreak)
                return ref InsertRun(out currentRunIndex, currentRunIndex);
            else
                return ref GetOrPushRun(ref currentRunIndex);
        }

        private ref ControlLayoutRun Pass1_UpdateRun (
            // TODO: These aren't necessary, remove them?
            ref ControlRecord control, ref ControlLayoutResult result, 
            ref ControlRecord child, ref ControlLayoutResult childResult, 
            float childWidth, float childHeight, ref int currentRunIndex
        ) {
            // We still generate runs even if a control is stacked/floating
            // This ensures that you can enumerate all of a control's children by enumerating its runs
            // We will then skip stacked/floating controls when enumerating runs (as appropriate)
            ref var run = ref Pass1_SelectRun(ref currentRunIndex, child.Flags.IsFlagged(ControlFlags.Layout_ForceBreak));

            if (result.FirstRunIndex < 0)
                result.FirstRunIndex = currentRunIndex;
            if (run.First.IsInvalid)
                run.First = child.Key;
            run.Last = child.Key;

            if (child.Flags.IsStackedOrFloating())
                return ref run;

            run.FlowCount++;
            if (ShouldExpand(ref control, ref child, LayoutDimensions.X))
                run.ExpandCountX++;
            if (ShouldExpand(ref control, ref child, LayoutDimensions.Y))
                run.ExpandCountY++;
            run.MaxWidth = Math.Max(run.MaxWidth, childWidth);
            run.MaxHeight = Math.Max(run.MaxHeight, childHeight);
            run.TotalWidth += childWidth;
            run.TotalHeight += childHeight;

            return ref run;
        }

        #endregion

        #region Pass 2: wrap and expand
        private void Pass2_ComputeForcedWrap (ref ControlRecord control, ref ControlLayoutResult result) {
            if (control.FirstChild.IsInvalid)
                return;

            // FIXME
        }

        private void Pass2_WrapAndExpand (ref ControlRecord control, ref ControlLayoutResult result) {
            if (control.FirstChild.IsInvalid)
                return;

            if (control.Flags.IsFlagged(ControlFlags.Container_Wrap))
                Pass2_ComputeForcedWrap(ref control, ref result);

            var vertical = control.Flags.IsFlagged(ControlFlags.Container_Column);
            float w = result.Rect.Width, h = result.Rect.Height;

            foreach (var runIndex in Runs(control.Key)) {
                ref var run = ref Run(runIndex);

                // We track our own count here so that when expansion hits a constraint, we
                //  can reduce the count to evenly distribute the leftovers to non-constrained controls
                int countX = run.ExpandCountX, countY = run.ExpandCountY,
                    newCountX = countX, newCountY = countY;
                // FIXME: Figure out how to expand secondary axis
                float xSpace = vertical ? 0f : w - run.TotalWidth,
                    ySpace = vertical ? h - run.TotalHeight : 0,
                    newXSpace = xSpace, newYSpace = ySpace;

                int allowedPasses = 3;
                while (allowedPasses-- >= 0) {
                    if (countX < 1)
                        xSpace = 0;
                    if (countY < 1)
                        ySpace = 0;

                    if ((xSpace <= 1) && (ySpace <= 1))
                        break;

                    foreach (var ckey in Enumerate(run.First.Key, run.Last.Key)) {
                        ref var child = ref this[ckey];
                        ref var childResult = ref Result(ckey);
                        bool expandX = child.Flags.IsFlagged(ControlFlags.Layout_Fill_Row),
                            expandY = child.Flags.IsFlagged(ControlFlags.Layout_Fill_Column);
                        float amountX = xSpace / countX, amountY = ySpace / countY;

                        if (child.Flags.IsStackedOrFloating()) {
                            // TODO: Expand stacked controls?
                        } else {
                            float childW = childResult.Rect.Width, 
                                childH = childResult.Rect.Height;

                            if (expandX) {
                                childW += amountX;
                                child.Width.Constrain(ref childW, true);
                                float expanded = childW - childResult.Rect.Width;
                                if (expanded < amountX)
                                    newCountX--;
                                newXSpace -= expanded;
                                childResult.Rect.Width = childW;
                                run.TotalWidth += expanded;
                                run.MaxWidth = Math.Max(run.MaxWidth, childW);
                            }

                            if (expandY) {
                                childH += amountY;
                                child.Height.Constrain(ref childH, true);
                                float expanded = childH - childResult.Rect.Height;
                                if (expanded < amountY)
                                    newCountY--;
                                newXSpace -= expanded;
                                childResult.Rect.Height = childH;
                                run.TotalHeight += expanded;
                                run.MaxHeight = Math.Max(run.MaxHeight, childH);
                            }
                        }
                    }

                    countX = newCountX;
                    countY = newCountY;
                    xSpace = newXSpace;
                    ySpace = newYSpace;
                }

                if (allowedPasses < 0)
                    ;

                // HACK: It would be ideal if we could do this in the previous loop
                foreach (var ckey in Enumerate(run.First.Key, run.Last.Key)) {
                    ref var child = ref this[ckey];
                    ref var childResult = ref Result(ckey);
                    Pass2_WrapAndExpand(ref child, ref childResult);
                }
            }
        }

        private bool ShouldExpand (ref ControlRecord parent, ref ControlRecord child, LayoutDimensions dim) {
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

        #endregion

        #region Pass 3: arrange
        private void Pass3_Arrange (ref ControlRecord control, ref ControlLayoutResult result) {
            // FIXME
            result.ContentRect = result.Rect;
            result.ContentRect.Left += control.Padding.Left;
            result.ContentRect.Top += control.Padding.Top;
            result.ContentRect.Width -= control.Padding.X;
            result.ContentRect.Height -= control.Padding.Y;

            ControlKey firstProcessed = ControlKey.Invalid,
                lastProcessed = ControlKey.Invalid;
            var vertical = control.Flags.IsFlagged(ControlFlags.Container_Column);
            float w = result.ContentRect.Width, h = result.ContentRect.Height,
                x = 0, y = 0;

            foreach (var runIndex in Runs(control.Key)) {
                ref var run = ref Run(runIndex);
                float rw = vertical ? run.MaxWidth : run.TotalWidth,
                    rh = vertical ? run.TotalHeight : run.MaxHeight,
                    space = vertical ? h - rh : w - rw,
                    alignWeight = control.Flags.GetContainerAlignmentF();

                if (vertical)
                    y = space * alignWeight;
                else
                    x = space * alignWeight;

                foreach (var ckey in Enumerate(run.First.Key, run.Last.Key)) {
                    if (firstProcessed.IsInvalid)
                        firstProcessed = ckey;
                    lastProcessed = ckey;
                    ref var child = ref this[ckey];
                    ref var childResult = ref Result(ckey);

                    if (child.Flags.IsStackedOrFloating()) {
                        if (child.Flags.IsFlagged(ControlFlags.Layout_Floating))
                            childResult.Rect.Position = child.FloatingPosition;
                        else
                            // FIXME: shrink to margins?
                            childResult.Rect.Position = result.ContentRect.Position + new Vector2(child.Margins.Left, child.Margins.Top);
                    } else {
                        childResult.Rect.Left = result.ContentRect.Left + child.Margins.Left + x;
                        childResult.Rect.Top = result.ContentRect.Top + child.Margins.Top + y;
                        if (vertical)
                            y += childResult.Rect.Height + child.Margins.Y;
                        else
                            x += childResult.Rect.Width + child.Margins.X;
                    }

                    // FIXME: Constrain rects to parent rect

                    Pass3_Arrange(ref child, ref childResult);
                }

                if (vertical) {
                    x += run.MaxWidth;
                    y = 0;
                } else {
                    x = 0;
                    y += run.MaxHeight;
                }
            }

            Assert(firstProcessed == control.FirstChild);
            Assert(lastProcessed == control.LastChild);
        }
        #endregion

        private void PerformLayout (ref ControlRecord control) {
            ref var result = ref UnsafeResult(control.Key);
            Pass1_ComputeSizesAndBuildRuns(ref control, ref result, 0);
            Pass2_WrapAndExpand(ref control, ref result);
            Pass3_Arrange(ref control, ref result);
            ;
        }
   
    }
}