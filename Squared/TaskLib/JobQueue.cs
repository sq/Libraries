using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Security;

namespace Squared.Task {
    public interface IJobQueue : IDisposable {
        void QueueWorkItem (Action item);
        void Clear ();
        void Step ();
        void WaitForFuture (Future future);
        bool WaitForWorkItems (double timeout);
        int Count { get; }
    }

    public static class JobQueue {
        public static IJobQueue SingleThreaded () {
            return new SingleThreadedJobQueue();
        }

        public static IJobQueue MultiThreaded () {
            return new MultiThreadedJobQueue();
        }

        public static IJobQueue WindowsMessageBased () {
            return new WindowsMessageJobQueue();
        }
    }

    public class SingleThreadedJobQueue : IJobQueue {
        protected Queue<Action> _Queue = new Queue<Action>();

        public virtual void QueueWorkItem (Action item) {
            _Queue.Enqueue(item);
        }

        public void Step () {
            Action item = null;
            do {
                item = GetNextWorkItem();
                if (item != null)
                    item();
            } while (item != null);
        }

        public void WaitForFuture (Future future) {
            Action item = null;
            while (!future.Completed) {
                item = GetNextWorkItem();
                if (item != null)
                    item();
            }
        }

        protected virtual Action GetNextWorkItem () {
            if (_Queue.Count == 0)
                return null;

            return _Queue.Dequeue();
        }

        public virtual bool WaitForWorkItems (double timeout) {
            return (Count > 0);
        }

        public virtual void Clear () {
            _Queue.Clear();
        }

        public virtual int Count {
            get {
                int count = _Queue.Count;
                return count;
            }
        }

        public virtual void Dispose () {
            Clear();
        }
    }

    public class MultiThreadedJobQueue : SingleThreadedJobQueue {
        private ManualResetEvent _NewWorkItemEvent = new ManualResetEvent(false);

        public override void QueueWorkItem (Action item) {
            lock (_Queue)
                base.QueueWorkItem(item);

            if (!_NewWorkItemEvent.SafeWaitHandle.IsClosed)
                _NewWorkItemEvent.Set();
        }

        protected override Action GetNextWorkItem() {
            lock (_Queue)
                return base.GetNextWorkItem();
        }

        public override bool WaitForWorkItems (double timeout) {
            lock (_Queue) {
                if (_Queue.Count != 0)
                    return true;
                else
                    _NewWorkItemEvent.Reset();
            }

            if (timeout > 0) {
                bool result = _NewWorkItemEvent.WaitOne((int)Math.Floor(timeout * 1000), true);
                return result;
            } else {
                _NewWorkItemEvent.WaitOne();
                return true;
            }
        }

        public override void Clear () {
            lock (_Queue)
                base.Clear();
        }

        public override int Count {
            get {
                lock (_Queue)
                    return base.Count;
            }
        }

        public override void Dispose () {
            base.Dispose();
            _NewWorkItemEvent.Close();
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
            : base () {
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

        public void WaitForFuture (Future future) {
            while (!future.Completed) {
                Action item = null;
                lock (_Queue)
                    if (_Queue.Count > 0)
                        item = _Queue.Dequeue();

                if (item != null)
                    item();
                else
                    Thread.Sleep(0);
            }
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
}
