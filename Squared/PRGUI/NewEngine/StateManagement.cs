using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Squared.PRGUI.Layout;

namespace Squared.PRGUI.NewEngine {
    public partial class LayoutEngine {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ref LayoutRun Run (int index) {
            if ((index < 0) || (index >= _RunCount))
                throw new ArgumentOutOfRangeException(nameof(index));
            return ref RunBuffer[index];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ref LayoutRun PushRun (out int index) {
            return ref InsertRun(out index, -1);
        }

        private ref LayoutRun InsertRun (out int index, int afterIndex) {
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ref LayoutRun GetOrPushRun (ref int index) {
            if (index < 0)
                return ref PushRun(out index);
            else
                return ref Run(index);
        }

        private void InitializeResult (ref BoxRecord control, ref BoxLayoutResult result, int depth) {
            result.Tag = control.Tag;
            result.Rect = result.ContentRect = default;
            result.FirstRunIndex = -1;
            result.FloatingRunIndex = -1;
#if DEBUG
            result.Break = control.OldFlags.IsFlagged(ControlFlags.Layout_ForceBreak) ||
                control.Config.ForceBreak;
            result.Depth = depth;
#endif
            result.Pass1Ready = result.Pass1Processed = result.Pass2Processed = false;
            result.SizeSetByParent = false;
            result.Version = Version;
            _Count = Math.Max(control.Key.ID + 1, _Count);
        }

    }
}
