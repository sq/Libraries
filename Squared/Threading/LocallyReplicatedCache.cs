using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Squared.Util;
using Id = System.Int32;

namespace Squared.Threading {
    public sealed class LocallyReplicatedCache {
        public sealed class EntryComparer : IEqualityComparer<Entry> {
            [TargetedPatchingOptOut("")]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool Equals (Entry x, Entry y) {
                return x.Equals(y);
            }

            [TargetedPatchingOptOut("")]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public int GetHashCode (Entry obj) {
                return obj.HashCode;
            }
        }

        public readonly struct Entry {
            public readonly int HashCode;
            public readonly WeakReference<object> Weak;

            public Entry (object obj, int hashCode) {
                HashCode = hashCode;
                Weak = new WeakReference<object>(obj, false);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool Equals (Entry rhs) {
                if (HashCode != rhs.HashCode)
                    return false;

                object o1 = null, o2 = null;
                Weak?.TryGetTarget(out o1);
                rhs.Weak?.TryGetTarget(out o2);

                return ReferenceEquals(o1, o2);
            }

            public override int GetHashCode () {
                return HashCode;
            }

            public override string ToString () {
                if (Weak == null)
                    return "default";
                else if (Weak.TryGetTarget(out var o))
                    return o.ToString ();
                else
                    return string.Concat("dead ", HashCode.ToString());
            }
        }
        
        public sealed class Table {
            private UnorderedList<Entry> ValuesById;
            private Dictionary<Entry, Id> IdsByValue;

            internal Table (EntryComparer comparer) {
                ValuesById = new UnorderedList<Entry>(1024) {
                    default
                };
                IdsByValue = new Dictionary<Entry, Id>(comparer);
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

            public void Set (Id id, Entry value) {
                var count = ValuesById.Count;
                if (count == id) {
                    ValuesById.Add(value);
                } else {
                    throw new InvalidOperationException();
                }
                IdsByValue[value] = id;
            }

            public Entry GetValue (Id id) {
                return ValuesById.DangerousGetItem(id);
            }

            public bool TryGetValue (Id id, out Entry value) {
                return ValuesById.DangerousTryGetItem(id, out value);
            }

            public bool TryGetId (Entry value, out Id id) {
                return IdsByValue.TryGetValue(value, out id) && (id > 0);
            }

            internal int RemoveDeadEntries () {
                var result = 0;
                var dead = default(Entry);

                using (var e = ValuesById.GetEnumerator())
                while (e.GetNext(out var entry)) {
                    // HACK: Preserve #0 == null
                    if (entry.Weak == null)
                        continue;

                    if (entry.Weak.TryGetTarget(out _))
                        continue;

                    IdsByValue.Remove(entry);
                    e.ReplaceCurrent(ref dead);
                    result++;
                }

                return result;
            }
        }

        public int Count { get; private set; }

        public readonly EntryComparer Comparer;
        private ReaderWriterLockSlim SharedCacheLock = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);
        private int NextId = 1;
        private Table SharedCache;
        private ThreadLocal<Table> LocalCache;

        public LocallyReplicatedCache () {
            Comparer = new EntryComparer();
            SharedCache = AllocateTable();
            LocalCache = new ThreadLocal<Table>(AllocateTable, true);
        }

        internal Table AllocateTable () {
            return new Table(Comparer);
        }

        public bool RemoveDeadEntries () {
            SharedCacheLock.EnterWriteLock();
            try {
                var removed = SharedCache.RemoveDeadEntries();
                if (removed > 0) {
                    foreach (var table in LocalCache.Values)
                        table.RemoveDeadEntries();
                }
                return removed > 0;
            } finally {
                SharedCacheLock.ExitWriteLock();
            }
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

        public Id GetOrAssignId (Entry value) {
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
                    try {
                        SharedCacheLock.EnterWriteLock();
                        id = NextId++;
                        SharedCache.Set(id, value);
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

        public bool TryGetId (Entry value, out Id id) {
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

        public bool TryGetValue (Id id, out Entry value) {
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

        internal void UpdateSnapshot (Table table) {
            try {
                SharedCacheLock.EnterReadLock();
                table.ReplicateFrom(SharedCache);
            } finally {
                SharedCacheLock.ExitReadLock();
            }
        }
    }

    public readonly struct LocalObjectCache<TObject>
        where TObject : class
    {
        internal readonly LocallyReplicatedCache.Table Table;

        internal LocalObjectCache (LocallyReplicatedCache.Table table) {
            Table = table;
        }

        public TObject GetValue (Id id) {
            if (id <= 0)
                return null;

            var entry = Table.GetValue(id);
            if (entry.Weak?.TryGetTarget(out var result) == true)
                return (TObject)result;
            else
                return null;
        }

        public bool RemoveDeadEntries () {
            return Table.RemoveDeadEntries() > 0;
        }

        public bool TryGetValue (Id id, out TObject result) {
            if (id <= 0) {
                result = null;
                return true;
            }

            LocallyReplicatedCache.Entry entry;
            if (!Table.TryGetValue(id, out entry)) {
                result = null;
                return false;
            }

            if (entry.Weak?.TryGetTarget(out var obj) == true) {
                result = obj as TObject;
                return true;
            }

            result = null;
            return false;
        }
    }

    public sealed class LocallyReplicatedObjectCache<TObject>
        where TObject : class
    {
        private LocallyReplicatedCache Cache;

        public LocallyReplicatedObjectCache () {
            Cache = new LocallyReplicatedCache();
        }

        public bool RemoveDeadEntries () {
            return Cache.RemoveDeadEntries();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void GetShareableSnapshot (ref LocalObjectCache<TObject> result) {
            if (result.Table == null)
                result = new LocalObjectCache<TObject>(Cache.AllocateTable());

            lock (result.Table)
                Cache.UpdateSnapshot(result.Table);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public LocalObjectCache<TObject> GetCurrentLocalCache () {
            return new LocalObjectCache<TObject>(Cache.GetCurrentLocalCache());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TObject GetValue (Id id) {
            if (id <= 0)
                return null;

            if (!Cache.TryGetValue(id, out var entry))
                return null;

            if (entry.Weak?.TryGetTarget(out var result) == true)
                return (TObject)result;
            else
                return null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Id GetId (TObject obj) {
            if (obj == null)
                return 0;

            var key = new LocallyReplicatedCache.Entry(obj, obj.GetHashCode());
            return Cache.GetOrAssignId(key);
        }
    }
}
