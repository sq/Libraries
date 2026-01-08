using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Squared.PRGUI.Layout;

namespace Squared.PRGUI.NewEngine {
    public partial class LayoutEngine {
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void ThrowKeyOutOfRange () {
            throw new ArgumentOutOfRangeException("key");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void ThrowIndexOutOfRange () {
            throw new ArgumentOutOfRangeException("index");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ref LayoutRun Run (int index) {
            return ref RunBuffer[index];
        }

        /// <param name="relativeToIndex">For floating runs: The first non-floating run. For normal runs: The run to insert this one after</param>
        private ref LayoutRun InsertRun (out int index, int relativeToIndex, bool stackedOrFloating) {
            ref var result = ref RunBuffer.New(out index);
            result.IsStackedOrFloating = stackedOrFloating;
            result.Index = index;
            if (relativeToIndex >= 0) {
                if (stackedOrFloating) {
                    result.NextRunIndex = relativeToIndex;
                } else {
                    ref var after = ref Run(relativeToIndex);
                    var beforeIndex = after.NextRunIndex;
                    after.NextRunIndex = index;
                    result.NextRunIndex = beforeIndex;
                }
            } else
                result.NextRunIndex = -1;
            return ref result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ref LayoutRun GetOrPushRun (ref int index, int relativeToIndex, bool stackedOrFloating) {
            if (index < 0)
                return ref InsertRun(out index, relativeToIndex, stackedOrFloating);
            else
                return ref Run(index);
        }

        private void InitializeResult (ref BoxRecord control, ref BoxLayoutResult result, int depth) {
            result.Tag = control.Tag;
            result.Rect = result.ContentRect = default;
            result.AvailableSpace = result.ContentSize = default;
            result.FirstRunIndex = -1;
            result.FloatingRunIndex = -1;
            result.ParentRunIndex = -1;
#if DEBUG
            result.Break = control.OldFlags.IsFlagged(ControlFlags.Layout_ForceBreak) ||
                control.Config.ForceBreak;
            result.Depth = depth;
            var dl = Controls[control.Key.ID]?.DebugLabel;
            control.DebugLabel = dl ?? control.DebugLabel;
#endif
            result.Pass1Ready = result.Pass1Processed = result.Pass2Processed = false;
            result.SizeSetByParent = false;
            result.Version = Version;
        }

    }
}
