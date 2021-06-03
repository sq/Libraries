using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Squared.Util;
using Id = System.Int32;

namespace Squared.Threading {
    public class LocallyReplicatedCache<TValue> {
        public class Table {
            private UnorderedList<TValue> ValuesById;
            private Dictionary<TValue, Id> IdsByValue;

            internal Table (IEqualityComparer<TValue> comparer) {
                ValuesById = new UnorderedList<TValue>(1024);
                ValuesById.Add(default(TValue));
                IdsByValue = new Dictionary<TValue, Id>(comparer);
            }

            public void ReplicateFrom (Table source) {
                var sourceArray = source.ValuesById;
                var sourceCount = sourceArray.Count;
                var i = ValuesById.Count;
                while (i < sourceCount) {
                    var item = sourceArray.DangerousGetItem(i);
                    ValuesById.Add(item);
                    IdsByValue[item] = i;
                    i++;
                }
            }

            public int Count {
                get {
                    return ValuesById.Count;
                }
            }

            public void Set (Id id, TValue value) {
                var count = ValuesById.Count;
                if (count == id) {
                    ValuesById.Add(value);
                } else {
                    throw new InvalidOperationException();
                }
                IdsByValue[value] = id;
            }

            public TValue GetValue (Id id) {
                return ValuesById.DangerousGetItem(id);
            }

            public bool TryGetValue (Id id, out TValue value) {
                return ValuesById.DangerousTryGetItem(id, out value);
            }

            public bool TryGetId (TValue value, out Id id) {
                return IdsByValue.TryGetValue(value, out id) && (id > 0);
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
                result.ReplicateFrom(SharedCache);
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
                    lc.ReplicateFrom(SharedCache);
                    return id;
                }

                {
                    var storageValue = PrepareValueForStorage(value);
                    try {
                        SharedCacheLock.EnterWriteLock();
                        id = NextId++;
                        SharedCache.Set(id, storageValue);
                        Count++;
                        lc.ReplicateFrom(SharedCache);
                    } finally {
                        SharedCacheLock.ExitWriteLock();
                    }

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
                        lc.ReplicateFrom(SharedCache);
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
                        lc.ReplicateFrom(SharedCache);
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

    public struct LocalObjectCache<TObject>
        where TObject : class
    {
        internal LocallyReplicatedCache<LocallyReplicatedObjectCache<TObject>.Entry>.Table Table;

        public TObject GetValue (Id id) {
            if (id <= 0)
                return null;

            var entry = Table.GetValue(id);
            return entry.Object ?? (TObject)entry.Handle.Target;
        }

        public bool TryGetValue (Id id, out TObject result) {
            if (id <= 0) {
                result = null;
                return true;
            }

            LocallyReplicatedObjectCache<TObject>.Entry entry;
            if (!Table.TryGetValue(id, out entry)) {
                result = null;
                return false;
            }
            result = entry.Object ?? (TObject)entry.Handle.Target;
            return true;
        }
    }

    public class LocallyReplicatedObjectCache<TObject>
        where TObject : class
    {
        public sealed class EntryComparer : IEqualityComparer<Entry> {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool Equals (Entry x, Entry y) {
                return x.Equals(y);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public int GetHashCode (Entry obj) {
                return obj.HashCode;
            }
        }

        public struct Entry {
            public int HashCode;
            public GCHandle Handle;
            public TObject Object;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public LocalObjectCache<TObject> GetCurrentLocalCache () {
            return new LocalObjectCache<TObject> {
                Table = Cache.GetCurrentLocalCache()
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TObject GetValue (Id id) {
            if (id <= 0)
                return null;

            if (!Cache.TryGetValue(id, out Entry entry))
                return null;
            return (TObject)(entry.Object ?? entry.Handle.Target);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
