using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Graphics;
using Squared.Render.Buffers;
using Squared.Render.Internal;
using Squared.Task;
using Squared.Threading;
using Squared.Util;

namespace Squared.Render {
    public sealed class Frame : IDisposable, IBatchContainer {
        public interface ISlab {
            Type Type { get; }
            void Reset ();
        }

        internal class Slab<T> : ISlab {
            public const int IdealSizeInBytesNonRef = 1024 * 1024,
                IdealSizeInBytesRef = 1024 * 32;
            public const int MinimumItemCount = 256;

            public T[] Array;
            public int UsedSlots;

            private static int EstimatedItemSize,
                IdealSizeInItems;

            private static int EstimateSize (Type type, ref bool containsRefs) {
                if (!type.IsValueType) {
                    containsRefs = true;
                    return Environment.Is64BitProcess ? 8 : 4;
                } else if (type.IsPrimitive)
                    return Marshal.SizeOf(type);

                var result = 0;
                foreach (var field in type.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
                    result += EstimateSize(field.FieldType, ref containsRefs);
                return result;
            }

            static Slab () {
                var t = typeof(T);
                bool containsRefs = false;
                EstimatedItemSize = EstimateSize(t, ref containsRefs);
                var idealSizeInBytes = containsRefs ? IdealSizeInBytesRef : IdealSizeInBytesNonRef;
                IdealSizeInItems = (idealSizeInBytes + EstimatedItemSize - 1) / EstimatedItemSize;
                if (IdealSizeInItems < MinimumItemCount)
                    IdealSizeInItems = MinimumItemCount;
            }

            public Slab (int minimumCapacity) {
                var itemCount = IdealSizeInItems;
                if (itemCount < minimumCapacity)
                    itemCount = minimumCapacity;
                Array = new T[itemCount];
                UsedSlots = 0;
            }

            public Type Type => typeof(T);

            public bool TryAllocate (int count, out ArraySegment<T> result) {
                var array = Array;
                var space = array.Length - UsedSlots;
                if (space < count) {
                    result = default;
                    return false;
                }

                result = new ArraySegment<T>(Array, UsedSlots, count);
                UsedSlots += count;
                return true;
            }

            public void Reset () {
                System.Array.Clear(Array, 0, UsedSlots);
                UsedSlots = 0;
            }
        }

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

        private Dictionary<Type, List<ISlab>> _Slabs = new (1024, ReferenceComparer<Type>.Instance);

        public static BatchComparer BatchComparer = new BatchComparer();

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

        internal ListBatchDrawCalls<Batch> Batches = default;
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
            // Batches.ListPool = _ListPool;
            Batches.Initialize(this, false);
            Coordinator = coordinator;
            this.RenderManager = renderManager;
            Index = index;
            State = (int)States.Initialized;
            Label = "Frame";
            ChangeRenderTargets = true;
            if (PrepareData == null)
                PrepareData = new FramePrepareData();

            lock (_Slabs)
            foreach (var list in _Slabs.Values) {
                lock (list)
                foreach (var slab in list)
                    slab.Reset();
            }
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

                Batches.Add(ref batch);
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

            var q = group.GetQueueForType<ReadbackWorkItem>();
            q.WaitUntilDrained(100);

            var started = Time.Ticks;
            foreach (var rb in ReadbackQueue)
                q.Enqueue(rb);
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
                var items = Batches.Items;
                for (int i = 0, c = items.Count; i < c; i++)
                    items.Array[i + items.Offset]?.IssueAndWrapExceptions(dm);
            } finally {
                dm.Finish();
                RenderManager.PrepareManager.CleanupTextureCache();
                lock (RenderManager.ReleaseQueue) {
                    while (RenderManager.ReleaseQueue.Count > 0)
                        RenderManager.ReleaseQueue.Dequeue().ReleaseResources();
                }
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

        internal ArraySegment<T> AllocateFromSlab<T> (int newSize) {
            var slabs = _Slabs;
            List<ISlab> slabList;
            var t = typeof(T);

            lock (slabs) {
                slabs.TryGetValue(t, out slabList);
                if (slabList == null) {
                    slabList = new List<ISlab>();
                    slabs.Add(t, slabList);
                }
            }

            lock (slabList) {
                foreach (var _slab in slabList) {
                    var slab = (Slab<T>)_slab;
                    if (slab.TryAllocate(newSize, out var result))
                        return result;
                }

                {
                    var slab = new Slab<T>(newSize);
                    slabList.Add(slab);
                    slab.TryAllocate(newSize, out var result);
                    return result;
                }
            }
        }
    }
}
