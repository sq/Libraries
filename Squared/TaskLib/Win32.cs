using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Security;

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
                    Thread.Sleep(0);
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

    public class TaskForm : Form {
        public readonly TaskScheduler Scheduler;
        protected HashSet<IFuture> OwnedFutures = new HashSet<IFuture>();

        internal TaskForm ()
            : base() {
            Scheduler = null;
        }

        public TaskForm (TaskScheduler scheduler)
            : base() {
            Scheduler = scheduler;
        }

        public IFuture Start (ISchedulable schedulable) {
            var f = Scheduler.Start(schedulable, TaskExecutionPolicy.RunWhileFutureLives);
            OwnedFutures.Add(f);
            return f;
        }

        public IFuture Start (IEnumerator<object> task) {
            var f = Scheduler.Start(task, TaskExecutionPolicy.RunWhileFutureLives);
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

        protected override void Dispose (bool disposing) {
            foreach (var future in OwnedFutures)
                future.Dispose();
            OwnedFutures.Clear();

            base.Dispose(disposing);
        }
    }
}