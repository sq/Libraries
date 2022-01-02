using System;
using System.Collections.Generic;
using System.Threading;
using Squared.Util;
using System.Collections.Concurrent;
using Squared.Threading;

using CallContext = System.Runtime.Remoting.Messaging.CallContext;
using System.Diagnostics;
using System.Reflection;

namespace Squared.Task {
    public interface ISchedulable {
        void Schedule (TaskScheduler scheduler, IFuture future);
    }

    public interface ISchedulable<T> : ISchedulable {
        void Schedule (TaskScheduler scheduler, Future<T> future);
    }

    public enum TaskExecutionPolicy {
        RunWhileFutureLives,
        RunAsBackgroundTask,
        Default = RunWhileFutureLives
    }

    public interface ITaskResult {
        object Value {
            get;
        }
        void CompleteFuture (IFuture f);
    }

    public class Result : ITaskResult {
        private readonly object _Value;

        public Result (object value) {
            _Value = value;
        }

        public object Value {
            get {
                return _Value;
            }
        }

        public void CompleteFuture (IFuture future) {
            future.Complete(_Value);
        }

        public static Result New (object value) {
            return new Result(value);
        }

        public static TaskResult<T> New<T> (T value) {
            return new TaskResult<T>(value);
        }

        public static TaskResult<T> New<T> (ref T value) {
            return new TaskResult<T>(ref value);
        }
    }

    public class TaskResult<T> : ITaskResult {
        private readonly T _Value;

        public TaskResult (T value) {
            _Value = value;
        }

        public TaskResult (ref T value) {
            _Value = value;
        }

        public void CompleteFuture (Future<T> future) {
            future.Complete(_Value);
        }

        void ITaskResult.CompleteFuture (IFuture future) {
            var stronglyTyped = future as Future<T>;
            if (stronglyTyped != null)
                CompleteFuture(stronglyTyped);
            else
                future.Complete(_Value);
        }

        object ITaskResult.Value {
            get {
                return _Value;
            }
        }

        T Value {
            get {
                return _Value;
            }
        }
    }

    public class TaskException : Exception {
        public TaskException (string message, Exception innerException)
            : base(message, innerException) {
        }
    }

    public class TaskYieldedValueException : Exception {
        public IEnumerator<object> Task;

        public TaskYieldedValueException (IEnumerator<object> task)
            : base("A task directly yielded a value. To yield a result from a task, yield a Result() object containing the value.") {
            Task = task;
        }
    }

    struct SleepItem : IComparable<SleepItem> {
        private static readonly long WarningLatency = TimeSpan.FromMilliseconds(2).Ticks;

        public long Until;
        public IFuture Future;

        public bool Tick (long now) {
            long ticksLeft = Math.Max(Until - now, 0);
            if (ticksLeft == 0) {
                try {
                    Future.Complete();
                } catch (FutureAlreadyHasResultException ex) {
                    if (ex.Future != Future)
                        throw;
                }

                return true;
            } else {
                return false;
            }
        }

        public int CompareTo (SleepItem rhs) {
            if (Future == null) {
                if (rhs.Future == null)
                    return 0;
                else
                    return 1;
            } else if (rhs.Future == null)
                return -1;
            else
                return Until.CompareTo(rhs.Until);
        }
    }

    /// <summary>Responsible for filtering/handling errors.</summary>
    /// <param name="error">The error.</param>
    /// <returns>True if the error has been fully processed; false to allow the error to propagate up the stack and/or get rethrown on the main thread.</returns>
    public delegate bool BackgroundTaskErrorHandler (Exception error);

    public sealed class TaskSchedulerSynchronizationContext : SynchronizationContext {
        public readonly TaskScheduler Scheduler;

        public TaskSchedulerSynchronizationContext (TaskScheduler scheduler) {
            Scheduler = scheduler;
        }

        public override void Post (SendOrPostCallback d, object state) {
            Scheduler.QueueWorkItem(new WorkItemQueueEntry { Action = d, Arg1 = state });
        }

        public override void Send (SendOrPostCallback d, object state) {
            // FIXME
            d(state);
        }
    }

    public sealed class TaskScheduler : IDisposable, IWorkItemQueueTarget {
        /// <summary>
        /// By default, attempts to queue a work item on a disposed scheduler will do nothing.
        /// Setting this to true will cause them to throw instead.
        /// </summary>
        public bool ThrowOnQueueWhileDisposed = false;

        internal struct PushedActivity : IDisposable {
            public readonly SynchronizationContext PriorContext;

            public readonly TaskScheduler Scheduler;
            public readonly TaskScheduler Prior;

            public PushedActivity (TaskScheduler scheduler) {
                Scheduler = scheduler;

                PriorContext = System.Threading.SynchronizationContext.Current;

                Prior = Current;
                if (Prior != Scheduler)
                    Current = scheduler;

                if (PriorContext != Scheduler.SynchronizationContext)
                    System.Threading.SynchronizationContext.SetSynchronizationContext(Scheduler.SynchronizationContext);
            }

            public void Dispose () {
                var current = Current;

                if (System.Threading.SynchronizationContext.Current == Scheduler.SynchronizationContext) {
                    if (PriorContext != Scheduler.SynchronizationContext)
                        System.Threading.SynchronizationContext.SetSynchronizationContext(PriorContext);
                }

                if (current == Scheduler) {
                    if (Prior != Scheduler)
                        Current = Prior;
                } else
                    throw new ThreadStateException("Mismatched scheduler activity push");
            }
        }

        public static int SleepThreadTimeoutMs = 10000;

        const long SleepFudgeFactor = 100;
        const long SleepSpinThreshold = 1000;
        const long MinimumSleepLength = 10000;
        const long MaximumSleepLength = Time.SecondInTicks * 60;

        private static readonly ThreadLocal<TaskScheduler> _Default = new ThreadLocal<TaskScheduler>();
        private OnFutureResolved BackgroundTaskOnComplete;
        private OnFutureResolvedWithData OnResolvedDispatcher, OnResolvedDispatcher_SkipQueue;
        private MethodInfo mOnResolvedDispatcher, mOnResolvedDispatcher_SkipQueue;
        private Dictionary<Type, Delegate> OnResolvedDispatchersForType_SkipQueue = 
                new Dictionary<Type, Delegate>(new ReferenceComparer<Type>());

        public BackgroundTaskErrorHandler ErrorHandler = null;
        public Thread MainThread { get; private set; }
        
        private bool _IsDisposed = false;
        private IJobQueue _JobQueue = null;
        private Internal.WorkerThread<PriorityQueue<SleepItem>> _SleepWorker;

        public readonly ITimeProvider TimeProvider;
        public TaskSchedulerSynchronizationContext SynchronizationContext { get; private set; }

        public TaskScheduler (Func<IJobQueue> JobQueueFactory, ITimeProvider timeProvider = null, Thread mainThread = null) {
            MainThread = mainThread ?? Thread.CurrentThread;
            TimeProvider = timeProvider ?? Time.DefaultTimeProvider;

            BackgroundTaskOnComplete = _BackgroundTaskOnComplete;
            OnResolvedDispatcher = _OnResolvedDispatcher;
            OnResolvedDispatcher_SkipQueue = _OnResolvedDispatcher_SkipQueue;

            mOnResolvedDispatcher_SkipQueue = GetType().GetMethod("_OnResolvedDispatcher_SkipQueue_Generic", BindingFlags.Instance | BindingFlags.NonPublic);

            _JobQueue = JobQueueFactory();
            _SleepWorker = new Internal.WorkerThread<PriorityQueue<SleepItem>>(
                SleepWorkerThreadFunc, ThreadPriority.AboveNormal, "TaskScheduler Sleep Provider"
            );

            SynchronizationContext = new TaskSchedulerSynchronizationContext(this);

            if (!_Default.IsValueCreated) {
                _Default.Value = this;
                WorkItemQueueTarget.SetDefaultIfNone(this);
            } else
                _Default.Value = null;
        }

        public TaskScheduler ()
            : this(JobQueue.ThreadSafe) {
        }

        public static TaskScheduler Current {
            get {
                var result = (TaskScheduler)CallContext.LogicalGetData("TaskScheduler");
                if (result == null)
                    result = _Default.Value;

                return result;
            }

            internal set {
                CallContext.LogicalSetData("TaskScheduler", value);
                WorkItemQueueTarget.Current = value;
            }
        }

        internal PushedActivity IsActive {
            get {
                return new PushedActivity(this);
            }
        }

        public bool WaitForWorkItems (double timeout = 0) {
            if (_IsDisposed)
                throw new ObjectDisposedException("TaskScheduler");
            
            using (IsActive)
                return _JobQueue.WaitForWorkItems(timeout);
        }

        private void _BackgroundTaskOnComplete (IFuture f) {
            using (IsActive) {
                var e = f.Error;
                if (e != null)
                    OnTaskError(e);
            }
        }

        public void OnTaskError (Exception exception) {
            if (_IsDisposed)
                throw new TaskException("Unhandled exception in background task", exception);

            this.QueueWorkItem(() => {
                if (ErrorHandler != null)
                    if (ErrorHandler(exception))
                        return;

                throw new TaskException("Unhandled exception in background task", exception);
            });
        }

        public void Start (IFuture future, ISchedulable task, TaskExecutionPolicy executionPolicy) {
            using (IsActive)
                task.Schedule(this, future);

            switch (executionPolicy) {
                case TaskExecutionPolicy.RunAsBackgroundTask:
                    future.RegisterOnResolved(BackgroundTaskOnComplete);
                break;
                default:
                break;
            }
        }

        public void Start<T> (Future<T> future, ISchedulable<T> task, TaskExecutionPolicy executionPolicy) {
            using (IsActive)
                task.Schedule(this, future);

            switch (executionPolicy) {
                case TaskExecutionPolicy.RunAsBackgroundTask:
                    future.RegisterOnResolved(BackgroundTaskOnComplete);
                break;
                default:
                break;
            }
        }

        public IFuture Start (System.Threading.Tasks.Task task, TaskExecutionPolicy executionPolicy = TaskExecutionPolicy.RunAsBackgroundTask) {
            var future = task.GetFuture();
            switch (executionPolicy) {
                case TaskExecutionPolicy.RunAsBackgroundTask:
                    future.RegisterOnResolved(BackgroundTaskOnComplete);
                    break;
            }
            return future;
        }

        public Future<T> Start<T> (System.Threading.Tasks.Task<T> task, TaskExecutionPolicy executionPolicy = TaskExecutionPolicy.RunAsBackgroundTask) {
            var future = task.GetFuture();
            switch (executionPolicy) {
                case TaskExecutionPolicy.RunAsBackgroundTask:
                    future.RegisterOnResolved(BackgroundTaskOnComplete);
                    break;
            }
            return future;
        }

        public IFuture Start (ISchedulable task, TaskExecutionPolicy executionPolicy = TaskExecutionPolicy.Default) {
            var future = new Future<object>();
            Start(future, task, executionPolicy);
            return future;
        }

        public Future<T> Start<T> (ISchedulable<T> task, TaskExecutionPolicy executionPolicy = TaskExecutionPolicy.Default) {
            var future = new Future<T>();
            Start(future, task, executionPolicy);
            return future;
        }

        public IFuture Start (IEnumerator<object> task, TaskExecutionPolicy executionPolicy = TaskExecutionPolicy.Default) {
            return Start(new SchedulableGeneratorThunk(task), executionPolicy);
        }

        private void _OnResolvedDispatcher (IFuture future, object handler) {
            QueueWorkItem(new WorkItemQueueEntry {
                Action = (Delegate)handler,
                Arg1 = future
            });
        }

        private void _OnResolvedDispatcher_SkipQueue_Generic<T> (Future<T> future, object _handler) {
            if (Thread.CurrentThread == MainThread) {
                if (_handler is Action<T> a)
                    a(future.Result2);
                else if (_handler is OnFutureResolved<T> ofrt)
                    ofrt(future);
                else if (_handler is OnFutureResolved ofr)
                    ofr(future);
                else
                    throw new ArgumentOutOfRangeException($"Unsupported handler type: {_handler.GetType()}");
            } else
                QueueWorkItem(new WorkItemQueueEntry {
                    Action = (Delegate)_handler,
                    Arg1 = future.Result2
                });
        }

        private void _OnResolvedDispatcher_SkipQueue (IFuture future, object _handler) {
            if (Thread.CurrentThread == MainThread) {
                if (_handler is Action a)
                    a();
                else if (_handler is OnFutureResolved ofr)
                    ofr(future);
                else
                    throw new ArgumentOutOfRangeException($"Unsupported handler type: {_handler.GetType()}");
            } else
                QueueWorkItem(new WorkItemQueueEntry {
                    Action = (Delegate)_handler,
                    Arg1 = future
                });            
        }

        private void QueuedWhileDisposed () {
            if (ThrowOnQueueWhileDisposed)
                throw new ObjectDisposedException("TaskScheduler");
        }

        public void QueueWorkItem (Action workItem) {
            if (_IsDisposed) {
                QueuedWhileDisposed();
                return;
            }

            _JobQueue.QueueWorkItem(workItem);
        }

        public void QueueWorkItem (WorkItemQueueEntry entry) {
            if (_IsDisposed) {
                QueuedWhileDisposed();
                return;
            }

            _JobQueue.QueueWorkItem(entry);
        }

        public void QueueWorkItemForNextStep (Action workItem) {
            if (_IsDisposed) {
                QueuedWhileDisposed();
                return;
            }

            _JobQueue.QueueWorkItemForNextStep(workItem);
        }

        public void QueueWorkItemForNextStep (WorkItemQueueEntry entry) {
            if (_IsDisposed) {
                QueuedWhileDisposed();
                return;
            }

            _JobQueue.QueueWorkItemForNextStep(entry);
        }

        private void RegisterOnResolved_Impl (IFuture f, Delegate handler, bool skipQueueOnMainThread = false) {
            f.RegisterOnResolved(
                skipQueueOnMainThread
                    ? OnResolvedDispatcher_SkipQueue
                    : OnResolvedDispatcher,
                handler
            );
        }
                
        /// <summary>
        /// Registers an OnResolved handler onto a specified future that will push the onComplete handler
        ///  onto this scheduler's work queue once the future is completed or disposed.
        /// </summary>
        /// <param name="skipQueueOnMainThread">If the future is completed/disposed from the main thread, run the handler synchronously</param>
        public void RegisterOnResolved (IFuture f, Action onComplete, bool skipQueueOnMainThread = false) {
            RegisterOnResolved_Impl(f, onComplete, skipQueueOnMainThread);
        }

        /// <summary>
        /// Registers an OnResolved handler onto a specified future that will push the onComplete handler
        ///  onto this scheduler's work queue once the future is completed or disposed.
        /// </summary>
        /// <param name="skipQueueOnMainThread">If the future is completed/disposed from the main thread, run the handler synchronously</param>
        public void RegisterOnResolved (IFuture f, OnFutureResolved onResolved, bool skipQueueOnMainThread = false) {
            RegisterOnResolved_Impl(f, onResolved, skipQueueOnMainThread);
        }
        
        private void RegisterOnResolved_Impl<T> (Future<T> f, Delegate onComplete, bool skipQueueOnMainThread = false) {
            if (skipQueueOnMainThread) {
                var dict = OnResolvedDispatchersForType_SkipQueue;
                Delegate handler;
                lock (dict)
                    dict.TryGetValue(typeof(T), out handler);
                if (handler == null) {
                    var genMethod = mOnResolvedDispatcher_SkipQueue;
                    var method = genMethod.MakeGenericMethod(typeof(T));
                    handler = Delegate.CreateDelegate(typeof(OnFutureResolvedWithData<T>), this, method, true);
                    lock (dict)
                        dict[typeof(T)] = handler;
                }
                f.RegisterOnResolved2((OnFutureResolvedWithData<T>)handler, onComplete);
            } else {
                f.RegisterOnResolved(OnResolvedDispatcher, onComplete);
            }
        }

        /// <summary>
        /// Registers an OnResolved handler onto a specified future that will push the onComplete handler
        ///  onto this scheduler's work queue once the future is completed or disposed.
        /// </summary>
        /// <param name="skipQueueOnMainThread">If the future is completed/disposed from the main thread, run the handler synchronously</param>
        public void RegisterOnResolved<T> (Future<T> f, Action<T> onComplete, bool skipQueueOnMainThread = false) {
            RegisterOnResolved_Impl(f, onComplete, skipQueueOnMainThread);
        }

        /// <summary>
        /// Registers an OnResolved handler onto a specified future that will push the onComplete handler
        ///  onto this scheduler's work queue once the future is completed or disposed.
        /// </summary>
        /// <param name="skipQueueOnMainThread">If the future is completed/disposed from the main thread, run the handler synchronously</param>
        public void RegisterOnResolved<T> (Future<T> f, OnFutureResolved<T> onComplete, bool skipQueueOnMainThread = false) {
            RegisterOnResolved_Impl(f, onComplete, skipQueueOnMainThread);
        }

        internal void SleepWorkerThreadFunc (PriorityQueue<SleepItem> pendingSleeps, System.Threading.ManualResetEventSlim newSleepEvent) {
            while (true) {
                long now = TimeProvider.Ticks;

                SleepItem currentSleep;
                Monitor.Enter(pendingSleeps);
                if (pendingSleeps.Peek(out currentSleep)) {
                    if (currentSleep.Tick(now)) {
                        pendingSleeps.Dequeue();
                        Monitor.Exit(pendingSleeps);
                        continue;
                    } else {
                        Monitor.Exit(pendingSleeps);
                    }
                } else {
                    Monitor.Exit(pendingSleeps);

                    if (!newSleepEvent.Wait(SleepThreadTimeoutMs))
                        return;

                    newSleepEvent.Reset();
                    continue;
                }

                long sleepUntil = currentSleep.Until;

                now = TimeProvider.Ticks;
                long timeToSleep = (sleepUntil - now) + SleepFudgeFactor;

                if (timeToSleep < SleepSpinThreshold) {
                    int iteration = 1;

                    while (TimeProvider.Ticks < sleepUntil) {
                        Thread.SpinWait(20 * iteration);
                        iteration += 1;
                    }

                    timeToSleep = 0;
                }

                if (timeToSleep > 0) {
                    if (timeToSleep > MaximumSleepLength)
                        timeToSleep = MaximumSleepLength;

                    int msToSleep = 0;
                    if (timeToSleep >= MinimumSleepLength) {
                        msToSleep = (int)(timeToSleep / Time.MillisecondInTicks);
                    }

                    if (newSleepEvent != null) {
                        newSleepEvent.Reset();
                        newSleepEvent.Wait(msToSleep);
                    }
                }
            }
        }

        internal void QueueSleep (long completeWhen, IFuture future) {
            if (_IsDisposed)
                return;

            long now = TimeProvider.Ticks;
            if (now > completeWhen) {
                using (IsActive)
                    future.Complete();

                return;
            }

            SleepItem sleep = new SleepItem { Until = completeWhen, Future = future };

            lock (_SleepWorker.WorkItems)
                _SleepWorker.WorkItems.Enqueue(sleep);
            _SleepWorker.Wake();
        }

        public void Step () {
            if (_IsDisposed)
                return;

            using (IsActive)
                _JobQueue.Step();
        }

        public object WaitFor (ISchedulable schedulable) {
            var f = Start(schedulable, TaskExecutionPolicy.RunWhileFutureLives);
            return WaitFor(f);
        }

        public object WaitFor (IEnumerator<object> task) {
            var f = Start(task, TaskExecutionPolicy.RunWhileFutureLives);
            return WaitFor(f);
        }

        public T WaitFor<T> (Future<T> future, double? timeout = null) {
            if (_IsDisposed)
                throw new ObjectDisposedException("TaskScheduler");

            if (!_JobQueue.CanPumpOnThisThread) {
                var evt = future.GetCompletionEvent();
                if (timeout.HasValue) {
                    if (evt.Wait((int)(timeout * 1000)))
                        return future.Result;
                    else
                        throw new TimeoutException();
                } else
                    evt.Wait();
            }

            long started = Stopwatch.GetTimestamp();

            using (IsActive)
            while (true) {
                if (_JobQueue.WaitForFuture(future))
                    return future.Result;

                if (timeout.HasValue) {
                    var elapsed = Stopwatch.GetTimestamp() - started;

                    if ((double)elapsed / Time.SecondInTicks >= timeout)
                        throw new TimeoutException();
                }
            }
        }

        public object WaitFor (IFuture future, double? timeout = null) {
            if (_IsDisposed)
                throw new ObjectDisposedException("TaskScheduler");

            long started = Stopwatch.GetTimestamp();

            using (IsActive)
            while (true) {
                if (_JobQueue.WaitForFuture(future))
                    return future.Result;

                if (timeout.HasValue) {
                    var elapsed = Stopwatch.GetTimestamp() - started;

                    if (((double)elapsed / Time.SecondInTicks) >= timeout)
                        throw new TimeoutException();
                }
            }
        }

        public bool HasPendingTasks {
            get {
                return (_JobQueue.Count > 0) || (_JobQueue.NextStepCount > 0);
            }
        }

        public bool IsDisposed {
            get {
                return _IsDisposed;
            }
        }

        public void Dispose () {
            if (_IsDisposed)
                return;

            _IsDisposed = true;
            Thread.MemoryBarrier();

            using (IsActive)
            lock (_SleepWorker.WorkItems) {
                while (_SleepWorker.WorkItems.Count > 0) {
                    var item = _SleepWorker.WorkItems.Dequeue();

                    try {
                        item.Future.Dispose();
                    } catch (FutureHandlerException fhe) {
                        // FIXME: Maybe we should introduce two levels of disposed state, and in the first level,
                        //  queueing work items silently fails?
                        if (!(fhe.InnerException is ObjectDisposedException))
                            throw;
                    }
                }
            }
                
            _SleepWorker.Dispose();
            _JobQueue.Dispose();
        }
    }
}
