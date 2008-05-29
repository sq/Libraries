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

            AddEventHandler(EventType.CombatHit, OnEventCombatHit);
            AddEventHandler(EventType.CombatMiss, OnEventCombatMiss);
            AddEventHandler(EventType.CombatStart, OnEventCombatStart);
            AddEventHandler(EventType.Death, OnEventDeath);
            AddEventHandler(EventType.Emote, OnEventEmote);
            AddEventHandler(EventType.Enter, OnEventEnter);
            AddEventHandler(EventType.Leave, OnEventLeave);
            AddEventHandler(EventType.Say, OnEventSay);
            AddEventHandler(EventType.Tell, OnEventTell);
        }

        public override void Dispose () {
            base.Dispose();
            World.Players.Remove(this.Name);
            Console.WriteLine("{0} has left the world", Name);
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
                        SendPrompt();
                    } else {
                        Event.Send(new { Type = EventType.Say, Sender = this, Text = string.Join(" ", words, 1, words.Length - 1) });
                    }
                    return null;
                case "emote":
                    if (words.Length < 2) {
                        SendMessage("What were you trying to do?");
                        SendPrompt();
                    } else {
                        Event.Send(new { Type = EventType.Emote, Sender = this, Text = string.Join(" ", words, 1, words.Length - 1) });
                    }
                    return null;
                case "tell":
                    if (words.Length < 3) {
                        SendMessage("Who did you want to <tell> what?");
                        SendPrompt();
                    } else {
                        string name = words[1];
                        IEntity to = Location.ResolveName(name);
                        if (to != null) {
                            Event.Send(new { Type = EventType.Tell, Sender = this, Recipient = to, Text = string.Join(" ", words, 2, words.Length - 1) });
                        } else {
                            SendMessage("Who do you think you're talking to? There's nobody named {0} here.", name);
                            SendPrompt();
                        }
                    }
                    return null;
                case "look":
                    PerformLook();
                    SendPrompt();
                    return null;
                case "go":
                    if (CurrentHealth <= 0) {
                        SendMessage("You can't do that while dead.");
                        SendPrompt();
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
                                SendPrompt();
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
                        SendPrompt();
                    }
                    return null;
                case "kill":
                    if (words.Length < 2) {
                        SendMessage("Who did you want to kill?");
                        SendPrompt();
                    } else if (CurrentHealth <= 0) {
                        SendMessage("You can't do that while dead.");
                        SendPrompt();
                    } else if (InCombat) {
                        SendMessage("You're already busy fighting!");
                        SendPrompt();
                    } else {
                        string name = words[1];
                        IEntity to = Location.ResolveName(name);
                        if (to == this) {
                            SendMessage("You don't really want to kill yourself, you're just looking for attention.");
                            SendPrompt();
                        } else if (to != null) {
                            if (to is CombatEntity) {
                                CombatEntity cto = to as CombatEntity;
                                if (cto.InCombat == false) {
                                    this.StartCombat(cto);
                                    cto.StartCombat(this);
                                    Event.Send(new { Type = EventType.CombatStart, Sender = this, Target = cto });
                                } else {
                                    SendMessage("They're already in combat, and you don't want to interfere.");
                                    SendPrompt();
                                }
                            } else {
                                SendMessage("You don't think that's such a great idea.");
                                SendPrompt();
                            }
                        } else {
                            SendMessage("Who are you trying to kill, exactly? There's nobody named {0} here.", name);
                            SendPrompt();
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
                    SendPrompt();
                    return null;
                default:
                    SendMessage("Hmm... that doesn't make any sense. Do you need some <help>?");
                    SendPrompt();
                    return null;
            }
        }

        private IEnumerator<object> PromptHelper (IEnumerator<object> task, OnComplete onComplete) {
            Start ts = new Start(task, TaskExecutionPolicy.RunAsBackgroundTask);
            yield return ts;
            ts.Future.RegisterOnComplete(onComplete);
        }

        // Ensure that we send a new prompt to the user after dispatching any events that send output
        protected override IEnumerator<object> DispatchEvent (EventType type, object evt) {
            int prevMessages = _NumMessagesSent;
            OnComplete oc = (f, r, e) => {
                if (_NumMessagesSent > prevMessages)
                    SendPrompt();
            };

            IEnumerator<object> result = base.DispatchEvent(type, evt);

            if (result != null) {
                return PromptHelper(result, oc);
            } else {
                oc(null, null, null);
                return null;
            }
        }

        private IEnumerator<object> OnEventEnter (EventType type, object evt) {
            IEntity sender = Event.GetProp<IEntity>("Sender", evt);

            if (sender == this) {
                _LastPrompt = false;
                Client.ClearScreen();
                SendMessage("You enter {0}.", Location.Title ?? Location.Name);
                PerformLook();
            } else {
                SendMessage("{0} enters the room.", sender);
            }

            return null;
        }

        private IEnumerator<object> OnEventLeave (EventType type, object evt) {
            IEntity sender = Event.GetProp<IEntity>("Sender", evt);

            if (sender != this)
                SendMessage("{0} leaves the room.", sender);

            return null;
        }

        private IEnumerator<object> OnEventSay (EventType type, object evt) {
            IEntity sender = Event.GetProp<IEntity>("Sender", evt);
            string text = Event.GetProp<string>("Text", evt);

            if (sender == this) {
                SendMessage("You say, \"{0}\"", text);
            } else {
                SendMessage("{0} says, \"{1}\"", sender, text);
            }

            return null;
        }

        private IEnumerator<object> OnEventEmote (EventType type, object evt) {
            SendMessage(Event.Format("{Sender} {Text}", this, evt));
            return null;
        }

        private IEnumerator<object> OnEventDeath (EventType type, object evt) {
            IEntity sender = Event.GetProp<IEntity>("Sender", evt);

            if (sender == this) {
                SendMessage("You collapse onto the floor and release your last breath.");
            } else {
                SendMessage("{0} collapses onto the floor, releasing their last breath!", sender);
            }

            return null;
        }

        private IEnumerator<object> OnEventTell (EventType type, object evt) {
            IEntity sender = Event.GetProp<IEntity>("Sender", evt);
            IEntity recipient = Event.GetProp<IEntity>("Recipient", evt);
            string text = Event.GetProp<string>("Text", evt);

            if (sender == this) {
                SendMessage("You tell {0}, \"{1}\"", recipient, text);
            } else {
                SendMessage("{0} tells you, \"{1}\"", sender, text);
            }

            return null;
        }

        private IEnumerator<object> OnEventCombatStart (EventType type, object evt) {
            IEntity sender = Event.GetProp<IEntity>("Sender", evt);
            IEntity target = Event.GetProp<IEntity>("Target", evt);

            if (sender == this) {
                SendMessage("You lunge at {0} and attack!", target);
            } else {
                SendMessage(Event.Format("{Sender} charges at {Target} and attacks!", this, evt));
            }

            return null;
        }

        private IEnumerator<object> OnEventCombatHit (EventType type, object evt) {
            IEntity sender = Event.GetProp<IEntity>("Sender", evt);

            if (sender == this) {
                SendMessage(Event.Format("You hit {Target} with your {WeaponName} for {Damage} damage.", this, evt));
            } else {
                SendMessage(Event.Format("{Sender} hits {Target} with their {WeaponName} for {Damage} damage.", this, evt));
            }

            return null;
        }

        private IEnumerator<object> OnEventCombatMiss (EventType type, object evt) {
            IEntity sender = Event.GetProp<IEntity>("Sender", evt);
            IEntity target = Event.GetProp<IEntity>("Target", evt);
            string weaponName = Event.GetProp<string>("WeaponName", evt);

            if (sender == this) {
                SendMessage("You miss {0} with your {1}.", target, weaponName);
            } else {
                SendMessage(Event.Format("{Sender} misses {Target} with their {WeaponName}.", this, evt));
            }

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

            Console.WriteLine("{0} has entered the world", Name);
            World.Players[Name] = this;

            while (true) {
                Future newInputLine = Client.ReadLineText();
                yield return newInputLine;
                string line = newInputLine.Result as string;

                if (line != null) {
                    object next = ProcessInput(line);
                    if (next != null)
                        yield return next;
                }
            }
        }
    }
}
