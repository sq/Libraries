using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
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
            if (batch.IsPrepareQueued)
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
                _Pool.TryPopBack(out result);

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
        void WaitForWorkItems (int timeoutMs = -1);
    }

    public class ListPool<T> : IDrainable, IListPool<T> {
        private struct ListClearWorkItem : IWorkItem {
            public static WorkItemConfiguration Configuration =>
                new WorkItemConfiguration {
                    Priority = -1
                };

            public UnorderedList<T> List;

            public void Execute (ThreadGroup group) {
                List.Clear();
                ClearedLists.Value.Add(List);
            }
        }

        private static readonly ThreadLocal<UnorderedList<UnorderedList<T>>> ClearedLists = 
            new ThreadLocal<UnorderedList<UnorderedList<T>>>(
                () => new UnorderedList<UnorderedList<T>>()
            );
        private UnorderedList<UnorderedList<T>> _Pool = new UnorderedList<UnorderedList<T>>();
        private UnorderedList<UnorderedList<T>> _LargePool = new UnorderedList<UnorderedList<T>>();

        public UnorderedList<T>.Allocator Allocator = UnorderedList<T>.Allocator.Default;

        public static int DeferredClearSizeThreshold = 128;

        public          int SmallPoolCapacity;
        public          int LargePoolCapacity;
        public readonly int InitialItemSize;
        public          int SmallPoolMaxItemSize;
        public          int LargePoolMaxItemSize;
        public         bool FastClearEnabled = false;

        private ThreadGroup _ThreadGroup;
        private WorkQueue<ListClearWorkItem> _ClearQueue;
        public ThreadGroup ThreadGroup {
            get {
                return _ThreadGroup;
            }
            set {
                if (_ThreadGroup == value)
                    return;
                _ThreadGroup = value;
                _ClearQueue = value?.GetQueueForType<ListClearWorkItem>();
                _ClearQueue?.RegisterDrainListener(OnClearListDrained);
            }
        }

        private void OnClearListDrained (int listsCleared, bool moreRemain) {
            var cl = ClearedLists.Value;
            if (cl == null) // FIXME: This shouldn't be possible
                return;

            bool isSmallLocked = false, isLargeLocked = false;

            try {
                foreach (var item in cl) {
                    var isLarge = (item.Capacity > SmallPoolMaxItemSize);
                    if (isLarge) {
                        if (!isLargeLocked) {
                            isLargeLocked = true;
                            Monitor.Enter(_LargePool);
                        }
                        if (_LargePool.Count >= LargePoolCapacity)
                            continue;
                        _LargePool.Add(item);
                    } else {
                        if (!isSmallLocked) {
                            isSmallLocked = true;
                            Monitor.Enter(_Pool);
                        }
                        if (_Pool.Count >= SmallPoolCapacity)
                            continue;
                        _Pool.Add(item);
                    }
                }
            } finally {
                if (isSmallLocked)
                    Monitor.Exit(_Pool);
                if (isLargeLocked)
                    Monitor.Exit(_LargePool);
                cl.Clear();
            }
        }

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
                if (_LargePool.Count > 0)
                    lock (_LargePool)
                        _LargePool.TryPopBack(out result);
            }

            if (capacityIsHint || !isBig || (LargePoolCapacity <= 0)) {
                if (_Pool.Count > 0)
                    lock (_Pool)
                        _Pool.TryPopBack(out result);
            }

            if (result == null)
                result = new UnorderedList<T>(capacity.GetValueOrDefault(InitialItemSize), Allocator);

            return result;
        }

        void IDrainable.WaitForWorkItems (int timeoutMs) {
            // FIXME: Priority inversion
            _ClearQueue?.WaitUntilDrained(timeoutMs);
        }

        private void ClearAndReturn (UnorderedList<T> list, UnorderedList<UnorderedList<T>> pool, int limit, WorkQueueNotifyMode notifyMode = WorkQueueNotifyMode.Always) {
            if (
                !FastClearEnabled &&
                (list.Count > DeferredClearSizeThreshold) && 
                (_ClearQueue != null)
            ) {
                _ClearQueue.Enqueue(new ListClearWorkItem {
                    List = list,
                }, notifyChanged: notifyMode);
                return;
            }

            // HACK: Acquiring the lock here would be technically correct but
            //  unnecessarily slow, since we're just trying to avoid doing a clear
            //  in cases where the list will be GCd anyway
            if (pool.Count >= limit)
                return;

            if (FastClearEnabled)
                list.UnsafeFastClear();
            else
                list.Clear();

            lock (pool) {
                if (pool.Count >= limit)
                    return;

                pool.Add(list);
            }
        }

        void IListPool<T>.Release (ref UnorderedList<T> _list) {
            // HACK: Render manager wakes the thread pool before waiting so at worst 
            //  we will wait until the end of the frame to clear and return lists, which
            //  is way better than constantly burning cycles waking the thread group
            Release(ref _list, WorkQueueNotifyMode.Never);
        }

        public void Release (ref UnorderedList<T> _list, WorkQueueNotifyMode notifyMode) {
            var list = _list;
            _list = null;

            if (list == null)
                return;

            if (list.Capacity > SmallPoolMaxItemSize) {
                if (list.Capacity <= LargePoolMaxItemSize) {
                    lock (_LargePool) {
                        if (_LargePool.Count >= LargePoolCapacity)
                            return;
                    }

                    ClearAndReturn(list, _LargePool, LargePoolCapacity, notifyMode);
                }

                return;
            }

            lock (_Pool) {
                if (_Pool.Count >= SmallPoolCapacity)
                    return;
            }

            ClearAndReturn(list, _Pool, SmallPoolCapacity, notifyMode);
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
        public readonly struct Allocation {
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
                pool.TryPopBack(out result);

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
