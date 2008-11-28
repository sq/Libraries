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
        internal delegate Node NodeAllocator ();
        internal delegate void NodeDeallocator (Node node);

        internal class NodePool {
            int _MaxSize;
            AtomicQueue<T> _SpareNodes;

            public NodePool (int size) {
                _MaxSize = size;
                var allocator = (NodeAllocator)(() => new Node());
                _SpareNodes = new AtomicQueue<T>(allocator, null);

                for (int i = 0; i < size; i++)
                    _SpareNodes.EnqueueNode(new Node());
            }

            public Node Create () {
                Node temp, deadNode;
                if (_SpareNodes.DequeueNode(out temp, out deadNode))
                    return deadNode;
                else
                    return new Node();
            }

            public void Destroy (Node node) {
                if (_SpareNodes.Count >= _MaxSize)
                    return;

                node.Next = null;
                node.Value = default(T);
                _SpareNodes.EnqueueNode(node);
            }
        }

        internal class Node {
            public T Value;
            public volatile Node Next = null;
        }

        NodeAllocator _Allocator = null;
        NodeDeallocator _Deallocator = null;

        volatile Node _Head;
        volatile Node _Tail;

        volatile int _ConsumerLock = 0;
        volatile int _ProducerLock = 0;

        int _Count = 0;

        public AtomicQueue () {
            _Allocator = () => new Node();
            _Deallocator = null;
            _Head = _Tail = _Allocator();
        }

        internal AtomicQueue (NodeAllocator allocator, NodeDeallocator deallocator) {
            _Allocator = allocator;
            _Deallocator = deallocator;
            _Head = _Tail = _Allocator();
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
            _Tail = node;

            Interlocked.Increment(ref _Count);

            _ProducerLock = 0;
        }

        public void Enqueue (T value) {
            var temp = _Allocator();
            temp.Value = value;

            EnqueueNode(temp);
        }

        internal bool DequeueNode (out Node result, out Node deadNode) {
            int iterations = 1;
            while (Interlocked.CompareExchange(ref _ConsumerLock, 1, 0) != 0) {
                SpinWait(iterations++);
            }

            bool success = false;

            var head = _Head;
            var next = head.Next;
            if (next != null) {
                head.Next = null;
                head.Value = default(T);
                _Head = next;
                Interlocked.Decrement(ref _Count);

                deadNode = head;
                result = next;
                success = true;
            } else {
                result = null;
                deadNode = null;
            }

            _ConsumerLock = 0;

            return success;
        }

        public bool Dequeue (out T result) {
            Node resultNode, deadNode;

            bool success = DequeueNode(out resultNode, out deadNode);

            if (resultNode != null)
                result = resultNode.Value;
            else
                result = default(T);

            if (deadNode != null)
                if (_Deallocator != null)
                    _Deallocator(deadNode);

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
