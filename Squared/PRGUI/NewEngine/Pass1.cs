using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Squared.PRGUI.Layout;
using Squared.PRGUI.NewEngine.Enums;

namespace Squared.PRGUI.NewEngine {
    public partial class LayoutEngine {
        private unsafe void Pass1_ComputeSizesAndBuildRuns (
            ref BoxRecord control, ref BoxLayoutResult result, int depth
        ) {
            ref readonly var config = ref control.Config;
            var isVertical = config.IsVertical;
            InitializeResult(ref control, ref result, depth);

            if (result.Pass1Processed)
                throw new Exception("Visited twice");
            result.Pass1Processed = true;

            bool expandX = (config.ChildFlags & ContainerFlags.ExpandForContent_X) != default,
                expandY = (config.ChildFlags & ContainerFlags.ExpandForContent_Y) != default,
                grid = config.GridColumnCount > 0;

            int* columns = stackalloc int[config.GridColumnCount];
            for (int i = 0; i < config.GridColumnCount; i++) {
                ref var run = ref InsertRun(out columns[i], -1, false);
                // FIXME: It shouldn't be necessary to do all this
                run.NextRunIndex = -1;
                if (i == 0)
                    result.FirstRunIndex = columns[i];
                else
                    Run(columns[i - 1]).NextRunIndex = columns[i];
            }

            // During this pass, result.Rect contains our minimum size:
            // Size constraints, largest of all our children, etc
            // result.ContentRect will contain the total size of *all* of our children, ignoring forced wrapping
            result.Rect.Width = control.Size(LayoutDimensions.X).EffectiveMinimum;
            result.Rect.Height = control.Size(LayoutDimensions.Y).EffectiveMinimum;

            float padX = control.Padding.X, padY = control.Padding.Y, p = 0;
            int currentRunIndex = -1, currentColumnIndex = 0;

            ref var child = ref FirstChild(ref control);
            while (!child.IsInvalid) {
                ref var childResult = ref UnsafeResult(child.Key);

                Pass1_ComputeSizesAndBuildRuns(ref child, ref childResult, depth + 1);

                if (!childResult.Pass1Ready)
                    throw new Exception("Not ready to process control");

                float outerW = childResult.Rect.Width + child.Margins.X,
                    outerH = childResult.Rect.Height + child.Margins.Y;

                if (grid) {
                    var column = columns[currentColumnIndex];
                    ref var run = ref Pass1_UpdateRun(
                        ref control, ref result,
                        ref child, ref childResult,
                        ref column
                    );
                    // HACK: Clear margins of grid children because we don't fully implement them.
                    child.Margins = default;
                    currentColumnIndex = (currentColumnIndex + 1) % config.GridColumnCount;

                    result.Rect.Height = Math.Max(result.Rect.Height, run.TotalHeight + padY);
                    // result.Rect.Width = Math.Max(result.Rect.Width, (run.TotalWidth + padX));
                } else {
                    ref var run = ref Pass1_UpdateRun(
                        ref control, ref result, 
                        ref child, ref childResult, 
                        ref currentRunIndex
                    );

                    if (!child.Config.IsFloating) {
                        p += isVertical ? outerW : outerH;

                        // At a minimum we should be able to hold all our children if they were stacked on each other
                        if (expandX)
                            result.Rect.Width = Math.Max(
                                result.Rect.Width, 
                                (child.Config.Flags & BoxFlags.CollapseMargins) != default 
                                    ? Math.Max(outerW, childResult.Rect.Width + padX)
                                    : outerW + padX
                            );

                        if (expandY)
                            result.Rect.Height = Math.Max(
                                result.Rect.Height, 
                                (child.Config.Flags & BoxFlags.CollapseMargins) != default 
                                    ? Math.Max(outerH, childResult.Rect.Height + padY)
                                    : outerH + padY
                            );

                        // If we're not in wrapped mode, we will try to expand to hold our largest run
                        // FIXME: Collapse margins
                        if (!config.IsWrap || child.Config.IsStacked) {
                            if (child.Config.IsStacked || (isVertical && expandY))
                                result.Rect.Height = Math.Max(result.Rect.Height, run.TotalHeight + padY);

                            if (child.Config.IsStacked || (!isVertical && expandX))
                                result.Rect.Width = Math.Max(result.Rect.Width, run.TotalWidth + padX);
                        }
                    }
                }

                child = ref NextSibling(ref child);
            }

            IncreaseContentSizeForCompletedRun(in control, ref result, currentRunIndex);

            // We have our minimum size in result.Rect and the size of all our content in result.ContentRect
            // Now we add padding to the contentrect and pick the biggest of the two
            // This gives us proper autosize for non-forced-wrap
            // FIXME: Collapse margins
            if (expandX != default)
                result.Rect.Width = Math.Max(result.Rect.Width, result.ContentSize.X + padX);
            if (expandY != default)
                result.Rect.Height = Math.Max(result.Rect.Height, result.ContentSize.Y + padY);

            control.Width.Constrain(ref result.Rect.Width, true);
            control.Height.Constrain(ref result.Rect.Height, true);
            result.Pass1Ready = true;
        }

        private ref LayoutRun SelectRunForBuildingPass (
            ref int floatingRun, ref int firstRun,
            ref int currentRunIndex, bool isStackedOrFloating, bool isBreak
        ) {
            if (isStackedOrFloating)
                return ref GetOrPushRun(ref floatingRun, firstRun, true);
            else if (isBreak)
                return ref InsertRun(out currentRunIndex, currentRunIndex, false);
            else
                return ref GetOrPushRun(ref currentRunIndex, -1, false);
        }

        private void IncreaseContentSizeForCompletedRun (
            in BoxRecord control, ref BoxLayoutResult result, int runIndex
        ) {
            if (runIndex < 0)
                return;

            // If we are ending the current run we want to expand our control on the secondary axis
            //  to account for the size of the current run
            ref var completedRun = ref Run(runIndex);

            if (control.Config.IsVertical) {
                result.ContentSize.X += completedRun.MaxOuterWidth;
                result.ContentSize.Y = Math.Max(result.ContentSize.Y, completedRun.MaxOuterHeight);
            } else {
                result.ContentSize.X = Math.Max(result.ContentSize.X, completedRun.MaxOuterWidth);
                result.ContentSize.Y += completedRun.MaxOuterHeight;
            }
        }

        private ref LayoutRun Pass1_UpdateRun (
            ref BoxRecord control, ref BoxLayoutResult result, 
            ref BoxRecord child, ref BoxLayoutResult childResult, 
            ref int currentRunIndex
        ) {
            var previousRunIndex = currentRunIndex;

            // We still generate runs even if a control is stacked/floating
            // This ensures that you can enumerate all of a control's children by enumerating its runs
            // We will then skip stacked/floating controls when enumerating runs (as appropriate)
            // TODO: Generate a single special run for all stacked/floating controls instead?
            ref var run = ref SelectRunForBuildingPass(
                ref result.FloatingRunIndex, ref result.FirstRunIndex, 
                ref currentRunIndex, child.Config.IsStackedOrFloating, child.Config.ForceBreak
            );
            if (currentRunIndex != previousRunIndex)
                IncreaseContentSizeForCompletedRun(in control, ref result, previousRunIndex);

            if (UpdateRunCommon(
                ref run, ref control, ref result,
                ref child, ref childResult,
                ref result.FirstRunIndex, currentRunIndex
            )) {
                if (result.FloatingRunIndex >= 0)
                    Run(result.FloatingRunIndex).NextRunIndex = currentRunIndex;
            }

            return ref run;
        }
    }
}
