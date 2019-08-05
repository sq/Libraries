#define USE_INDEXED_SORT

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework;
using Squared.Game;
using Squared.Render.Internal;
using Squared.Render.Tracing;
using Squared.Util;
using System.Reflection;
using Squared.Util.DeclarativeSort;
using System.Threading;
using System.Runtime.CompilerServices;

namespace Squared.Render {
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct CornerVertex : IVertexType {
        public short Corner;
        public short Unused;

        public static readonly VertexElement[] Elements;
        static readonly VertexDeclaration _VertexDeclaration;

        static CornerVertex () {
            var tThis = typeof(CornerVertex);

            Elements = new VertexElement[] {
                new VertexElement( Marshal.OffsetOf(tThis, "Corner").ToInt32(), 
                    VertexElementFormat.Short2, VertexElementUsage.BlendIndices, 0 )
            };

            _VertexDeclaration = new VertexDeclaration(Elements);
        }

        public VertexDeclaration VertexDeclaration {
            get { return _VertexDeclaration; }
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct BitmapVertex : IVertexType {
        public Vector3 Position;
        public Vector4 Texture1Region;
        public Vector4 Texture2Region;
        public Vector2 Scale;
        public Vector2 Origin;
        public float Rotation;
        public Color MultiplyColor;
        public Color AddColor;

        public static readonly VertexElement[] Elements;
        static readonly VertexDeclaration _VertexDeclaration;

        static BitmapVertex () {
            var tThis = typeof(BitmapVertex);

            Elements = new VertexElement[] {
                new VertexElement( Marshal.OffsetOf(tThis, "Position").ToInt32(), 
                    VertexElementFormat.Vector3, VertexElementUsage.Position, 0 ),
                new VertexElement( Marshal.OffsetOf(tThis, "Texture1Region").ToInt32(), 
                    VertexElementFormat.Vector4, VertexElementUsage.Position, 1 ),
                new VertexElement( Marshal.OffsetOf(tThis, "Texture2Region").ToInt32(), 
                    VertexElementFormat.Vector4, VertexElementUsage.Position, 2 ),
                // ScaleOrigin
                new VertexElement( Marshal.OffsetOf(tThis, "Scale").ToInt32(), 
                    VertexElementFormat.Vector4, VertexElementUsage.Position, 3 ),
                new VertexElement( Marshal.OffsetOf(tThis, "Rotation").ToInt32(), 
                    VertexElementFormat.Single, VertexElementUsage.Position, 4 ),
                new VertexElement( Marshal.OffsetOf(tThis, "MultiplyColor").ToInt32(), 
                    VertexElementFormat.Color, VertexElementUsage.Color, 0 ),
                new VertexElement( Marshal.OffsetOf(tThis, "AddColor").ToInt32(), 
                    VertexElementFormat.Color, VertexElementUsage.Color, 1 ),
            };

            _VertexDeclaration = new VertexDeclaration(Elements);
        }

        public VertexDeclaration VertexDeclaration {
            get { return _VertexDeclaration; }
        }
    }

    public sealed class BitmapDrawCallSorterComparer : IRefComparer<BitmapDrawCall>, IComparer<BitmapDrawCall> {
        public Sorter<BitmapDrawCall>.SorterComparer Comparer;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Compare (ref BitmapDrawCall x, ref BitmapDrawCall y) {
            var result = Comparer.Compare(x, y);

            if (result == 0) {
                result = (x.Textures.HashCode > y.Textures.HashCode)
                ? 1
                : (
                    (x.Textures.HashCode < y.Textures.HashCode)
                    ? -1
                    : 0
                );
            }

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Compare (BitmapDrawCall x, BitmapDrawCall y) {
            return Compare(ref x, ref y);
        }
    }

    public sealed class BitmapDrawCallOrderAndTextureComparer : IRefComparer<BitmapDrawCall>, IComparer<BitmapDrawCall> {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe int Compare (ref BitmapDrawCall x, ref BitmapDrawCall y) {
            var result = FastMath.CompareF(x.SortKey.Order, y.SortKey.Order);
            if (result == 0)
                result = (x.Textures.HashCode - y.Textures.HashCode);
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Compare (BitmapDrawCall x, BitmapDrawCall y) {
            return Compare(ref x, ref y);
        }
    }

    public sealed class BitmapDrawCallTextureComparer : IRefComparer<BitmapDrawCall>, IComparer<BitmapDrawCall> {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Compare (ref BitmapDrawCall x, ref BitmapDrawCall y) {
            return (x.Textures.HashCode > y.Textures.HashCode)
                ? 1
                : (
                    (x.Textures.HashCode < y.Textures.HashCode)
                    ? -1
                    : 0
                );
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Compare (BitmapDrawCall x, BitmapDrawCall y) {
            return Compare(ref x, ref y);
        }
    }

    public interface IBitmapBatch : IBatch {
        Sorter<BitmapDrawCall> Sorter {
            get; set;
        }

        void Add (BitmapDrawCall item);
        void Add (ref BitmapDrawCall item);
        void AddRange (ArraySegment<BitmapDrawCall> items);
    }

    public static class QuadUtils {
        private static readonly ushort[] QuadIndices = new ushort[] {
            0, 1, 2,
            0, 2, 3
        };

        public static BufferGenerator<CornerVertex>.SoftwareBuffer CreateCornerBuffer (IBatchContainer container) {
            BufferGenerator<CornerVertex>.SoftwareBuffer result;
            var cornerGenerator = container.RenderManager.GetBufferGenerator<BufferGenerator<CornerVertex>>();
            // TODO: Is it OK to share the buffer?
            if (!cornerGenerator.TryGetCachedBuffer("QuadCorners", 4, 6, out result)) {
                result = cornerGenerator.Allocate(4, 6, true);
                cornerGenerator.SetCachedBuffer("QuadCorners", result);
                // TODO: Can we just skip filling the buffer here?
            }

            var verts = result.Vertices;
            var indices = result.Indices;

            var v = new CornerVertex();
            for (var i = 0; i < 4; i++) {
                v.Corner = v.Unused = (short)i;
                verts.Array[verts.Offset + i] = v;
            }

            for (var i = 0; i < QuadIndices.Length; i++)
                indices.Array[indices.Offset + i] = QuadIndices[i];

            return result;
        }
    }

    public abstract class BitmapBatchBase<TDrawCall> : ListBatch<TDrawCall> {
        public struct Reservation {
            public readonly BitmapBatchBase<TDrawCall> Batch;
            public readonly int ID;

            public readonly TDrawCall[] Array;
            public readonly int Offset;
            public int Count;
            public readonly StackTrace Stack;

            internal Reservation (BitmapBatchBase<TDrawCall> batch, TDrawCall[] array, int offset, int count) {
                Batch = batch;
                ID = ++batch.LastReservationID;
                Array = array;
                Offset = offset;
                Count = count;
                if (CaptureStackTraces)
                    Stack = new StackTrace(2, true);
                else
                    Stack = null;
            }

            public void Shrink (int newCount) {
                if (ID != Batch.LastReservationID)
                    throw new InvalidOperationException("You can't shrink a reservation after another one has been created");
                if (newCount > Count)
                    throw new ArgumentException("Can't grow using shrink, silly", "newCount");
                if (newCount == Count)
                    return;

                Batch.RemoveRange(Offset + newCount, Count - newCount);
                Count = newCount;
            }
        }

        protected struct NativeBatch {
            public readonly Material Material;

            public SamplerState SamplerState;
            public SamplerState SamplerState2;

            public readonly ISoftwareBuffer SoftwareBuffer;
            public readonly TextureSet TextureSet;

            public readonly Vector2 Texture1Size, Texture1HalfTexel;
            public readonly Vector2 Texture2Size, Texture2HalfTexel;

            public readonly int LocalVertexOffset;
            public readonly int VertexCount;

            public NativeBatch (
                ISoftwareBuffer softwareBuffer, TextureSet textureSet, 
                int localVertexOffset, int vertexCount, Material material,
                SamplerState samplerState, SamplerState samplerState2
            ) {
                Material = material;
                SamplerState = samplerState;
                SamplerState2 = samplerState2;

                SoftwareBuffer = softwareBuffer;
                TextureSet = textureSet;

                LocalVertexOffset = localVertexOffset;
                VertexCount = vertexCount;

                var tex1 = textureSet.Texture1.Instance;
                var tex2 = textureSet.Texture2.Instance;

                Texture1Size = new Vector2(tex1.Width, tex1.Height);
                Texture1HalfTexel = new Vector2(1.0f / Texture1Size.X, 1.0f / Texture1Size.Y);

                if (textureSet.Texture2 != null) {
                    Texture2Size = new Vector2(tex2.Width, tex2.Height);
                    Texture2HalfTexel = new Vector2(1.0f / Texture2Size.X, 1.0f / Texture2Size.Y);
                } else {
                    Texture2HalfTexel = Texture2Size = Vector2.Zero;
                }
            }
        }

        public const int NativeBatchSize = 1024;
        protected const int NativeBatchCapacityLimit = 1024;

        protected int LastReservationID = 0;

        protected static ListPool<NativeBatch> _NativePool = new ListPool<NativeBatch>(
            320, 4, 64, 256, 1024
        );
        protected DenseList<NativeBatch> _NativeBatches;

        protected enum BitmapBatchPrepareState : int {
            Invalid,
            NotPrepared,
            Preparing,
            Prepared,
            Issuing,
            Issued
        }

        protected volatile int _State = (int)BitmapBatchPrepareState.Invalid;

        protected UnorderedList<Reservation> RangeReservations = null;
  
        protected BufferGenerator<BitmapVertex> _BufferGenerator = null;
        protected BufferGenerator<CornerVertex>.SoftwareBuffer _CornerBuffer = null;

        protected static ThreadLocal<VertexBufferBinding[]> _ScratchBindingArray = 
            new ThreadLocal<VertexBufferBinding[]>(() => new VertexBufferBinding[2]);
        protected static ThreadLocal<int[]> _SortIndexArray = new ThreadLocal<int[]>();

        /// <summary>
        /// If set and no declarative sorter is provided, draw calls will only be sorted by texture,
        ///  and the z-buffer will be relied on to provide sorting of individual draw calls.
        /// </summary>
        public bool UseZBuffer = false;

        protected int[] GetIndexArray (int minimumSize) {
            const int rounding = 4096;
            var size = ((minimumSize + (rounding - 1)) / rounding) * rounding + 16;
            var array = _SortIndexArray.Value;
            if ((array == null) || (array.Length < size))
                _SortIndexArray.Value = array = new int[size];

            return array;
        }

        protected void AllocateNativeBatches () {
            // If the batch contains a lot of draw calls, try to make sure we allocate our native batch from the large pool.
            int? nativeBatchCapacity = null;
            if (_DrawCalls.Count >= BatchCapacityLimit)
                nativeBatchCapacity = Math.Min(NativeBatchCapacityLimit + 2, _DrawCalls.Count / 8);

            _NativeBatches.Clear();
            _NativeBatches.ListPool = _NativePool;
            _NativeBatches.ListCapacity = nativeBatchCapacity;
        }

        protected void CreateNewNativeBatch (
            BufferGenerator<BitmapVertex>.SoftwareBuffer softwareBuffer, ref TextureSet currentTextures,
            ref int vertCount, ref int vertOffset, bool isFinalCall,
            Material material, SamplerState samplerState1, SamplerState samplerState2
        ) {
            if ((currentTextures.Texture1 == null) || currentTextures.Texture1.IsDisposed)
                throw new InvalidDataException("Invalid draw call(s)");

            _NativeBatches.Add(new NativeBatch(
                softwareBuffer, currentTextures,
                vertOffset, vertCount,
                material, samplerState1, samplerState2
            ));

            if (!isFinalCall) {
                vertOffset += vertCount;
                vertCount = 0;
            }
        }

        protected unsafe bool FillOneSoftwareBuffer (
            int[] indices, ArraySegment<BitmapDrawCall> drawCalls, ref int drawCallsPrepared, int count,
            Material material, SamplerState samplerState1, SamplerState samplerState2
        ) {
            int totalVertCount = 0;
            int vertCount = 0, vertOffset = 0;
            int nativeBatchSizeLimit = NativeBatchSize;
            int vertexWritePosition = 0;

            TextureSet currentTextures = new TextureSet();

            var remainingDrawCalls = (count - drawCallsPrepared);
            var remainingVertices = remainingDrawCalls;

            int nativeBatchSize = Math.Min(nativeBatchSizeLimit, remainingVertices);
            var softwareBuffer = _BufferGenerator.Allocate(nativeBatchSize, 1);

            float zBufferFactor = UseZBuffer ? 1.0f : 0.0f;

            var callCount = drawCalls.Count;
            var callArray = drawCalls.Array;

            bool result = true;

            fixed (BitmapVertex* pVertices = &softwareBuffer.Vertices.Array[softwareBuffer.Vertices.Offset]) {
                for (int i = drawCallsPrepared; i < count; i++) {
                    if (totalVertCount >= nativeBatchSizeLimit) {
                        result = false;
                        break;
                    }

                    int callIndex;
                    if (indices != null)
                        callIndex = indices[i];
                    else
                        callIndex = i;

                    if (callIndex >= callCount)
                        break;

                    bool texturesEqual = callArray[callIndex].Textures.Equals(ref currentTextures);

                    if (!texturesEqual) {
                        if (vertCount > 0)
                            CreateNewNativeBatch(
                                softwareBuffer, ref currentTextures, ref vertCount, ref vertOffset, false,
                                material, samplerState1, samplerState2
                            );

                        currentTextures = callArray[callIndex].Textures;
                    }

                    FillOneBitmapVertex(
                        softwareBuffer, ref callArray[callIndex], out pVertices[vertexWritePosition],
                        ref vertCount, ref vertOffset, zBufferFactor
                    );

                    vertexWritePosition += 1;
                    totalVertCount += 1;
                    vertCount += 1;

                    drawCallsPrepared += 1;
                }
            }

            if (vertexWritePosition > softwareBuffer.Vertices.Count)
                throw new InvalidOperationException("Wrote too many vertices");

            if (vertCount > 0) {
                CreateNewNativeBatch(
                    softwareBuffer, ref currentTextures, ref vertCount, ref vertOffset, true,
                    material, samplerState1, samplerState2
                );
            }

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void FillOneBitmapVertex (
            BufferGenerator<BitmapVertex>.SoftwareBuffer softwareBuffer, ref BitmapDrawCall call, out BitmapVertex result, 
            ref int vertCount, ref int vertOffset, float zBufferFactor
        ) {
            var p = call.Position;
            result = new BitmapVertex {
                Position = {
                    X = p.X,
                    Y = p.Y,
                    Z = call.SortKey.Order * zBufferFactor
                },
                Texture1Region = call.TextureRegion.ToVector4(),
                MultiplyColor = call.MultiplyColor,
                AddColor = call.AddColor,
                Scale = call.Scale,
                Origin = call.Origin,
                Rotation = call.Rotation
            };
            if (call.TextureRegion2.TopLeft == call.TextureRegion2.BottomRight)
                result.Texture2Region = result.Texture1Region;
            else
                result.Texture2Region = call.TextureRegion2.ToVector4();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected void StateTransition (BitmapBatchPrepareState from, BitmapBatchPrepareState to) {
            var prior = (BitmapBatchPrepareState)Interlocked.Exchange(ref _State, (int)to);
            if (prior != from)
                throw new ThreadStateException(string.Format(
                    "Expected to transition this batch from {0} to {1}, but state was {2}",
                    from, to, prior
                ));
        }

        public Reservation ReserveSpace (int count) {
            var range = _DrawCalls.ReserveSpace(count);
            var reservation = new Reservation(
                this, range.Array, range.Offset, range.Count
            );

            if (CaptureStackTraces) {
                if (RangeReservations == null)
                    RangeReservations = new UnorderedList<Reservation>();

                RangeReservations.Add(reservation);
            }

            return reservation;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveRange (int index, int count) {
            _DrawCalls.RemoveRange(index, count);
        }

        private struct CurrentNativeBatchState {
            public Material Material { get; private set; }
            public DefaultMaterialSetEffectParameters Parameters { get; private set; }
            public SamplerState SamplerState1, SamplerState2;
            public EffectParameter Texture1, Texture2;
            public TextureSet Textures;

            public CurrentNativeBatchState (DeviceManager dm) {
                SamplerState1 = null;
                SamplerState2 = null;
                Textures = new Render.TextureSet();
                Material = null;
                Parameters = null;
                Texture1 = Texture2 = null;
            }

            public void SetMaterial (Material m) {
                Material = m;
                Parameters = m.Parameters;
                var ep = m?.Effect?.Parameters;
                if (ep != null) {
                    Texture1 = ep["BitmapTexture"];
                    Texture2 = ep["SecondTexture"];
                } else {
                    Texture1 = Texture2 = null;
                }
            }
        }

        private void PerformNativeBatchTextureTransition (
            DeviceManager manager,
            ref NativeBatch nb, ref CurrentNativeBatchState cnbs,
            bool force
        ) {
            if (!nb.TextureSet.Equals(ref cnbs.Textures) || force) {
                cnbs.Textures = nb.TextureSet;
                var tex1 = nb.TextureSet.Texture1;
                var tex2 = nb.TextureSet.Texture2;

                cnbs.Texture1?.SetValue((Texture2D)null);
                if (tex1 != null)
                    cnbs.Texture1?.SetValue(tex1.Instance);

                cnbs.Texture2?.SetValue((Texture2D)null);
                if (tex2 != null)
                    cnbs.Texture2?.SetValue(tex2.Instance);

                cnbs.Parameters.BitmapTextureSize?.SetValue(nb.Texture1Size);
                cnbs.Parameters.BitmapTextureSize2?.SetValue(nb.Texture2Size);
                cnbs.Parameters.HalfTexel?.SetValue(nb.Texture1HalfTexel);
                cnbs.Parameters.HalfTexel2?.SetValue(nb.Texture2HalfTexel);
            }

            manager.CurrentMaterial.Flush();

            if (cnbs.SamplerState1 != null)
                manager.Device.SamplerStates[0] = cnbs.SamplerState1;
            if (cnbs.SamplerState2 != null)
                manager.Device.SamplerStates[1] = cnbs.SamplerState2;
        }

        private bool PerformNativeBatchTransition (
            DeviceManager manager,
            ref NativeBatch nb, ref CurrentNativeBatchState cnbs
        ) {
            var result = false;

            if (nb.Material != cnbs.Material) {
                manager.ApplyMaterial(nb.Material);
                cnbs.SetMaterial(nb.Material);
                result = true;
            }

            if (nb.SamplerState != null) {
                cnbs.SamplerState1 = nb.SamplerState;
                manager.Device.SamplerStates[0] = cnbs.SamplerState1;
            }
            if (nb.SamplerState2 != null) {
                cnbs.SamplerState2 = nb.SamplerState2;
                manager.Device.SamplerStates[1] = cnbs.SamplerState2;
            }

            return result;
        }

        protected abstract void PrepareDrawCalls (PrepareManager manager);

        protected sealed override void Prepare (PrepareManager manager) {
            var prior = (BitmapBatchPrepareState)Interlocked.Exchange(ref _State, (int)BitmapBatchPrepareState.Preparing);
            if ((prior == BitmapBatchPrepareState.Issuing) || (prior == BitmapBatchPrepareState.Preparing))
                throw new ThreadStateException("This batch is currently in use");
            else if (prior == BitmapBatchPrepareState.Invalid)
                throw new ThreadStateException("This batch is not valid");

            if (_DrawCalls.Count > 0)
                PrepareDrawCalls(manager);

            Squared.Render.NativeBatch.RecordCommands(_NativeBatches.Count);

            base.Prepare(manager);

            StateTransition(BitmapBatchPrepareState.Preparing, BitmapBatchPrepareState.Prepared);
        }

        public override void Issue (DeviceManager manager) {
            if (_DrawCalls.Count > 0) {
                StateTransition(BitmapBatchPrepareState.Prepared, BitmapBatchPrepareState.Issuing);

                if (State.IsCombined)
                    throw new InvalidOperationException("Batch was combined into another batch");

                if (_BufferGenerator == null)
                    throw new InvalidOperationException("Already issued");

                var device = manager.Device;

                IHardwareBuffer previousHardwareBuffer = null;

                // if (RenderTrace.EnableTracing)
                //    RenderTrace.ImmediateMarker("BitmapBatch.Issue(layer={0}, count={1})", Layer, _DrawCalls.Count);

                VertexBuffer vb, cornerVb;
                DynamicIndexBuffer ib, cornerIb;

                var totalDraws = 0;

                var cornerHwb = _CornerBuffer.HardwareBuffer;
                try {
                    cornerHwb.SetActive();
                    cornerHwb.GetBuffers(out cornerVb, out cornerIb);
                    if (device.Indices != cornerIb)
                        device.Indices = cornerIb;

                    var scratchBindings = _ScratchBindingArray.Value;

                    var previousSS1 = device.SamplerStates[0];
                    var previousSS2 = device.SamplerStates[1];

                    var cnbs = new CurrentNativeBatchState(manager);

                    {
                        for (int nc = _NativeBatches.Count, n = 0; n < nc; n++) {
                            NativeBatch nb;
                            if (!_NativeBatches.TryGetItem(n, out nb))
                                break;

                            var forceTextureTransition = PerformNativeBatchTransition(manager, ref nb, ref cnbs);
                            PerformNativeBatchTextureTransition(manager, ref nb, ref cnbs, forceTextureTransition);

                            if (UseZBuffer) {
                                var dss = device.DepthStencilState;
                                if (dss.DepthBufferEnable == false)
                                    throw new InvalidOperationException("UseZBuffer set to true but depth buffer is disabled");
                            }


                            var swb = nb.SoftwareBuffer;
                            var hwb = swb.HardwareBuffer;
                            if (previousHardwareBuffer != hwb) {
                                if (previousHardwareBuffer != null)
                                    previousHardwareBuffer.SetInactive();

                                hwb.SetActive();
                                previousHardwareBuffer = hwb;
                            }

                            hwb.GetBuffers(out vb, out ib);

                            scratchBindings[0] = cornerVb;
                            scratchBindings[1] = new VertexBufferBinding(vb, swb.HardwareVertexOffset + nb.LocalVertexOffset, 1);

                            device.SetVertexBuffers(scratchBindings);
                            device.DrawInstancedPrimitives(
                                PrimitiveType.TriangleList, 
                                0, _CornerBuffer.HardwareVertexOffset, 4, 
                                _CornerBuffer.HardwareIndexOffset, 2, 
                                nb.VertexCount
                            );

                            totalDraws += nb.VertexCount;
                        }

                        if (previousHardwareBuffer != null)
                            previousHardwareBuffer.SetInactive();
                    }

                    cnbs.Texture1?.SetValue((Texture2D)null);
                    cnbs.Texture2?.SetValue((Texture2D)null);

                    device.SamplerStates[0] = previousSS1;
                    device.SamplerStates[1] = previousSS2;
                } finally {
                    cornerHwb.TrySetInactive();
                    if (previousHardwareBuffer != null)
                        previousHardwareBuffer.TrySetInactive();
                }

                _BufferGenerator = null;
                _CornerBuffer = null;

                StateTransition(BitmapBatchPrepareState.Issuing, BitmapBatchPrepareState.Issued);

                if (totalDraws != _DrawCalls.Count)
                    ; // throw new Exception();
            }

            base.Issue(manager);
        }
    }

    public struct AbstractTextureReference {
        private readonly object Reference;

        public IDynamicTexture Dynamic {
            get {
                return Reference as IDynamicTexture;
            }
        }

        public Texture2D Static {
            get {
                return Reference as Texture2D;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public AbstractTextureReference (Texture2D tex) {
            Reference = tex;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public AbstractTextureReference (IDynamicTexture tex) {
            Reference = tex;
        }

        public bool IsDisposed {
            get {
                if (Dynamic != null)
                    return Dynamic.IsDisposed;
                else if (Static != null)
                    return Static.IsDisposed;
                else // FIXME: True?
                    return false;
            }
        }

        // FIXME: Make this a method?
        public Texture2D Instance {
            get {
                var dyn = Reference as IDynamicTexture;
                if (dyn != null)
                    return dyn.Texture;
                else
                    return (Texture2D)Reference;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode () {
            return Reference.GetHashCode();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals (AbstractTextureReference rhs) {
            return Object.ReferenceEquals(Reference, rhs.Reference);
        }

        public override bool Equals (object obj) {
            if (obj is AbstractTextureReference)
                return Equals((AbstractTextureReference)obj);
            else
                return Object.ReferenceEquals(Reference, obj);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator == (AbstractTextureReference lhs, AbstractTextureReference rhs) {
            return lhs.Equals(rhs);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator != (AbstractTextureReference lhs, AbstractTextureReference rhs) {
            return !lhs.Equals(rhs);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator AbstractTextureReference (Texture2D tex) {
            return new AbstractTextureReference(tex);
        }
    }

    public struct TextureSet {
        public readonly AbstractTextureReference Texture1, Texture2;
        public readonly int HashCode;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TextureSet (AbstractTextureReference texture1) {
            Texture1 = texture1;
            Texture2 = default(AbstractTextureReference);
            HashCode = texture1.GetHashCode();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TextureSet (AbstractTextureReference texture1, AbstractTextureReference texture2) {
            Texture1 = texture1;
            Texture2 = texture2;
            HashCode = texture1.GetHashCode() ^ texture2.GetHashCode();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TextureSet (Texture2D texture1) {
            Texture1 = texture1;
            Texture2 = default(AbstractTextureReference);
            HashCode = texture1.GetHashCode();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TextureSet (Texture2D texture1, Texture2D texture2) {
            Texture1 = texture1;
            Texture2 = texture2;
            HashCode = texture1.GetHashCode() ^ texture2.GetHashCode();
        }

        public Texture2D this[int index] {
            get {
                if (index == 0)
                    return Texture1.Instance;
                else if (index == 1)
                    return Texture2.Instance;
                else
                    throw new InvalidOperationException();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator TextureSet (Texture2D texture1) {
            return new TextureSet(texture1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals (ref TextureSet rhs) {
            return (HashCode == rhs.HashCode) && 
                (Texture1 == rhs.Texture1) && 
                (Texture2 == rhs.Texture2);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals (object obj) {
            if (obj is TextureSet) {
                var rhs = (TextureSet)obj;
                return Equals(ref rhs);
            } else {
                return false;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator == (TextureSet lhs, TextureSet rhs) {
            return (lhs.HashCode == rhs.HashCode) && (lhs.Texture1 == rhs.Texture1) && (lhs.Texture2 == rhs.Texture2);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator != (TextureSet lhs, TextureSet rhs) {
            return (lhs.Texture1 != rhs.Texture1) || (lhs.Texture2 != rhs.Texture2);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode () {
            return HashCode;
        }
    }

    public class ImageReference {
        public readonly Texture2D Texture;
        public readonly Bounds TextureRegion;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ImageReference (Texture2D texture, Bounds region) {
            Texture = texture;
            TextureRegion = region;
        }
    }

    public struct DrawCallSortKey {
        public float Order;
        public Tags  Tags;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public DrawCallSortKey (Tags tags = default(Tags), float order = 0) {
            Tags = tags;
            Order = order;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator DrawCallSortKey (Tags tags) {
            return new DrawCallSortKey(tags: tags);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator DrawCallSortKey (float order) {
            return new DrawCallSortKey(order: order);
            
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BitmapDrawCall {
        public Vector2    Position;
        public Vector2    Scale;
        public Vector2    Origin;
        public float      Rotation;
        public Color      MultiplyColor, AddColor, UserData;
        public Bounds     TextureRegion;
        public Bounds     TextureRegion2;
        public TextureSet Textures;
        public DrawCallSortKey SortKey;

#if DEBUG
        public static bool ValidateFields = false;
#else
        public static bool ValidateFields = false;
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public BitmapDrawCall (Texture2D texture, Vector2 position)
            : this(texture, position, texture.Bounds()) {
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public BitmapDrawCall (Texture2D texture, Vector2 position, Color color)
            : this(texture, position, texture.Bounds(), color) {
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public BitmapDrawCall (Texture2D texture, Vector2 position, Bounds textureRegion)
            : this(texture, position, textureRegion, Color.White) {
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public BitmapDrawCall (Texture2D texture, Vector2 position, Bounds textureRegion, Color color)
            : this(texture, position, textureRegion, color, Vector2.One) {
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public BitmapDrawCall (Texture2D texture, Vector2 position, float scale)
            : this(texture, position, texture.Bounds(), Color.White, new Vector2(scale, scale)) {
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public BitmapDrawCall (Texture2D texture, Vector2 position, Vector2 scale)
            : this(texture, position, texture.Bounds(), Color.White, scale) {
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public BitmapDrawCall (Texture2D texture, Vector2 position, Bounds textureRegion, Color color, float scale)
            : this(texture, position, textureRegion, color, new Vector2(scale, scale)) {
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public BitmapDrawCall (Texture2D texture, Vector2 position, Bounds textureRegion, Color color, Vector2 scale)
            : this(texture, position, textureRegion, color, scale, Vector2.Zero) {
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public BitmapDrawCall (Texture2D texture, Vector2 position, Bounds textureRegion, Color color, Vector2 scale, Vector2 origin)
            : this(texture, position, textureRegion, color, scale, origin, 0.0f) {
        }

        public BitmapDrawCall (Texture2D texture, Vector2 position, Bounds textureRegion, Color color, Vector2 scale, Vector2 origin, float rotation)
            : this(new TextureSet(texture), position, textureRegion, color, scale, origin, rotation) {
        }

        public BitmapDrawCall (TextureSet textures, Vector2 position, Bounds textureRegion, Color color, Vector2 scale, Vector2 origin, float rotation) {
            if (textures.Texture1 == null)
                throw new ArgumentNullException("texture1");
            else if (textures.Texture1.IsDisposed)
                throw new ObjectDisposedException("texture1");

            if ((textures.Texture2 != null) && textures.Texture2.IsDisposed)
                throw new ObjectDisposedException("texture2");

            Textures = textures;
            Position = position;
            TextureRegion = textureRegion;
            TextureRegion2 = default(Bounds);
            MultiplyColor = color;
            AddColor = default(Color);
            Scale = scale;
            Origin = origin;
            Rotation = rotation;
            UserData = default(Color);

            SortKey = default(DrawCallSortKey);
        }

        public void Mirror (bool x, bool y) {
            var newBounds = TextureRegion;

            if (x) {
                newBounds.TopLeft.X = TextureRegion.BottomRight.X;
                newBounds.BottomRight.X = TextureRegion.TopLeft.X;
            }

            if (y) {
                newBounds.TopLeft.Y = TextureRegion.BottomRight.Y;
                newBounds.BottomRight.Y = TextureRegion.TopLeft.Y;
            }

            TextureRegion = newBounds;
        }

        public Texture2D Texture {
            get {
                if (Textures.Texture2 == null)
                    return Textures.Texture1.Instance;
                else
                    throw new InvalidOperationException("DrawCall has multiple textures");
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set {
                if (value == null)
                    throw new ArgumentNullException("texture");

                Textures = new TextureSet(value);
            }
        }

        public float ScaleF {
            get {
                return (Scale.X + Scale.Y) / 2.0f;
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set {
                Scale = new Vector2(value, value);
            }
        }

        public Tags SortTags {
            get {
                return SortKey.Tags;
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set {
                SortKey.Tags = value;
            }
        }

        public float SortOrder {
            get {
                return SortKey.Order;
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set {
                SortKey.Order = value;
            }
        }

        public Rectangle TextureRectangle {
            get {
                // WARNING: Loss of precision!
                return new Rectangle(
                    (int)Math.Floor(TextureRegion.TopLeft.X * Texture.Width),
                    (int)Math.Floor(TextureRegion.TopLeft.Y * Texture.Height),
                    (int)Math.Ceiling(TextureRegion.Size.X * Texture.Width),
                    (int)Math.Ceiling(TextureRegion.Size.Y * Texture.Height)
                );
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set {
                TextureRegion = Texture.BoundsFromRectangle(ref value);
            }
        }

        public Color Color {
            get {
                return MultiplyColor;
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set {
                MultiplyColor = value;
            }
        }

        public void AdjustOrigin (Vector2 newOrigin) {
            var newPosition = Position;

            var textureSize = new Vector2(Texture.Width, Texture.Height) * TextureRegion.Size;
            newPosition += ((newOrigin - Origin) * textureSize * Scale);

            Position = newPosition;
            Origin = newOrigin;
        }

        public Bounds EstimateDrawBounds () {
            var tex1 = Textures.Texture1.Instance;
            var texSize = new Vector2(tex1.Width, tex1.Height);
            var texRgn = (TextureRegion.BottomRight - TextureRegion.TopLeft) * texSize * Scale;
            var offset = Origin * texRgn;

            return new Bounds(
                Position - offset,
                Position + texRgn - offset
            );
        }

        // Returns true if the draw call was modified at all
        public bool Crop (Bounds cropBounds) {
            // HACK
            if (Math.Abs(Rotation) >= 0.01)
                return false;

            AdjustOrigin(Vector2.Zero);

            var tex1 = Textures.Texture1.Instance;
            var texSize = new Vector2(tex1.Width, tex1.Height);
            var texRgnPx = TextureRegion.Scale(texSize);
            var drawBounds = EstimateDrawBounds();

            var newBounds_ = Bounds.FromIntersection(drawBounds, cropBounds);
            if (!newBounds_.HasValue) {
                TextureRegion = new Bounds(Vector2.Zero, Vector2.Zero);
                return true;
            }

            var newBounds = newBounds_.Value;
            var scaledSize = texSize * Scale;

            if (newBounds.TopLeft.X > drawBounds.TopLeft.X) {
                Position.X += (newBounds.TopLeft.X - drawBounds.TopLeft.X);
                TextureRegion.TopLeft.X += (newBounds.TopLeft.X - drawBounds.TopLeft.X) / scaledSize.X;
            }
            if (newBounds.TopLeft.Y > drawBounds.TopLeft.Y) {
                Position.Y += (newBounds.TopLeft.Y - drawBounds.TopLeft.Y);
                TextureRegion.TopLeft.Y += (newBounds.TopLeft.Y - drawBounds.TopLeft.Y) / scaledSize.Y;
            }

            if (newBounds.BottomRight.X < drawBounds.BottomRight.X)
                TextureRegion.BottomRight.X += (newBounds.BottomRight.X - drawBounds.BottomRight.X) / scaledSize.X;
            if (newBounds.BottomRight.Y < drawBounds.BottomRight.Y)
                TextureRegion.BottomRight.Y += (newBounds.BottomRight.Y - drawBounds.BottomRight.Y) / scaledSize.Y;

            return true;
        }

        public bool IsValid {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                if (ValidateFields) {
                    if (!Position.IsFinite())
                        return false;
                    if (!TextureRegion.TopLeft.IsFinite())
                        return false;
                    if (!TextureRegion.BottomRight.IsFinite())
                        return false;
                    if (TextureRegion2.TopLeft != TextureRegion2.BottomRight) {
                        if (!TextureRegion2.TopLeft.IsFinite())
                            return false;
                        if (!TextureRegion2.BottomRight.IsFinite())
                            return false;
                    }
                    if (!Arithmetic.IsFinite(Rotation))
                        return false;
                    if (!Scale.IsFinite())
                        return false;
                }

                var instance1 = Textures.Texture1.Instance;

                return ((instance1 != null) && !instance1.IsDisposed && !instance1.GraphicsDevice.IsDisposed);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static BitmapDrawCall operator * (BitmapDrawCall dc, float opacity) {
            dc.MultiplyColor *= opacity;
            dc.AddColor *= opacity;
            return dc;
        }

        public override string ToString () {
            string name = null;
            if (Texture == null)
                name = "null";
            else if (!ObjectNames.TryGetName(Texture, out name))
                name = string.Format("{2:X4} {0}x{1}", Texture.Width, Texture.Height, Texture.GetHashCode());

            return string.Format("tex {0} pos {1}", name, Position);
        }
    }
}