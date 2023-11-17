using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Squared.Util.Event {
    public delegate void AfterBroadcastEventHandler (EventBus sender, object eventSource, string eventType, object eventArgs, bool eventWasHandled);

    public struct EventFilter {
        public readonly WeakReference WeakSource;
        public readonly object StrongSource;
        public readonly int SourceHashCode;
        public readonly string Type;
        public readonly int TypeHashCode;

        public EventFilter (object source, string type, bool weak) {
            if (source == null)
                throw new ArgumentNullException("source");
            if (type == null)
                throw new ArgumentNullException("type");

            if (weak) {
                WeakSource = new WeakReference(source);
                StrongSource = null;
            } else {
                WeakSource = null;
                StrongSource = source;
            }
            SourceHashCode = source.GetHashCode();
            Type = type;
            TypeHashCode = type.GetHashCode();
        }

        public object Source {
            get {
                if (StrongSource != null)
                    return StrongSource;
                else
                    return WeakSource?.Target;
            }
        }

        public override int GetHashCode () {
            return SourceHashCode ^ TypeHashCode;
        }

        public override bool Equals (object obj) {
            if (obj is EventFilter) {
                var ef = (EventFilter)obj;
                return this.Equals(ef);
            } else
                return base.Equals(obj);
        }

        public bool Equals (EventFilter rhs) {
            return (SourceHashCode == rhs.SourceHashCode) &&
                (TypeHashCode == rhs.TypeHashCode) &&
                (Type == rhs.Type) &&
                (Source == rhs.Source);
        }
    }

    public sealed class EventFilterComparer : IEqualityComparer<EventFilter> {
        public static readonly EventFilterComparer Instance = new EventFilterComparer();

        public bool Equals (EventFilter x, EventFilter y) {
            if ((x.SourceHashCode != y.SourceHashCode) || (x.TypeHashCode != y.TypeHashCode) || (x.Type != y.Type))
                return false;

            return (x.Source == y.Source);
        }

        public int GetHashCode (EventFilter obj) {
            return obj.SourceHashCode ^ obj.TypeHashCode;
        }
    }

    public interface IEventInfo {
        EventBus Bus { get; }
        object Source { get; }
        EventCategoryToken Category { get; }
        string CategoryName { get; }
        string Type { get; }
        object Arguments { get; }
        bool IsConsumed { get; }

        void Consume ();
    }

    public interface IEventInfo<out T> : IEventInfo {
        new T Arguments { get; }
    }

    // FIXME: Make this a struct?
    public sealed class EventInfo<T> : IEventInfo<T> {
        public EventBus Bus { private set; get; }
        public object Source { private set; get; }
        public EventCategoryToken Category { private set; get; }
        public string CategoryName { private set; get; }
        public string Type { private set; get; }
        public T Arguments { internal set; get; }

        object IEventInfo.Arguments {
            get {
                return Arguments;
            }
        }

        public bool IsConsumed { private set; get; }

        public void Consume () {
            IsConsumed = true;
        }

        public EventInfo (EventBus bus, object source, EventCategoryToken categoryToken, string categoryName, string type, T arguments) {
            Bus = bus;
            Source = source;
            Category = categoryToken;
            CategoryName = categoryName;
            Type = type;
            Arguments = arguments;
            IsConsumed = false;
        }

        public EventInfo<T> Clone () {
            return new EventInfo<T>(Bus, Source, Category, CategoryName, Type, Arguments);
        }
    }

    public delegate void EventSubscriber (IEventInfo e);
    public delegate void TypedEventSubscriber<in T> (IEventInfo<T> e, T arguments);

    internal struct EventSubscriberEntry {
        public object StrongReference;
        public WeakReference WeakReference;

        public object Value {
            get {
                if (StrongReference != null)
                    return StrongReference;
                else if (WeakReference != null)
                    return WeakReference.Target;
                else
                    return null;
            }
        }
    }

    internal sealed class EventSubscriberList {
        public DenseList<EventSubscriberEntry> Entries;

        internal void Add (object subscriber, bool weak) {
            // FIXME: Throw?
            if (subscriber == null)
                throw new ArgumentNullException(nameof(subscriber));

            if (Entries.Count > 3)
                CollectGarbage();

            if (weak)
                Entries.Add(new EventSubscriberEntry {
                    WeakReference = new WeakReference(subscriber)
                });
            else
                Entries.Add(new EventSubscriberEntry {
                    StrongReference = subscriber
                });
        }

        internal bool Remove (object subscriber) {
            if (subscriber == null)
                return false;

            bool result = false;
            for (int i = Entries.Count - 1; i >= 0; i--) {
                if (Entries[i].Value == subscriber) {
                    Entries.RemoveAt(i);
                    result = true;
                }
            }

            if (Entries.Count > 3)
                CollectGarbage();

            return result;
        }

        internal void CollectGarbage () {
            for (int i = Entries.Count - 1; i >= 0; i--) {
                if (Entries[i].Value == null)
                    Entries.RemoveAt(i);
            }
        }
    }

    public struct EventSubscription : IDisposable {
        public EventBus EventBus { get; private set; }
        public EventSubscriber EventSubscriber { get; private set; }

        private EventFilter _EventFilter;
        public EventFilter EventFilter => _EventFilter;

        public EventSubscription (EventBus eventBus, in EventFilter eventFilter, EventSubscriber subscriber) {
            EventBus = eventBus;
            _EventFilter = eventFilter;
            EventSubscriber = subscriber;
        }

        public override int GetHashCode () {
            return EventBus.GetHashCode() ^ EventSubscriber.GetHashCode() ^ _EventFilter.GetHashCode();
        }

        public override bool Equals (object obj) {
            if (obj is EventSubscription) {
                var es = (EventSubscription)obj;
                return this.Equals(es);
            } else
                return base.Equals(obj);
        }

        public bool Equals (EventSubscription rhs) {
            return (EventBus == rhs.EventBus) &&
                (EventSubscriber == rhs.EventSubscriber) &&
                (_EventFilter.Equals(rhs._EventFilter));
        }

        public void Dispose () {
            if ((EventBus != null) && (EventSubscriber != null)) {
                EventBus.Unsubscribe(ref _EventFilter, EventSubscriber);
                _EventFilter = default;
                EventSubscriber = null;
                EventBus = null;
            }
        }
    }

    public struct TypedEventSubscription<T> : IDisposable {
        public EventBus EventBus { get; private set; }
        public TypedEventSubscriber<T> EventSubscriber { get; private set; }

        private EventFilter _EventFilter;
        public EventFilter EventFilter => _EventFilter;

        public TypedEventSubscription (EventBus eventBus, in EventFilter eventFilter, TypedEventSubscriber<T> subscriber) {
            EventBus = eventBus;
            _EventFilter = eventFilter;
            EventSubscriber = subscriber;
        }

        public override int GetHashCode () {
            return EventBus.GetHashCode() ^ EventSubscriber.GetHashCode() ^ _EventFilter.GetHashCode();
        }

        public override bool Equals (object obj) {
            if (obj is EventSubscription) {
                var es = (EventSubscription)obj;
                return this.Equals(es);
            } else
                return base.Equals(obj);
        }

        public bool Equals (TypedEventSubscription<T> rhs) {
            return (EventBus == rhs.EventBus) &&
                (EventSubscriber == rhs.EventSubscriber) &&
                (_EventFilter.Equals(rhs._EventFilter));
        }

        public void Dispose () {
            if ((EventBus != null) && (EventSubscriber != null)) {
                EventBus.Unsubscribe(ref _EventFilter, EventSubscriber);
                _EventFilter = default;
                EventSubscriber = null;
                EventBus = null;
            }
        }
    }

    public struct EventThunk {
        public readonly EventBus EventBus;
        public readonly object Source;
        public readonly string Type;

        public EventThunk (EventBus eventBus, object source, string type) {
            EventBus = eventBus;
            Source = source;
            Type = type;
        }

        public void Broadcast<T> (T arguments) {
            EventBus.Broadcast(Source, Type, arguments);
        }

        public void Broadcast () {
            EventBus.Broadcast(Source, Type, null);
        }

        public EventSubscription Subscribe (EventSubscriber subscriber, bool weak = false) {
            return EventBus.Subscribe(Source, Type, subscriber, weak);
        }

        public TypedEventSubscription<T> Subscribe<T> (TypedEventSubscriber<T> subscriber, bool weak = false)
            where T : class {
            return EventBus.Subscribe<T>(Source, Type, subscriber, weak);
        }
    }

    public interface IEventSource {
        string CategoryName {
            get;
        }
    }

    public sealed class EventCategoryToken {
        public readonly string Name;

        public EventCategoryToken (string name) {
            Name = name;
        }

        public override int GetHashCode () {
            return Name.GetHashCode();
        }
    }

    public sealed class EventBus : IDisposable {
        public struct CategoryCollection {
            public readonly EventBus EventBus;

            public CategoryCollection (EventBus eventBus) {
                EventBus = eventBus;
            }

            public EventCategoryToken this [string categoryName] {
                get {
                    return EventBus.GetCategory(categoryName);
                }
            }
        }

        public static readonly object AnySource = "<Any Source>";
        public static readonly string AnyType = "<Any Type>";

        private readonly Dictionary<string, EventCategoryToken> _Categories = 
            new Dictionary<string, EventCategoryToken>();

        private readonly Dictionary<EventFilter, EventSubscriberList> _UnsourcedSubscribers =
            new Dictionary<EventFilter, EventSubscriberList>(EventFilterComparer.Instance);
        private ConditionalWeakTable<object, Dictionary<EventFilter, EventSubscriberList>> _SourcedSubscribers =
            new ConditionalWeakTable<object, Dictionary<EventFilter, EventSubscriberList>>();

        /// <summary>
        /// Return false to suppress broadcast of this event
        /// </summary>
        public Func<object, string, object, bool> OnBroadcast;
        /// <summary>
        /// Fired after an event has been broadcast
        /// </summary>
        public event AfterBroadcastEventHandler AfterBroadcast;

        private static void CreateFilter (object source, string type, out EventFilter filter, bool weak) {
            filter = new EventFilter(source ?? AnySource, type ?? AnyType, weak);
        }

        private Dictionary<EventFilter, EventSubscriberList> GetTableForFilter (ref EventFilter filter, bool createIfMissing) {
            var src = filter.Source;
            if (src == null)
                return _UnsourcedSubscribers;

            if (_SourcedSubscribers == null)
                return null;

            lock (_SourcedSubscribers) {
                _SourcedSubscribers.TryGetValue(src, out var result);
                if (createIfMissing && (result == null)) {
                    result = new Dictionary<EventFilter, EventSubscriberList>(EventFilterComparer.Instance);
                    _SourcedSubscribers.Add(src, result);
                }
                return result;
            }
        }

        public EventSubscription Subscribe (object source, string type, EventSubscriber subscriber, bool weak = false) {
            EventFilter filter;
            CreateFilter(source, type, out filter, true);

            var table = GetTableForFilter(ref filter, true);

            lock (table) {
                if (!table.TryGetValue(filter, out var subscribers)) {
                    subscribers = new EventSubscriberList();
                    table[filter] = subscribers;
                }

                lock (subscribers)
                    subscribers.Add(subscriber, weak);
            }

            return new EventSubscription(this, in filter, subscriber);
        }

        private EventCategoryToken GetCategory (string name) {
            EventCategoryToken result;

            lock (_Categories)
            if (!_Categories.TryGetValue(name, out result)) {
                result = new EventCategoryToken(name);
                _Categories[name] = result;
            }

            return result;
        }

        public CategoryCollection Categories {
            get {
                return new CategoryCollection(this);
            }
        }

        public TypedEventSubscription<T> Subscribe<T> (object source, string type, TypedEventSubscriber<T> subscriber, bool weak = false) {
            EventFilter filter;
            CreateFilter(source, type, out filter, true);

            var table = GetTableForFilter(ref filter, true);
            lock (table) {
                if (!table.TryGetValue(filter, out var subscribers)) {
                    subscribers = new EventSubscriberList();
                    table[filter] = subscribers;
                }

                lock (subscribers)
                    subscribers.Add(subscriber, weak);
            }

            return new TypedEventSubscription<T>(this, in filter, subscriber);
        }

        public bool Unsubscribe (object source, string type, EventSubscriber subscriber) {
            EventFilter filter;
            CreateFilter(source, type, out filter, false);
            return Unsubscribe(ref filter, subscriber);
        }

        public bool Unsubscribe<T> (object source, string type, TypedEventSubscriber<T> subscriber) {
            EventFilter filter;
            CreateFilter(source, type, out filter, false);
            return Unsubscribe(ref filter, subscriber);
        }

        public bool Unsubscribe (ref EventFilter filter, EventSubscriber subscriber) {
            var table = GetTableForFilter(ref filter, false);
            if (table == null)
                return false;

            lock (table) {
                EventSubscriberList subscribers;
                lock (table)
                    if (!table.TryGetValue(filter, out subscribers))
                        return false;

                lock (subscribers)
                    return subscribers.Remove(subscriber);
            }
        }

        public bool Unsubscribe<T> (ref EventFilter filter, TypedEventSubscriber<T> subscriber) {
            var table = GetTableForFilter(ref filter, false);
            if (table == null)
                return false;

            lock (table) {
                EventSubscriberList subscribers;
                lock (table)
                    if (!table.TryGetValue(filter, out subscribers))
                        return false;

                lock (subscribers)
                    return subscribers.Remove(subscriber);
            }
        }

        private bool BroadcastToSubscribers<T> (object source, string type, T arguments) {
            EventInfo<T> info = null;
            EventSubscriberList subscribers;
            EventFilter filter;
            EventCategoryToken categoryToken = null;
            string categoryName = null;

            IEventSource iSource = source as IEventSource;
            if (iSource != null) {
                categoryName = iSource.CategoryName;
                categoryToken = GetCategory(categoryName);
            }

            for (int i = 0; i < 6; i++) {
                string typeFilter = (i & 1) == 1 ? type : AnyType;
                object sourceFilter;

                switch (i) {
                    case 0:
                    case 1:
                        sourceFilter = AnySource;
                        break;
                    case 2:
                    case 3:
                        sourceFilter = categoryToken;
                        break;
                    default:
                        sourceFilter = source;
                        break;
                }

                if ((sourceFilter == null) || (typeFilter == null))
                    continue;

                CreateFilter(
                    sourceFilter,
                    typeFilter,
                    out filter,
                    false
                );

                var table = GetTableForFilter(ref filter, false);
                if (table == null)
                    continue;

                lock (table)
                    if (!table.TryGetValue(filter, out subscribers))
                        continue;

                lock (subscribers)
                    if (subscribers.Entries.Count <= 0)
                        continue;

                if (info == null)
                    info = new EventInfo<T>(this, source, categoryToken, categoryName, type, arguments);

                int count;
                BufferPool<EventSubscriberEntry>.Buffer b;
                lock (subscribers) {
                    count = subscribers.Entries.Count;
                    b = BufferPool<EventSubscriberEntry>.Allocate(count);
                    subscribers.Entries.CopyTo(b.Data);
                }

                using (b) {
                    for (int j = count - 1; j >= 0; j--) {
                        var d = b.Data[j].Value;
                        if (d is TypedEventSubscriber<T> ts)
                            ts(info, arguments);
                        else if (d is EventSubscriber es)
                            es(info);
                        else // HACK: This shouldn't be possible
                            ;

                        if (info.IsConsumed)
                            return true;
                    }
                }
            }

            return false;
        }

        public bool Broadcast (object source, string type, object arguments) {
            if (source == null)
                throw new ArgumentNullException("source");
            if (type == null)
                throw new ArgumentNullException("type");

            if ((OnBroadcast != null) && !OnBroadcast(source, type, arguments))
                return true;

            var result = BroadcastToSubscribers(source, type, arguments);
            if (AfterBroadcast != null)
                AfterBroadcast(this, source, type, arguments, result);

            return result;
        }

        public bool Broadcast<T> (object source, string type, T arguments) {
            if (source == null)
                throw new ArgumentNullException("source");
            if (type == null)
                throw new ArgumentNullException("type");

            if ((OnBroadcast != null) && !OnBroadcast(source, type, arguments))
                return true;

            var result = BroadcastToSubscribers(source, type, arguments);
            if (AfterBroadcast != null)
                AfterBroadcast(this, source, type, arguments, result);

            return result;
        }

        public EventThunk GetThunk (object sender, string type) {
            return new EventThunk(this, sender, type);
        }

        public void Dispose () {
            lock (_UnsourcedSubscribers)
                _UnsourcedSubscribers.Clear();
            _SourcedSubscribers = null;
        }
    }
}
