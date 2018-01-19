using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Squared.Render {
    public sealed class Frame : IDisposable, IBatchContainer {
        private enum States : int {
            Initialized = 0,
            Preparing = 1,
            Prepared = 2,
            Drawing = 3,
            Drawn = 4,
            Disposed = 5
        }

        private static readonly object PrepareLock = new object();

        private readonly object BatchLock = new object();

        public static BatchComparer BatchComparer = new BatchComparer();

        private static ListPool<Batch> _ListPool = new ListPool<Batch>(
            16, 256, 4096
        );

        public RenderManager RenderManager;
        public int Index;
        public long InitialBatchCount;

        internal DenseList<Batch> Batches = new DenseList<Batch>();
        internal List<Batch> BatchesToRelease = new List<Batch>();

        volatile int State = (int)States.Disposed;

        // If you allocate a frame manually instead of using the pool, you'll need to initialize it
        //  yourself using Initialize (below).
        public Frame () {
        }

        RenderManager IBatchContainer.RenderManager {
            get {
                return RenderManager;
            }
        }

        internal void Initialize (RenderManager renderManager, int index) {
            Batches.ListPool = _ListPool;
            Batches.Clear();
            RenderManager = renderManager;
            Index = index;
            InitialBatchCount = Batch.LifetimeCount;
            State = (int)States.Initialized;
            BatchesToRelease.Clear();
        }

        public void Add (Batch batch) {
            if (State != (int)States.Initialized)
                throw new InvalidOperationException();

            if (batch == null)
                throw new ArgumentNullException("batch");

            lock (BatchLock) {
                /*
#if DEBUG && PARANOID
                if (Batches.Contains(batch))
                    throw new InvalidOperationException("Batch already added to this frame");
#endif
                */

                Batches.Add(batch);
                batch.Container = this;
            }
        }

        public void Prepare (bool parallel) {
            if (Interlocked.Exchange(ref State, (int)States.Preparing) != (int)States.Initialized)
                throw new InvalidOperationException("Frame was not in initialized state when prepare operation began ");

            if (false) {
                var totalBatches = Batch.LifetimeCount - InitialBatchCount;
                Console.WriteLine("Frame contains {0} batches", totalBatches);
            }

            var numRemoved = BatchCombiner.CombineBatches(ref Batches, BatchesToRelease);
            // Batch combining shuffles the batches around to group by type. Once it's done,
            //  we need to do the final sort to preserve layer and material ordering.
            Batches.Sort(BatchComparer);

            if (!Monitor.TryEnter(PrepareLock, 5000)) {
                throw new InvalidOperationException("Spent more than five seconds waiting for a previous prepare operation.");
            }

            try {
                if (parallel)
                    RenderManager.ParallelPrepareBatches(this);
                else
                    RenderManager.SynchronousPrepareBatches(this);
            } finally {
                Monitor.Exit(PrepareLock);
            }

            if (Interlocked.Exchange(ref State, (int)States.Prepared) != (int)States.Preparing)
                throw new InvalidOperationException("Frame was not in preparing state when prepare operation completed");
        }

        public void Draw () {
            if (Interlocked.Exchange(ref State, (int)States.Drawing) != (int)States.Prepared)
                throw new InvalidOperationException();

            if (Tracing.RenderTrace.EnableTracing)
                Tracing.RenderTrace.ImmediateMarker("Frame {0:0000} : Begin Draw", Index);

            var dm = RenderManager.DeviceManager;
            var device = dm.Device;

            int c = Batches.Count;
            var _batches = Batches.GetBuffer(false);
            for (int i = 0; i < c; i++) {
                var batch = _batches[i];
                if (batch != null)
                    batch.IssueAndWrapExceptions(dm);
            }

            dm.Finish();

            if (Tracing.RenderTrace.EnableTracing)
                Tracing.RenderTrace.ImmediateMarker("Frame {0:0000} : End Draw", Index);

            if (Interlocked.Exchange(ref State, (int)States.Drawn) != (int)States.Drawing)
                throw new InvalidOperationException();
        }

        public void Dispose () {
            if (State == (int)States.Disposed)
                return;

            foreach (var batch in BatchesToRelease)
                batch.ReleaseResources();

            BatchesToRelease.Clear();

            var _batches = Batches.GetBuffer(false);
            for (int i = 0; i < Batches.Count; i++) {
                var batch = _batches[i];
                if (batch != null)
                    batch.ReleaseResources();
            }

            Batches.Dispose();

            RenderManager.ReleaseFrame(this);

            State = (int)States.Disposed;
        }

        public bool IsReleased {
            get {
                return State == (int)States.Disposed;
            }
        }

        public override int GetHashCode() {
            return Index;
        }
    }
}
