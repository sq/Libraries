using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Graphics;
using Squared.Util;

namespace Squared.Render {
    public class RenderTimer : IDisposable {
        private static RenderTimer Newest;

        public bool IsDisposed { get; private set; }
        public event Action<RenderTimer> Completed;

        private readonly OcclusionQuery Query;
        private readonly ITimeProvider  TimeProvider;

        private long  CPUStarted;
        private long? CPUEnded;
        private long? GPUEnded;

        public RenderTimer (
            GraphicsDevice device, ITimeProvider timeProvider = null
        ) {
            Query = new OcclusionQuery(device);
            TimeProvider = timeProvider ?? Time.DefaultTimeProvider;
        }

        public void Begin () {
            CPUStarted = TimeProvider.Ticks;
            Query.Begin();

            RenderTimerManager.EnqueueTimer(this);
        }

        public void End () {
            Query.End();
            CPUEnded = TimeProvider.Ticks;
        }

        public double? CPUDuration {
            get {
                if (!CPUEnded.HasValue)
                    return null;

                return (CPUEnded.Value - CPUStarted) / (double)Time.SecondInTicks;
            }
        }

        public double? GPUDuration {
            get {
                if (!GPUEnded.HasValue)
                    return null;

                return (GPUEnded.Value - CPUStarted) / (double)Time.SecondInTicks;
            }
        }

        internal static void OnComplete (object sender) {
            var timer = (RenderTimer)sender;
            timer.Completed(timer);
        }

        internal bool Tick () {
            if (IsDisposed)
                return true;

            if (Query.IsComplete) {
                GPUEnded = TimeProvider.Ticks;

                if (Completed != null)
                    ThreadPool.QueueUserWorkItem(OnComplete, this);

                Dispose();
                return true;
            }

            return false;
        }

        public void Dispose () {
            if (!IsDisposed)
                Query.Dispose();

            IsDisposed = true;
        }
    }

    public static class RenderTimerManager {
        private static readonly UnorderedList<RenderTimer> PendingTimers = new UnorderedList<RenderTimer>();
        private static volatile bool IsPolling;

        private static void PollingTask (object _) {
            IsPolling = true;

            try {
                int count;
                do {
                    RenderTimer timer;

                    lock (PendingTimers) {
                        count = PendingTimers.Count;
                        using (var enumerator = PendingTimers.GetEnumerator())
                        while (enumerator.GetNext(out timer)) {
                            if (timer.Tick())
                                enumerator.RemoveCurrent();
                        }
                    }

                    Thread.Sleep(0);
                } while (count > 0);
            } finally {
                IsPolling = false;
            }
        }

        public static void EnqueueTimer (RenderTimer timer) {
            lock (PendingTimers) {
                PendingTimers.Add(timer);

                if (!IsPolling)
                    ThreadPool.QueueUserWorkItem(PollingTask);
            }
        }
    }
}
