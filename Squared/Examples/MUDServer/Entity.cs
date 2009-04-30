using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Squared.Task;

namespace MUDServer {
    public delegate IEnumerator<object> EventHandler (EventType type, object evt);

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

        void NotifyEvent (EventType type, object evt);
    }

    public class EntityBase : IEntity, IDisposable {
        private static int _EntityCount;

        private Dictionary<EventType, List<EventHandler>> _EventHandlers = new Dictionary<EventType, List<EventHandler>>();
        private BlockingQueue<object> _EventQueue = new BlockingQueue<object>();
        private Future _ThinkTask, _EventDispatchTask;
        private Location _Location;
        protected string _Name = null;
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

                OnLocationChange(_Location, value);
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

        protected virtual void OnLocationChange (Location oldLocation, Location newLocation) { }

        protected virtual bool ShouldFilterNewEvent (EventType type, object evt) {
            return false;
        }

        protected void AddEventHandler (EventType type, EventHandler handler) {
            if (!_EventHandlers.ContainsKey(type))
                _EventHandlers[type] = new List<EventHandler>();
            _EventHandlers[type].Add(handler);
        }

        public void NotifyEvent (EventType type, object evt) {
            if (!ShouldFilterNewEvent(type, evt))
                _EventQueue.Enqueue(evt);
        }

        protected IFuture GetNewEvent () {
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
            _EventDispatchTask = Program.Scheduler.Start(EventDispatchTask(), TaskExecutionPolicy.RunAsBackgroundTask);
        }

        protected IEnumerator<object> InvokeEventHandlers (EventType type, object evt, EventHandler[] handlers) {
            foreach (EventHandler handler in handlers) {
                yield return handler(type, evt);
            }
        }

        protected virtual IEnumerator<object> DispatchEvent (EventType type, object evt) {
            List<EventHandler> handlers;
            if (_EventHandlers.TryGetValue(type, out handlers))
                return InvokeEventHandlers(type, evt, handlers.ToArray());
            else
                return null;
        }

        protected virtual IEnumerator<object> EventDispatchTask () {
            while (true) {
                var f = GetNewEvent();
                yield return f;
                object evt = f.Result;
                var type = Event.GetProp<EventType>("Type", evt);

                IEnumerator<object> task = DispatchEvent(type, evt);
                if (task != null)
                    yield return new Start(task, TaskExecutionPolicy.RunAsBackgroundTask);
            }
        }

        protected virtual IEnumerator<object> ThinkTask () {
            yield break;
        }

        public virtual void Dispose () {
            if (_ThinkTask != null) {
                _ThinkTask.Dispose();
                _ThinkTask = null;
            }

            if (_EventDispatchTask != null) {
                _EventDispatchTask.Dispose();
                _EventDispatchTask = null;
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
            CombatPeriod = 2.0 + Program.RNG.NextDouble();
            _MaximumHealth = 40 + Program.RNG.Next(15);
            _CurrentHealth = _MaximumHealth;

            AddEventHandler(EventType.Leave, OnEventLeave);
        }

        private IEnumerator<object> OnEventLeave (EventType type, object evt) {
            if (!InCombat)
                return null;

            IEntity sender = Event.GetProp<IEntity>("Sender", evt);

            if (sender == _CombatTarget) {
                _CombatTarget.EndCombat();
                EndCombat();
            }

            return null;
        }

        public void Hurt (int damage) {
            if (_CurrentHealth <= 0)
                return;

            _CurrentHealth -= damage;
            if (_CurrentHealth <= 0) {
                Event.Send(new { Type = EventType.Death, Sender = this });
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
                int damage = Program.RNG.Next(1, 6 - 1) + Program.RNG.Next(1, 6 - 1);
                if (Program.RNG.Next(0, 3) <= 1) {
                    Event.Send(new { Type = EventType.CombatHit, Sender = this, Target = _CombatTarget, WeaponName = "Longsword", Damage = damage });
                    _CombatTarget.Hurt(damage);
                } else {
                    Event.Send(new { Type = EventType.CombatMiss, Sender = this, Target = _CombatTarget, WeaponName = "Longsword" });
                }
            }
        }
    }
}
