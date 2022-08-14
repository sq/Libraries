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
            WorkQueue.Clear();
            Pass0_BuildQueue(ref root, ref UnsafeResult(root.Key), 0);

            while (WorkQueue.Count > 0) {
                WorkQueue.Sort(WorkQueueSorter);
                int count = WorkQueue.Count;
                for (int i = 0; i < count; i++) {
                    var tup = WorkQueue[i];
                    ref var item = ref UnsafeItem(tup.Item1);
                    ref var result = ref UnsafeResult(tup.Item1);
                    switch (tup.Item3) {
                        case LayoutPhase.Pass1:
                            Pass1_ComputeSizesAndBuildRuns(ref item, ref result, tup.Item2);
                            break;
                        case LayoutPhase.Pass2:
                            Pass2_ExpandAndProcessMesses(ref item, ref result, tup.Item2);
                            break;
                        case LayoutPhase.Pass3:
                            Pass3_Arrange(ref item, ref result);
                            break;
                    }
                }
                WorkQueue.RemoveRange(0, count);
            }
        }

        int WorkQueueSorter ((ControlKey, int, LayoutPhase) lhs, (ControlKey, int, LayoutPhase) rhs) {
            if (lhs.Item3 != rhs.Item3)
                return lhs.Item3.CompareTo(rhs.Item3);

            return lhs.Item3 == LayoutPhase.Pass1 ? rhs.Item2.CompareTo(lhs.Item2) : lhs.Item2.CompareTo(rhs.Item2);
        }

        private void Pass0_BuildQueue (ref BoxRecord item, ref BoxLayoutResult result, int depth) {
            InitializeResult(ref item, ref result, depth);

            // FIXME: Do the wrap/unwrapped split here too?
            foreach (var ckey in Children(item.Key))
                Pass0_BuildQueue(ref UnsafeItem(ckey), ref UnsafeResult(ckey), depth + 1);

            WorkQueue.Add((item.Key, depth, LayoutPhase.Pass1));
        }
    }
}