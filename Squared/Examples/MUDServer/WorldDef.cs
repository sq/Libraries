using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Squared.Task;

namespace MUDServer {
    public static partial class World {
        public static void Create () {
            Location _;
            
            _ = PlayerStartLocation = new Location("StartingRoom") {
                Title = "A small cottage",
                Description = "You find yourself inside a small, single-room forest cottage.\r\n" +
                "A table sits in the center of the room surrounded by two chairs.\r\n" +
                "There is a wooden door to your south that leads outside.",
                Exits = {
                    new Exit("Door", "A wooden door", "StartingForestA")
                }
            };

            new StartingRoomOldMan(_);
            new StartingRoomCat(_);

            _ = new Location("StartingForestA") {
                Title = "Forest clearing",
                Description = "You are standing in a clearing within a green, verdant forest.\r\n" +
                "In front of you is a small wooden cottage. A door leads inside.\r\n" +
                "An unkempt forest path leads to the south.",
                Exits = {
                    new Exit("Door", "Cottage door", "StartingRoom"),
                    new Exit("Path", "Forest path", "StartingForestB")
                }
            };

            new ForestBird(_);

            _ = new Location("StartingForestB") {
                Title = "Forest path",
                Description = "You are standing on an overgrown path in the forest.\r\n" +
                "You can hear the sounds of the forest all around you, and the sun is barely visible through the thick foliage.\r\n" +
                "The path winds along to the north and south.",
                Exits = {
                    new Exit("North", "North", "StartingForestA"),
                    new Exit("South", "South", "StartingForestC")
                }
            };

            new ForestBird(_);
            new ForestBird(_);

            _ = new Location("StartingForestC") {
                Title = "Forest path",
                Description = "You are standing on an overgrown path in the forest.\r\n" +
                "The wind blows quietly through the area, rustling the leaves of the trees.\r\n" +
                "The path winds along to the north and south.",
                Exits = {
                    new Exit("North", "North", "StartingForestB"),
                    new Exit("South", "South", "StartingForestD2")
                }
            };

            new ForestBird(_);

            _ = new Location("StartingForestD2") {
                Title = "Forest entrance",
                Description = "You are standing at the entrance to a small forest.\r\n" +
                "The forest opens onto a small, overgrown path heading north. The grassline ends abruptly to your south.\r\n" +
                "The path leads north into the forest. To the south you see a small path winding through sharp crags and worn stone. The treeline runs to your east and west.",
                Exits = {
                    new Exit("North", "North", "StartingForestC"),
                    new Exit("West", "West", "StartingForestD1"),
                    new Exit("East", "East", "StartingForestD3"),
                    new Exit("South", "Winding path", "StonePathA")
                }
            };

            _ = new Location("StartingForestD1") {
                Title = "Forest outskirts",
                Description = "You are standing at the edge of a small forest.\r\n" +
                "You see thick, overgrown grass and trees to the north. The way south and west is blocked by rocky, impassable terrain.",
                Exits = {
                    new Exit("East", "East", "StartingForestD2"),
                }
            };

            _ = new Location("StartingForestD3") {
                Title = "Forest outskirts",
                Description = "You are standing at the edge of a small forest.\r\n" +
                "You see thick, overgrown grass and trees to the north. The way south and east is blocked by rocky, impassable terrain.",
                Exits = {
                    new Exit("West", "West", "StartingForestD2"),
                }
            };

            _ = new Location("StonePathA") {
                Title = "Winding path entrance",
                Description = "You are standing at the entrance to a small, winding path.\r\n" +
                "The path carves its way through solid stone, worn and aged by the passage of time.\r\n" +
                "To your north you can see trees in the distance. The path continues southward.",
                Exits = {
                    new Exit("North", "Forest entrance", "StartingForestD2"),
                    new Exit("South", "South", "StonePathB")
                }
            };

            _ = new Location("StonePathB") {
                Title = "Winding path",
                Description = "You stand upon a small, winding path.\r\n" +
                "The stone here is cracked and weary, and shows signs of having once been home to a flowing stream.\r\n" +
                "The path continues to the north and south.",
                Exits = {
                    new Exit("North", "North", "StonePathA"),
                    new Exit("South", "South", "StonePathC")
                }
            };

            _ = new Location("StonePathC") {
                Title = "Winding path",
                Description = "You stand upon a winding path, at the mouth of a cave.\r\n" +
                "The cave in front of you is small and foreboding. Faint traces of sunlight illuminate the ground within.\r\n" +
                "The path continues to the north and south. The cave beckons.",
                Exits = {
                    new Exit("North", "North", "StonePathB"),
                    new Exit("South", "South", "StonePathD"),
                    new Exit("Cave", "Cave entrance", "CaveA")
                }
            };
        }
    }

    public class StartingRoomOldMan : EntityBase {
        List<string> _RememberedPlayers = new List<string>();
        Dictionary<string, Future> _PlayersToNag = new Dictionary<string, Future>();

        public StartingRoomOldMan (Location location)
            : base(location, "An old man") {
            _State = "sitting at the table";
            AddEventHandler(EventType.Enter, OnEventEnter);
            AddEventHandler(EventType.Leave, OnEventLeave);
        }

        private IEnumerator<object> OnEventEnter (EventType type, object evt) {
            var sender = Event.GetProp<IEntity>("Sender", evt) as Player;
            if (sender == null)
                yield break;

            string messageText;

            if (_RememberedPlayers.Contains(sender.Name)) {
                messageText = String.Format("Why hello again, {0}. It light'ns mah heart to see yer face.", sender);
            } else {
                messageText = String.Format("Ah don't believe I've seen yer round here 'fore, {0}. What brings ya?", sender);
                _RememberedPlayers.Add(sender.Name);
            }
            Event.Send(new { Type = EventType.Say, Sender = this, Text = messageText });

            if (_PlayersToNag.ContainsKey(sender.Name)) {
                _PlayersToNag[sender.Name].Dispose();
                _PlayersToNag.Remove(sender.Name);
            }
        }

        private IEnumerator<object> OnEventLeave (EventType type, object evt) {
            var sender = Event.GetProp<IEntity>("Sender", evt) as Player;
            if (sender == null)
                yield break;

            string messageText = "Always comin' and goin, always leavin' so soon... 'tis a shame.";

            Event.Send(new { Type = EventType.Tell, Sender = this, Recipient = sender, Text = messageText });

            var tr = new Start(NagTask(sender.Name), TaskExecutionPolicy.RunWhileFutureLives);
            yield return tr;
            _PlayersToNag[sender.Name] = tr.Future;
        }

        IEnumerator<object> NagTask(string player) {
            yield return new Sleep(45);
            IEntity ent = Location.ResolveName(player);
            if (ent != null) {
                string messageText = "Kids 'ese days... 'ever stoppin by to visit an ol man... 'eesh.";
                Event.Send(new { Type = EventType.Tell, Sender = this, Recipient = ent, Text = messageText });
            }
            _PlayersToNag.Remove(player);
        }
    }

    public class StartingRoomCat : EntityBase {
        public StartingRoomCat (Location location)
            : base(location, GetDefaultName()) {
            _Description = "A cat";
            _State = "curled up on the ground, sleeping";

            AddEventHandler(EventType.Emote, OnEventEmote);
            AddEventHandler(EventType.WorldConstructed, OnEventWorldConstructed);
        }

        private IEnumerator<object> OnEventWorldConstructed (EventType type, object evt) {
            foreach (Location l in this.Location.GetExits()) {
                l.AddEventListener(this);
            }

            return null;
        }

        private IEnumerator<object> OnEventEmote (EventType type, object evt) {
            IEntity sender = Event.GetProp<IEntity>("Sender", evt);
            if (sender == null)
                return null;

            if (sender.Description.ToLower().Contains("bird"))
                Event.Send(new { Type = EventType.Emote, Sender = this, Text = "'s ears perk up at the sound of a bird outside." });

            return null;
        }
    }

    public class ForestBird : EntityBase {
        public ForestBird (Location location)
            : base(location, GetDefaultName()) {
            _Description = "A bird";

            string[] states = new string[] {
                "perched on a nearby branch",
                "flying around nearby",
                "pecking around for worms"
            };

            _State = states[Program.RNG.Next(0, states.Length)];
        }

        protected override IEnumerator<object> ThinkTask () {
            while (true) {
                yield return new Sleep((Program.RNG.NextDouble() * 30.0) + 10);

                string[] emotes = new string[] {
                    "chirps.",
                    "tweets.",
                    "whistles a happy tune.",
                    "squawks loudly for no particular reason.",
                    "makes a bizarre rhythmic humming noise for exactly 75 milliseconds and then becomes eerily silent."
                };

                string emoteText = emotes[Program.RNG.Next(0, emotes.Length)];
                Event.Send(new { Type = EventType.Emote, Sender = this, Text = emoteText });
            }
        }
    }
}
