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
        private void UpdateRunCommon (
            ref LayoutRun run, 
            in BoxRecord control, ref BoxLayoutResult result,
            in BoxRecord child, ref BoxLayoutResult childResult,
            ref int firstRunIndex, int currentRunIndex
        ) {
            if (firstRunIndex < 0)
                firstRunIndex = currentRunIndex;
            if (run.First.IsInvalid)
                run.First = child.Key;
            run.Last = child.Key;

            ref readonly var childConfig = ref child.Config;
            if (childConfig.IsStackedOrFloating)
                return;

            float childOuterWidth = childResult.Rect.Width + child.Margins.X,
                childOuterHeight = childResult.Rect.Height + child.Margins.Y;

            // FIXME
            // ApplyClip(in control, ref result, in child, ref childResult, in run);

            run.FlowCount++;
            if ((childConfig.Anchor & Enums.BoxAnchorMode.FillRow) == Enums.BoxAnchorMode.FillRow)
                run.ExpandCountX++;
            if ((childConfig.Anchor & Enums.BoxAnchorMode.FillColumn) == Enums.BoxAnchorMode.FillColumn)
                run.ExpandCountY++;
            run.MaxOuterWidth = Math.Max(run.MaxOuterWidth, childOuterWidth);
            run.MaxOuterHeight = Math.Max(run.MaxOuterHeight, childOuterHeight);
            run.TotalWidth += childOuterWidth;
            run.TotalHeight += childOuterHeight;
        }

        public void UpdateSubtree (ControlKey control) {
            PerformLayout(ref this[control]);
        }

        internal void PerformLayout (ref BoxRecord control) {
            ref var result = ref UnsafeResult(control.Key);
            Pass1_ComputeSizesAndBuildRuns(ref control, ref result, 0, false);
            Pass2_ExpandAndProcessMesses(ref control, ref result, 0, false);
            Pass3_Arrange(ref control, ref result);
            ;
        }
    }
}