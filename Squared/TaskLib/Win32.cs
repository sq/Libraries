using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Security;
using Squared.Util.Event;

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

        private Queue<Action> _Queue = new Queue<Action>();
        private int ExecutionDepth = 0;

        public WindowsMessageJobQueue ()
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
        }

        protected override void WndProc (ref Message m) {
            if (m.Msg == WM_RUN_WORK_ITEM) {
                SingleStep();
            } else {
                base.WndProc(ref m);
            }
        }

        public void QueueWorkItem (Action item) {
            lock (_Queue)
                _Queue.Enqueue(item);
            PostMessage(Handle, WM_RUN_WORK_ITEM, IntPtr.Zero, IntPtr.Zero);
        }

        public void Clear () {
            lock (_Queue)
                _Queue.Clear();
        }

        internal void SingleStep () {
            Action item = null;
            lock (_Queue) {
                if (_Queue.Count == 0)
                    return;
                item = _Queue.Dequeue();
            }
            if (item != null) {
                try {
                    int depth = Interlocked.Increment(ref ExecutionDepth);
                    item();
                } finally {
                    Interlocked.Decrement(ref ExecutionDepth);
                }
            }
        }

        public bool WaitForFuture (IFuture future) {
            while (!future.Completed) {
                Action item = null;
                lock (_Queue)
                    if (_Queue.Count > 0)
                        item = _Queue.Dequeue();

                if (item != null)
                    item();
                else {
                    Application.DoEvents();
                    Thread.Sleep(10);
                    return false;
                }
            }

            return true;
        }

        public void Step () {
            try {
                int depth = Interlocked.Increment(ref ExecutionDepth);
                if (depth > 1)
                    throw new InvalidOperationException();

                Application.DoEvents();
            } finally {
                Interlocked.Decrement(ref ExecutionDepth);
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
        protected HashSet<IFuture> OwnedFutures = new HashSet<IFuture>();
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
            foreach (var future in OwnedFutures)
                future.Dispose();
            OwnedFutures.Clear();

            foreach (var subscription in OwnedSubscriptions)
                subscription.Dispose();
            OwnedSubscriptions.Clear();

            base.Dispose(disposing);
        }
    }

    public class TaskUserControl : UserControl, ITaskOwner {
        public readonly TaskScheduler Scheduler;
        protected HashSet<IFuture> OwnedFutures = new HashSet<IFuture>();

        internal TaskUserControl ()
            : base() {
            Scheduler = null;
        }

        public TaskUserControl (TaskScheduler scheduler)
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

        protected override void Dispose (bool disposing) {
            foreach (var future in OwnedFutures)
                future.Dispose();
            OwnedFutures.Clear();

            base.Dispose(disposing);
        }
    }
}