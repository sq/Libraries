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
using Squared.Threading;

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

        private ConcurrentQueue<WorkItemQueueEntry> _Queue = new ConcurrentQueue<WorkItemQueueEntry>();
        private ConcurrentQueue<WorkItemQueueEntry> _NextStepQueue = new ConcurrentQueue<WorkItemQueueEntry>();
        private int StepIsPending = 0;
        private int ExecutionDepth = 0;

        public const int StepDurationCheckInterval = 500;
        public static readonly long? DefaultMaxStepDuration = Squared.Util.Time.SecondInTicks / 100;

        public readonly long? MaxStepDuration;
        public event MaxStepDurationExceededHandler MaxStepDurationExceeded;
        public event UnhandledExceptionEventHandler UnhandledException;

        public readonly Thread OwnerThread;
        public ThreadGroup ThreadGroup;

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

            OwnerThread = Thread.CurrentThread;
        }

        protected override void WndProc (ref Message m) {
            if (m.Msg == WM_RUN_WORK_ITEM) {
                InternalStep(MaxStepDuration);
            } else {
                base.WndProc(ref m);
            }
        }

        public void QueueWorkItem (Action item) {
            _Queue.Enqueue(new WorkItemQueueEntry { Action = item });
            MarkPendingStep();
        }

        public void QueueWorkItem (WorkItemQueueEntry entry) {
            _Queue.Enqueue(entry);
            MarkPendingStep();
        }

        public void QueueWorkItemForNextStep (Action item) {
            _NextStepQueue.Enqueue(new WorkItemQueueEntry { Action = item });
            MarkPendingStep();
        }

        public void QueueWorkItemForNextStep (WorkItemQueueEntry entry) {
            _NextStepQueue.Enqueue(entry);
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

            bool markPending = false;

            int i = 0;
            WorkItemQueueEntry item;
            while (_Queue.TryDequeue(out item)) {
                try {
                    item.Invoke();
                } catch (Exception exc) {
                    if (UnhandledException != null)
                        UnhandledException(this, new UnhandledExceptionEventArgs(exc, false));
                    else
                        throw;
                }

                i++;

                if ((maxStepDuration.HasValue) && ((i % StepDurationCheckInterval) == 0)) {
                    var elapsedTicks = (Time.Ticks - stepStarted);

                    if (elapsedTicks > maxStepDuration.Value) {
                        if (!OnMaxStepDurationExceeded(elapsedTicks)) {
                            markPending = true;
                            break;
                        }
                    }
                }
            }

            while (_NextStepQueue.TryDequeue(out item)) {
                _Queue.Enqueue(item);
                markPending = true;
            }

            if (markPending)
                MarkPendingStep();
        }

        public bool CanPumpOnThisThread {
            get {
                return Thread.CurrentThread == OwnerThread;
            }
        }

        // Flush the message queue, then sleep for a moment if the future is still not completed.
        public bool WaitForFuture (IFuture future) {
            if (!future.Completed)
                Application.DoEvents();
            if (!future.Completed)
                ThreadGroup?.StepMainThread();
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
            return !IsEmpty;
        }

        public bool IsEmpty => _Queue.IsEmpty;
        public bool NextStepIsEmpty => _NextStepQueue.IsEmpty;

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
}