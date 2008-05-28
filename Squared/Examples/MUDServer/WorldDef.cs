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
                    new Exit("A wooden door", "StartingForestA")
                }
            };

            new StartingRoomOldMan(_);

            _ = new Location("StartingForestA") {
                Title = "Forest clearing",
                Description = "You are standing in a clearing within a green, verdant forest.\r\n" +
                "In front of you is a small wooden cottage. A door leads inside.\r\n" +
                "An unkempt forest path leads to the south.",
                Exits = {
                    new Exit("Cottage door", "StartingRoom"),
                    new Exit("Forest path", "StartingForestB")
                }
            };

            new ForestBird(_);

            _ = new Location("StartingForestB") {
                Title = "Forest path",
                Description = "You are standing on an overgrown path in the forest.\r\n" +
                "You can hear the sounds of the forest all around you, and the sun is barely visible through the thick foliage.\r\n" +
                "The path winds along to the north and south.",
                Exits = {
                    new Exit("North", "StartingForestA"),
                    new Exit("South", "StartingForestC")
                }
            };

            new ForestBird(_);
            new ForestBird(_);
        }
    }

    public class StartingRoomOldMan : EntityBase {
        List<string> _RememberedPlayers = new List<string>();
        Dictionary<string, Future> _PlayersToNag = new Dictionary<string, Future>();

        public StartingRoomOldMan (Location location)
            : base(location, "An old man") {
            _State = "sitting at the table";
        }

        protected override IEnumerator<object> ThinkTask () {
            while (true) {
                var f = GetNewEvent();
                yield return f;
                var evt = f.Result as Event;

                switch (evt.Type) {
                    case EventType.Enter:
                        if (evt.Sender is Player) {
                            Player p = evt.Sender as Player;
                            if (_RememberedPlayers.Contains(p.Name)) {
                                new EventSay(this, String.Format("Why hello again, {0}. It light'ns mah heart to see yer face.", p)).Send();
                            } else {
                                new EventSay(this, String.Format("Ah don't believe I've seen yer round here 'fore, {0}. What brings ya?", p)).Send();
                                _RememberedPlayers.Add(p.Name);
                            }
                            if (_PlayersToNag.ContainsKey(p.Name)) {
                                _PlayersToNag[p.Name].Dispose();
                                _PlayersToNag.Remove(p.Name);
                            }
                        }
                        break;
                    case EventType.Leave:
                        if (evt.Sender is Player) {
                            Player p = evt.Sender as Player;
                            new EventTell(this, "Always comin' and goin, always leavin' so soon... 'tis a shame.", evt.Sender).Send();
                            var tr = new Start(NagTask(p.Name), TaskExecutionPolicy.RunWhileFutureLives);
                            yield return tr;
                            _PlayersToNag[p.Name] = tr.Future;
                        }
                        break;
                }
            }
        }

        IEnumerator<object> NagTask(string player) {
            yield return new Sleep(45);
            try {
                Player p = World.Players[player];
                new EventTell(this, "Kids 'ese days... 'ever stoppin by to visit an ol man... 'eesh.", p).Send();
            } catch (KeyNotFoundException) {
                System.Diagnostics.Debug.WriteLine("Couldn't nag player because they are no longer in-game");
            }
            _PlayersToNag.Remove(player);
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
                yield return new Sleep((Program.RNG.NextDouble() * 20.0) + 5.0);

                string[] emotes = new string[] {
                    "chirps.",
                    "tweets.",
                    "whistles a happy tune.",
                    "squawks loudly for no particular reason.",
                    "makes a bizarre rhythmic humming noise for exactly 75 milliseconds and then becomes eerily silent."
                };

                new EventEmote(this, emotes[Program.RNG.Next(0, emotes.Length)]).Send();
            }
        }
    }
}
