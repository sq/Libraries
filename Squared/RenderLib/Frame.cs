using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Graphics;
using Squared.Render.Buffers;
using Squared.Render.Internal;
using Squared.Threading;
using Squared.Util;

namespace Squared.Render {
    public sealed class Frame : IDisposable, IBatchContainer {
        internal struct ReadbackWorkItem : IWorkItem {
            public static WorkItemConfiguration Configuration =>
                new WorkItemConfiguration {
                    MaxConcurrency = 1,
                    DefaultStepCount = 4
                };

            public object Lock;
            public Texture2D Source;
            public Array Destination;

            public void Execute (ThreadGroup group) {
                if (Source.IsDisposed)
                    return;

                var gch = GCHandle.Alloc(Destination, GCHandleType.Pinned);
                try {
                    var size = Marshal.SizeOf(Destination.GetType().GetElementType()) * Destination.Length;
                    lock (Lock)
                        Source.GetDataPointerEXT(0, null, gch.AddrOfPinnedObject(), size);
                } finally {
                    gch.Free();
                }
            }
        }

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
                new (() => new PolygonBuffer(), LazyThreadSafetyMode.ExecutionAndPublication);

            public void Initialize (RenderManager manager) {
                _PolygonBuffer.Value.Clear();
                Array.Clear(CornerBuffers, 0, CornerBuffers.Length);
                for (int i = 1; i < CornerBuffers.Length; i++)
                    CornerBuffers[i] = QuadUtils.CreateCornerBuffer(manager, i);
            }

            internal PolygonBuffer GetPolygonBuffer () {
                return _PolygonBuffer.Value;
            }

            public BufferGenerator<CornerVertex>.GeometryBuffer GetCornerBuffer (RenderManager manager, int repeatCount = 1) {
                var result = Volatile.Read(ref CornerBuffers[repeatCount]);
                if (result == null) {
                    result = QuadUtils.CreateCornerBuffer(manager, repeatCount);
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
        internal DenseList<ReadbackWorkItem> ReadbackQueue = new DenseList<ReadbackWorkItem>();

        volatile int State = (int)States.Disposed;

        // If you allocate a frame manually instead of using the pool, you'll need to initialize it
        //  yourself using Initialize (below).
        public Frame () {
        }

        public RenderCoordinator Coordinator { get; private set; }
        public RenderManager RenderManager { get; private set; }
        public FramePrepareData PrepareData { get; private set; }

        Frame IBatchContainer.Frame { get { return this; } }
        int IBatchContainer.FrameIndex { get { return Index; } }

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
        }

        public unsafe void Readback<T> (Texture2D source, T[] destination)
            where T : unmanaged
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));
            var destinationLengthInBytes = sizeof(T) * destination.Length;
            var requiredSize = source.Width * source.Height * Texture.GetFormatSizeEXT(source.Format);
            if (requiredSize > destinationLengthInBytes)
                throw new ArgumentException($"Readback buffer too small: {destinationLengthInBytes} < {requiredSize}");
            if (source.IsDisposed)
                throw new ObjectDisposedException(nameof(source));
            ReadbackQueue.Add(new ReadbackWorkItem {
                Lock = Coordinator.UseResourceLock,
                Source = source,
                Destination = destination
            });
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

            // This has to happen late, right before we prepare our batches, because the buffer generators
            //  get reset pretty late instead of when the frame is initialized.
            PrepareData.Initialize(RenderManager);

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

        internal void PerformReadbackAsync (ThreadGroup group) {
            if (ReadbackQueue.Count == 0)
                return;

            group.GetQueueForType<ReadbackWorkItem>().WaitUntilDrained(100);

            var started = Time.Ticks;
            foreach (var rb in ReadbackQueue)
                group.Enqueue(rb);
            var ended = Time.Ticks;
            // Debug.WriteLine($"Readback took {Time.SecondsFromTicks(ended - started)}sec");

            ReadbackQueue.Clear();
        }

        internal void PerformReadback () {
            if (ReadbackQueue.Count == 0)
                return;

            var started = Time.Ticks;
            foreach (var rb in ReadbackQueue)
                rb.Execute(null);
            var ended = Time.Ticks;
            // Debug.WriteLine($"Readback took {Time.SecondsFromTicks(ended - started)}sec");

            ReadbackQueue.Clear();
        }

        internal void Draw () {
            if (Interlocked.Exchange(ref State, (int)States.Drawing) != (int)States.Prepared)
                throw new InvalidOperationException();

            var dm = RenderManager.DeviceManager;
            dm.FrameIndex = Index;
            dm.RenderStartTimeSeconds = (float)Time.Seconds;
            var device = dm.Device;

            if (Tracing.RenderTrace.EnableTracing)
                Tracing.RenderTrace.ImmediateMarker(device, "{1} {0:0000} : Begin Draw", Index, Label);

            RenderManager.PrepareManager.UpdateTextureCache();

            dm.Begin(ChangeRenderTargets);

            try {
                int c = Batches.Count;
                var _batches = Batches.GetBuffer(false);
                for (int i = 0; i < c; i++) {
                    var batch = _batches[i];
                    if (batch != null)
                        batch.IssueAndWrapExceptions(dm);
                }
            } finally {
                dm.Finish();
                RenderManager.PrepareManager.CleanupTextureCache();
                while (RenderManager.ReleaseQueue.TryDequeue(out var batch))
                    batch.ReleaseResources();
            }

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
