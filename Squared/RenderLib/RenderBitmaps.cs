﻿#define USE_INDEXED_SORT

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
using Squared.Threading;
using System.Runtime;

namespace Squared.Render {
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct CornerVertex : IVertexType {
        public Vector4 CornerWeightsAndIndex;

        public static readonly VertexElement[] Elements;
        static readonly VertexDeclaration _VertexDeclaration;

        static CornerVertex () {
            var tThis = typeof(CornerVertex);

            Elements = new VertexElement[] {
                new VertexElement( Marshal.OffsetOf(tThis, "CornerWeightsAndIndex").ToInt32(),
                    VertexElementFormat.Vector4, VertexElementUsage.Normal, 2)
            };

            _VertexDeclaration = new VertexDeclaration(Elements);
        }

        public override string ToString () {
            return string.Format("{0},{1}", CornerWeightsAndIndex.X, CornerWeightsAndIndex.Y);
        }

        public VertexDeclaration VertexDeclaration {
            get { return _VertexDeclaration; }
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct BitmapVertex : IVertexType {
        public Vector4 PositionAndRotation;
        public Vector4 Texture1Region;
        public Vector4 Texture2Region;
        public Vector4 UserData;
        public Vector4 ScaleOrigin;
        public Color MultiplyColor;
        public Color AddColor;
        public short WorldSpace, TexID; // for debugging

        public static readonly VertexElement[] Elements;
        static readonly VertexDeclaration _VertexDeclaration;

        static BitmapVertex () {
            var tThis = typeof(BitmapVertex);

            Elements = new VertexElement[] {
                new VertexElement( Marshal.OffsetOf(tThis, "PositionAndRotation").ToInt32(), 
                    VertexElementFormat.Vector4, VertexElementUsage.Position, 0 ),
                new VertexElement( Marshal.OffsetOf(tThis, "Texture1Region").ToInt32(), 
                    VertexElementFormat.Vector4, VertexElementUsage.Position, 1 ),
                new VertexElement( Marshal.OffsetOf(tThis, "Texture2Region").ToInt32(), 
                    VertexElementFormat.Vector4, VertexElementUsage.Position, 2 ),
                new VertexElement( Marshal.OffsetOf(tThis, "ScaleOrigin").ToInt32(), 
                    VertexElementFormat.Vector4, VertexElementUsage.Position, 3 ),
                new VertexElement( Marshal.OffsetOf(tThis, "MultiplyColor").ToInt32(), 
                    VertexElementFormat.Color, VertexElementUsage.Color, 0 ),
                new VertexElement( Marshal.OffsetOf(tThis, "AddColor").ToInt32(), 
                    VertexElementFormat.Color, VertexElementUsage.Color, 1 ),
                new VertexElement( Marshal.OffsetOf(tThis, "UserData").ToInt32(), 
                    VertexElementFormat.Vector4, VertexElementUsage.Color, 2 ),
                new VertexElement( Marshal.OffsetOf(tThis, "WorldSpace").ToInt32(), 
                    VertexElementFormat.Short2, VertexElementUsage.BlendIndices, 1 ),
            };

            _VertexDeclaration = new VertexDeclaration(Elements);
        }

        public override string ToString () {
            return string.Format("tex{4} sr={2} mc={3} tex1r={1} pnr={0}", PositionAndRotation, Texture1Region, ScaleOrigin, MultiplyColor, TexID);
        }

        public VertexDeclaration VertexDeclaration {
            get { return _VertexDeclaration; }
        }
    }

    public sealed class BitmapDrawCallSorterComparer : IRefComparer<BitmapDrawCall>, IComparer<BitmapDrawCall> {
        public Sorter<BitmapDrawCall>.SorterComparer Comparer;

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Compare (ref BitmapDrawCall x, ref BitmapDrawCall y) {
            var result = Comparer.Compare(x, y);

            if (result == 0)
                result = x.Textures.CompareTo(in y.Textures);

            return result;
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Compare (BitmapDrawCall x, BitmapDrawCall y) {
            return Compare(ref x, ref y);
        }
    }

    public sealed class BitmapDrawCallOrderAndTextureComparer : IRefComparer<BitmapDrawCall>, IComparer<BitmapDrawCall> {
        private FastMath.U32F32 Buffer = new FastMath.U32F32();

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe int Compare (ref BitmapDrawCall x, ref BitmapDrawCall y) {
            unchecked {
                Buffer.F1 = x.SortOrder; Buffer.F2 = y.SortOrder;
                var result = FastMath.CompareF(ref Buffer);
                if (result == 0)
                    result = x.Textures.CompareTo(in y.Textures);
                return result;
            }
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Compare (BitmapDrawCall x, BitmapDrawCall y) {
            return Compare(ref x, ref y);
        }
    }

    public sealed class BitmapDrawCallTextureAndReverseOrderComparer : IRefComparer<BitmapDrawCall>, IComparer<BitmapDrawCall> {
        private FastMath.U32F32 Buffer = new FastMath.U32F32();

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe int Compare (ref BitmapDrawCall x, ref BitmapDrawCall y) {
            unchecked {
                var result = x.Textures.CompareTo(in y.Textures);
                if (result == 0) {
                    Buffer.F1 = y.SortOrder; Buffer.F2 = x.SortOrder;
                    result = FastMath.CompareF(ref Buffer);
                }
                return result;
            }
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Compare (BitmapDrawCall x, BitmapDrawCall y) {
            return Compare(ref x, ref y);
        }
    }

    public sealed class BitmapDrawCallTextureComparer : IRefComparer<BitmapDrawCall>, IComparer<BitmapDrawCall> {
        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Compare (ref BitmapDrawCall x, ref BitmapDrawCall y) {
            unchecked {
                return x.Textures.CompareTo(in y.Textures);
            }
        }

        [TargetedPatchingOptOut("")]
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

        const int RepeatLimit = 32;

        private static readonly string[] CornerBufferNames = new string[RepeatLimit];

        static QuadUtils () {
            for (int i = 0; i < CornerBufferNames.Length; i++)
                CornerBufferNames[i] = string.Intern("QuadCorners" + i);
        }

        public static BufferGenerator<CornerVertex>.SoftwareBuffer CreateCornerBuffer (IBatchContainer container, int repeatCount = 1) {
            if ((repeatCount < 1) || (repeatCount >= RepeatLimit))
                throw new ArgumentOutOfRangeException(nameof(repeatCount));

            BufferGenerator<CornerVertex>.SoftwareBuffer result;
            int vertCount = 4 * repeatCount;
            int indexCount = 6 * repeatCount;
            var cornerGenerator = container.RenderManager.GetBufferGenerator<BufferGenerator<CornerVertex>>();
            if (!cornerGenerator.TryGetCachedBuffer(CornerBufferNames[repeatCount], vertCount, indexCount, out result)) {
                // Console.Write($"Alloc corner buffer {bufferName} -> ");
                result = cornerGenerator.Allocate(vertCount, indexCount, true);
                // FIXME: For some reason the logic for pruning unused buffers triggers on corner buffers every frame
                // HACK: So until that's fixed we just disable it for corner buffers
                result.HardwareBuffer.ProtectedFromDeath = true;
                // Console.WriteLine(result.ToString());
                cornerGenerator.SetCachedBuffer(CornerBufferNames[repeatCount], result);
                // TODO: Can we just skip filling the buffer here?
            }

            var verts = result.Vertices;
            var indices = result.Indices;
            var vertOffset = verts.Offset;
            var indexOffset = indices.Offset;

            for (int j = 0; j < repeatCount; j++) {
                verts.Array[vertOffset + 0].CornerWeightsAndIndex = new Vector4(0, 0, 0, j);
                verts.Array[vertOffset + 1].CornerWeightsAndIndex = new Vector4(1, 0, 0, j);
                verts.Array[vertOffset + 2].CornerWeightsAndIndex = new Vector4(1, 1, 0, j);
                verts.Array[vertOffset + 3].CornerWeightsAndIndex = new Vector4(0, 1, 0, j);

                for (var i = 0; i < QuadIndices.Length; i++)
                    indices.Array[indexOffset + i] = (ushort)(QuadIndices[i] + (j * 4));

                vertOffset += 4;
                indexOffset += 6;
            }

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

        public struct NativeBatch {
            public readonly Material Material;

            public SamplerState SamplerState;
            public SamplerState SamplerState2;

            public readonly ISoftwareBuffer SoftwareBuffer;
            public readonly TextureSet TextureSet;

            public readonly Vector2 Texture1Size, Texture1HalfTexel;
            public readonly Vector2 Texture2Size, Texture2HalfTexel;
            public readonly Vector4 Texture1Traits, Texture2Traits;

            public readonly int LocalVertexOffset;
            public int VertexCount;

            public readonly bool Invalid;

            public NativeBatch (
                ISoftwareBuffer softwareBuffer, TextureSet textureSet, 
                int localVertexOffset, int vertexCount, Material material,
                SamplerState samplerState, SamplerState samplerState2,
                LocalObjectCache<object> textureCache
            ) {
                if (material == null)
                    throw new ArgumentNullException("material");

                Material = material;
                SamplerState = samplerState;
                SamplerState2 = samplerState2;

                SoftwareBuffer = softwareBuffer;
                TextureSet = textureSet;

                LocalVertexOffset = localVertexOffset;
                VertexCount = vertexCount;

                var tex1 = textureSet.Texture1.GetInstance(textureCache);
                var tex2 = textureSet.Texture2.GetInstance(textureCache);
                if (tex1 == null) {
                    Texture1Size = Texture2Size = default;
                    Texture1HalfTexel = Texture2HalfTexel = default;
                    Texture1Traits = Texture2Traits = default;
                    Invalid = true;
                    return;
                }

                Texture1Size = new Vector2(tex1.Width, tex1.Height);
                Texture1HalfTexel = new Vector2(1.0f / Texture1Size.X, 1.0f / Texture1Size.Y);
                Texture1Traits = Evil.TextureUtils.GetTraits(tex1.Format);

                if (textureSet.Texture2.IsInitialized) {
                    Texture2Size = new Vector2(tex2.Width, tex2.Height);
                    Texture2HalfTexel = new Vector2(1.0f / Texture2Size.X, 1.0f / Texture2Size.Y);
                    Texture2Traits = Evil.TextureUtils.GetTraits(tex2.Format);
                } else {
                    Texture2HalfTexel = Texture2Size = Vector2.Zero;
                    Texture2Traits = default;
                }

                Invalid = false;
            }
        }

        public const int NativeBatchSize = 4096;
        protected const int NativeBatchCapacityLimit = 4096;

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

        protected TDrawCall[] _TinyScratchBuffer = new TDrawCall[4];

        protected static ThreadLocal<DepthStencilState> _DepthPrePass = new ThreadLocal<DepthStencilState>();
        protected static BlendState PrePassBlend = new BlendState {
            ColorWriteChannels = ColorWriteChannels.None,
            ColorWriteChannels1 = ColorWriteChannels.None,
            ColorWriteChannels2 = ColorWriteChannels.None,
            ColorWriteChannels3 = ColorWriteChannels.None,
            ColorDestinationBlend = Blend.Zero,
            ColorSourceBlend = Blend.Zero,
            AlphaDestinationBlend = Blend.Zero,
            AlphaSourceBlend = Blend.Zero,
            Name = "Depth Pre-pass"
        };

        /// <summary>
        /// If set, z-buffer values will be generated for each draw call.
        /// </summary>
        public bool UseZBuffer = false;

        /// <summary>
        /// If set and no declarative sorter is provided, draw calls will only be sorted by texture,
        ///  and the z-buffer will be relied on to provide sorting of individual draw calls.
        /// This only works if UseZBuffer is true.
        /// </summary>
        public bool ZBufferOnlySorting = false;

        /// <summary>
        /// If set and z-buffering is enabled, the bitmaps will be drawn in two passes -
        ///  first a depth pass and then a color pass. This reduces overdraw.
        /// </summary>
        public bool TwoPassDraw = false;

        /// <summary>
        /// If set and z-buffering is enabled, only a depth pre-pass will be performed.
        /// You'll have to draw your bitmaps in another batch later.
        /// </summary>
        public bool DepthPrePassOnly = false;

        /// <summary>
        /// Sets the default value for screen/world space
        /// </summary>
        public bool WorldSpace = false;

        public override int InternalSortOrdering {
            get {
                // HACK: If a prepass and non-prepass batch exist with the same layer,
                //  ensure the prepass issues first.
                return UseZBuffer && DepthPrePassOnly
                    ? -1
                    : 0;
            }
        }

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
            _NativeBatches.ListPoolOrAllocator = _NativePool;
            if (nativeBatchCapacity.HasValue)
                _NativeBatches.ListCapacity = (short)Math.Min(nativeBatchCapacity.Value, short.MaxValue);
        }

        protected bool CreateNewNativeBatch (
            BufferGenerator<BitmapVertex>.SoftwareBuffer softwareBuffer, 
            ref BatchBuilderState state, ref BatchBuilderParameters parameters
        ) {
            if (!state.currentTextures.Texture1.IsInitialized)
                return false;

            var nb = new NativeBatch(
                softwareBuffer, state.currentTextures,
                state.vertOffset, state.vertCount,
                parameters.material, parameters.samplerState1, parameters.samplerState2,
                parameters.textureCache
            );
            if (nb.Invalid)
                return false;
            _NativeBatches.Add(nb);

            state.vertOffset += state.vertCount;
            state.vertCount = 0;

            return true;
        }

        protected struct BatchBuilderParameters {
            public Material material;
            public SamplerState samplerState1, samplerState2;
            public LocalObjectCache<object> textureCache;
        }

        protected struct BatchBuilderState {
            public int totalVertCount, vertCount, vertOffset,
                vertexWritePosition;
            public TextureSet currentTextures;
        }

        protected unsafe bool FillOneSoftwareBuffer (
            int[] indices, ArraySegment<BitmapDrawCall> drawCalls, ref int drawCallsPrepared, int count,
            ref BatchBuilderParameters parameters,            
            out bool failed
        ) {
            var state = new BatchBuilderState {
                currentTextures = TextureSet.Invalid
            };
            int nativeBatchSizeLimit = NativeBatchSize;

            var remainingDrawCalls = (count - drawCallsPrepared);
            var remainingVertices = remainingDrawCalls;

            int allocatedBatchSize = Math.Min(nativeBatchSizeLimit, remainingVertices);
            var softwareBuffer = _BufferGenerator.Allocate(allocatedBatchSize, 1);

            float zBufferFactor = UseZBuffer ? 1.0f : 0.0f;

            var callCount = drawCalls.Count;
            var callArray = drawCalls.Array;

            bool result = true;
            failed = false;
            var worldSpace = WorldSpace;

            unchecked {
                fixed (BitmapVertex* pVertices = &softwareBuffer.Vertices.Array[softwareBuffer.Vertices.Offset]) {
                    for (int i = drawCallsPrepared; i < count; i++) {
                        if (state.totalVertCount >= nativeBatchSizeLimit) {
                            result = false;
                            break;
                        }

                        int callIndex;
                        if (indices != null) {
                            callIndex = indices[i];
                            if (callIndex >= callCount) {
                                drawCallsPrepared += 1;
                                continue;
                            }
                        } else {
                            callIndex = i;
                            if (callIndex >= callCount)
                                break;
                        }

                        ref var call = ref callArray[callIndex + drawCalls.Offset];
                        // HACK: If we don't do this, it's possible for an entire prepare to fail and then none
                        //  of the bitmaps in this batch get issued
                        if (!call.Textures.Texture1.IsInitialized) {
                            // If we don't update the drawCallsPrepared counter, we will erroneously prepare some of
                            //  our bitmaps to be drawn a second time and the result will be that an arbitrary subset
                            //  of all our bitmaps draws twice
                            drawCallsPrepared += 1;
                            continue;
                        }

                        bool texturesEqual = call.Textures.Equals(in state.currentTextures);

                        if (!texturesEqual) {
                            if (state.vertCount > 0)
                                failed |= !CreateNewNativeBatch(
                                    softwareBuffer, ref state, ref parameters
                                );

                            state.currentTextures = call.Textures;
                            if (failed)
                                break;
                        }

                        ref var resultVertex = ref pVertices[state.vertexWritePosition];

                        resultVertex.PositionAndRotation.X = call.Position.X;
                        resultVertex.PositionAndRotation.Y = call.Position.Y;
                        resultVertex.PositionAndRotation.Z = call.SortOrder * zBufferFactor;
                        resultVertex.PositionAndRotation.W = call.Rotation;
                        resultVertex.Texture1Region = call.TextureRegion.ToVector4();
                        if (!call.TextureRegion2.HasValue)
                            // HACK: We use this as a 'no value' flag so that the vertex shader can
                            //  just copy Texture1Region
                            // If we were to copy Texture1Region into Texture2Region it would create
                            //  a very expensive execution stall and burn memory bandwidth
                            resultVertex.Texture2Region.X = -99999f;
                        else
                            resultVertex.Texture2Region = call.TextureRegion2.ToVector4();
                        resultVertex.UserData = call.UserData;
                        resultVertex.ScaleOrigin.X = call.Scale.X;
                        resultVertex.ScaleOrigin.Y = call.Scale.Y;
                        resultVertex.ScaleOrigin.Z = call.Origin.X;
                        resultVertex.ScaleOrigin.W = call.Origin.Y;
                        resultVertex.MultiplyColor = call.MultiplyColor;
                        resultVertex.AddColor = call.AddColor;
                        resultVertex.WorldSpace = (short)((call.WorldSpace ?? worldSpace) ? 1 : 0);
                        resultVertex.TexID = (short)call.Textures.Texture1.Id;

                        state.vertexWritePosition += 1;
                        state.totalVertCount += 1;
                        state.vertCount += 1;

                        drawCallsPrepared += 1;
                    }
                }
            }

            if (state.vertexWritePosition > softwareBuffer.Vertices.Count)
                throw new InvalidOperationException("Wrote too many vertices");

            if (state.vertCount > 0) {
                failed |= !CreateNewNativeBatch(
                    softwareBuffer, ref state, ref parameters
                );
            }

            return result;
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

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveRange (int index, int count) {
            _DrawCalls.RemoveRange(index, count);
        }

        private struct CurrentNativeBatchState {
            public Material Material { get; private set; }
            public MaterialEffectParameters Parameters { get; private set; }
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
                Parameters = m?.Parameters;
                Texture1 = m?.Parameters?.BitmapTexture;
                Texture2 = m?.Parameters?.SecondTexture;
            }
        }

        private void PerformNativeBatchTextureTransition (
            DeviceManager manager,
            ref NativeBatch nb, ref CurrentNativeBatchState cnbs,
            bool force, LocalObjectCache<object> textureCache
        ) {
            var changedTextures = false;
            if (!nb.TextureSet.Equals(in cnbs.Textures) || force) {
                changedTextures = true;
                cnbs.Textures = nb.TextureSet;
                var tex1 = nb.TextureSet.Texture1;
                var tex2 = nb.TextureSet.Texture2;

#if !FNA
                cnbs.Texture1?.SetValue((Texture2D)null);
#endif
                if (tex1.IsInitialized)
                    cnbs.Texture1?.SetValue(tex1.GetInstance(textureCache));

#if !FNA
                cnbs.Texture2?.SetValue((Texture2D)null);
#endif
                if (tex2.IsInitialized)
                    cnbs.Texture2?.SetValue(tex2.GetInstance(textureCache));

                cnbs.Parameters.BitmapTextureSize?.SetValue(nb.Texture1Size);
                cnbs.Parameters.BitmapTextureSize2?.SetValue(nb.Texture2Size);
                cnbs.Parameters.HalfTexel?.SetValue(nb.Texture1HalfTexel);
                cnbs.Parameters.HalfTexel2?.SetValue(nb.Texture2HalfTexel);
                cnbs.Parameters.BitmapTraits?.SetValue(nb.Texture1Traits);
                cnbs.Parameters.BitmapTraits2?.SetValue(nb.Texture2Traits);
            }

            manager.CurrentMaterial.Flush(manager);

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
                manager.ApplyMaterial(nb.Material, ref MaterialParameters);
                cnbs.SetMaterial(nb.Material);
                result = true;
            }

            if (nb.SamplerState != null) {
                cnbs.SamplerState1 = nb.SamplerState;
                manager.Device.SamplerStates[0] = cnbs.SamplerState1;
                result = true;
            }
            if (nb.SamplerState2 != null) {
                cnbs.SamplerState2 = nb.SamplerState2;
                manager.Device.SamplerStates[1] = cnbs.SamplerState2;
                result = true;
            }

            return result;
        }

        private bool PrepareSucceeded;
        protected abstract bool PrepareDrawCalls (PrepareManager manager);

        protected sealed override void Prepare (PrepareManager manager) {
            var prior = (BitmapBatchPrepareState)Interlocked.Exchange(ref _State, (int)BitmapBatchPrepareState.Preparing);
            if ((prior == BitmapBatchPrepareState.Issuing) || (prior == BitmapBatchPrepareState.Preparing))
                throw new ThreadStateException("This batch is currently in use");
            else if (prior == BitmapBatchPrepareState.Invalid)
                throw new ThreadStateException("This batch is not valid");

            if (_DrawCalls.Count > 0)
                PrepareSucceeded = PrepareDrawCalls(manager);

            Squared.Render.NativeBatch.RecordCommands(_NativeBatches.Count);

            base.Prepare(manager);

            StateTransition(BitmapBatchPrepareState.Preparing, BitmapBatchPrepareState.Prepared);
        }

        private static bool PrintedDPPWarning, PrintedMiscWarning;

        public override void Issue (DeviceManager manager) {
            base.Issue(manager);

            if (PrepareSucceeded && (_DrawCalls.Count > 0)) {
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

                var textureCache = manager.RenderManager.TextureCache;

                var totalDraws = 0;

                var cornerHwb = _CornerBuffer.HardwareBuffer;
                var sds = new Convenience.SavedDeviceState(device);
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
                            ref var nb = ref _NativeBatches.Item(n);

                            var forceTextureTransition = PerformNativeBatchTransition(manager, ref nb, ref cnbs);
                            PerformNativeBatchTextureTransition(manager, ref nb, ref cnbs, forceTextureTransition, textureCache);

                            var actualUseZBuffer = UseZBuffer;

                            if (actualUseZBuffer && !DepthPrePassOnly) {
                                var dss = device.DepthStencilState;
                                if (dss.DepthBufferEnable == false)
                                    actualUseZBuffer = false;
//                                     throw new InvalidOperationException("UseZBuffer set to true but depth buffer is disabled");
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

                            var bindOffset = swb.HardwareVertexOffset + nb.LocalVertexOffset;
                            scratchBindings[0] = cornerVb;
                            scratchBindings[1] = new VertexBufferBinding(vb, bindOffset, 1);

                            device.SetVertexBuffers(scratchBindings);

                            if ((TwoPassDraw || DepthPrePassOnly) && actualUseZBuffer) {
                                var bs = device.BlendState;
                                var dss = device.DepthStencilState;

                                var dpp = GetDepthPrePass(dss);
                                device.DepthStencilState = dpp;
                                device.BlendState = PrePassBlend;

                                device.DrawInstancedPrimitives(
                                    PrimitiveType.TriangleList, 
                                    0, _CornerBuffer.HardwareVertexOffset, 4, 
                                    _CornerBuffer.HardwareIndexOffset, 2, 
                                    nb.VertexCount
                                );

                                device.DepthStencilState = dss;
                                device.BlendState = bs;
                            } else if (DepthPrePassOnly && !actualUseZBuffer) {
                                // This means that for some reason they enabled depth pre-pass but then
                                //  didn't configure the depth buffer right. Well, okay
                                if (!PrintedDPPWarning) {
                                    PrintedDPPWarning = true;
                                    Console.Error.WriteLine("WARNING: Depth prepass misconfigured, not drawing");
                                }
                            } else if (!DepthPrePassOnly || !actualUseZBuffer) {
                                device.DrawInstancedPrimitives(
                                    PrimitiveType.TriangleList, 
                                    0, _CornerBuffer.HardwareVertexOffset, 4, 
                                    _CornerBuffer.HardwareIndexOffset, 2, 
                                    nb.VertexCount
                                );
                            } else {
                                if (!PrintedMiscWarning) {
                                    PrintedMiscWarning = true;
                                    Console.Error.WriteLine("WARNING: Bitmap batch misconfigured, not drawing");
                                }
                            }

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
                    sds.Restore(device);
                }

                _BufferGenerator = null;
                _CornerBuffer = null;

                StateTransition(BitmapBatchPrepareState.Issuing, BitmapBatchPrepareState.Issued);
            }
        }

        protected static DepthStencilState GetDepthPrePass (DepthStencilState dss) {
            var result = _DepthPrePass.Value;
            if (result == null)
                _DepthPrePass.Value = result = new DepthStencilState();

            result.DepthBufferEnable = true;
            result.DepthBufferWriteEnable = true;
            if (dss.DepthBufferEnable) {
                result.DepthBufferFunction = dss.DepthBufferFunction;
            } else {
                // HACK: If the current depth-stencil state is None, just pick a sensible default. This is usually right
                result.DepthBufferFunction = CompareFunction.GreaterEqual;
            }
            result.Name = "Depth pre-pass";

            return result;
        }
    }

    public readonly struct AbstractTextureReference {
        public sealed class Comparer : IEqualityComparer<AbstractTextureReference>, IComparer<AbstractTextureReference> {
            public static Comparer Instance = new Comparer();

            public int Compare (AbstractTextureReference x, AbstractTextureReference y) {
                unchecked {
                    return x.Id - y.Id;
                }
            }

            public bool Equals (AbstractTextureReference x, AbstractTextureReference y) {
                return x.Equals(y);
            }

            public int GetHashCode (AbstractTextureReference obj) {
                return obj.GetHashCode();
            }
        }

        public static readonly AbstractTextureReference Invalid;

        public static readonly LocallyReplicatedObjectCache<object> Cache = 
            new LocallyReplicatedObjectCache<object>();

        static AbstractTextureReference () {
            Invalid = new AbstractTextureReference(-9958923);
        }

        public readonly int Id;
        public object Reference {
            [TargetedPatchingOptOut("")]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                return Cache.GetValue(Id);
            }
        }

        private AbstractTextureReference (int id) {
            Id = id;
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public AbstractTextureReference (Texture2D tex) {
            Id = Cache.GetId(tex);
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public AbstractTextureReference (IDynamicTexture tex) {
            Id = Cache.GetId(tex);
        }

        public bool IsInitialized {
            get {
                return (Id != 0);
            }
        }

        public bool IsNull {
            get {
                if (Id == 0)
                    return true;

                var obj = Reference;
                if (obj == null)
                    return true;

                return false;
            }
        }

        public bool IsDisposedOrNull {
            get {
                if (Id == 0)
                    return true;

                var obj = Reference;
                if (obj == null)
                    return true;

                var dyn = obj as IDynamicTexture;
                if (dyn != null)
                    return dyn.IsDisposed;
                else
                    return ((Texture)obj).IsDisposed;
            }
        }

        public bool IsDisposed {
            get {
                if (Id == 0)
                    return false;

                var obj = Reference;
                if (obj == null)
                    return false;

                var dyn = obj as IDynamicTexture;
                if (dyn != null)
                    return dyn.IsDisposed;
                else
                    return ((Texture)obj).IsDisposed;
            }
        }

        public Texture2D GetInstance (LocalObjectCache<object> cache) {
            var obj = cache.GetValue(Id);
            var dyn = obj as IDynamicTexture;
            if (dyn != null)
                return dyn.Texture;
            else
                return (Texture2D)obj;
        }

        // FIXME: Make this a method?
        public Texture2D Instance {
            get {
                var obj = Reference;
                var dyn = obj as IDynamicTexture;
                if (dyn != null)
                    return dyn.Texture;
                else
                    return (Texture2D)obj;
            }
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode () {
            return Id.GetHashCode();
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals (AbstractTextureReference rhs) {
            return Id == rhs.Id;
        }

        public override bool Equals (object obj) {
            if (obj is AbstractTextureReference)
                return Equals((AbstractTextureReference)obj);
            else
                return Object.ReferenceEquals(Reference, obj);
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator == (AbstractTextureReference lhs, object rhs) {
            if (rhs == null)
                return lhs.IsNull;
            return lhs.Equals(rhs);
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator != (AbstractTextureReference lhs, object rhs) {
            if (rhs == null)
                return !lhs.IsNull;
            return !lhs.Equals(rhs);
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator == (object lhs, AbstractTextureReference rhs) {
            if (lhs == null)
                return rhs.IsNull;
            return rhs.Equals(lhs);
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator != (object lhs, AbstractTextureReference rhs) {
            if (lhs == null)
                return !rhs.IsNull;
            return !rhs.Equals(lhs);
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator == (AbstractTextureReference lhs, AbstractTextureReference rhs) {
            return lhs.Equals(rhs);
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator != (AbstractTextureReference lhs, AbstractTextureReference rhs) {
            return !lhs.Equals(rhs);
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator AbstractTextureReference (Texture2D tex) {
            return new AbstractTextureReference(tex);
        }

        public override string ToString () {
            if (IsNull)
                return "{null}";
            else if (IsDisposed)
                return "{disposed}";
            else
                return Id.ToString();
        }
    }

    public readonly struct TextureSet : IComparable<TextureSet> {
        public static readonly TextureSet Invalid;

        public readonly AbstractTextureReference Texture1, Texture2;

        static TextureSet () {
            Invalid = new TextureSet(AbstractTextureReference.Invalid, AbstractTextureReference.Invalid);
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TextureSet (AbstractTextureReference texture1) {
            Texture1 = texture1;
            Texture2 = default(AbstractTextureReference);
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TextureSet (AbstractTextureReference texture1, AbstractTextureReference texture2) {
            Texture1 = texture1;
            Texture2 = texture2;
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TextureSet (Texture2D texture1) {
            Texture1 = texture1;
            Texture2 = default(AbstractTextureReference);
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TextureSet (Texture2D texture1, Texture2D texture2) {
            Texture1 = texture1;
            Texture2 = texture2;
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

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator TextureSet (Texture2D texture1) {
            return new TextureSet(texture1);
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals (in TextureSet rhs) {
            return (Texture1 == rhs.Texture1) && 
                (Texture2 == rhs.Texture2);
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals (object obj) {
            if (obj is TextureSet rhs) {
                return Equals(rhs);
            } else {
                return false;
            }
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator == (TextureSet lhs, TextureSet rhs) {
            return (lhs.Texture1 == rhs.Texture1) && (lhs.Texture2 == rhs.Texture2);
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator != (TextureSet lhs, TextureSet rhs) {
            return (lhs.Texture1 != rhs.Texture1) || (lhs.Texture2 != rhs.Texture2);
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode () {
            return (Texture1.Id ^ Texture2.Id).GetHashCode();
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int CompareTo (TextureSet rhs) {
            return CompareTo(in rhs);
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int CompareTo (in TextureSet rhs) {
            unchecked {
                return (Texture1.Id | (Texture2.Id << 16)) - (rhs.Texture1.Id | (rhs.Texture2.Id << 16));
            }
        }
    }

    public sealed class ImageReference {
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

    // HACK: Pack=1 reduces the size of this struct by a decent amount, and so does Pack=4
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct BitmapDrawCall {
        // 8
        public Vector2    Position;
        // 16
        // TODO: ScaleMinusOne
        public Vector2    Scale;
        // 24
        public Vector2    Origin;
        // 28
        public float      Rotation;
        // 32
        public float      SortOrder;
        // 36, 40
        public Color      MultiplyColor, AddColor;
        // 56
        public Vector4    UserData;
        // 72
        public Bounds     TextureRegion;
        // 88
        public Bounds     TextureRegion2;
        // 100
        public TextureSet Textures;
        // 104
        public Tags       SortTags;
        // 106
        /// <summary>
        /// Scratch storage space for user purposes. This data is not forwarded to the GPU
        /// </summary>
        public short      LocalData1;
        // 107
        private sbyte     _WorldSpace;
        // 108
        /// <summary>
        /// Scratch storage space for user purposes. This data is not forwarded to the GPU
        /// </summary>
        public byte       LocalData2;

        // TODO: Consider allocating TextureRegion2 and UserData on the heap only if used,
        //  to shrink most draw calls by 32 bytes

        public bool?      WorldSpace {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                switch (_WorldSpace) {
                    case 1:
                        return true;
                    case 0:
                        return false;
                    default:
                        return null;
                }
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set {
                if (value.HasValue)
                    _WorldSpace = value.Value ? (sbyte)1 : (sbyte)0;
                else
                    _WorldSpace = -1;
            }
        }

#if DEBUG && PARANOID
        public const bool ValidateFields = true;
#else
        public const bool ValidateFields = false;
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public BitmapDrawCall (TextureSet textures, Vector2 position)
            : this(textures, position, textures.Texture1.Instance.Bounds(), Color.White, Vector2.One, Vector2.Zero, 0f) {
        }

        public BitmapDrawCall (TextureSet textures, Vector2 position, Bounds textureRegion, Color color, Vector2 scale, Vector2 origin, float rotation) {
            if (!textures.Texture1.IsInitialized)
                throw new NullReferenceException("texture1");
#if DEBUG
            if (textures.Texture1.IsDisposedOrNull)
                throw new ObjectDisposedException("texture1");
            else if (textures.Texture2.IsDisposed)
                throw new ObjectDisposedException("texture2");
#endif

            Textures = textures;
            Position = position;
            TextureRegion = textureRegion;
            TextureRegion2 = default(Bounds);
            MultiplyColor = color;
            AddColor = default(Color);
            Scale = scale;
            Origin = origin;
            Rotation = rotation;
            UserData = default(Vector4);
            _WorldSpace = -1;
            SortOrder = 0f;
            SortTags = default(Tags);
            LocalData1 = 0;
            LocalData2 = 0;
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

        public AbstractTextureReference Texture1 {
            get {
                return Textures.Texture1;
            }
            set {
                Textures = new TextureSet(value, Textures.Texture2);
            }
        }

        public AbstractTextureReference Texture2 {
            get {
                return Textures.Texture2;
            }
            set {
                Textures = new TextureSet(Textures.Texture1, value);
            }
        }

        public Texture2D Texture {
            get {
                if (!Textures.Texture2.IsInitialized)
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

        // Not a getter to prevent accidentally using it when you shouldn't
        public DrawCallSortKey GetSortKey () {
            return new DrawCallSortKey(SortTags, SortOrder);
        }

        public DrawCallSortKey SortKey {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set {
                SortOrder = value.Order;
                SortTags = value.Tags;
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
                TextureRegion = Texture.BoundsFromRectangle(in value);
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
            if (Origin == newOrigin)
                return;

            // FIXME: Rotation
            var newPosition = Position;

            var textureSize = new Vector2(Texture.Width, Texture.Height) * TextureRegion.Size;
            newPosition += ((newOrigin - Origin) * textureSize * Scale);

            Position = newPosition;
            Origin = newOrigin;
        }

        public Bounds EstimateDrawBounds () {
            var tex1 = Textures.Texture1.Instance;
            if (tex1 == null)
                return Bounds.FromPositionAndSize(Position, Vector2.Zero);

            var texSize = new Vector2(tex1.Width, tex1.Height);
            var texRgn = (TextureRegion.BottomRight - TextureRegion.TopLeft) * texSize * Scale;
            var offset = Origin * texRgn;

            return new Bounds(
                Position - offset,
                Position + texRgn - offset
            );
        }

        /// <summary>
        /// Attempts to crop the draw call to a sub-region of the source texture.
        /// May fail if the draw call is rotated or otherwise complex.
        /// </summary>
        /// <param name="cropBounds"></param>
        /// <returns>true if the draw call was modified</returns>
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

        /// <summary>
        /// Attempts to align Texture2 with Texture1 while maintaining Texture2's aspect ratio and size.
        /// If Texture2 is smaller than Texture1, this will produce texture coordinates that will wrap the texture.
        /// </summary>
        /// <returns>true if successful</returns>
        public bool AlignTexture2 (float scaleRatio = 1.0f, bool preserveAspectRatio = true) {
            var instance1 = Texture1.Instance;
            var instance2 = Texture2.Instance;
            if (instance1 == null)
                return false;
            if (instance2 == null)
                return false;

            var size1 = TextureRegion.Size * new Vector2(instance1.Width, instance1.Height);

            var rgn2 = TextureRegion2;
            if (rgn2.TopLeft == rgn2.BottomRight)
                rgn2 = Bounds.Unit;
            var size2 = rgn2.Size * new Vector2(instance2.Width, instance2.Height);

            var scaleRatioX = size1.X / size2.X;
            var scaleRatioY = size1.Y / size2.Y;
            if (float.IsInfinity(scaleRatioX) || float.IsNaN(scaleRatioX))
                return false;
            if (float.IsInfinity(scaleRatioY) || float.IsNaN(scaleRatioY))
                return false;

            if (!preserveAspectRatio) {
                var xy = Math.Min(scaleRatioX, scaleRatioY);
                rgn2.Size = rgn2.Size * (xy / scaleRatio);
            } else {
                var xy = new Vector2(scaleRatioX / scaleRatio, scaleRatioY / scaleRatio);
                rgn2.Size = rgn2.Size * xy;
            }
            TextureRegion2 = rgn2;

            return true;
        }

        public static bool CheckFieldValidity (in BitmapDrawCall drawCall) {
            if (!drawCall.Position.IsFinite())
                return false;
            if (!drawCall.TextureRegion.TopLeft.IsFinite())
                return false;
            if (!drawCall.TextureRegion.BottomRight.IsFinite())
                return false;
            if (drawCall.TextureRegion2.HasValue) {
                if (!drawCall.TextureRegion2.TopLeft.IsFinite())
                    return false;
                if (!drawCall.TextureRegion2.BottomRight.IsFinite())
                    return false;
            }
            if (!Arithmetic.IsFinite(drawCall.Rotation))
                return false;
            if (!drawCall.Scale.IsFinite())
                return false;

            return true;
        }

        private bool AreFieldsValid {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                if (ValidateFields)
                    return CheckFieldValidity(in this);
                else
                    return true;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool CheckValid (ref BitmapDrawCall drawCall) {
            if (ValidateFields && !CheckFieldValidity(in drawCall))
                return false;

#if DEBUG
            var instance1 = drawCall.Textures.Texture1.Instance;
            return ((instance1 != null) && !instance1.IsDisposed && !instance1.GraphicsDevice.IsDisposed);
#else
            return drawCall.Textures.Texture1.IsInitialized;
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool CheckValid (in BitmapDrawCall drawCall, LocalObjectCache<object> textureCache) {
            if (ValidateFields && !CheckFieldValidity(in drawCall))
                return false;

#if DEBUG
            var instance1 = drawCall.Textures.Texture1.GetInstance(textureCache);
            return ((instance1 != null) && !instance1.IsDisposed && !instance1.GraphicsDevice.IsDisposed);
#else
            return drawCall.Textures.Texture1.IsInitialized;
#endif
        }

        public bool IsValid {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                return CheckValid(ref this);
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
                name = string.Format("{2:X4} {0}x{1}", Texture.Width, Texture.Height, Textures.Texture1.Id);

            return string.Format("tex {0} pos {1} sort {2}", name, Position, SortOrder);
        }
    }
}