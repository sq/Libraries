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

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool Equals (Entry x, Entry y) {
                return x.Equals(y);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public int GetHashCode (Entry obj) {
                return obj.HashCode;
            }
        }

        public readonly struct Entry {
            public readonly int HashCode;
            public readonly WeakReference<object> Weak;
            public readonly object Strong;

            public Entry (object obj, int hashCode, bool strong) {
                Weak = null;
                Strong = null;
                HashCode = hashCode;
                if (strong)
                    Strong = obj;
                else
                    Weak = new WeakReference<object>(obj, false);
            }

            public object Instance {
                get {
                    if (Weak != null) {
                        Weak.TryGetTarget(out var result);
                        return result;
                    }
                    return Strong;
                }
            }

            public Entry ConvertToWeak () {
                if (Weak != null)
                    return this;
                if (Strong == null)
                    return this;
                return new Entry(Strong, HashCode, false);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool Equals (Entry rhs) {
                if (HashCode != rhs.HashCode)
                    return false;

                return ReferenceEquals(Instance, rhs.Instance);
            }

            public override int GetHashCode () {
                return HashCode;
            }

            public override string ToString () {
                var instance = Instance;
                if (instance == null) {
                    if (Weak == null)
                        return "null";
                    else
                        return string.Concat("dead ", HashCode.ToString());
                } else
                    return instance.ToString();
            }
        }
        
        public sealed class Table {
            private sealed class IdComparer : IEqualityComparer<Id> {
                public static readonly IdComparer Instance = new ();

                public bool Equals (int x, int y) => x == y;
                public int GetHashCode (int obj) => obj;
            }

            private Dictionary<Id, Entry> ValuesById;
            private Dictionary<Entry, Id> IdsByValue;
            private List<Entry> DeadEntries = new ();

            internal Table (EntryComparer comparer) {
                ValuesById = new (1024, IdComparer.Instance);
                IdsByValue = new (1024, comparer);
            }

            public void ReplicateFrom (Table source) {
                var sourceDict = source.ValuesById;
                ValuesById.Clear();
                IdsByValue.Clear();
                foreach (var kvp in sourceDict) {
                    ValuesById[kvp.Key] = kvp.Value;
                    IdsByValue[kvp.Value] = kvp.Key;
                }
            }

            public int Count {
                get {
                    return ValuesById.Count;
                }
            }

            public void Set (Id id, Entry value) {
                // We convert the entry's strong reference (if present) into a weak reference,
                //  so that the table does not prevent its values from being collected.
                value = value.ConvertToWeak();
                ValuesById[id] = value;
                IdsByValue[value] = id;
            }

            public Entry GetValue (Id id) =>
                ValuesById[id];

            public bool TryGetValue (Id id, out Entry value) =>
                ValuesById.TryGetValue(id, out value);

            public bool TryGetId (Entry value, out Id id) =>
                IdsByValue.TryGetValue(value, out id) && (id > 0);

            internal int RemoveDeadEntries () {
                var result = 0;

                DeadEntries.Clear();
                foreach (var entry in ValuesById.Values) {
                    // HACK: Preserve #0 == null
                    if (entry.Weak == null)
                        continue;

                    if (entry.Weak.TryGetTarget(out _))
                        continue;

                    DeadEntries.Add(entry);
                }

                foreach (var deadEntry in DeadEntries) {
                    var id = IdsByValue[deadEntry];
                    ValuesById.Remove(id);
                    IdsByValue.Remove(deadEntry);
                    result++;
                }
                DeadEntries.Clear();

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
            object result = null;
            if (entry.Weak?.TryGetTarget(out result) == true)
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

            object obj = null;
            if (entry.Weak?.TryGetTarget(out obj) == true) {
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

            return (TObject)entry.Instance;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Id GetId (TObject obj) {
            if (obj == null)
                return 0;

            // We create a strong-reference entry for the purposes of performing cache lookup.
            // Otherwise, every lookup would create a temporary weakref that gets destroyed.
            var key = new LocallyReplicatedCache.Entry(obj, obj.GetHashCode(), true);
            return Cache.GetOrAssignId(key);
        }
    }
}
