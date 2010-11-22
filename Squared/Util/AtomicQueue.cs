using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace Squared.Util {
    // Thanks to Herb Sutter
    // http://www.ddj.com/cpp/211601363
    public class AtomicQueue<T> {
        internal delegate Node NodeAllocator ();
        internal delegate void NodeDeallocator (Node node);

        internal class Node {
            public T Value;
            public Node Next = null;
        }

        Node _Head;
        Node _Tail;

        int _ConsumerLock = 0;
        int _ProducerLock = 0;

        int _Count = 0;

        public AtomicQueue () {
            _Head = _Tail = new Node();
        }

        public int Count {
            get {
                return _Count;
            }
        }

        internal void EnqueueNode (Node node) {
            int iterations = 1;
            while (Interlocked.CompareExchange(ref _ProducerLock, 1, 0) != 0) {
                SpinWait(iterations++);
            }

            _Tail.Next = node;
            Thread.MemoryBarrier();
            _Tail = node;

            Interlocked.Increment(ref _Count);

            _ProducerLock = 0;
            Thread.MemoryBarrier();
        }

        public void Enqueue (T value) {
            var temp = new Node { Value = value };

            EnqueueNode(temp);
        }

        internal bool DequeueNode (out Node result, out Node deadNode) {
            int iterations = 1;
            while (Interlocked.CompareExchange(ref _ConsumerLock, 1, 0) != 0) {
                SpinWait(iterations++);
            }

            bool success = false;

            var head = _Head;
            Thread.MemoryBarrier();
            var next = head.Next;
            Thread.MemoryBarrier();
            if (next != null) {
                Thread.MemoryBarrier();
                head.Next = null;
                head.Value = default(T);
                Thread.MemoryBarrier();
                _Head = next;
                Interlocked.Decrement(ref _Count);

                deadNode = head;
                result = next;
                success = true;
            } else {
                result = null;
                deadNode = null;
            }

            Thread.MemoryBarrier();
            _ConsumerLock = 0;
            Thread.MemoryBarrier();

            return success;
        }

        public bool Dequeue (out T result) {
            Node resultNode, deadNode;

            bool success = DequeueNode(out resultNode, out deadNode);

            if (resultNode != null)
                result = resultNode.Value;
            else
                result = default(T);

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
