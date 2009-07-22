using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Squared.Util.Event {
    public struct EventFilter {
        public readonly object Source;
        public readonly string Type;

        public EventFilter (object source, string type) {
            if (source == null)
                throw new ArgumentNullException("source");
            if (type == null)
                throw new ArgumentNullException("type");

            Source = source;
            Type = type;
        }

        public override int GetHashCode () {
            return Source.GetHashCode() ^ Type.GetHashCode();
        }
    }

    public class EventFilterComparer : IEqualityComparer<EventFilter> {
        public bool Equals (EventFilter x, EventFilter y) {
            return (x.Source == y.Source) && (x.Type == y.Type);
        }

        public int GetHashCode (EventFilter obj) {
            return obj.GetHashCode();
        }
    }

    public class EventInfo {
        public readonly object Source;
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

        public EventInfo (object source, string type, object arguments) {
            Source = source;
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

    public class EventBus : IDisposable {
        public static readonly object AnySource = "<Any Source>";
        public static readonly string AnyType = "<Any Type>";

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

        public EventSubscription Subscribe<T> (object source, string type, TypedEventSubscriber<T> subscriber) 
            where T : class {
            return Subscribe(source, type, (e) => subscriber(e, e.Arguments as T));
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

            for (int i = 0; i < 4; i++) {
                CreateFilter(                    
                    (i & 1) == 1 ? source : AnySource,
                    (i & 2) == 2 ? type : AnyType,
                    out filter
                );

                if (!_Subscribers.TryGetValue(filter, out subscribers))
                    continue;

                int count = subscribers.Count;
                if (count <= 0)
                    continue;

                if (info == null)
                    info = new EventInfo(source, type, arguments);

                using (var b = BufferPool<EventSubscriber>.Allocate(count)) {
                    var temp = b.Data;
                    subscribers.CopyTo(0, temp, 0, count);

                    for (int j = count - 1; j >= 0; j--) {
                        var subscriber = temp[j];
                        subscriber(info);

                        if (info.IsConsumed)
                            return;
                    }
                }
            }
        }

        public EventThunk GetThunk (object sender, string type) {
            return new EventThunk(this, sender, type);
        }

        public void Dispose () {
            _Subscribers.Clear();
        }
    }
}
