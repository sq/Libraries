using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MUDServer {
    public enum EventType {
        /// <summary>
        /// Enter { entity }
        /// </summary>
        Enter,
        /// <summary>
        /// Say { from, message }
        /// </summary>
        Say,
        /// <summary>
        /// Tell { from, message, to }
        /// </summary>
        Tell,
        /// <summary>
        /// Emote { from, action }
        /// </summary>
        Emote,
        /// <summary>
        /// Leave { entity }
        /// </summary>
        Leave
    }

    public class Event {
        public EventType Type;
        public object[] Arguments;

        public Event (EventType type, params object[] arguments) {
            Type = type;
            Arguments = arguments;
        }

        protected Event (EventType type, int argumentCount) {
            Type = type;
            Arguments = new object[argumentCount + 1];
        }

        protected void SetValues (params object[] values) {
            Array.Copy(values, Arguments, values.Length);
        }

        public IEntity Sender {
            get {
                return Arguments[0] as IEntity;
            }
        }

        public object this[int index] {
            get {
                return Arguments[index + 1];
            }
        }

        public virtual void Send () {
            IEntity sender = Sender;
            sender.NotifyEvent(this);
            foreach (var e in Sender.Location.Entities.Values)
                if (e != sender)
                    e.NotifyEvent(this);
        }
    }

    public class TargetedEvent : Event {
        protected TargetedEvent (EventType type, int argumentCount)
            : base(type, argumentCount + 1) {
        }

        public object Recipient {
            get {
                return Arguments[Arguments.Length - 1];
            }
        }

        public override void Send () {
            Sender.NotifyEvent(this);
            if (Recipient == Sender)
                return;
            else if (Recipient is IEntity)
                (Recipient as IEntity).NotifyEvent(this);
            else if (Recipient is string)
                Sender.Location.Entities[Recipient as string].NotifyEvent(this);
            else
                throw new ArgumentException("Recipient must br a string or entity reference", "recipient");
        }
    }

    public class EventSay : Event {
        public EventSay (IEntity sender, string text)
            : base(EventType.Say, 1) {
            SetValues(sender, text);
        }
    }

    public class EventTell : TargetedEvent {
        public EventTell (IEntity from, string text, IEntity to)
            : base(EventType.Tell, 1) {
            SetValues(from, text, to);
        }
    }

    public class EventEnter : Event {
        public EventEnter (IEntity sender)
            : base(EventType.Enter, 0) {
            SetValues(sender);
        }
    }

    public class EventLeave : Event {
        public EventLeave (IEntity sender)
            : base(EventType.Leave, 0) {
            SetValues(sender);
        }
    }

    public class EventEmote : Event {
        public EventEmote (IEntity sender, string text)
            : base(EventType.Emote, 1) {
            SetValues(sender, text);
        }
    }
}
