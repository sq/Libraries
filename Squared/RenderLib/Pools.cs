using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Squared.Threading;
using Squared.Util;

namespace Squared.Render {
    public interface IBatchPool {
        void Release (Batch batch);
        void SetCapacity (int newCapacity);
    }

    public class BatchPool<T> : BaseObjectPool<T>, IBatchPool
        where T : Batch, new() {

        public readonly Func<IBatchPool, T> Allocator;

        public BatchPool (Func<IBatchPool, T> allocator, int poolCapacity = BaseObjectPool<T>.DefaultCapacity)
            : this(poolCapacity) {
            Allocator = allocator;
        }

        public BatchPool (int poolCapacity)
            : base(poolCapacity) {
        }

        protected override T AllocateNew () {
            return Allocator(this);
        }

        void IBatchPool.Release (Batch batch) {
            if (batch.State.IsPrepareQueued)
                throw new Exception("Queued for prepare");

            Release((T)batch);
        }

        public void SetCapacity (int newCapacity) {
            PoolCapacity = newCapacity;
        }
    }

    public abstract class BaseObjectPool<T>
        where T : class {

        private UnorderedList<T> _Pool;

        public const int DefaultCapacity = 512;

        public int PoolCapacity;

        public BaseObjectPool ()
            : this(DefaultCapacity) {
        }

        public BaseObjectPool (int poolCapacity) {
            PoolCapacity = poolCapacity;
            _Pool = new UnorderedList<T>(Math.Max(poolCapacity / 2, 64));
        }

        public virtual T Allocate () {
            T result = null;

            lock (_Pool)
                _Pool.TryPopFront(out result);

            if (result == null)
                result = AllocateNew();

            return result;
        }

        protected abstract T AllocateNew ();

        public virtual void Release (T obj) {
            lock (_Pool) {
                if (_Pool.Count > PoolCapacity)
                    return;

                _Pool.Add(obj);
            }
        }
    }

    public interface IDrainable {
        void WaitForWorkItems ();
    }

    public class ListPool<T> : IDrainable {
        private struct ListClearWorkItem : IWorkItem {
            public UnorderedList<T> List;
            public UnorderedList<UnorderedList<T>> Pool;
            public int Limit;

            public void Execute () {
                lock (Pool)
                    if (Pool.Count >= Limit)
                        return;

                List.Clear();
                lock (Pool) {
                    if (Pool.Count >= Limit)
                        return;

                    Pool.Add(List);
                }
            }
        }

        private UnorderedList<UnorderedList<T>> _Pool = new UnorderedList<UnorderedList<T>>();
        private UnorderedList<UnorderedList<T>> _LargePool = new UnorderedList<UnorderedList<T>>();

        public          int SmallPoolCapacity;
        public          int LargePoolCapacity;
        public readonly int InitialItemSize;
        public          int SmallPoolMaxItemSize;
        public          int LargePoolMaxItemSize;

        public ThreadGroup ThreadGroup { get; set; }

        public ListPool (int smallPoolCapacity, int initialItemSize, int maxItemSize) {
            SmallPoolCapacity = smallPoolCapacity;
            InitialItemSize = initialItemSize;
            SmallPoolMaxItemSize = maxItemSize;

            LargePoolCapacity = 0;
            LargePoolMaxItemSize = 0;
        }

        public ListPool (int smallPoolCapacity, int largePoolCapacity, int initialItemSize, int maxSmallItemSize, int maxLargeItemSize) {
            SmallPoolCapacity = smallPoolCapacity;
            LargePoolCapacity = largePoolCapacity;
            InitialItemSize = initialItemSize;
            SmallPoolMaxItemSize = maxSmallItemSize;
            LargePoolMaxItemSize = maxLargeItemSize;
        }

        public UnorderedList<T> Allocate (int? capacity, bool capacityIsHint = false) {
            UnorderedList<T> result = null;

            var isBig = capacity.HasValue &&
                (capacity.Value > SmallPoolMaxItemSize);

            if (
                (LargePoolCapacity > 0) && isBig
            ) {
                lock (_LargePool)
                    _LargePool.TryPopFront(out result);
            }

            if (capacityIsHint || !isBig || (LargePoolCapacity <= 0)) {
                lock (_Pool)
                    _Pool.TryPopFront(out result);

                if (
                    (LargePoolCapacity > 0) &&
                    (result == null)
                ) {
                    lock (_LargePool)
                        _LargePool.TryPopFront(out result);
                }
            }

            if (result == null)
                result = new UnorderedList<T>(capacity.GetValueOrDefault(InitialItemSize));

            return result;
        }

        void IDrainable.WaitForWorkItems () {
            if (ThreadGroup == null)
                return;

            ThreadGroup.GetQueueForType<ListClearWorkItem>().WaitUntilDrained();
        }

        private void ClearAndReturn (UnorderedList<T> list, UnorderedList<UnorderedList<T>> pool, int limit) {
            if (ThreadGroup != null) {
                ThreadGroup.Enqueue(new ListClearWorkItem {
                    List = list,
                    Pool = pool,
                    Limit = limit
                });
                return;
            }

            if (pool.Count >= limit)
                return;

            list.Clear();
            pool.Add(list);
        }

        public void Release (ref UnorderedList<T> _list) {
            var list = _list;
            _list = null;

            if (list == null)
                return;

            if (list.Capacity > SmallPoolMaxItemSize) {
                if (list.Capacity < LargePoolMaxItemSize) {
                    lock (_LargePool) {
                        if (_LargePool.Count >= LargePoolCapacity)
                            return;
                    }

                    lock (_LargePool)
                        ClearAndReturn(list, _LargePool, LargePoolCapacity);
                }

                return;
            }

            lock (_Pool) {
                if (_Pool.Count >= SmallPoolCapacity)
                    return;
            }

            lock (_Pool)
                ClearAndReturn(list, _Pool, SmallPoolCapacity);
        }
    }

    public interface IPoolAllocator {
        void Collect ();
    }

    public interface IArrayPoolAllocator {
        void Step ();
    }

    // Thread-safe
    public class ArrayPoolAllocator<T> : IArrayPoolAllocator {
        public struct Allocation {
            public readonly int Origin;
            public readonly T[] Buffer;

            internal Allocation (int origin, T[] buffer) {
                Origin = origin;
                Buffer = buffer;
            }
        }

        public class Pool : UnorderedList<T[]> {
            public UnorderedList<Allocation> LiveAllocations = new UnorderedList<Allocation>(64);
            public readonly int AllocationSize;

            public Pool (int allocationSize, int capacity)
                : base(capacity) {
                AllocationSize = allocationSize;
            }
        }

        public const int MinPower = 6;
        public const int MaxPower = 20;
        public const int PowerCount = (MaxPower - MinPower) + 1;
        public const int MinSize = 1 << MinPower;
        public const int MaxSize = 1 << MaxPower;
        public const int CollectionAge = 2;

        public const int DefaultPoolCapacity = 32;
        public const int MaxPoolCapacity = 512;

        private int StepIndex = 0;
        private Pool[] Pools = new Pool[PowerCount];

        public ArrayPoolAllocator () {
            for (int power = MinPower; power <= MaxPower; power++)
                Pools[power - MinPower] = new Pool(1 << power, DefaultPoolCapacity);
        }

        private static int IntLog2 (int x) {
            int l = 0;

            if (x >= 1 << 16) {
                x >>= 16;
                l |= 16;
            }
            if (x >= 1 << 8) {
                x >>= 8;
                l |= 8;
            }
            if (x >= 1 << 4) {
                x >>= 4;
                l |= 4;
            }
            if (x >= 1 << 2) {
                x >>= 2;
                l |= 2;
            }
            if (x >= 1 << 1)
                l |= 1;
            return l;
        }

        private int SelectPool (int capacity) {
            int log2 = IntLog2(capacity) - MinPower + 1;
            if (log2 < 0)
                log2 = 0;

            for (int power = log2; power < PowerCount; power++) {
                var poolSize = Pools[power].AllocationSize;
                if (poolSize > capacity)
                    return power;
            }

            throw new InvalidOperationException("Allocation size out of range");
        }

        public Allocation Allocate (int capacity) {
            int poolId = SelectPool(capacity);
            var pool = Pools[poolId];

            T[] result;
            lock (pool)
                pool.TryPopFront(out result);

            if (result == null)
                result = new T[pool.AllocationSize];

            var a = new Allocation(StepIndex, result);
            lock (pool)
                pool.LiveAllocations.Add(a);
            return a;
        }

        public void Step () {
            int expirationThreshold = StepIndex - CollectionAge;
            StepIndex += 1;

            for (int power = 0; power < PowerCount; power++) {
                var pool = Pools[power];
                lock (pool) {

                    using (var e = pool.LiveAllocations.GetEnumerator())
                        while (e.MoveNext()) {
                            var a = e.Current;

                            if (a.Origin <= expirationThreshold) {
                                pool.Add(a.Buffer);
                                e.RemoveCurrent();
                            }
                        }
                }
            }
        }
    }
}
