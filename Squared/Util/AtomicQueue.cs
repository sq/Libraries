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

        Node _Head;
        Node _Tail;

        int _ConsumerLock;
        int _ProducerLock;

        public AtomicQueue () {
            _Head = _Tail = new Node(default(T));
            _ConsumerLock = _ProducerLock = 0;
        }

        public void Enqueue (T value) {
            var temp = new Node(value);

            int threadId = Thread.CurrentThread.ManagedThreadId;
            int iterations = 1;
            while (Interlocked.CompareExchange(ref _ProducerLock, threadId, 0) != 0) {
                SpinWait(iterations++);
            }

            _Tail.Next = temp;
            _Tail = temp;

            var x = Interlocked.Exchange(ref _ProducerLock, 0);
            if (x != threadId)
                throw new ThreadStateException();
        }

        public bool Dequeue (out T result) {
            int threadId = Thread.CurrentThread.ManagedThreadId;
            int iterations = 1;
            while (Interlocked.CompareExchange(ref _ConsumerLock, threadId, 0) != 0) {
                SpinWait(iterations++);
            }

            bool success = false;

            var head = _Head;
            var next = head.Next;
            if (next != null) {
                result = next.Value;
                _Head = next;
                success = true;
            } else {
                result = default(T);
            }

            var x = Interlocked.Exchange(ref _ConsumerLock, 0);
            if (x != threadId)
                throw new ThreadStateException();

            return success;
        }

        private void SpinWait (int iterationCount) {
            if ((iterationCount < 5) && (Environment.ProcessorCount > 1)) {
                Thread.SpinWait(10 * iterationCount);
            } else if (iterationCount < 8) {
                Thread.Sleep(0);
            } else {
                Thread.Sleep(1);
            }
        }
    }
}
