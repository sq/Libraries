using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Text.RegularExpressions;

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
        /// <summary>
        /// WorldConstructed { }
        /// </summary>
        WorldConstructed
    }

    public static class Event {
        private static Regex EventFormatRegex = new Regex(
            @"\{(?'id'[A-Za-z_]([A-Za-z0-9_]*))\}", 
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture);

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
                sender.Location.NotifyEvent(sender, type, evt);
            }
        }

        public static void Broadcast (object evt) {
            Type t = evt.GetType();
            EventType type = GetProp<EventType>("Type", evt, t);
            foreach (Location l in World.Locations.Values) {
                l.NotifyEvent(null, type, evt);
            }
        }

        public static string Format (string formatString, IEntity you, object evt, params object[] extraArguments) {
            Type t = evt.GetType();
            MatchEvaluator evaluator = (m) => {
                string propertyName = m.Groups["id"].Value;
                PropertyInfo prop = t.GetProperty(propertyName);
                if (prop != null) {
                    object value = prop.GetValue(evt, null);
                    if (value == you) {
                        return "You";
                    } else {
                        return value.ToString();
                    }
                } else {
                    return "{{" + propertyName + "}}";
                }
            };
            formatString = EventFormatRegex.Replace(formatString, evaluator);
            return String.Format(formatString, extraArguments);
        }
    }
}
