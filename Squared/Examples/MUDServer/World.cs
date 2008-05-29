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

        public Exit (string description, string target) {
            Description = description;
            Target = target;
        }
    }

    public class Location {
        public string Title;
        public string Description;
        public Dictionary<string, IEntity> Entities = new Dictionary<string, IEntity>();
        public List<Exit> Exits = new List<Exit>();

        private string _Name;

        public string Name {
            get {
                return _Name;
            }
        }

        public IEntity ResolveName (string name) {
            if (Entities.ContainsKey(name))
                return Entities[name];
            else if (World.Players.ContainsKey(name))
                return World.Players[name];
            else
                return null;
        }

        public void Enter (IEntity entity) {
            Entities[entity.Name] = entity;
            Event.Send(new { Type = EventType.Enter, Sender = entity });
        }

        public void Exit (IEntity entity) {
            Event.Send(new { Type = EventType.Leave, Sender = entity });
            Entities.Remove(entity.Name);
        }

        public Location (string name) {
            _Name = name;
            World.Locations.Add(name, this);
        }
    }
}
