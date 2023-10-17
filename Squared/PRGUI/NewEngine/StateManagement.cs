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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ref LayoutRun PushRun (out int index, bool floating) {
            return ref InsertRun(out index, -1, floating);
        }

        private ref LayoutRun InsertRun (out int index, int afterIndex, bool floating) {
            ref var result = ref RunBuffer.New(out index);
            result.IsFloating = floating;
            result.Index = index;
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
        private ref LayoutRun GetOrPushRun (ref int index, bool floating) {
            if (index < 0)
                return ref PushRun(out index, floating);
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
