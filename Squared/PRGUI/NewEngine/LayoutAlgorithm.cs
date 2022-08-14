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
            TopDown.Clear();
            DeepestFirst.Clear();
            Pass0_BuildTables(ref root, ref UnsafeResult(root.Key), 0);
            DeepestFirst.Sort(DeepestFirstSorter);

            foreach (var tup in DeepestFirst)
                Pass1_ComputeSizesAndBuildRuns(ref UnsafeItem(tup.Item1), ref UnsafeResult(tup.Item1), tup.Item2);
            foreach (var tup in TopDown)
                Pass2_ExpandAndProcessMesses(ref UnsafeItem(tup.Item1), ref UnsafeResult(tup.Item1), tup.Item2);
            foreach (var tup in TopDown)
                Pass3_Arrange(ref UnsafeItem(tup.Item1), ref UnsafeResult(tup.Item1));

            ;
        }

        int DeepestFirstSorter ((ControlKey, int) lhs, (ControlKey, int) rhs) {
            return rhs.Item2.CompareTo(lhs.Item2);
        }

        private void Pass0_BuildTables (ref BoxRecord item, ref BoxLayoutResult result, int depth) {
            InitializeResult(ref item, ref result, depth);

            TopDown.Add((item.Key, depth));

            foreach (var ckey in Children(item.Key))
                Pass0_BuildTables(ref UnsafeItem(ckey), ref UnsafeResult(ckey), depth + 1);

            DeepestFirst.Add((item.Key, depth));
        }
    }
}