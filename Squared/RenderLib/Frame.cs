using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Squared.Render.Internal;
using Squared.Util;

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

        public bool ChangeRenderTargets;
        public string Label;

        public int Index;

        public sealed class FramePrepareData {
            private BufferGenerator<CornerVertex>.GeometryBuffer[] CornerBuffers =
                new BufferGenerator<CornerVertex>.GeometryBuffer[8];
            private Lazy<PolygonBuffer> _PolygonBuffer = 
                new Lazy<PolygonBuffer>(() => new PolygonBuffer(), LazyThreadSafetyMode.ExecutionAndPublication);

            public void Initialize () {
                _PolygonBuffer.Value.Clear();
                Array.Clear(CornerBuffers, 0, CornerBuffers.Length);
            }

            internal PolygonBuffer GetPolygonBuffer (IBatchContainer container) {
                return _PolygonBuffer.Value;
            }

            public BufferGenerator<CornerVertex>.GeometryBuffer GetCornerBuffer (IBatchContainer container, int repeatCount = 1) {
                var result = Volatile.Read(ref CornerBuffers[repeatCount]);
                if (result == null) {
                    result = QuadUtils.CreateCornerBuffer(container, repeatCount);
                    var exchanged = Interlocked.CompareExchange(ref CornerBuffers[repeatCount], result, null);
                    if (exchanged != null)
                        // FIXME: Dispose result?
                        ;
                }
                return result;
            }
        }

        internal DenseList<Batch> Batches = new DenseList<Batch>();
        internal DenseList<Batch> BatchesToRelease = new DenseList<Batch>();

        volatile int State = (int)States.Disposed;

        // If you allocate a frame manually instead of using the pool, you'll need to initialize it
        //  yourself using Initialize (below).
        public Frame () {
        }

        public RenderCoordinator Coordinator { get; private set; }
        public RenderManager RenderManager { get; private set; }
        public FramePrepareData PrepareData { get; private set; }

        Frame IBatchContainer.Frame { get { return this; } }

        internal void Initialize (RenderCoordinator coordinator, RenderManager renderManager, int index) {
            Batches.ListPool = _ListPool;
            Batches.Clear();
            Coordinator = coordinator;
            this.RenderManager = renderManager;
            Index = index;
            State = (int)States.Initialized;
            Label = "Frame";
            ChangeRenderTargets = true;
            if (PrepareData == null)
                PrepareData = new FramePrepareData();
            else
                PrepareData.Initialize();
        }

        public void Add (Batch batch) {
            var state = State;
            if (state != (int)States.Initialized)
                throw new InvalidOperationException($"Frame state should have been initialized but was {(States)state}");

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

        void IBatchContainer.PrepareChildren (ref Batch.PrepareContext context) {
            throw new InvalidOperationException("This should never be invoked on a Frame");
        }

        public void Prepare (bool parallel) {
            if (Interlocked.Exchange(ref State, (int)States.Preparing) != (int)States.Initialized)
                throw new InvalidOperationException("Frame was not in initialized state when prepare operation began ");

            var numRemoved = BatchCombiner.CombineBatches(ref Batches, ref BatchesToRelease);
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

            var dm = RenderManager.DeviceManager;
            dm.FrameIndex = Index;
            var device = dm.Device;

            if (Tracing.RenderTrace.EnableTracing)
                Tracing.RenderTrace.ImmediateMarker(device, "{1} {0:0000} : Begin Draw", Index, Label);

            RenderManager.PrepareManager.UpdateTextureCache();

            dm.Begin(ChangeRenderTargets);

            int c = Batches.Count;
            var _batches = Batches.GetBuffer(false);
            for (int i = 0; i < c; i++) {
                var batch = _batches[i];
                if (batch != null)
                    batch.IssueAndWrapExceptions(dm);
            }

            dm.Finish();

            RenderManager.PrepareManager.CleanupTextureCache();

            if (Tracing.RenderTrace.EnableTracing)
                Tracing.RenderTrace.ImmediateMarker(device, "Frame {0:0000} : End Draw", Index);

            if (Interlocked.Exchange(ref State, (int)States.Drawn) != (int)States.Drawing)
                throw new InvalidOperationException();
        }

        public void Dispose () {
            if (State == (int)States.Disposed)
                return;

            foreach (var batch in BatchesToRelease)
                batch.ReleaseResources();

            BatchesToRelease.Clear();

            Batch temp;
            for (int i = 0, c = Batches.Count; i < Batches.Count; i++) {
                if (Batches.TryGetItem(i, out temp))
                    temp.ReleaseResources();
            }

            Batches.Dispose();

            RenderManager.ReleaseFrame(this);

            State = (int)States.Disposed;
        }

        bool IBatchContainer.IsEmpty {
            get {
                return Batches.Count == 0;
            }
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
