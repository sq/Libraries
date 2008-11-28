#pragma warning disable 0420 // a reference to a volatile field will not be treated as volatile

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace Squared.Util {
    // Thanks to Herb Sutter
    // http://www.ddj.com/cpp/211601363

    public class AtomicQueue<T> {
        internal class Node {
            public Node (T value) {
                Value = value;
                Next = null;
            }

            public T Value;
            public volatile Node Next;
        }

        volatile Node _Head;
        volatile Node _Tail;

        volatile int _ConsumerLock;
        volatile int _ProducerLock;

        int _Count;

        public AtomicQueue () {
            _Head = _Tail = new Node(default(T));
            _ConsumerLock = _ProducerLock = 0;
            _Count = 0;
        }

        public int GetCount () {
            return _Count;
        }

        public void Enqueue (T value) {
            var temp = new Node(value);

            int iterations = 1;
            while (Interlocked.CompareExchange(ref _ProducerLock, 1, 0) != 0) {
                SpinWait(iterations++);
            }

            _Tail.Next = temp;
            _Tail = temp;

            Interlocked.Increment(ref _Count);

            _ProducerLock = 0;
        }

        public bool Dequeue (out T result) {
            int iterations = 1;
            while (Interlocked.CompareExchange(ref _ConsumerLock, 1, 0) != 0) {
                SpinWait(iterations++);
            }

            bool success = false;

            var head = _Head;
            var next = head.Next;
            if (next != null) {
                result = next.Value;
                _Head = next;
                success = true;
                Interlocked.Decrement(ref _Count);
            } else {
                result = default(T);
            }

            _ConsumerLock = 0;

            return success;
        }

        private static void SpinWait (int iterationCount) {
#if !XBOX
            if ((iterationCount < 4) && (Environment.ProcessorCount > 1)) {
                Thread.SpinWait(5 * iterationCount);
            } else if (iterationCount < 7) {
#else
            if (iterationCount < 3) {
#endif
                Thread.Sleep(0);
            } else {
                Thread.Sleep(1);
            }
        }
    }
}
