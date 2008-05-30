using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Collections.Specialized;

namespace MUDServer {
    public static partial class World {
        public static Dictionary<string, Player> Players = new Dictionary<string, Player>();
        public static Dictionary<string, Location> Locations = new Dictionary<string, Location>();
        public static Location PlayerStartLocation;
    }

    public struct Exit {
        public string Description;
        public string Target;
        public string Name;

        public Exit (string name, string description, string target) {
            Description = description;
            Target = target;
            Name = name;
            foreach (char c in Name.ToLower())
                if (c < 'a' || c > 'z')
                    throw new InvalidOperationException("Exit created with name that was not alphabetical.");
        }
    }

    public class Location {
        public string Title;
        public string Description;
        public Dictionary<string, IEntity> Entities = new Dictionary<string, IEntity>();
        public List<Exit> Exits = new List<Exit>();

        private List<IEntity> _EventListeners = new List<IEntity>();
        private string _Name;

        public string Name {
            get {
                return _Name;
            }
        }

        public IEnumerable<Location> GetExits () {
            foreach (Exit exit in Exits) {
                string key = exit.Target.ToLower();
                if (World.Locations.ContainsKey(key))
                    yield return World.Locations[key];
            }
        }

        public IEntity ResolveName (string name) {
            if (name == null || name.Length == 0)
                return null;
            string lowername = name.ToLower();
            if (Entities.ContainsKey(lowername))
                return Entities[lowername];
            else if (World.Players.ContainsKey(lowername))
                return World.Players[lowername];
            else
                return null;
        }

        public void AddEventListener (IEntity listener) {
            _EventListeners.Add(listener);
        }

        public void RemoveEventListener (IEntity listener) {
            _EventListeners.Remove(listener);
        }

        public void NotifyEvent (IEntity sender, EventType type, object evt) {
            foreach (IEntity entity in _EventListeners) {
                entity.NotifyEvent(type, evt);
            }

            foreach (IEntity entity in Entities.Values) {
                if (entity != sender)
                    entity.NotifyEvent(type, evt);
            }
        }

        public void Enter (IEntity entity) {
            Entities[entity.Name.ToLower()] = entity;
            Event.Send(new { Type = EventType.Enter, Sender = entity });
        }

        public void Exit (IEntity entity) {
            Event.Send(new { Type = EventType.Leave, Sender = entity });
            Entities.Remove(entity.Name.ToLower());
        }

        public Location (string name) {
            _Name = name;
            World.Locations.Add(name.ToLower(), this);
        }
    }
}
