using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Squared.Util.Event {
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
                    return WeakSource.Target;
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

    public class EventFilterComparer : IEqualityComparer<EventFilter> {
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
        T Arguments { get; }
    }

    // FIXME: Make this a struct?
    public class EventInfo<T> : IEventInfo<T> {
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

        public virtual EventInfo<T> Clone () {
            return new EventInfo<T>(Bus, Source, Category, CategoryName, Type, Arguments);
        }
    }

    public delegate void EventSubscriber (IEventInfo e);
    public delegate void TypedEventSubscriber<in T> (IEventInfo<T> e, T arguments);

    public class EventSubscriberList : List<EventSubscriber> {
    }

    public struct EventSubscription : IDisposable {
        public readonly EventBus EventBus;
        public readonly EventSubscriber EventSubscriber;

        private EventFilter _EventFilter;

        public EventFilter EventFilter {
            get {
                return _EventFilter;
            }
        }

        public EventSubscription (EventBus eventBus, ref EventFilter eventFilter, EventSubscriber subscriber) {
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
            if ((EventBus != null) && (EventSubscriber != null))
                EventBus.Unsubscribe(ref _EventFilter, EventSubscriber);
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

        public EventSubscription Subscribe (EventSubscriber subscriber) {
            return EventBus.Subscribe(Source, Type, subscriber);
        }

        public EventSubscription Subscribe<T> (TypedEventSubscriber<T> subscriber)
            where T : class {
            return EventBus.Subscribe<T>(Source, Type, subscriber);
        }
    }

    public interface IEventSource {
        string CategoryName {
            get;
        }
    }

    public class EventCategoryToken {
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

        private readonly Dictionary<EventFilter, EventSubscriberList> _Subscribers =
            new Dictionary<EventFilter, EventSubscriberList>(new EventFilterComparer());

        /// <summary>
        /// Return false to suppress broadcast of this event
        /// </summary>
        public Func<object, string, object, bool> OnBroadcast;

        private static void CreateFilter (object source, string type, out EventFilter filter, bool weak) {
            filter = new EventFilter(source ?? AnySource, type ?? AnyType, weak);
        }

        public EventSubscription Subscribe (object source, string type, EventSubscriber subscriber) {
            EventFilter filter;
            CreateFilter(source, type, out filter, true);

            EventSubscriberList subscribers;
            if (!_Subscribers.TryGetValue(filter, out subscribers)) {
                subscribers = new EventSubscriberList();
                _Subscribers[filter] = subscribers;
            }

            subscribers.Add(subscriber);

            return new EventSubscription(this, ref filter, subscriber);
        }

        private EventCategoryToken GetCategory (string name) {
            EventCategoryToken result;

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

        public EventSubscription Subscribe<T> (object source, string type, TypedEventSubscriber<T> subscriber) 
            where T : class {
            return Subscribe(source, type, (e) => {
                var info = e as IEventInfo<T>;
                if (info != null)
                    subscriber(info, info.Arguments);
            });
        }

        public bool Unsubscribe (object source, string type, EventSubscriber subscriber) {
            EventFilter filter;
            CreateFilter(source, type, out filter, false);
            return Unsubscribe(ref filter, subscriber);
        }

        public bool Unsubscribe (ref EventFilter filter, EventSubscriber subscriber) {
            EventSubscriberList subscribers;
            if (_Subscribers.TryGetValue(filter, out subscribers))
                return subscribers.Remove(subscriber);

            return false;
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

                if (!_Subscribers.TryGetValue(filter, out subscribers))
                    continue;

                int count = subscribers.Count;
                if (count <= 0)
                    continue;

                if (info == null)
                    info = new EventInfo<T>(this, source, categoryToken, categoryName, type, arguments);

                using (var b = BufferPool<EventSubscriber>.Allocate(count)) {
                    var temp = b.Data;
                    subscribers.CopyTo(temp);

                    for (int j = count - 1; j >= 0; j--) {
                        var ts = temp[j] as TypedEventSubscriber<T>;
                        if (ts != null)
                            ts(info, arguments);
                        else
                            temp[j](info);

                        if (info.IsConsumed)
                            return true;
                    }

                    b.Clear();
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

            return BroadcastToSubscribers(source, type, arguments);
        }

        public bool Broadcast<T> (object source, string type, T arguments) {
            if (source == null)
                throw new ArgumentNullException("source");
            if (type == null)
                throw new ArgumentNullException("type");

            if ((OnBroadcast != null) && !OnBroadcast(source, type, arguments))
                return true;

            return BroadcastToSubscribers(source, type, arguments);
        }

        public int Compact () {
            int result = 0;
            var keys = new EventFilter[_Subscribers.Count];
            _Subscribers.Keys.CopyTo(keys, 0);

            foreach (var ef in keys) {
                if (!(ef.WeakSource.IsAlive)) {
                    _Subscribers.Remove(ef);
                    result += 1;
                }
            }

            return result;
        }

        public EventThunk GetThunk (object sender, string type) {
            return new EventThunk(this, sender, type);
        }

        public void Dispose () {
            _Subscribers.Clear();
        }
    }
}
