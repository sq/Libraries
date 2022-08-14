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
        private void Pass1_ComputeSizesAndBuildRuns (
            ref BoxRecord control, ref BoxLayoutResult result, int depth
        ) {
            ref readonly var config = ref control.Config;
            InitializeResult(ref control, ref result, depth);

            if (result.Pass1Processed)
                throw new Exception("Visited twice");
            result.Pass1Processed = true;

            bool expandX = (config.ChildFlags & ContainerFlags.ExpandForContent_X) != default,
                expandY = (config.ChildFlags & ContainerFlags.ExpandForContent_Y) != default;

            // During this pass, result.Rect contains our minimum size:
            // Size constraints, largest of all our children, etc
            // result.ContentRect will contain the total size of *all* of our children, ignoring forced wrapping
            result.Rect.Width = control.Size(LayoutDimensions.X).EffectiveMinimum;
            result.Rect.Height = control.Size(LayoutDimensions.Y).EffectiveMinimum;

            float padX = control.Padding.X, padY = control.Padding.Y, p = 0;
            var currentRunIndex = -1;
            foreach (var ckey in Children(control.Key)) {
                ref var child = ref this[ckey];
                ref var childResult = ref UnsafeResult(ckey);

                Pass1_ComputeSizesAndBuildRuns(ref child, ref childResult, depth + 1);

                if (!childResult.Pass1Ready)
                    throw new Exception("Not ready to process control");

                float outerW = childResult.Rect.Width + child.Margins.X,
                    outerH = childResult.Rect.Height + child.Margins.Y;
                ref var run = ref Pass1_UpdateRun(
                    in control, ref result, 
                    in child, ref childResult, 
                    ref currentRunIndex
                );

                childResult.PositionInRun = p;
                p += config.IsVertical ? outerW : outerH;

                // At a minimum we should be able to hold all our children if they were stacked on each other
                if (!expandX)
                    result.Rect.Width = Math.Max(
                        result.Rect.Width, 
                        (child.Config.Flags & BoxFlags.CollapseMargins) != default 
                            ? Math.Max(outerW, childResult.Rect.Width + padX)
                            : outerW + padX
                    );

                if (!expandY)
                    result.Rect.Height = Math.Max(
                        result.Rect.Height, 
                        (child.Config.Flags & BoxFlags.CollapseMargins) != default 
                            ? Math.Max(outerH, childResult.Rect.Height + padY)
                            : outerH + padY
                    );

                // If we're not in wrapped mode, we will try to expand to hold our largest run
                // FIXME: Collapse margins
                if (!config.IsWrap) {
                    if (config.IsVertical) {
                        if (expandY)
                            result.Rect.Height = Math.Max(result.Rect.Height, run.TotalHeight + padY);
                    } else {
                        if (expandX)
                            result.Rect.Width = Math.Max(result.Rect.Width, run.TotalWidth + padX);
                    }
                }
            }

            IncreaseContentSizeForCompletedRun(in control, ref result, currentRunIndex);

            // We have our minimum size in result.Rect and the size of all our content in result.ContentRect
            // Now we add padding to the contentrect and pick the biggest of the two
            // This gives us proper autosize for non-forced-wrap
            // FIXME: Collapse margins
            if (expandX != default)
                result.Rect.Width = Math.Max(result.Rect.Width, result.ContentRect.Width + padX);
            if (expandY != default)
                result.Rect.Height = Math.Max(result.Rect.Height, result.ContentRect.Height + padY);

            if (control.FirstChild.IsInvalid) {
                // HACK
                result.ContentRect = result.Rect;
            }

            control.Width.Constrain(ref result.Rect.Width, true);
            control.Height.Constrain(ref result.Rect.Height, true);
            result.Pass1Ready = true;
        }

        private ref LayoutRun SelectRunForBuildingPass (ref int floatingRun, ref int currentRunIndex, bool isStackedOrFloating, bool isBreak) {
            if (isStackedOrFloating)
                return ref GetOrPushRun(ref floatingRun);
            else if (isBreak)
                return ref InsertRun(out currentRunIndex, currentRunIndex);
            else
                return ref GetOrPushRun(ref currentRunIndex);
        }

        private void IncreaseContentSizeForCompletedRun (
            in BoxRecord control, ref BoxLayoutResult result, int runIndex
        ) {
            if (runIndex < 0)
                return;

            // If we are ending the current run we want to expand our control on the secondary axis
            //  to account for the size of the current run
            ref var completedRun = ref Run(runIndex);

            if (control.Config.IsVertical)
                result.ContentRect.Width += completedRun.MaxOuterWidth;
            else
                result.ContentRect.Height += completedRun.MaxOuterHeight;
        }

        private ref LayoutRun Pass1_UpdateRun (
            in BoxRecord control, ref BoxLayoutResult result, 
            in BoxRecord child, ref BoxLayoutResult childResult, 
            ref int currentRunIndex
        ) {
            var previousRunIndex = currentRunIndex;

            // We still generate runs even if a control is stacked/floating
            // This ensures that you can enumerate all of a control's children by enumerating its runs
            // We will then skip stacked/floating controls when enumerating runs (as appropriate)
            // TODO: Generate a single special run for all stacked/floating controls instead?
            ref var run = ref SelectRunForBuildingPass(
                ref result.FloatingRunIndex, ref currentRunIndex, 
                child.Config.IsStackedOrFloating, child.Config.ForceBreak
            );
            if (currentRunIndex != previousRunIndex)
                IncreaseContentSizeForCompletedRun(in control, ref result, previousRunIndex);

            UpdateRunCommon(
                ref run, in control, ref result,
                in child, ref childResult,
                ref result.FirstRunIndex, currentRunIndex
            );

            return ref run;
        }
    }
}
