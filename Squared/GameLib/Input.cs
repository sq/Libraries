using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Squared.Util;
using Squared.Util.Event;

namespace Squared.Game.Input {
    // Return true to suppress
    public delegate bool InputEventListener (InputControl c, InputEvent e);

    public struct InputEventSubscription : IDisposable {
        public readonly InputControl Control;
        public readonly InputEventListener Listener;

        public InputEventSubscription (InputControl control, InputEventListener listener) {
            Control = control;
            Listener = listener;
        }

        public void Dispose () {
            Control.RemoveListener(Listener);
        }
    }

    public abstract class InputControlCollection : IEnumerable<InputControl>, IEventSource {
        public readonly EventBus EventBus;

        protected Dictionary<string, InputControl> _Controls;
        protected string[] _ControlNames = null;
        protected long _UpdateFlag = 0;

        public InputControlCollection () {
            EventBus = new EventBus();

            _Controls = new Dictionary<string, InputControl>();
            var fields = GetType().GetFields();

            foreach (var field in fields) {
                if (field.FieldType == typeof(InputControl)) {
                    var name = String.Intern(field.Name);
                    var newControl = new InputControl(EventBus, name);
                    _Controls.Add(name, newControl);
                    field.SetValue(this, newControl);
                }
            }

            var props = GetType().GetProperties();
            foreach (var prop in props) {
                var ip = prop.GetIndexParameters();

                if ((ip != null) && (ip.Length > 0))
                    continue;

                if (prop.PropertyType == typeof(InputControl)) {
                    var name = String.Intern(prop.Name);
                    var ctl = (InputControl)prop.GetValue(this, null);
                    ctl.AlternateNames.Add(name);
                    _Controls.Add(name, ctl);
                }
            }
        }

        public int Count {
            get {
                return _Controls.Count;
            }
        }

        public bool ContainsKey (string name) {
            return _Controls.ContainsKey(name);
        }

        public InputControl this[string name] {
            get {
                return _Controls[name];
            }
        }

        public InputControl this[int index] {
            get {
                if (_ControlNames == null)
                    _ControlNames = _Controls.Keys.ToArray();

                return _Controls[_ControlNames[index]];
            }
        }

        private void SendEvent (InputControl ic, InputEventType et, long now) {
            var e = new InputEvent {
                Type = et,
                Control = ic
            };

            ic.NotifyEvent(ref e);
            ic.LastEventTime = now;
        }

        public void Update (bool dispatchEvents) {
            _UpdateFlag = _UpdateFlag + 1;

            for (int i = 0; i < Count; i++) {
                var ic = this[i];

                if (ic.UpdateFlag >= _UpdateFlag)
                    continue;
                ic.UpdateFlag = _UpdateFlag;

                ic.PreviousState = ic.State;
                ic.Value = 0.0f;
                ic.State = false;
            }

            OnUpdate();

            if (!dispatchEvents)
                return;

            long now = Time.Ticks;
            long repeatRate = TimeSpan.FromSeconds(GetRepeatRate()).Ticks;

            _UpdateFlag = _UpdateFlag + 1;

            for (int i = 0; i < Count; i++) {
                var ic = this[i];

                if (ic.UpdateFlag >= _UpdateFlag)
                    continue;
                ic.UpdateFlag = _UpdateFlag;

                if (ic.PreviousState != ic.State) {
                    ic.FirstEventTime = now;
                    SendEvent(ic, ic.State ? InputEventType.Press : InputEventType.Release, now);
                } else if (ic.State == true) {
                    long timeSincePress = now - ic.LastEventTime;

                    if (timeSincePress >= repeatRate)
                        SendEvent(ic, InputEventType.RepeatPress, now);
                }
            }
        }

        protected abstract void OnUpdate ();

        protected abstract float GetRepeatRate ();

        public IEnumerator<InputControl> GetEnumerator () {
            return _Controls.Values.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator () {
            return this.GetEnumerator();
        }

        string IEventSource.CategoryName {
            get {
                return "InputControls";
            }
        }
    }

    public class InputControl {
        public const string EventPress = "Press";
        public const string EventRepeatPress = "RepeatPress";
        public const string EventRelease = "Release";

        public static readonly string[] EventTypeNames = new string[] {
            EventPress, 
            EventRepeatPress,
            EventRelease
        };

        public readonly EventBus EventBus;
        public readonly string Name;
        public List<string> AlternateNames = new List<string>();
        public bool PreviousState = false;
        public long FirstEventTime = long.MinValue;
        public long LastEventTime = long.MinValue;
        public long UpdateFlag = 0;
        public float DeadZone = 0.35f;

        protected bool _State = false;
        protected float _Value = 0.0f;
        protected Dictionary<InputEventListener, EventSubscriber> _EventListeners;

        public InputControl (EventBus eventBus, string name) {
            EventBus = eventBus;
            Name = name;
            _EventListeners = new Dictionary<InputEventListener, EventSubscriber>();
        }

        public bool State {
            get {
                return _State;
            }
            set {
                _State = value;
                if (_State)
                    _Value = 1.0f;
            }
        }

        public float Value {
            get {
                return _Value;
            }
            set {
                _Value = value;
                if (_Value >= DeadZone)
                    _State = true;
            }
        }

        public static implicit operator bool (InputControl _this) {
            return _this.State;
        }

        protected EventSubscriber WrapEventListener (InputEventListener listener) {
            return (e) => {
                InputEventType eventType;
                if (e.Type == EventPress)
                    eventType = InputEventType.Press;
                else if (e.Type == EventRepeatPress)
                    eventType = InputEventType.RepeatPress;
                else if (e.Type == EventRelease)
                    eventType = InputEventType.Release;
                else
                    return;

                InputEvent evt = new InputEvent {
                    Control = (InputControl)e.Source,
                    Type = eventType
                };

                if (listener(evt.Control, evt))
                    e.Consume();
            };
        }

        public InputEventSubscription AddListener (InputEventListener listener) {
            if (_EventListeners.ContainsKey(listener))
                throw new InvalidOperationException("Listener is already subscribed to this control");

            var wrapper = WrapEventListener(listener);
            EventBus.Subscribe(this, null, wrapper);
            _EventListeners[listener] = wrapper;

            return new InputEventSubscription(this, listener);
        }

        public void RemoveListener (InputEventListener listener) {
            EventSubscriber wrapper;
            if (!_EventListeners.TryGetValue(listener, out wrapper))
                return;

            EventBus.Unsubscribe(this, null, wrapper);
            _EventListeners.Remove(listener);
        }

        public void NotifyEvent (ref InputEvent e) {
            var eventType = EventTypeNames[(int)e.Type];
            EventBus.Broadcast(this, eventType, null);
        }
    }

    public enum InputEventType : int {
        Press = 0,
        RepeatPress = 1,
        Release = 2
    }

    public struct InputEvent {
        public InputEventType Type;
        public InputControl Control;
    }
}
