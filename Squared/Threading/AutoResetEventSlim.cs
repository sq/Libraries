using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Squared.Threading {
    public class AutoResetEventSlim {
        private ManualResetEventSlim Inner;
        private object SetCountLock = new object();
        private long SetCount;

        public AutoResetEventSlim (bool initialState) {
            Inner = new ManualResetEventSlim(initialState);
            SetCount = 1;
        }

        public void Set () {
            lock (SetCountLock)
                SetCount++;
            Inner.Set();
        }

        public bool Wait (int timeoutMs) {
            long expectedSetCount;
            lock (SetCountLock)
                expectedSetCount = SetCount + 1;
            var result = Inner.Wait(timeoutMs);
            lock (SetCountLock) {
                // We need to ensure that we only reset the internal event if exactly one
                //  set request occurred since we started waiting
                if (SetCount > expectedSetCount)
                    return result;
                Inner.Reset();
            }
            return result;
        }
    }
}
