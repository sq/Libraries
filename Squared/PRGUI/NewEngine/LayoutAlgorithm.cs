using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using System.Runtime.CompilerServices;
using Squared.PRGUI.Layout;
using Squared.PRGUI.NewEngine.Enums;

namespace Squared.PRGUI.NewEngine {
    public partial class LayoutEngine {
        private void UpdateRunCommon (
            ref LayoutRun run, 
            in BoxRecord control, ref BoxLayoutResult result,
            in BoxRecord child, ref BoxLayoutResult childResult,
            ref int firstRunIndex, int currentRunIndex
        ) {
            if (firstRunIndex < 0)
                firstRunIndex = currentRunIndex;

            if (run.First.IsInvalid) {
                run.First = child.Key;
                run.IsVertical = control.Config.IsVertical;
            }

            run.Last = child.Key;
            childResult.ParentRunIndex = run.Index;

            ref readonly var childConfig = ref child.Config;

            float childOuterWidth = childResult.Rect.Width + child.Margins.X,
                childOuterHeight = childResult.Rect.Height + child.Margins.Y;

            // FIXME
            // ApplyClip(in control, ref result, in child, ref childResult, in run);

            // FIXME: Collapse margins    
            run.FlowCount++;
            if (childConfig.FillRow && !child.Width.HasFixed)
                run.ExpandCountX++;
            if (childConfig.FillColumn && !child.Height.HasFixed)
                run.ExpandCountY++;
            // It is important that floating controls do not update the size of the floating run.
            // Stacked controls should, though.
            if (!childConfig.IsFloating) {
                run.MaxOuterWidth = Math.Max(run.MaxOuterWidth, childOuterWidth);
                run.MaxOuterHeight = Math.Max(run.MaxOuterHeight, childOuterHeight);
            }
            if (!childConfig.IsStackedOrFloating) {
                run.TotalWidth += childOuterWidth;
                run.TotalHeight += childOuterHeight;
            }
        }

        public void UpdateSubtree (ControlKey control) {
            PerformLayout(ref this[control]);
        }

        internal void PerformLayout (ref BoxRecord root) {
            ref var result = ref UnsafeResult(root.Key);
            Pass1_ComputeSizesAndBuildRuns(ref root, ref result, 0);
            Pass2(ref root, ref result, 0);

            // During passes 2a/2b we built a bottom-up list of controls that need their
            //  size and content size recalculated due to wrapping. We start with the control
            //  that had children wrapped, then walk up and recalculate all of its ancestors
            //  since their sizes also changed.
            // FIXME: We probably need to rebuild their runs too, right?
            foreach (var key in RecalcSizeQueue)
                Pass2c_Recalculate(ref UnsafeItem(key), ref UnsafeResult(key));

            RecalcSizeQueue.Clear();

            Pass3_Arrange(ref root, ref result, 0);
        }
    }
}