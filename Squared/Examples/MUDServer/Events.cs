using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;

namespace MUDServer {
    public enum EventType {
        None,
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
        Leave,
        /// <summary>
        /// Death { entity }
        /// </summary>
        Death,
        /// <summary>
        /// CombatStart { from, to }
        /// </summary>
        CombatStart,
        /// <summary>
        /// CombatHit { from, weaponname, damage, to }
        /// </summary>
        CombatHit,
        /// <summary>
        /// CombatMiss { from, weaponname, to }
        /// </summary>
        CombatMiss,
    }

    public static class Event {
        public static T GetProp<T> (string propertyName, object obj) {
            return GetProp<T>(propertyName, obj, obj.GetType());
        }

        public static T GetProp<T> (string propertyName, object obj, Type type) {
            PropertyInfo prop = type.GetProperty(propertyName);
            if (prop != null) {
                if (typeof(T).IsAssignableFrom(prop.PropertyType))
                    return (T)prop.GetValue(obj, null);
                else
                    return default(T);
            } else {
                return default(T);
            }
        }

        public static void Send (object evt) {
            Type t = evt.GetType();
            EventType type = GetProp<EventType>("Type", evt, t);
            IEntity sender = GetProp<IEntity>("Sender", evt, t);
            IEntity recipient = GetProp<IEntity>("Recipient", evt, t);
            sender.NotifyEvent(type, evt);
            if (recipient != null) {
                if (recipient != sender)
                    recipient.NotifyEvent(type, evt);
            } else {
                foreach (var e in sender.Location.Entities.Values)
                    if (e != sender)
                        e.NotifyEvent(type, evt);
            }
        }
    }
}
