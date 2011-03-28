using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Security;
using Squared.Util.Event;
using Squared.Util;
using System.Collections.Concurrent;

namespace Squared.Task {
    public static partial class JobQueue {
        public static IJobQueue WindowsMessageBased () {
            return new WindowsMessageJobQueue();
        }
    }

    public class WindowsMessageJobQueue : NativeWindow, IJobQueue {
        [DllImport("User32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern int RegisterWindowMessage (string lpString);

        [return: MarshalAs(UnmanagedType.Bool)]
        [DllImport("user32.dll", SetLastError = true)]
        [SuppressUnmanagedCodeSecurity]
        internal static extern bool PostMessage (IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        private int WM_RUN_WORK_ITEM;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        private ConcurrentQueue<Action> _Queue = new ConcurrentQueue<Action>();
        private int StepIsPending = 0;
        private int ExecutionDepth = 0;

        public const int StepDurationCheckInterval = 500;
        public static readonly long? DefaultMaxStepDuration = Squared.Util.Time.SecondInTicks / 100;

        public readonly long? MaxStepDuration;
        public event MaxStepDurationExceededHandler MaxStepDurationExceeded;

        public WindowsMessageJobQueue ()
            : this(DefaultMaxStepDuration) {
        }

        public WindowsMessageJobQueue (long? maxStepDuration)
            : base() {
            WM_RUN_WORK_ITEM = RegisterWindowMessage("Squared.TaskLib.RunWorkItem");
            var cp = new CreateParams {
                Caption = "Squared.TaskLib.Win32JobQueue",
                X = 0,
                Y = 0,
                Width = 0,
                Height = 0,
                Style = 0,
                ExStyle = WS_EX_NOACTIVATE,
                Parent = new IntPtr(-3)
            };
            CreateHandle(cp);

            MaxStepDuration = maxStepDuration;
        }

        protected override void WndProc (ref Message m) {
            if (m.Msg == WM_RUN_WORK_ITEM) {
                InternalStep(MaxStepDuration);
            } else {
                base.WndProc(ref m);
            }
        }

        public void QueueWorkItem (Action item) {
            _Queue.Enqueue(item);
            MarkPendingStep();
        }

        protected void MarkPendingStep () {
            if (Interlocked.CompareExchange(ref StepIsPending, 1, 0) == 0)
                PostMessage(Handle, WM_RUN_WORK_ITEM, IntPtr.Zero, IntPtr.Zero);
        }

        protected bool OnMaxStepDurationExceeded (long elapsedTicks) {
            if (MaxStepDurationExceeded != null)
                return MaxStepDurationExceeded(elapsedTicks);
            else
                return false;
        }

        protected void InternalStep (long? maxStepDuration) {
            StepIsPending = 0;

            long stepStarted = 0;
            if (maxStepDuration.HasValue)
                stepStarted = Time.Ticks;

            int i = 0;
            Action item;
            while (_Queue.TryDequeue(out item)) {
                if (item != null)
                    item();

                i++;

                if ((maxStepDuration.HasValue) && ((i % StepDurationCheckInterval) == 0)) {
                    var elapsedTicks = (Time.Ticks - stepStarted);
                    if (elapsedTicks > maxStepDuration.Value) {
                        if (!OnMaxStepDurationExceeded(elapsedTicks))
                            return;

                        MarkPendingStep();
                    }
                }
            }
        }

        // Flush the message queue, then sleep for a moment if the future is still not completed.
        public bool WaitForFuture (IFuture future) {
            if (!future.Completed)
                Application.DoEvents();
            if (!future.Completed)
                Thread.Sleep(0);

            if (future.Disposed)
                throw new FutureDisposedException(future);

            return future.Completed;
        }

        public void Step () {
            try {
                int depth = ++ExecutionDepth;
                if (depth > 1)
                    throw new InvalidOperationException("A manual step is already in progress for this job queue");

                Application.DoEvents();
            } finally {
                ExecutionDepth--;
            }
        }

        public bool WaitForWorkItems (double timeout) {
            return (Count > 0);
        }

        public int Count {
            get {
                lock (_Queue)
                    return _Queue.Count;
            }
        }

        public void Dispose () {
            DestroyHandle();
        }
    }

    public interface ITaskOwner {
        IFuture Start (ISchedulable schedulable);
        IFuture Start (IEnumerator<object> task);
    }

    public class ControlWaitCursor : IDisposable {
        public readonly Control Control;
        public readonly bool OldUseWaitCursor;

        public ControlWaitCursor (Control ctl) {
            Control = ctl;
            OldUseWaitCursor = ctl.UseWaitCursor;
            ctl.UseWaitCursor = true;
        }

        public virtual void Dispose () {
            Control.UseWaitCursor = OldUseWaitCursor;
        }
    }

    public class ControlDisabler : ControlWaitCursor {
        public readonly bool OldEnabled;

        public ControlDisabler (Control ctl) 
            : base (ctl) {
            OldEnabled = ctl.Enabled;
            ctl.Enabled = false;
        }

        public override void Dispose () {
            base.Dispose();
            Control.Enabled = OldEnabled;
        }
    }

    public class TaskForm : Form, ITaskOwner {
        public readonly TaskScheduler Scheduler;
        protected OwnedFutureSet OwnedFutures = new OwnedFutureSet();
        protected HashSet<EventSubscription> OwnedSubscriptions = new HashSet<EventSubscription>();

        internal TaskForm ()
            : base() {
            Scheduler = null;
        }

        public TaskForm (TaskScheduler scheduler)
            : base() {
            Scheduler = scheduler;
        }

        public IFuture Start (ISchedulable schedulable) {
            var f = Scheduler.Start(schedulable, TaskExecutionPolicy.RunAsBackgroundTask);
            OwnedFutures.Add(f);
            return f;
        }

        public IFuture Start (IEnumerator<object> task) {
            var f = Scheduler.Start(task, TaskExecutionPolicy.RunAsBackgroundTask);
            OwnedFutures.Add(f);
            return f;
        }

        new public SignalFuture Show () {
            return this.Show(null);
        }

        new public SignalFuture Show (IWin32Window owner) {
            var f = new SignalFuture();
            FormClosedEventHandler del = (s, e) =>
                f.Complete();
            f.RegisterOnComplete((_) => {
                this.FormClosed -= del;
            });
            this.FormClosed += del;

            if (owner != null)
                base.Show(owner);
            else
                base.Show();

            return f;
        }

        public void SubscribeTo (EventBus eventBus, object source, string type, EventSubscriber subscriber) {
            OwnedSubscriptions.Add(eventBus.Subscribe(source, type, subscriber));
        }

        public void SubscribeTo<T> (EventBus eventBus, object source, string type, TypedEventSubscriber<T> subscriber) 
            where T : class {
            OwnedSubscriptions.Add(eventBus.Subscribe(source, type, subscriber));
        }

        public void SubscribeTo (EventBus eventBus, object source, string type, Func<EventInfo, IEnumerator<object>> subscriber) {
            OwnedSubscriptions.Add(eventBus.Subscribe(source, type, Scheduler, subscriber));
        }

        public void SubscribeTo<T> (EventBus eventBus, object source, string type, Func<EventInfo, T, IEnumerator<object>> subscriber)
            where T : class {
            OwnedSubscriptions.Add(eventBus.Subscribe(source, type, Scheduler, subscriber));
        }

        protected override void Dispose (bool disposing) {
            OwnedFutures.Dispose();

            foreach (var subscription in OwnedSubscriptions)
                subscription.Dispose();
            OwnedSubscriptions.Clear();

            base.Dispose(disposing);
        }
    }

    public class TaskUserControl : UserControl, ITaskOwner {
        protected TaskScheduler _Scheduler = null;
        protected OwnedFutureSet OwnedFutures = new OwnedFutureSet();

        public TaskUserControl ()
            : base() {
            _Scheduler = null;
        }

        public TaskUserControl (TaskScheduler scheduler)
            : base() {
            _Scheduler = scheduler;
        }

        public IFuture Start (ISchedulable schedulable) {
            var f = Scheduler.Start(schedulable, TaskExecutionPolicy.RunAsBackgroundTask);
            OwnedFutures.Add(f);
            return f;
        }

        public IFuture Start (IEnumerator<object> task) {
            var f = Scheduler.Start(task, TaskExecutionPolicy.RunAsBackgroundTask);
            OwnedFutures.Add(f);
            return f;
        }

        public TaskScheduler Scheduler {
            get {
                return _Scheduler;
            }
            set {
                if (_Scheduler == null)
                    _Scheduler = value;
                else
                    throw new InvalidOperationException("Already has a scheduler set");
            }
        }

        protected override void Dispose (bool disposing) {
            OwnedFutures.Dispose();

            base.Dispose(disposing);
        }
    }
}