using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Squared.Util.Event {
    public struct EventFilter {
        public readonly WeakReference Source;
        public readonly int SourceHashCode;
        public readonly string Type;
        public readonly int TypeHashCode;

        public EventFilter (object source, string type) {
            if (source == null)
                throw new ArgumentNullException("source");
            if (type == null)
                throw new ArgumentNullException("type");

            Source = new WeakReference(source);
            SourceHashCode = source.GetHashCode();
            Type = type;
            TypeHashCode = type.GetHashCode();
        }

        public override int GetHashCode () {
            return SourceHashCode ^ TypeHashCode;
        }
    }

    public class EventFilterComparer : IEqualityComparer<EventFilter> {
        public bool Equals (EventFilter x, EventFilter y) {
            if ((x.SourceHashCode != y.SourceHashCode) || (x.TypeHashCode != y.TypeHashCode) || (x.Type != y.Type))
                return false;

            var xSource = x.Source.Target;
            var ySource = y.Source.Target;

            return (xSource == ySource);
        }

        public int GetHashCode (EventFilter obj) {
            return obj.SourceHashCode ^ obj.TypeHashCode;
        }
    }

    public class EventInfo {
        public readonly EventBus Bus;
        public readonly object Source;
        public readonly EventCategoryToken Category;
        public readonly string CategoryName;
        public readonly string Type;
        public readonly object Arguments;

        private bool _IsConsumed;

        public bool IsConsumed {
            get {
                return _IsConsumed;
            }
        }

        public void Consume () {
            _IsConsumed = true;
        }

        public EventInfo (EventBus bus, object source, EventCategoryToken categoryToken, string categoryName, string type, object arguments) {
            Bus = bus;
            Source = source;
            Category = categoryToken;
            CategoryName = categoryName;
            Type = type;
            Arguments = arguments;
            _IsConsumed = false;
        }
    }

    public delegate void EventSubscriber (EventInfo e);
    public delegate void TypedEventSubscriber<T> (EventInfo e, T arguments) where T : class;

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

        public void Dispose () {
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

        public void Broadcast (object arguments) {
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

    public class EventBus : IDisposable {
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

        private Dictionary<string, EventCategoryToken> _Categories = 
            new Dictionary<string, EventCategoryToken>();

        private Dictionary<EventFilter, EventSubscriberList> _Subscribers =
            new Dictionary<EventFilter, EventSubscriberList>(new EventFilterComparer());

        public EventBus () {
        }

        private void CreateFilter (object source, string type, out EventFilter filter) {
            filter = new EventFilter(source ?? AnySource, type ?? AnyType);
        }

        public EventSubscription Subscribe (object source, string type, EventSubscriber subscriber) {
            EventFilter filter;
            CreateFilter(source, type, out filter);

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
                var args = e.Arguments as T;
                if (args != null)
                    subscriber(e, args);
            });
        }

        public bool Unsubscribe (object source, string type, EventSubscriber subscriber) {
            EventFilter filter;
            CreateFilter(source, type, out filter);
            return Unsubscribe(ref filter, subscriber);
        }

        public bool Unsubscribe (ref EventFilter filter, EventSubscriber subscriber) {
            EventSubscriberList subscribers;
            if (_Subscribers.TryGetValue(filter, out subscribers))
                return subscribers.Remove(subscriber);

            return false;
        }

        public void Broadcast (object source, string type, object arguments) {
            if (source == null)
                throw new ArgumentNullException("source");
            if (type == null)
                throw new ArgumentNullException("type");

            EventInfo info = null;
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
                    out filter
                );

                if (!_Subscribers.TryGetValue(filter, out subscribers))
                    continue;

                int count = subscribers.Count;
                if (count <= 0)
                    continue;

                if (info == null)
                    info = new EventInfo(this, source, categoryToken, categoryName, type, arguments);

                using (var b = BufferPool<EventSubscriber>.Allocate(count)) {
                    var temp = b.Data;
                    subscribers.CopyTo(temp);

                    for (int j = count - 1; j >= 0; j--) {
                        temp[j](info);

                        if (info.IsConsumed)
                            return;
                    }
                }
            }
        }

        public int Compact () {
            int result = 0;
            var keys = new EventFilter[_Subscribers.Count];
            _Subscribers.Keys.CopyTo(keys, 0);

            foreach (var ef in keys) {
                if (!(ef.Source.IsAlive)) {
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
