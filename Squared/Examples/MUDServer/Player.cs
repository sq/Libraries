using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Squared.Task;

namespace MUDServer {
    public class Player : CombatEntity {
        public TelnetClient Client;
        private bool _LastPrompt;
        private int _NumMessagesSent;

        public Player (TelnetClient client, Location location)
            : base(location, null) {
            Client = client;
            client.RegisterOnDispose(OnDisconnected);
            SetEventHandler(EventType.None, ProcessEvent);
        }

        public override void Dispose () {
            base.Dispose();
            World.Players.Remove(this.Name);
        }

        private void OnDisconnected (Future f) {
            Location = null;
            this.Dispose();
        }

        public void SendMessage (string message, params object[] args) {
            _NumMessagesSent += 1;
            StringBuilder output = new StringBuilder();
            if (_LastPrompt) {
                output.AppendLine();
                _LastPrompt = false;
            }
            output.AppendFormat(message.Replace("{PlayerName}", Name), args);
            output.AppendLine();
            Client.SendText(output.ToString());
        }

        public void SendPrompt () {
            _LastPrompt = true;
            Client.SendText(String.Format("{0}/{1}hp> ", CurrentHealth, MaximumHealth));
        }

        private void PerformLook () {
            if (Location.Description != null)
                SendMessage(Location.Description);
            if (Location.Exits.Count != 0) {
                SendMessage("Exits from this location:");
                for (int i = 0; i < Location.Exits.Count; i++) {
                    SendMessage("{0}: {1}", i, Location.Exits[i].Description);
                }
            }
            foreach (var e in this.Location.Entities) {
                if (e.Value != this)
                    SendMessage("{0} is {1}.", e.Value.Description, e.Value.State ?? "standing nearby");
            }
        }

        public object ProcessInput (string text) {
            string[] words = text.Split(' ');
            if (words.Length < 1)
                return null;
            string firstWord = words[0].ToLower();

            _LastPrompt = false;

            switch (firstWord) {
                case "say":
                    if (words.Length < 2) {
                        SendMessage("What did you want to <say>, exactly?");
                    } else {
                        Event.Send(new { Type = EventType.Say, Sender = this, Text = string.Join(" ", words, 1, words.Length - 1) });
                    }
                    return null;
                case "emote":
                    if (words.Length < 2) {
                        SendMessage("What were you trying to do?");
                    } else {
                        Event.Send(new { Type = EventType.Emote, Sender = this, Text = string.Join(" ", words, 1, words.Length - 1) });
                    }
                    return null;
                case "tell":
                    if (words.Length < 3) {
                        SendMessage("Who did you want to <tell> what?");
                    } else {
                        string name = words[1];
                        IEntity to = Location.ResolveName(name);
                        if (to != null) {
                            Event.Send(new { Type = EventType.Tell, Sender = this, Recipient = to, Text = string.Join(" ", words, 2, words.Length - 1) });
                        } else {
                            SendMessage("Who do you think you're talking to? There's nobody named {0} here.", name);
                        }
                    }
                    return null;
                case "look":
                    PerformLook();
                    return null;
                case "go":
                    if (CurrentHealth <= 0) {
                        SendMessage("You can't do that while dead.");
                        return null;
                    }
                    try {
                        int exitId;
                        string exitText = string.Join(" ", words, 1, words.Length - 1).Trim().ToLower();
                        Action<Exit> go = (exit) => {
                            if (World.Locations.ContainsKey(exit.Target))
                                Location = World.Locations[exit.Target];
                            else {
                                Console.WriteLine("Warning: '{0}' exit '{1}' leads to undefined location '{2}'.", Location.Name, exit.Description, exit.Target);
                                SendMessage("Your attempt to leave via {0} is thwarted by a mysterious force.", exit.Description);
                            }
                        };
                        if (int.TryParse(exitText, out exitId)) {
                            go(Location.Exits[exitId]);
                        } else {
                            foreach (var e in Location.Exits) {
                                if (e.Description.ToLower().Contains(exitText)) {
                                    go(e);
                                    break;
                                }
                            }
                        }
                    } catch {
                        SendMessage("You can't find that exit.");
                    }
                    return null;
                case "kill":
                    if (words.Length < 2) {
                        SendMessage("Who did you want to kill?");
                    } else if (CurrentHealth <= 0) {
                        SendMessage("You can't do that while dead.");
                    } else if (InCombat) {
                        SendMessage("You're already busy fighting!");
                    } else {
                        string name = words[1];
                        IEntity to = Location.ResolveName(name);
                        if (to == this) {
                            SendMessage("You don't really want to kill yourself, you're just looking for attention.");
                        } else if (to != null) {
                            if (to is CombatEntity) {
                                CombatEntity cto = to as CombatEntity;
                                if (cto.InCombat == false) {
                                    this.StartCombat(cto);
                                    cto.StartCombat(this);
                                    Event.Send(new { Type = EventType.CombatStart, Sender = this, Target = cto });
                                } else {
                                    SendMessage("They're already in combat, and you don't want to interfere.");
                                }
                            } else {
                                SendMessage("You don't think that's such a great idea.");
                            }
                        } else {
                            SendMessage("Who are you trying to kill, exactly? There's nobody named {0} here.", name);
                        }
                    }
                    return null;
                case "help":
                    SendMessage("You can <say> things to those nearby, if you feel like chatting.");
                    SendMessage("You can also <tell> somebody things if you wish to speak privately.");
                    SendMessage("You can also <emote> within sight of others.");
                    SendMessage("If you're feeling lost, try taking a <look> around.");
                    SendMessage("If you wish to <go> out an exit, simply speak its name or number.");
                    SendMessage("Looking to make trouble? Try to <kill> someone!");
                    return null;
                default:
                    SendMessage("Hmm... that doesn't make any sense. Do you need some <help>?");
                    return null;
            }
        }

        private IEnumerator<object> ProcessEvent (EventType type, object evt) {
            IEntity sender = Event.GetProp<IEntity>("Sender", evt);
            IEntity recipient = Event.GetProp<IEntity>("Recipient", evt);
            IEntity target = Event.GetProp<IEntity>("Target", evt);
            string text = Event.GetProp<string>("Text", evt);

            int prevNumSent = _NumMessagesSent;

            switch (type) {
                case EventType.Enter:
                    if (sender == this) {
                        _LastPrompt = false;
                        Client.ClearScreen();
                        SendMessage("You enter {0}.", Location.Title ?? Location.Name);
                        PerformLook();
                    } else {
                        SendMessage("{0} enters the room.", sender);
                    }
                    break;
                case EventType.Leave:
                    if (sender != this)
                        SendMessage("{0} leaves the room.", sender);
                    break;
                case EventType.Say:
                    SendMessage("{0} says, \"{1}\"", sender, text);
                    break;
                case EventType.Tell:
                    if (sender == this) {
                        SendMessage("You tell {0}, \"{1}\"", recipient, text);
                    } else {
                        SendMessage("{0} tells you, \"{1}\"", sender, text);
                    }
                    break;
                case EventType.Emote:
                    SendMessage("{0} {1}", sender, text);
                    break;
                case EventType.Death:
                    if (sender == this) {
                        SendMessage("You collapse onto the floor and release your last breath.");
                    } else {
                        SendMessage("{0} collapses onto the floor, releasing their last breath!", sender);
                    }
                    break;
                case EventType.CombatStart:
                    if (sender == this) {
                        SendMessage("You lunge at {0} and attack!", target);
                    } else if (target == this) {
                        SendMessage("{0} lunges at you, weapon in hand!", sender);
                    } else {
                        SendMessage("{0} begins to attack {1}!", sender, target);
                    }
                    break;
                case EventType.CombatHit: {
                        string weaponName = Event.GetProp<string>("WeaponName", evt);
                        int damage = Event.GetProp<int>("Damage", evt);
                        if (sender == this) {
                            SendMessage("You hit {0} with your {1} and deal {2} damage!", target, weaponName, damage);
                        } else {
                            SendMessage("{0} hits you with their {1} for {2} damage.", sender, weaponName, damage);
                        }
                    }
                    break;
                case EventType.CombatMiss: {
                        string weaponName = Event.GetProp<string>("WeaponName", evt);
                        if (sender == this) {
                            SendMessage("You miss {0} with your {1}.", target, weaponName);
                        } else if (target == this) {
                            SendMessage("{0} misses you with their {1}.", sender, weaponName);
                        }
                    }
                    break;
            }

            if (_NumMessagesSent > prevNumSent)
                SendPrompt();

            return null;
        }

        protected override IEnumerator<object> ThinkTask () {
            while (Name == null) {
                Client.ClearScreen();
                Client.SendText("Greetings, traveller. What might your name be?\r\n");
                Future f = Client.ReadLineText();
                yield return f;
                try {
                    Name = (f.Result as string).Trim().Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)[0];
                } catch {
                }
            }

            World.Players[Name] = this;

            while (true) {
                Future newInputLine = Client.ReadLineText();
                yield return newInputLine;
                string line = newInputLine.Result as string;

                if (line != null) {
                    object next = ProcessInput(line);
                    if (next != null)
                        yield return next;
                    SendPrompt();
                }
            }
        }
    }
}
