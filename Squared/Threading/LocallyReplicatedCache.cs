using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Id = System.Int32;

namespace Squared.Threading {
    public class LocallyReplicatedCache<TValue> {
        public class Table {
            public Dictionary<Id, TValue> ValuesById;
            public Dictionary<TValue, Id> IdsByValue;

            internal Table (IEqualityComparer<TValue> comparer) {
                ValuesById = new Dictionary<Id, TValue>();
                IdsByValue = new Dictionary<TValue, Id>(comparer);
            }

            public void Set (Id id, TValue value) {
                ValuesById[id] = value;
                IdsByValue[value] = id;
            }

            public TValue GetValue (Id id) {
                return ValuesById[id];
            }

            public bool TryGetValue (Id id, out TValue value) {
                return ValuesById.TryGetValue(id, out value);
            }

            public bool TryGetId (TValue value, out Id id) {
                return IdsByValue.TryGetValue(value, out id);
            }
        }

        public int Count { get; private set; }

        public readonly IEqualityComparer<TValue> Comparer;
        private readonly Func<TValue, TValue> PrepareValueForStorage;
        private ReaderWriterLockSlim SharedCacheLock = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);
        private int NextId = 1;
        private Table SharedCache;
        private ThreadLocal<Table> LocalCache;

        public LocallyReplicatedCache (
            IEqualityComparer<TValue> comparer, 
            Func<TValue, TValue> prepareValueForStorage
        ) {
            Comparer = comparer;
            PrepareValueForStorage = prepareValueForStorage;
            SharedCache = AllocateTable();
            LocalCache = new ThreadLocal<Table>(AllocateTable);
        }

        private Table AllocateTable () {
            return new Table(Comparer);
        }

        /// <summary>
        /// Copies the current shared cache data into the local cache, then returns it.
        /// </summary>
        public Table GetCurrentLocalCache () {
            var result = LocalCache.Value;
            try {
                SharedCacheLock.EnterReadLock();
                foreach (var kvp in SharedCache.ValuesById)
                    result.Set(kvp.Key, kvp.Value);
                return result;
            } finally {
                SharedCacheLock.ExitReadLock();
            }
        }

        public Id GetOrAssignId (TValue value) {
            Id id;
            var lc = LocalCache.Value;
            if (lc.TryGetId(value, out id))
                return id;

            try {
                SharedCacheLock.EnterUpgradeableReadLock();
                if (SharedCache.TryGetId(value, out id)) {
                    var storageValue = SharedCache.GetValue(id);
                    lc.Set(id, value);
                    return id;
                }

                {
                    var storageValue = PrepareValueForStorage(value);
                    try {
                        SharedCacheLock.EnterWriteLock();
                        id = NextId++;
                        SharedCache.Set(id, storageValue);
                        Count = SharedCache.ValuesById.Count;
                    } finally {
                        SharedCacheLock.ExitWriteLock();
                    }

                    lc.Set(id, storageValue);
                    return id;
                }
            } finally {
                SharedCacheLock.ExitUpgradeableReadLock();
            }
        }

        public bool TryGetId (TValue value, out Id id) {
            var lc = LocalCache.Value;
            if (!lc.TryGetId(value, out id)) {
                try {
                    SharedCacheLock.EnterReadLock();
                    if (SharedCache.TryGetId(value, out id)) {
                        lc.Set(id, SharedCache.GetValue(id));
                        return true;
                    } else {
                        return false;
                    }
                } finally {
                    SharedCacheLock.ExitReadLock();
                }
            }
            return true;
        }

        public bool TryGetValue (Id id, out TValue value) {
            var lc = LocalCache.Value;
            if (!lc.TryGetValue(id, out value)) {
                try {
                    SharedCacheLock.EnterReadLock();
                    if (SharedCache.TryGetValue(id, out value)) {
                        lc.Set(id, SharedCache.GetValue(id));
                        return true;
                    } else {
                        return false;
                    }
                } finally {
                    SharedCacheLock.ExitReadLock();
                }
            }
            return true;
        }
    }

    public class LocallyReplicatedObjectCache<TObject>
        where TObject : class
    {
        public class EntryComparer : IEqualityComparer<Entry> {
            public bool Equals (Entry x, Entry y) {
                return x.Equals(y);
            }

            public int GetHashCode (Entry obj) {
                return obj.HashCode;
            }
        }

        public struct Entry {
            public int HashCode;
            public GCHandle Handle;
            public TObject Object;

            public bool Equals (Entry rhs) {
                if (HashCode != rhs.HashCode)
                    return false;

                object o1 = (Handle.IsAllocated ? Handle.Target : Object),
                    o2 = (rhs.Handle.IsAllocated ? rhs.Handle.Target : rhs.Object);

                return object.ReferenceEquals(o1, o2);
            }
        }

        private LocallyReplicatedCache<Entry> Cache;

        public LocallyReplicatedObjectCache () {
            Cache = new LocallyReplicatedCache<Entry>(new EntryComparer(), PrepareValueForStorage_Impl);
        }

        public TObject GetValue (Id id) {
            if (id <= 0)
                return null;

            if (!Cache.TryGetValue(id, out Entry entry))
                return null;
            return (TObject)(entry.Object ?? entry.Handle.Target);
        }

        public Id GetId (TObject obj) {
            if (obj == null)
                return 0;

            var key = new Entry {
                HashCode = obj.GetHashCode(),
                Object = obj
            };
            return Cache.GetOrAssignId(key);
        }

        private static Entry PrepareValueForStorage_Impl (Entry e) {
            if (e.Object != null) {
                return new Entry {
                    HashCode = e.HashCode,
                    Handle = GCHandle.Alloc(e.Object, GCHandleType.Weak),
                    Object = null
                };
            } else
                return e;
        }
    }
}
