using System;
using System.Collections.Concurrent;

namespace Squared.Util {
    // Just a compatibility shim
    [Obsolete("Use System.Collections.Concurrent.ConcurrentQueue<T> instead.")]
    public class AtomicQueue<T> : ConcurrentQueue<T> {
        public AtomicQueue ()
            : base () {
        }

        public bool Dequeue (out T result) {
            return base.TryDequeue(out result);
        }
    }
}
