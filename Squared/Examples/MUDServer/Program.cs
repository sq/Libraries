using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Squared.Task;

namespace MUDServer {
    public interface IEntity {
        string Name {
            get;
        }

        string State {
            get;
        }

        string Description {
            get;
        }

        Location Location {
            get;
        }

        void NotifyEvent (Event evt);
    }


    public class EntityBase : IEntity, IDisposable {
        private static int _EntityCount;

        private Location _Location;
        private string _Name = null;
        private BlockingQueue<Event> _EventQueue = new BlockingQueue<Event>();
        private Future _ThinkTask;
        protected string _State = null;
        protected string _Description = null;

        public override string ToString () {
            return Description;
        }

        public string Description {
            get {
                return _Description ?? _Name;
            }
        }

        public virtual string State {
            get {
                return _State;
            }
        }

        public Location Location {
            get {
                return _Location;
            }
            set {
                if (_Name != null && _Location != null) {
                    _Location.Exit(this);
                }

                _Location = value;

                if (_Name != null && _Location != null) {
                    _Location.Enter(this);
                }
            }
        }

        public string Name {
            get {
                return _Name;
            }
            set {
                if (_Name == null) {
                    _Name = value;
                    _Location.Enter(this);
                } else {
                    throw new InvalidOperationException("An entity's name cannot be changed once it has been set");
                }
            }
        }

        protected virtual bool ShouldFilterNewEvent (Event evt) {
            return false;
        }

        public void NotifyEvent (Event evt) {
            if (!ShouldFilterNewEvent(evt))
                _EventQueue.Enqueue(evt);
        }

        protected Future GetNewEvent () {
            return _EventQueue.Dequeue();
        }

        protected static string GetDefaultName () {
            return String.Format("Entity{0}", _EntityCount++);
        }

        public EntityBase (Location location, string name) {
            if (location == null)
                throw new ArgumentNullException("location");
            _Name = name;
            Location = location;
            _ThinkTask = Program.Scheduler.Start(ThinkTask(), TaskExecutionPolicy.RunAsBackgroundTask);
        }

        protected virtual IEnumerator<object> ThinkTask () {
            yield return null;
        }

        public virtual void Dispose () {
            if (_ThinkTask != null) {
                _ThinkTask.Dispose();
                _ThinkTask = null;
            }
        }
    }

    public class CombatEntity : EntityBase {
        private bool _InCombat;
        private Future _CombatTask;
        private CombatEntity _CombatTarget = null;
        private double CombatPeriod;
        private int _CurrentHealth;
        private int _MaximumHealth;

        public bool InCombat {
            get {
                return _InCombat;
            }
        }

        public int CurrentHealth {
            get {
                return _CurrentHealth;
            }
        }

        public int MaximumHealth {
            get {
                return _MaximumHealth;
            }
        }

        public override string State {
            get {
                if (InCombat)
                    return String.Format("engaged in combat with {0}", _CombatTarget.Name);
                else if (_CurrentHealth <= 0)
                    return String.Format("lying on the ground, dead");
                else
                    return _State;
            }
        }

        public CombatEntity (Location location, string name)
            : base(location, name) {
            _InCombat = false;
            CombatPeriod = Program.RNG.NextDouble() * 4.0;
            _MaximumHealth = 20 + Program.RNG.Next(50);
            _CurrentHealth = _MaximumHealth;
        }

        public void Hurt (int damage) {
            _CurrentHealth -= damage;
            if (_CurrentHealth <= 0) {
                new EventDeath(this).Send();
                _CurrentHealth = 0;
                _CombatTarget.EndCombat();
                EndCombat();
            }
        }

        public void StartCombat (CombatEntity target) {
            if (_InCombat)
                throw new InvalidOperationException("Attempted to start combat while already in combat.");

            _CombatTarget = target;
            _InCombat = true;
            _CombatTask = Program.Scheduler.Start(CombatTask(), TaskExecutionPolicy.RunAsBackgroundTask);
        }

        public void EndCombat () {
            if (!_InCombat)
                throw new InvalidOperationException("Attempted to end combat while not in combat.");

            _CombatTarget = null;
            _InCombat = false;
            _CombatTask.Dispose();
        }

        public virtual IEnumerator<object> CombatTask () {
            while (true) {
                yield return new Sleep(CombatPeriod);
                // Hitrate = 2/3
                // Damage = 2d6
                int damage = Program.RNG.Next(1, 6-1) + Program.RNG.Next(1, 6-1);
                if (Program.RNG.Next(0, 3) <= 1) {
                    new EventCombatHit(this, _CombatTarget, "Longsword", damage).Send();
                    _CombatTarget.Hurt(damage);
                }
                else {
                    new EventCombatMiss(this, _CombatTarget, "Longsword").Send();
                }
            }
        }

    }

    public class Player : CombatEntity {
        public TelnetClient Client;
        private bool _LastPrompt;

        public Player (TelnetClient client, Location location)
            : base(location, null) {
            Client = client;
            client.RegisterOnDispose(OnDisconnected);
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
            StringBuilder output = new StringBuilder();
            if (_LastPrompt) {
                output.AppendLine();
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
            
            switch (firstWord) {
                case "say":
                    if (words.Length < 2) {
                        SendMessage("What did you want to <say>, exactly?");
                    } else {
                        new EventSay(this, string.Join(" ", words, 1, words.Length - 1)).Send();
                    }
                    return null;
                case "emote":
                    if (words.Length < 2) {
                        SendMessage("What were you trying to do?");
                    } else {
                        new EventEmote(this, string.Join(" ", words, 1, words.Length - 1)).Send();
                    }
                    return null;
                case "tell":
                    if (words.Length < 3) {
                        SendMessage("Who did you want to <tell> what?");
                    } else {
                        string name = words[1];
                        IEntity to = Location.ResolveName(name);
                        if (to != null) {
                            new EventTell(this, string.Join(" ", words, 2, words.Length - 2), to).Send();
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
                    }
                    else if (CurrentHealth <= 0) {
                        SendMessage("You can't do that while dead.");
                    }
                    else if (InCombat) {
                        SendMessage("You're already busy fighting!");
                    }
                    else {
                        string name = words[1];
                        IEntity to = Location.ResolveName(name);
                        if (to == this) {
                            SendMessage("You don't really want to kill yourself, you're just looking for attention.");
                        }
                        else if (to != null) {
                            if (to is CombatEntity) {
                                CombatEntity cto = to as CombatEntity;
                                if (cto.InCombat == false) {
                                    this.StartCombat(cto);
                                    cto.StartCombat(this);
                                    new EventCombatStart(this, to).Send();
                                }
                                else {
                                    SendMessage("They're already in combat, and you don't want to interfere.");
                                }
                            }
                            else {
                                SendMessage("You don't think that's such a great idea.");
                            }
                        }
                        else {
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

        private object ProcessEvent (Event evt) {
            switch (evt.Type) {
                case EventType.Enter:
                    if (evt.Sender == this) {
                        Client.ClearScreen();
                        SendMessage("You enter {0}.", Location.Title ?? Location.Name);
                        PerformLook();
                    } else {
                        SendMessage("{0} enters the room.", evt.Sender);
                    }
                    break;
                case EventType.Leave:
                    if (evt.Sender != this)
                        SendMessage("{0} leaves the room.", evt.Sender);
                    break;
                case EventType.Say:
                    SendMessage("{0} says, \"{1}\"", evt.Sender, evt[0]);
                    break;
                case EventType.Tell:
                    if (evt.Sender == this) {
                        SendMessage("You tell {0}, \"{1}\"", evt[1], evt[0]);
                    } else {
                        SendMessage("{0} tells you, \"{1}\"", evt.Sender, evt[0]);
                    }
                    break;
                case EventType.Emote:
                    SendMessage("{0} {1}", evt.Sender, evt[0]);
                    break;
                case EventType.Death:
                    if (evt.Sender == this) {
                        SendMessage("You collapse onto the floor and release your last breath.");
                    }
                    else {
                        SendMessage("{0} collapses onto the floor, releasing their last breath!", evt.Sender);
                    }
                    break;
                case EventType.CombatStart:
                    if (evt.Sender == this) {
                        SendMessage("You lunge at {0} and attack!", evt[0]);
                    } else {
                        SendMessage("{0} lunges at you, weapon in hand!", evt.Sender);
                    }
                    break;
                case EventType.CombatHit:
                    if (evt.Sender == this) {
                        SendMessage("You hit {0} with your {1} and deal {2} damage!", evt[2], evt[0], evt[1] );
                    }
                    else {
                        SendMessage("{0} hits you with their {1} for {2} damage.", evt.Sender, evt[0], evt[1]);
                    }
                    break;
                case EventType.CombatMiss:
                    if (evt.Sender == this) {
                        SendMessage("You miss {0} with your {1}.", evt[1], evt[0]);
                    }
                    else {
                        SendMessage("{0} misses you with their {1}.", evt.Sender, evt[0]);
                    }
                    break;
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

            World.Players[Name] = this;

            Future newEvent = GetNewEvent();
            Future newInputLine = Client.ReadLineText();
            while (true) {
                Future w = Future.WaitForFirst(newEvent, newInputLine);
                yield return w;
                if (w.Result == newEvent) {
                    Event evt = newEvent.Result as Event;
                    newEvent = GetNewEvent();

                    object next = ProcessEvent(evt);
                    if (next != null)
                        yield return next;
                } else if (w.Result == newInputLine) {
                    string line = newInputLine.Result as string;
                    newInputLine = Client.ReadLineText();

                    if (line != null) {
                        object next = ProcessInput(line);
                        if (next != null)
                            yield return next;
                    }
                }
                SendPrompt();
            }
        }
    }

    public static class Program {
        public static Random RNG = new Random();
        public static TaskScheduler Scheduler;
        public static TelnetServer Server;

        static void Main (string[] args) {
            Scheduler = new TaskScheduler(true);

            World.Create();

            Server = new TelnetServer(Scheduler, System.Net.IPAddress.Any, 23);
            Scheduler.Start(HandleNewClients(), TaskExecutionPolicy.RunAsBackgroundTask);

            while (true) {
                Scheduler.Step();
                Scheduler.WaitForWorkItems();
            }
        }

        static IEnumerator<object> HandleNewClients () {
            while (true) {
                Future f = Server.AcceptNewClient();
                yield return f;
                TelnetClient client = f.Result as TelnetClient;
                Player player = new Player(client, World.PlayerStartLocation);
            }
        }
    }
}
