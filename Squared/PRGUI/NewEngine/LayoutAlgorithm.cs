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
            if (childConfig.FillRow && !child.Width.Fixed.HasValue)
                run.ExpandCountX++;
            if (childConfig.FillColumn && !child.Height.Fixed.HasValue)
                run.ExpandCountY++;
            run.MaxOuterWidth = Math.Max(run.MaxOuterWidth, childOuterWidth);
            run.MaxOuterHeight = Math.Max(run.MaxOuterHeight, childOuterHeight);
            run.TotalWidth += childOuterWidth;
            run.TotalHeight += childOuterHeight;
        }

        public void UpdateSubtree (ControlKey control) {
            PerformLayout(ref this[control]);
        }

        internal void PerformLayout (ref BoxRecord root) {
            ref var result = ref UnsafeResult(root.Key);
            Pass1_ComputeSizesAndBuildRuns(ref root, ref result, 0, false);
            while (WrapQueue.Count > 0) {
                var wrapKey = WrapQueue.Dequeue();
                // FIXME: Depth
                Pass1_ComputeSizesAndBuildRuns(ref UnsafeItem(wrapKey), ref UnsafeResult(wrapKey), 1, true);
            }
            while (Pass2Queue.Count > 0) {
                var key2 = Pass2Queue.Dequeue();
                // FIXME: Depth
                Pass2_ExpandAndProcessMesses(ref UnsafeItem(key2), ref UnsafeResult(key2), 1);
            }
            Pass3_Arrange(ref root, ref result);
            ;
        }
    }
}