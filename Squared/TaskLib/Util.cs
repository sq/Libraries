using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Squared.Util;
using System.Collections;

#if !XBOX
using System.Linq;
using System.Linq.Expressions;
using Squared.Util.Event;
using Squared.Util.Bind;
#endif

namespace Squared.Task {
    public static class EnumeratorExtensionMethods {
        internal struct Disposer : IDisposable {
            object Obj;

            public Disposer (object obj) {
                Obj = obj;
            }

            public void Dispose () {
                if (Obj is IDisposable)
                    ((IDisposable)Obj).Dispose();
            }
        }

        public static IEnumerator<object> EnumerateViaThreadpool<T> (IEnumerator<T> enumerator, int blockSize) {
            using (new Disposer(enumerator)) {
                var buffer = new List<T>(blockSize);
                var nv = new NextValue(null);

                WaitCallback moveNext = (state) => {
                    var f = (IFuture)state;
                    try {
                        while (buffer.Count < blockSize) {
                            if (enumerator.MoveNext()) {
                                buffer.Add(enumerator.Current);
                            } else {
                                f.SetResult(true, null);
                                return;
                            }
                        }

                        f.SetResult(false, null);
                    } catch (Exception e) {
                        f.SetResult(null, e);
                    }
                };

                while (true) {
                    buffer.Clear();
                    var f = new Future();
                    ThreadPool.QueueUserWorkItem(moveNext, f);
                    yield return f;

                    nv.Value = buffer;
                    yield return nv;

                    bool atEnd = (bool)f.Result;
                    if (atEnd)
                        yield break;
                }
            }
        }

        public static RunToCompletion<T> Run<T> (this IEnumerator<object> task, out Future<T> future) {
            var rtc = new RunToCompletion<T>(task, TaskExecutionPolicy.RunWhileFutureLives);
            future = rtc.Future;
            return rtc;
        }

        public static RunToCompletion Run (this IEnumerator<object> task, out Future future) {
            var rtc = new RunToCompletion(task, TaskExecutionPolicy.RunWhileFutureLives);
            future = rtc.Future;
            return rtc;
        }

#if !XBOX
        public static StoreResult<T> Bind<T> (this IEnumerator<object> task, Expression<Func<T>> target) {
            var sr = new StoreResult<T>(task, target, TaskExecutionPolicy.RunWhileFutureLives);
            return sr;
        }
#endif
    }

#if !XBOX
    /// <summary>
    /// Schedules a task to run to completion and store its result into a target field or property.
    /// </summary>
    public class StoreResult<T> : ISchedulable {
        IEnumerator<object> _Task;
        SchedulableGeneratorThunk _Thunk;
        TaskExecutionPolicy _ExecutionPolicy;
        Future<T> _Future;
        IFuture _CompletionSignal;

        public StoreResult (IEnumerator<object> task, Expression<Func<T>> target)
            : this(task, target, TaskExecutionPolicy.RunWhileFutureLives) {
        }

        public StoreResult (IEnumerator<object> task, Expression<Func<T>> target, TaskExecutionPolicy executionPolicy) {
            _Task = task;
            _Thunk = new SchedulableGeneratorThunk(_Task);
            _ExecutionPolicy = executionPolicy;
            _Future = (Future<T>)Squared.Task.Future.New<T>();
            _Future.Bind(target);
            _Future.RegisterOnComplete(Completed);
        }

        void Completed (IFuture f) {
            if (f.Failed)
                _CompletionSignal.Fail(f.Error);
            else
                _CompletionSignal.Complete();
        }

        void ISchedulable.Schedule (TaskScheduler scheduler, IFuture future) {
            _CompletionSignal = future;
            scheduler.Start(_Future, _Thunk, _ExecutionPolicy);
        }
    }
#endif

    /// <summary>
    /// Allows you to emulate a try { } finally block inside of a task, via using () { }.
    /// </summary>
    public struct Finally : IDisposable {
        Action _Action;

        public static Finally Do (Action action) {
            return new Finally { _Action = action };
        }

        public void Dispose () {
            if (_Action != null)
                _Action();
            _Action = null;
        }
    }

    /// <summary>
    /// Schedules your task to continue execution at the end of the current step.
    /// </summary>
    public class Yield : ISchedulable {
        void ISchedulable.Schedule (TaskScheduler scheduler, IFuture future) {
            future.Complete();
        }
    }

    /// <summary>
    /// Allows your task to yield a value as if it were a normal generator, and resume execution.
    /// </summary>
    public class NextValue {
        public object Value;

        public NextValue (object value) {
            Value = value;
        }
    }

    /// <summary>
    /// Schedules your task to continue execution at the beginning of the next step.
    /// </summary>
    public class WaitForNextStep : ISchedulable {
        void ISchedulable.Schedule (TaskScheduler scheduler, IFuture future) {
            scheduler.AddStepListener(future.Complete);
        }
    }

    /// <summary>
    /// Completes when the specified future completes, or when (timeout) seconds have elapsed, whichever comes first.
    /// </summary>
    public class WaitWithTimeout : ISchedulable {
        IFuture _Future, _TaskFuture, _SleepFuture;
        double _Timeout;

        public WaitWithTimeout (IFuture future, double timeout) {
            _Future = future;
            _Timeout = timeout;
        }

        void ISchedulable.Schedule (TaskScheduler scheduler, IFuture future) {
            _SleepFuture = scheduler.Start(new Sleep(_Timeout));
            _TaskFuture = Future.WaitForFirst(_Future, _SleepFuture);
            _TaskFuture.RegisterOnComplete((f) => {
                if (f.Result == _SleepFuture)
                    future.Fail(new TimeoutException("WaitWithTimeout timed out."));
                else
                    future.Complete();
            });
        }
    }

    /// <summary>
    /// Starts a task and stores the resulting future.
    /// </summary>
    public class Start : ISchedulable {
        IEnumerator<object> _Task;
        TaskExecutionPolicy _ExecutionPolicy;
        IFuture _Future;

        public Start (IEnumerator<object> task) 
            : this(task, TaskExecutionPolicy.RunWhileFutureLives) {
        }

        public Start (IEnumerator<object> task, TaskExecutionPolicy executionPolicy) {
            _Task = task;
            _ExecutionPolicy = executionPolicy;
            _Future = null;
        }

        void ISchedulable.Schedule (TaskScheduler scheduler, IFuture future) {
            _Future = scheduler.Start(_Task, _ExecutionPolicy);
            future.Complete(_Future);
        }

        public IFuture Future {
            get {
                return _Future;
            }
        }
    }

    public class RunToCompletion : RunToCompletion<object> {
        public RunToCompletion (IEnumerator<object> task)
            : base(task) {
        }

        public RunToCompletion (IEnumerator<object> task, TaskExecutionPolicy executionPolicy)
            : base(task, executionPolicy) {
        }

        new public Future Future {
            get {
                return (Future)base.Future;
            }
        }
    }

    /// <summary>
    /// Schedules a task to run to completion.
    /// </summary>
    public class RunToCompletion<T> : ISchedulable {
        IEnumerator<object> _Task;
        SchedulableGeneratorThunk _Thunk;
        TaskExecutionPolicy _ExecutionPolicy;
        Future<T> _Future;
        IFuture _CompletionSignal;

        public RunToCompletion (IEnumerator<object> task) 
            : this(task, TaskExecutionPolicy.RunWhileFutureLives) {
        }

        public RunToCompletion (IEnumerator<object> task, TaskExecutionPolicy executionPolicy) {
            _Task = task;
            _Thunk = new SchedulableGeneratorThunk(_Task);
            _Future = (Future<T>)Squared.Task.Future.New<T>();
            _Future.RegisterOnComplete(Completed);
            _ExecutionPolicy = executionPolicy;
        }

        void Completed (IFuture f) {
            _CompletionSignal.SetResult(_Future.Error == null, null);
        }

        void ISchedulable.Schedule (TaskScheduler scheduler, IFuture future) {
            _CompletionSignal = future;
            scheduler.Start(_Future, _Thunk, _ExecutionPolicy);
        }

        public void AssertSucceeded () {
            this._Future.AssertSucceeded();
        }

        public Future<T> Future {
            get {
                return _Future;
            }
        }

        public T Result {
            get {
                return (T)_Future.Result;
            }
        }
    }

    /// <summary>
    /// Waits until a specified time (in ticks).
    /// </summary>
    public class SleepUntil : ISchedulable {
        long _EndWhen;

        public SleepUntil (long when) {
            _EndWhen = when;
        }

        void ISchedulable.Schedule (TaskScheduler scheduler, IFuture future) {
            scheduler.QueueSleep(_EndWhen, future);
        }
    }

    /// <summary>
    /// Waits for a specified amount of time (in seconds).
    /// </summary>
    public class Sleep : ISchedulable {
        long _Duration;

        public Sleep (double duration) {
            _Duration = TimeSpan.FromSeconds(duration).Ticks;
        }

        public double Duration {
            set {
                _Duration = TimeSpan.FromSeconds(value).Ticks;
            }
            get {
                return TimeSpan.FromTicks(_Duration).TotalSeconds;
            }
        }

        void ISchedulable.Schedule (TaskScheduler scheduler, IFuture future) {
            scheduler.QueueSleep(Time.Ticks + _Duration, future);
        }
    }

    /// <summary>
    /// Manages iterating over a task that generates a sequence of values of type T. A task-oriented equivalent to IEnumerator.
    /// </summary>
    public class TaskEnumerator<T> : IDisposable, IEnumerable<T> {
        public static int DefaultBufferSize = 256;

        public class FetchThunk : ISchedulable {
            public readonly TaskEnumerator<T> Enumerator;

            public FetchThunk (TaskEnumerator<T> enumerator) {
                Enumerator = enumerator;
            }

            public void Schedule (TaskScheduler scheduler, IFuture future) {
                if (!Enumerator._Initialized)
                    Enumerator.Initialize(scheduler, future);
                else
                    Enumerator.WaitForRows(future);
            }
        }

        public struct Enumerator : IEnumerator<T> {
            public TaskEnumerator<T> Parent;
            public List<T>.Enumerator Inner;

            private bool _Ready;

            public Enumerator (TaskEnumerator<T> parent, List<T>.Enumerator inner) {
                Parent = parent;
                Inner = inner;
                _Ready = false;
            }

            public T Current {
                get {
                    if (_Ready)
                        return Inner.Current;
                    else
                        throw new InvalidOperationException();
                }
            }

            public void Dispose () {
                if (Parent != null) {
                    Inner.Dispose();

                    if (!Parent.Disposed)
                        Parent.ReadyForMore();

                    Parent = null;
                }
            }

            object IEnumerator.Current {
                get { return Inner.Current; }
            }

            public bool MoveNext () {
                if (Parent == null)
                    return _Ready = false;
                else if (Parent.Disposed)
                    return _Ready = false;

                if (Inner.MoveNext())
                    return _Ready = true;
                else
                    return _Ready = false;
            }

            public void Reset () {
                throw new NotImplementedException();
            }
        }

        public readonly int Capacity;
        public Action OnEarlyDispose = null;

        protected bool _Initialized = false;
        protected IEnumerator<object> _Task = null;
        protected TaskScheduler _Scheduler = null;
        protected SchedulableGeneratorThunk _Thunk = null;
        protected IFuture _ResumeFuture = null;
        protected IFuture _SequenceFuture = null;
        protected IFuture _ReadyForMoreFuture = null;
        protected List<T> _Buffer;
        protected FetchThunk _FetchThunk;

        protected TaskEnumerator (int capacity) {
            Capacity = capacity;
            _FetchThunk = new TaskEnumerator<T>.FetchThunk(this);
            _Buffer = new List<T>(capacity);
        }

        public TaskEnumerator (IEnumerator<object> task, int capacity)
            : this (capacity) {
            _Task = task;
        }

        public TaskEnumerator (IEnumerator<object> task)
            : this(task, DefaultBufferSize) {
        }

        protected virtual void Start () {
            _Thunk = new SchedulableGeneratorThunk(_Task);
            _Thunk.OnNextValue = OnNextValue;

            _SequenceFuture = _Scheduler.Start(_Thunk, TaskExecutionPolicy.RunWhileFutureLives);
            _SequenceFuture.RegisterOnDispose((f) => {
                Resume();
                Dispose();
            });
            _SequenceFuture.RegisterOnComplete((f) => {
                if (f.Failed)
                    Fail(f.Error);
                else
                    Resume();
            });
        }

        protected void Initialize (TaskScheduler scheduler, IFuture resumeFuture) {
            _Initialized = true;
            _Scheduler = scheduler;
            
            _ResumeFuture = resumeFuture;

            Start();
        }

        protected void WaitForRows (IFuture resumeFuture) {
            _ResumeFuture = resumeFuture;
        }

        public static TaskEnumerator<T> FromEnumerable (IEnumerable<T> enumerable) {
            return FromEnumerable(enumerable, DefaultBufferSize);
        }

        public static TaskEnumerator<T> FromEnumerable (IEnumerable enumerable) {
            return FromEnumerable(enumerable, DefaultBufferSize);
        }

        public static TaskEnumerator<T> FromEnumerable (IEnumerable<T> enumerable, int capacity) {
            return FromEnumerator(enumerable.GetEnumerator(), capacity);
        }

        public static TaskEnumerator<T> FromEnumerable (IEnumerable enumerable, int capacity) {
            return FromEnumerator(enumerable.GetEnumerator(), capacity);
        }

        public static TaskEnumerator<T> FromEnumerator (IEnumerator<T> enumerator, int capacity) {
            return new TaskEnumerator<T>(
                EnumeratorExtensionMethods.EnumerateViaThreadpool(enumerator, capacity), capacity
            );
        }

        public static TaskEnumerator<T> FromEnumerator (IEnumerator enumerator, int capacity) {
            return new TaskEnumerator<T>(
                EnumeratorExtensionMethods.EnumerateViaThreadpool<object>(Wrap(enumerator), capacity), capacity
            );
        }

        static IEnumerator<object> Wrap (IEnumerator inner) {
            using (new Squared.Task.EnumeratorExtensionMethods.Disposer(inner))
            while (inner.MoveNext())
                yield return inner.Current;
        }

        /// <summary>
        /// Fetches more items from the source sequence, if possible.
        /// </summary>
        public FetchThunk Fetch () {
            if (Disposed || (_Initialized && _SequenceFuture.Completed))
                return null;
            else {
                lock (_Buffer)
                    _Buffer.Clear();

                return _FetchThunk;
            }
        }

        protected void Fail (Exception error) {
            OnEarlyDispose = null;

            if (_ResumeFuture != null) {
                _ResumeFuture.Fail(error);
                _ResumeFuture = null;
            }

            if ((_SequenceFuture != null) && (!_SequenceFuture.Completed))
                _SequenceFuture.Fail(error);

            Dispose();
        }

        internal Exception TryConvertValue (object value, out T result) {
            try {
                result = (T)value;
                return null;
            } catch (InvalidCastException) {
                result = default(T);

                string valueString = "<?>";
                try {
                    valueString = value.ToString();
                } catch {
                }

                string errorString = String.Format(
                    "Unable to convert value '{0}' from {1} to {2}",
                    valueString, value.GetType().Name, typeof(T).Name
                );

                return new InvalidCastException(errorString);
            }
        }

        internal IFuture OnNextValue (object value) {
            OnEarlyDispose = null;

            int count;

            var seq = value as IEnumerable<T>;
            if (seq != null) {
                lock (_Buffer) {
                    _Buffer.AddRange(seq);
                    count = _Buffer.Count;
                }
            } else {
                T convertedValue;
                var e = TryConvertValue(value, out convertedValue);

                if (e != null) {
                    Fail(e);

                    var f = new SignalFuture();
                    f.Fail(e);
                    return f;
                } else {
                    lock (_Buffer) {
                        _Buffer.Add(convertedValue);
                        count = _Buffer.Count;
                    }
                }
            }

            if (count >= Capacity) {
                Resume();
                _ReadyForMoreFuture = new SignalFuture();
                return _ReadyForMoreFuture;
            } else {
                return null;
            }
        }

        IEnumerator IEnumerable.GetEnumerator () {
            return new Enumerator(this, _Buffer.GetEnumerator());
        }

        IEnumerator<T> IEnumerable<T>.GetEnumerator () {
            return new Enumerator(this, _Buffer.GetEnumerator());
        }

        public IEnumerator<T> CurrentItems {
            get {
                return new Enumerator(this, _Buffer.GetEnumerator());
            }
        }

        internal void ReadyForMore () {
            if (_SequenceFuture.Completed) {
                Dispose();
                return;
            }

            lock (_Buffer)
                if (_Buffer.Count == 0)
                    Dispose();
                else
                    _Buffer.Clear();

            if (_ReadyForMoreFuture != null) {
                _ReadyForMoreFuture.Complete();
                _ReadyForMoreFuture = null;
            }
        }

        protected void Resume () {
            OnEarlyDispose = null;

            if (_ResumeFuture != null) {
                int count = 0;
                lock (_Buffer)
                    count = _Buffer.Count;

                _ResumeFuture.SetResult((count > 0), null);
                _ResumeFuture = null;

                if (count == 0)
                    Dispose();
            } else {
                Dispose();
            }
        }

        public IEnumerator<object> GetArray () {
            var temp = new List<T>(Capacity);

            while (!Disposed) {
                yield return this.Fetch();

                temp.AddRange(_Buffer);

                new Enumerator(this, _Buffer.GetEnumerator()).Dispose();
            }

            yield return new Result(temp.ToArray());
        }

        public bool Disposed {
            get {
                return (_Task == null);
            }
        }

        public void Dispose () {
            if (OnEarlyDispose != null) {
                OnEarlyDispose();
                OnEarlyDispose = null;
            }

            if (_Task != null) {
                _Task.Dispose();
            }

            if (_SequenceFuture != null) {
                if (!_SequenceFuture.Completed)
                    _SequenceFuture.Dispose();
            }

            if (_Thunk != null) {
                _Thunk.Dispose();
                _Thunk = null;
            }

            _Task = null;
            _Scheduler = null;
        }
    }

    public class EventSink<T> : BlockingQueue<T>, IDisposable
        where T : EventArgs {

        public EventSink () {
        }

        public void OnEvent (object sender, T eventArgs) {
            this.Enqueue(eventArgs);
        }

        public static implicit operator EventHandler<T> (EventSink<T> sink) {
            return sink.OnEvent;
        }

        public void Dispose () {
        }
    }

    public class Signal<T> : IDisposable {
        private volatile Future<T> _Current = new Future<T>();

        public void Set (T value, Exception exception) {
            var newFuture = new Future<T>();
            var currentFuture = Interlocked.Exchange(ref _Current, newFuture);
            currentFuture.SetResult(value, null);
        }

        public Future<T> Wait () {
            return _Current;
        }

        public void Dispose () {
            var current = Interlocked.Exchange(ref _Current, null);
            if (current != null)
                current.Dispose();
        }
    }

    public class Signal : Signal<NoneType> {
        public void Set () {
            base.Set(NoneType.None, null);
        }
    }
}
