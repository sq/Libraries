using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Squared.Util;
using System.Collections;

namespace Squared.Task {
    public static class EnumeratorExtensionMethods {
        internal struct Disposer : IDisposable {
            IEnumerator _Enumerator;

            public Disposer (IEnumerator enumerator) {
                _Enumerator = enumerator;
            }

            public void Dispose () {
                if (_Enumerator is IDisposable)
                    ((IDisposable)_Enumerator).Dispose();
            }
        }

        public static IEnumerator<object> EnumerateViaThreadpool (IEnumerator enumerator) {
            WaitCallback moveNext = (state) => {
                var f = (IFuture)state;
                try {
                    bool r = enumerator.MoveNext();
                    f.SetResult(r, null);
                } catch (Exception e) {
                    f.SetResult(null, e);
                }
            };

            using (new Disposer(enumerator)) {
                while (true) {
                    var f = new Future();
                    ThreadPool.QueueUserWorkItem(moveNext, f);
                    yield return f;

                    bool hasValue = (bool)f.Result;
                    if (!hasValue)
                        yield break;

                    yield return new NextValue(enumerator.Current);
                }
            }
        }

        public static IEnumerator<object> GetTask (this IEnumerable obj) {
            return EnumerateViaThreadpool(obj.GetEnumerator());
        }

        public static IEnumerator<object> GetTask<T> (this IEnumerable<T> obj) {
            return EnumerateViaThreadpool(obj.GetEnumerator());
        }

        public static IEnumerator<object> GetTask (this IEnumerator enumerator) {
            return EnumerateViaThreadpool(enumerator);
        }

        public static IEnumerator<object> GetTask<T> (this IEnumerator<T> enumerator) {
            return EnumerateViaThreadpool(enumerator);
        }

        public static TaskIterator<T> GetTaskIterator<T> (this IEnumerable<T> obj) {
            return new TaskIterator<T>(EnumerateViaThreadpool(obj.GetEnumerator()));
        }

        public static TaskIterator<T> GetTaskIterator<T> (this IEnumerator<T> enumerator) {
            return new TaskIterator<T>(EnumerateViaThreadpool(enumerator));
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
    }

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
    public class TaskIterator<T> : IDisposable {
        class StartThunk : ISchedulable {
            TaskIterator<T> _Iterator;

            public StartThunk (TaskIterator<T> iterator) {
                _Iterator = iterator;
            }

            public void Schedule (TaskScheduler scheduler, IFuture future) {
                TaskIterator<T> i = _Iterator;
                i.Initialize(scheduler);
                future.Bind(i.MoveNext());
            }
        }; 
        
        protected IEnumerator<object> _Task = null;
        protected TaskScheduler _Scheduler = null;
        protected SchedulableGeneratorThunk _Thunk = null;
        protected SignalFuture _MoveNextFuture = new SignalFuture();
        protected Future<bool> _NextValueFuture = new Future<bool>();
        protected IFuture _SequenceFuture = null;
        protected bool _HasValue = false;
        protected T _Current = default(T);

        protected TaskIterator () {
        }

        public TaskIterator (IEnumerator<object> task)
            : this () {
            _Task = task;
        }

        protected void Initialize (TaskScheduler scheduler) {
            _Scheduler = scheduler;

            _Thunk = new SchedulableGeneratorThunk(_Task);
            _Thunk.OnNextValue = OnNextValue;

            _SequenceFuture = scheduler.Start(_Thunk, TaskExecutionPolicy.RunWhileFutureLives);
            _SequenceFuture.RegisterOnComplete((f) => {
                _NextValueFuture.Complete(false);
                Dispose();
            });
        }

        protected virtual void OnDispose () {
        }

        /// <summary>
        /// Returns a future that will be completed when the next item in the sequence is ready, or the sequence has completed iteration. The future's result will be true if another item is ready.
        /// </summary>
        public Future<bool> MoveNext () {
            lock (this) {
                if (_SequenceFuture == null)
                    throw new InvalidOperationException("The iterator is not ready.");
                if (_Thunk == null)
                    throw new InvalidOperationException("The iterator has been disposed.");

                var f = _MoveNextFuture;
                var nv = _NextValueFuture;
                _MoveNextFuture = new SignalFuture();
                f.Complete();
                return nv;
            }
        }

        internal IFuture OnNextValue (object value) {
            lock (this) {
                var f = _NextValueFuture;
                _NextValueFuture = new Future<bool>();
                try {
                    _Current = (T)value;
                    _HasValue = true;
                    f.Complete(true);
                } catch (InvalidCastException) {
                    _HasValue = false;
                    string valueString = "<?>";
                    try {
                        valueString = value.ToString();
                    } catch (Exception e) {
                        valueString = String.Format("<{0}>", e.ToString());
                    }
                    string errorString = String.Format(
                        "Unable to convert value '{0}' of type {1} to type {2}",
                        valueString, value.GetType().Name, default(T).GetType().Name
                    );
                    f.Fail(new InvalidCastException(errorString));
                }
                return _MoveNextFuture;
            }
        }

        internal IEnumerator<object> GetArray () {
            var temp = new List<T>();

            while (!Disposed) {
                if (_HasValue)
                    temp.Add(_Current);

                yield return MoveNext();
            }

            yield return new Result(temp.ToArray());
        }

        public Future<T[]> ToArray () {
            var f = new Future<T[]>();
            if (Disposed) {
                f.Complete(new T[0]);
            } else {
                f.Bind(_Scheduler.Start(GetArray(), TaskExecutionPolicy.RunWhileFutureLives));
            }
            return f;
        }

        public T Current {
            get {
                if (_Thunk == null)
                    throw new InvalidOperationException("The iterator has been disposed.");
                if (!_HasValue)
                    throw new InvalidOperationException("The iterator does not have a value yet.");

                return _Current;
            }
        }

        internal IFuture Future {
            get {
                return _SequenceFuture;
            }
        }

        public bool Disposed {
            get {
                return (_Task == null);
            }
        }

        public void Dispose () {
            OnDispose();

            if (_SequenceFuture != null) {
                _SequenceFuture.Dispose();
                _SequenceFuture = null;
            }
            if (_Thunk != null) {
                _Thunk.Dispose();
                _Thunk = null;
            }
            _Task = null;
            _Scheduler = null;
            _HasValue = false;
            _Current = default(T);
        }

        /// <summary>
        /// Yield the result of this function from within a task to initialize the TaskIterator. The iterator will automatically be advanced to the first item.
        /// </summary>
        public ISchedulable Start () {
            if (_Scheduler != null)
                return null;

            return GetStartThunk();
        }

        protected virtual ISchedulable GetStartThunk () {
            return new StartThunk(this);
        }
    }
}
