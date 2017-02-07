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
    public struct BitmapVertex : IVertexType {
        public Vector3 Position;
        public Vector2 TextureTopLeft;
        public Vector2 TextureBottomRight;
        public Vector2 Scale;
        public Vector2 Origin;
        public float Rotation;
        public Color MultiplyColor;
        public Color AddColor;
        
        public short Corner;
        public short Unused;

        public static readonly VertexElement[] Elements;
        static readonly VertexDeclaration _VertexDeclaration;

        static BitmapVertex () {
            var tThis = typeof(BitmapVertex);

            Elements = new VertexElement[] {
                new VertexElement( Marshal.OffsetOf(tThis, "Position").ToInt32(), 
                    VertexElementFormat.Vector3, VertexElementUsage.Position, 0 ),
                // TextureRegion
                new VertexElement( Marshal.OffsetOf(tThis, "TextureTopLeft").ToInt32(), 
                    VertexElementFormat.Vector4, VertexElementUsage.Position, 1 ),
                // ScaleOrigin
                new VertexElement( Marshal.OffsetOf(tThis, "Scale").ToInt32(), 
                    VertexElementFormat.Vector4, VertexElementUsage.Position, 2 ),
                new VertexElement( Marshal.OffsetOf(tThis, "Rotation").ToInt32(), 
                    VertexElementFormat.Single, VertexElementUsage.Position, 3 ),
                new VertexElement( Marshal.OffsetOf(tThis, "MultiplyColor").ToInt32(), 
                    VertexElementFormat.Color, VertexElementUsage.Color, 0 ),
                new VertexElement( Marshal.OffsetOf(tThis, "AddColor").ToInt32(), 
                    VertexElementFormat.Color, VertexElementUsage.Color, 1 ),
                new VertexElement( Marshal.OffsetOf(tThis, "Corner").ToInt32(), 
                    VertexElementFormat.Short2, VertexElementUsage.BlendIndices, 0 )
            };

            _VertexDeclaration = new VertexDeclaration(Elements);
        }

        public VertexDeclaration VertexDeclaration {
            get { return _VertexDeclaration; }
        }
    }

    public sealed class BitmapDrawCallSorterComparer : IComparer<BitmapDrawCall> {
        public Sorter<BitmapDrawCall>.SorterComparer Comparer;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Compare (BitmapDrawCall x, BitmapDrawCall y) {
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
    }

    public sealed class BitmapDrawCallOrderAndTextureComparer : IComparer<BitmapDrawCall> {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Compare (BitmapDrawCall x, BitmapDrawCall y) {
            var result = (x.SortKey.Order > y.SortKey.Order)
                ? 1
                : (
                    (x.SortKey.Order < y.SortKey.Order)
                    ? -1
                    : 0
                );
            if (result == 0)
                result = (x.Textures.HashCode > y.Textures.HashCode)
                ? 1
                : (
                    (x.Textures.HashCode < y.Textures.HashCode)
                    ? -1
                    : 0
                );
            return result;
        }
    }

    public sealed class BitmapDrawCallTextureComparer : IComparer<BitmapDrawCall> {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Compare (BitmapDrawCall x, BitmapDrawCall y) {
            return (x.Textures.HashCode > y.Textures.HashCode)
                ? 1
                : (
                    (x.Textures.HashCode < y.Textures.HashCode)
                    ? -1
                    : 0
                );
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

    public class BitmapBatch : ListBatch<BitmapDrawCall>, IBitmapBatch {
        public static readonly SamplerState DefaultSamplerState = SamplerState.LinearClamp;

        class BitmapBatchCombiner : IBatchCombiner {
            public bool CanCombine (Batch lhs, Batch rhs) {
                if ((lhs == null) || (rhs == null))
                    return false;

                BitmapBatch bblhs = lhs as BitmapBatch, bbrhs = rhs as BitmapBatch;

                if ((bblhs == null) || (bbrhs == null))
                    return false;

                if (bblhs.IsReusable || bbrhs.IsReusable)
                    return false;

                if (bblhs.Material.MaterialID != bbrhs.Material.MaterialID)
                    return false;

                if (bblhs.Layer != bbrhs.Layer)
                    return false;

                if (bblhs.UseZBuffer != bbrhs.UseZBuffer)
                    return false;

                if (bblhs.SamplerState != bbrhs.SamplerState)
                    return false;

                if (bblhs.SamplerState2 != bbrhs.SamplerState2)
                    return false;

                if (!bblhs.ReleaseAfterDraw)
                    return false;

                if (!bbrhs.ReleaseAfterDraw)
                    return false;

                return true;
            }

            public Batch Combine (Batch lhs, Batch rhs) {
                var bblhs = (BitmapBatch)lhs;
                var bbrhs = (BitmapBatch)rhs;

                var drawCallsLhs = bblhs._DrawCalls;
                var drawCallsRhs = bbrhs._DrawCalls;
                var drawCallsRhsBuffer = drawCallsRhs.GetBuffer();

                for (int i = 0, l = drawCallsRhs.Count; i < l; i++)
                    drawCallsLhs.Add(drawCallsRhsBuffer[i]);

                drawCallsRhs.Clear();
                rhs.IsCombined = true;

                return lhs;
            }
        }

        struct NativeBatch {
            public readonly ISoftwareBuffer SoftwareBuffer;
            public readonly TextureSet TextureSet;

            public readonly int LocalIndexOffset;
            public readonly int LocalVertexOffset;
            public readonly int VertexCount;

            public NativeBatch (ISoftwareBuffer softwareBuffer, TextureSet textureSet, int localIndexOffset, int localVertexOffset, int vertexCount) {
                SoftwareBuffer = softwareBuffer;
                TextureSet = textureSet;

                LocalIndexOffset = localIndexOffset;
                LocalVertexOffset = localVertexOffset;
                VertexCount = vertexCount;
            }
        }

        /// <summary>
        /// Specifies a declarative sorter that overrides the default sorting order for draw calls.
        /// Note that draw calls are still sorted by texture in the event that you provide no ordering
        ///  for a given pair of calls. 
        /// </summary>
        public Sorter<BitmapDrawCall> Sorter {
            get; set;
        }

        public bool DisableSorting = false;

        public SamplerState SamplerState;
        public SamplerState SamplerState2;

        /// <summary>
        /// If set and no declarative sorter is provided, draw calls will only be sorted by texture,
        ///  and the z-buffer will be relied on to provide sorting of individual draw calls.
        /// </summary>
        public bool UseZBuffer = false;

        internal static BitmapDrawCallOrderAndTextureComparer DrawCallComparer = new BitmapDrawCallOrderAndTextureComparer();
        internal static BitmapDrawCallTextureComparer DrawCallTextureComparer = new BitmapDrawCallTextureComparer();

        internal static ThreadLocal<BitmapDrawCallSorterComparer> DrawCallSorterComparer = new ThreadLocal<BitmapDrawCallSorterComparer>(
            () => new BitmapDrawCallSorterComparer()
        );

        public const int NativeBatchSize = 1024;
        private const int NativeBatchCapacityLimit = 1024;

        private ArrayPoolAllocator<BitmapVertex> _Allocator;
        private static ListPool<NativeBatch> _NativePool = new ListPool<NativeBatch>(
            256, 16, 64, NativeBatchCapacityLimit
        );
        private UnorderedList<NativeBatch> _NativeBatches = null;

        private enum PrepareState : int {
            Invalid,
            NotPrepared,
            Preparing,
            Prepared,
            Issuing,
            Issued
        }

        private volatile int _State = (int)PrepareState.Invalid;

        private static readonly ushort[] QuadIndices = new ushort[] {
            0, 1, 2,
            0, 2, 3
        };
  
        private XNABufferGenerator<BitmapVertex> _BufferGenerator = null;

        static BitmapBatch () {
            BatchCombiner.Combiners.Add(new BitmapBatchCombiner());
        }

        public static BitmapBatch New (IBatchContainer container, int layer, Material material, SamplerState samplerState = null, SamplerState samplerState2 = null, bool useZBuffer = false) {
            if (container == null)
                throw new ArgumentNullException("container");
            if (material == null)
                throw new ArgumentNullException("material");
            if (material.Effect == null)
                throw new ArgumentNullException("material.Effect");

            var result = container.RenderManager.AllocateBatch<BitmapBatch>();
            result.Initialize(container, layer, material, samplerState, samplerState2, useZBuffer);
            result.CaptureStack(0);
            return result;
        }

        public void Initialize (
            IBatchContainer container, int layer, 
            Material material, SamplerState samplerState = null, SamplerState samplerState2 = null, 
            bool useZBuffer = false, int? capacity = null
        ) {
            base.Initialize(container, layer, material, true, capacity);

            SamplerState = samplerState ?? BitmapBatch.DefaultSamplerState;
            SamplerState2 = samplerState2 ?? BitmapBatch.DefaultSamplerState;

            _Allocator = container.RenderManager.GetArrayAllocator<BitmapVertex>();

            UseZBuffer = useZBuffer;

            var prior = (PrepareState)Interlocked.Exchange(ref _State, (int)PrepareState.NotPrepared);
            if ((prior == PrepareState.Issuing) || (prior == PrepareState.Preparing))
                throw new ThreadStateException("This batch is currently in use");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void StateTransition (PrepareState from, PrepareState to) {
            var prior = (PrepareState)Interlocked.Exchange(ref _State, (int)to);
            if (prior != from)
                throw new ThreadStateException(string.Format(
                    "Expected to transition this batch from {0} to {1}, but state was {2}",
                    from, to, prior
                ));
        }

        public ArraySegment<BitmapDrawCall> ReserveSpace (int count) {
            return _DrawCalls.ReserveSpace(count);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add (BitmapDrawCall item) {
            if (!item.IsValid)
                throw new InvalidOperationException("Invalid draw call");

            _DrawCalls.Add(ref item);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        new public void Add (ref BitmapDrawCall item) {
            if (!item.IsValid)
                throw new InvalidOperationException("Invalid draw call");

            _DrawCalls.Add(ref item);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        new public void Add (ref BitmapDrawCall item, Material material) {
            if (!item.IsValid)
                throw new InvalidOperationException("Invalid draw call");
            if (material != null)
                throw new ArgumentException("Must be null because this is not a MultimaterialBitmapBatch", nameof(material));

            _DrawCalls.Add(ref item);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddRange (ArraySegment<BitmapDrawCall> items) {
            for (int i = 0; i < items.Count; i++)
                _DrawCalls.Add(ref items.Array[i + items.Offset]);
        }

        public void AddRange (
            BitmapDrawCall[] items, int firstIndex, int count, 
            Vector2? offset = null, Color? multiplyColor = null, Color? addColor = null, 
            DrawCallSortKey? sortKey = null, Vector2? scale = null, Material material = null
        ) {
            if (material != null)
                throw new ArgumentException("Must be null because this is not a MultimaterialBitmapBatch", nameof(material));

            for (int i = 0; i < count; i++) {
                var item = items[i + firstIndex];
                if (!item.IsValid)
                    continue;

                if (scale.HasValue)
                    item.Position *= scale.Value;
                if (offset.HasValue)
                    item.Position += offset.Value;
                if (multiplyColor.HasValue)
                    item.MultiplyColor = multiplyColor.Value;
                if (addColor.HasValue)
                    item.AddColor = addColor.Value;
                if (sortKey.HasValue)
                    item.SortKey = sortKey.Value;
                if (scale.HasValue)
                    item.Scale *= scale.Value;

                _DrawCalls.Add(ref item);
            }
        }

        private unsafe void FillOneSoftwareBuffer (BitmapDrawCall[] drawCalls, ref int drawCallsPrepared, int count) {
            int totalVertCount = 0;
            int vertCount = 0, vertOffset = 0;
            int indexCount = 0, indexOffset = 0;
            int nativeBatchSizeLimit = NativeBatchSize * 4;
            int vertexWritePosition = 0, indexWritePosition = 0;

            TextureSet currentTextures = new TextureSet();
            BitmapVertex vertex = new BitmapVertex();

            var remainingDrawCalls = (count - drawCallsPrepared);
            var remainingVertices = remainingDrawCalls * 4;

            int nativeBatchSize = Math.Min(nativeBatchSizeLimit, remainingVertices);
            var softwareBuffer = _BufferGenerator.Allocate(nativeBatchSize, (nativeBatchSize / 4) * 6);

            ushort indexBase = (ushort)softwareBuffer.HardwareVertexOffset;

            float zBufferFactor = UseZBuffer ? 1.0f : 0.0f;

            fixed (BitmapVertex* pVertices = &softwareBuffer.Vertices.Array[softwareBuffer.Vertices.Offset])
            fixed (ushort* pIndices = &softwareBuffer.Indices.Array[softwareBuffer.Indices.Offset])
                for (int i = drawCallsPrepared; i < count; i++) {
                    if (totalVertCount >= nativeBatchSizeLimit)
                        break;

                    var call = drawCalls[i];

                    bool texturesEqual = call.Textures.Equals(ref currentTextures);

                    if (!texturesEqual) {
                        if (vertCount > 0) {
                            _NativeBatches.Add(new NativeBatch(
                                softwareBuffer, currentTextures,
                                indexOffset,
                                vertOffset,
                                vertCount
                            ));

                            indexOffset += indexCount;
                            vertOffset += vertCount;
                            indexCount = 0;
                            vertCount = 0;
                        }

                        currentTextures = call.Textures;
                    }

                    vertex.Position.X = call.Position.X;
                    vertex.Position.Y = call.Position.Y;
                    vertex.Position.Z = call.SortKey.Order * zBufferFactor;
                    var tr = call.TextureRegion;
                    vertex.TextureTopLeft = tr.TopLeft;
                    vertex.TextureBottomRight = tr.BottomRight;
                    vertex.MultiplyColor = call.MultiplyColor;
                    vertex.AddColor = call.AddColor;
                    vertex.Scale = call.Scale;
                    vertex.Origin = call.Origin;
                    vertex.Rotation = call.Rotation;

                    for (var j = 0; j < 6; j++)
                        pIndices[indexWritePosition + j] = (ushort)(indexBase + QuadIndices[j]);

                    indexWritePosition += 6;

                    for (short j = 0; j < 4; j++) {
                        vertex.Unused = vertex.Corner = j;
                        pVertices[vertexWritePosition + j] = vertex;
                    }

                    vertexWritePosition += 4;
                    indexBase += 4;

                    totalVertCount += 4;
                    vertCount += 4;
                    indexCount += 6;

                    drawCallsPrepared += 1;
                }

            if (indexWritePosition > softwareBuffer.Indices.Count)
                throw new InvalidOperationException("Wrote too many indices");
            else if (vertexWritePosition > softwareBuffer.Vertices.Count)
                throw new InvalidOperationException("Wrote too many vertices");

            if (vertCount > 0)
                _NativeBatches.Add(new NativeBatch(
                    softwareBuffer, currentTextures,
                    indexOffset,
                    vertOffset,
                    vertCount
                ));
        }
        
        public override void Prepare (PrepareManager manager) {
            var prior = (PrepareState)Interlocked.Exchange(ref _State, (int)PrepareState.Preparing);
            if ((prior == PrepareState.Issuing) || (prior == PrepareState.Preparing))
                throw new ThreadStateException("This batch is currently in use");
            else if (prior == PrepareState.Invalid)
                throw new ThreadStateException("This batch is not valid");

            if (_DrawCalls.Count == 0)
                return;

            Squared.Render.NativeBatch.RecordPrimitives(_DrawCalls.Count * 2);

            if (_NativeBatches == null) {
                // If the batch contains a lot of draw calls, try to make sure we allocate our native batch from the large pool.
                int? nativeBatchCapacity = null;
                if (_DrawCalls.Count >= BatchCapacityLimit)
                    nativeBatchCapacity = Math.Min(NativeBatchCapacityLimit + 2, _DrawCalls.Count / 8);

                _NativeBatches = _NativePool.Allocate(nativeBatchCapacity);
            }

            if (DisableSorting) {
            } else if (Sorter != null) {
                var comparer = DrawCallSorterComparer.Value;
                comparer.Comparer = Sorter.GetComparer(true);
                _DrawCalls.FastCLRSort(comparer);
            } else if (UseZBuffer) {
                _DrawCalls.FastCLRSort(DrawCallTextureComparer);
            } else {
                _DrawCalls.FastCLRSort(DrawCallComparer);
            }

            var count = _DrawCalls.Count;

            _BufferGenerator = Container.RenderManager.GetBufferGenerator<XNABufferGenerator<BitmapVertex>>();

            var _drawCalls = _DrawCalls.GetBuffer();
            int drawCallsPrepared = 0;

            while (drawCallsPrepared < count)
                FillOneSoftwareBuffer(_drawCalls, ref drawCallsPrepared, count);

            StateTransition(PrepareState.Preparing, PrepareState.Prepared);
        }
            
        public override void Issue (DeviceManager manager) {
            if (_DrawCalls.Count == 0)
                return;

            StateTransition(PrepareState.Prepared, PrepareState.Issuing);

            if (IsCombined)
                throw new InvalidOperationException("Batch was combined into another batch");

            if (_BufferGenerator == null)
                throw new InvalidOperationException("Already issued");

            var device = manager.Device;

            IHardwareBuffer previousHardwareBuffer = null;

            // if (RenderTrace.EnableTracing)
            //    RenderTrace.ImmediateMarker("BitmapBatch.Issue(layer={0}, count={1})", Layer, _DrawCalls.Count);

            using (manager.ApplyMaterial(Material)) {
                TextureSet currentTexture = new TextureSet();
                var paramSize = manager.CurrentParameters.BitmapTextureSize;
                var paramHalfTexel = manager.CurrentParameters.HalfTexel;

                var m = manager.CurrentMaterial;
                var paramTexture1 = m.Effect.Parameters["BitmapTexture"];
                var paramTexture2 = m.Effect.Parameters["SecondTexture"];

                foreach (var nb in _NativeBatches) {
                    if (nb.TextureSet != currentTexture) {
                        currentTexture = nb.TextureSet;
                        var tex1 = currentTexture.Texture1;

                        device.SamplerStates[0] = SamplerState;
                        device.SamplerStates[1] = SamplerState2;

                        // FIXME: What is going wrong with XNA here?
                        paramTexture1.SetValue((Texture2D)null);
                        paramTexture1.SetValue(tex1);
                        if (paramTexture2 != null) {
                            paramTexture2.SetValue((Texture2D)null);
                            paramTexture2.SetValue(currentTexture.Texture2);
                        }

                        var vSize = new Vector2(tex1.Width, tex1.Height);
                        paramSize.SetValue(vSize);
                        paramHalfTexel.SetValue(new Vector2(1.0f / vSize.X, 1.0f / vSize.Y) * 0.5f);

                        manager.CurrentMaterial.Flush();
                    }

                    if (UseZBuffer) {
                        var dss = device.DepthStencilState;
                        if (dss.DepthBufferEnable == false)
                            throw new InvalidOperationException("UseZBuffer set to true but depth buffer is disabled");
                    }

                    var swb = nb.SoftwareBuffer;
                    var hwb = swb.HardwareBuffer;
                    if (previousHardwareBuffer != hwb) {
                        if (previousHardwareBuffer != null)
                            previousHardwareBuffer.SetInactive(device);

                        hwb.SetActive(device);
                        previousHardwareBuffer = hwb;
                    }

                    var primitiveCount = nb.VertexCount / 2;

                    device.DrawIndexedPrimitives(
                        PrimitiveType.TriangleList, 0, 
                        swb.HardwareVertexOffset + nb.LocalVertexOffset, 
                        nb.VertexCount, 
                        swb.HardwareIndexOffset + nb.LocalIndexOffset,
                        primitiveCount
                    );
                }

                if (previousHardwareBuffer != null)
                    previousHardwareBuffer.SetInactive(device);
            }

            _BufferGenerator = null;

            base.Issue(manager);

            StateTransition(PrepareState.Issuing, PrepareState.Issued);
        }

        protected override void OnReleaseResources () {
            _State = (int)PrepareState.Invalid;
            _BufferGenerator = null;

            _NativePool.Release(ref _NativeBatches);

            base.OnReleaseResources();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveRange (int index, int count) {
            _DrawCalls.RemoveRange(index, count);
        }
    }

    public struct MaterialBitmapDrawCall {
        public readonly BitmapDrawCall DrawCall;
        public readonly Material Material;
        public readonly SamplerState SamplerState1, SamplerState2;

        public MaterialBitmapDrawCall (
            ref BitmapDrawCall drawCall, Material material, 
            SamplerState samplerState1, SamplerState samplerState2
        ) {
            DrawCall = drawCall;
            Material = material;
            SamplerState1 = samplerState1;
            SamplerState2 = samplerState2;
        }
    }

    public class MultimaterialBitmapBatch : ListBatch<MaterialBitmapDrawCall>, IBitmapBatch {
        internal class MultimaterialComparer : IComparer<MaterialBitmapDrawCall> {
            public IComparer<BitmapDrawCall> DrawCallComparer;
            public static readonly ReferenceComparer<Material> MaterialComparer = new ReferenceComparer<Material>();

            public int Compare (MaterialBitmapDrawCall lhs, MaterialBitmapDrawCall rhs) {
                var result = DrawCallComparer.Compare(lhs.DrawCall, rhs.DrawCall);

                if (result == 0)
                    result = lhs.Material.MaterialID.CompareTo(rhs.Material.MaterialID);

                if (result == 0)
                    result = lhs.DrawCall.Textures.HashCode.CompareTo(rhs.DrawCall.Textures.HashCode);

                return result;
            }
        }

        internal static ThreadLocal<MultimaterialComparer> Comparer = new ThreadLocal<MultimaterialComparer>(
            () => new MultimaterialComparer()
        );

        /// <summary>
        /// Specifies a declarative sorter that overrides the default sorting order for draw calls.
        /// Note that draw calls are still sorted by texture in the event that you provide no ordering
        ///  for a given pair of calls.
        /// </summary>
        public Sorter<BitmapDrawCall> Sorter {
            get; set;
        }

        /// <summary>
        /// If set and no declarative sorter is provided, draw calls will only be sorted by texture,
        ///  and the z-buffer will be relied on to provide sorting of individual draw calls.
        /// </summary>
        public bool UseZBuffer = false;

        private BatchGroup _Group;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add (BitmapDrawCall item) {
            Add(item, Material, null, null);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        new public void Add (ref BitmapDrawCall item) {
            Add(ref item, Material, null, null);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        new public void AddRange (ArraySegment<BitmapDrawCall> items) {
            for (int i = 0; i < items.Count; i++)
                Add(ref items.Array[i + items.Offset]);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add (BitmapDrawCall item, Material material, SamplerState samplerState1 = null, SamplerState samplerState2 = null) {
            if (!item.IsValid)
                throw new InvalidOperationException("Invalid draw call");

            var dcm = material ?? Material;
            if ((dcm == null) || (dcm.Effect == null))
                throw new InvalidOperationException("Draw call has no material and this batch has no material");

            _DrawCalls.Add(new MaterialBitmapDrawCall(ref item, dcm, samplerState1, samplerState2));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add (ref BitmapDrawCall item, Material material, SamplerState samplerState1 = null, SamplerState samplerState2 = null) {
            if (!item.IsValid)
                throw new InvalidOperationException("Invalid draw call");

            var dcm = material ?? Material;
            if ((dcm == null) || (dcm.Effect == null))
                throw new InvalidOperationException("Draw call has no material and this batch has no material");

            _DrawCalls.Add(new MaterialBitmapDrawCall(ref item, dcm, samplerState1, samplerState2));
        }

        public void AddRange (
            BitmapDrawCall[] items, int firstIndex, int count, 
            Vector2? offset = null, Color? multiplyColor = null, Color? addColor = null, 
            DrawCallSortKey? sortKey = null, Vector2? scale = null, Material customMaterial = null,
            SamplerState samplerState1 = null, SamplerState samplerState2 = null
        ) {
            for (int i = 0; i < count; i++) {
                var item = items[i + firstIndex];
                if (!item.IsValid)
                    continue;

                if (scale.HasValue)
                    item.Position *= scale.Value;
                if (offset.HasValue)
                    item.Position += offset.Value;
                if (multiplyColor.HasValue)
                    item.MultiplyColor = multiplyColor.Value;
                if (addColor.HasValue)
                    item.AddColor = addColor.Value;
                if (sortKey.HasValue)
                    item.SortKey = sortKey.Value;
                if (scale.HasValue)
                    item.Scale *= scale.Value;

                var dcm = customMaterial ?? Material;
                if ((dcm == null) || (dcm.Effect == null))
                    throw new InvalidOperationException("Draw call has no material and this batch has no material");

                _DrawCalls.Add(new MaterialBitmapDrawCall(ref item, dcm, samplerState1, samplerState2));
            }
        }

        public static MultimaterialBitmapBatch New (
            IBatchContainer container, int layer, Material material, 
            bool useZBuffer = false, Sorter<BitmapDrawCall> sorter = null
        ) {
            if (container == null)
                throw new ArgumentNullException("container");

            var result = container.RenderManager.AllocateBatch<MultimaterialBitmapBatch>();
            result.Initialize(container, layer, material, sorter, useZBuffer);
            result.CaptureStack(0);            
            return result;
        }

        new public void Initialize (
            IBatchContainer container, int layer,             
            Material material, Sorter<BitmapDrawCall> sorter,
            bool useZBuffer, int? capacity = null
        ) {
            base.Initialize(container, layer, material, true, capacity);

            Sorter = sorter;
            UseZBuffer = useZBuffer;

            _DrawCalls.Clear();

            _Group = container.RenderManager.AllocateBatch<BatchGroup>();
            _Group.Initialize(container, layer, null, null, null, false);
            _Group.CaptureStack(0);
        }

        public override void Prepare (PrepareManager manager) {
            var drawCalls = _DrawCalls.GetBuffer();
            var count = _DrawCalls.Count;

            var comparer = Comparer.Value;
            if (Sorter != null)
                comparer.DrawCallComparer = Sorter.GetComparer(true);
            else
                comparer.DrawCallComparer = BitmapBatch.DrawCallComparer;

            Sort.FastCLRSort(
                drawCalls, comparer, 0, count
            );

            BitmapBatch currentBatch = null;
            int layerIndex = 0;

            for (var i = 0; i < count; i++) {
                var dc = drawCalls[i];
                var material = dc.Material;
                if (material == null)
                    throw new Exception("Missing material for draw call");

                var ss1 = dc.SamplerState1 ?? BitmapBatch.DefaultSamplerState;
                var ss2 = dc.SamplerState2 ?? BitmapBatch.DefaultSamplerState;

                if (
                    (currentBatch == null) ||
                    (currentBatch.Material != material) ||
                    (currentBatch.SamplerState != ss1) ||
                    (currentBatch.SamplerState2 != ss2)
                ) {
                    if (currentBatch != null)
                        currentBatch.Dispose();

                    currentBatch = BitmapBatch.New(
                        _Group, layerIndex++, material, dc.SamplerState1, dc.SamplerState2, UseZBuffer
                    );

                    // We've already sorted the draw calls.
                    currentBatch.DisableSorting = true;
                }

                currentBatch.Add(dc.DrawCall);
            }

            if (currentBatch != null)
                currentBatch.Dispose();

            _Group.Dispose();
            manager.PrepareAsync(_Group);
        }

        public override void Issue (DeviceManager manager) {
            using (manager.ApplyMaterial(Material))
                _Group.Issue(manager);
        }
    }

    public struct TextureSet {
        public readonly Texture2D Texture1, Texture2;
        public readonly int HashCode;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TextureSet (Texture2D texture1) {
            Texture1 = texture1;
            Texture2 = null;
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
                    return Texture1;
                else if (index == 1)
                    return Texture2;
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
            return (HashCode == rhs.HashCode) && (Texture1 == rhs.Texture1) && (Texture2 == rhs.Texture2);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals (object obj) {
            if (obj is TextureSet) {
                var rhs = (TextureSet)obj;
                return this.Equals(ref rhs);
            } else {
                return base.Equals(obj);
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
        public Tags  Tags;
        public float Order;

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

    public struct BitmapDrawCall {
        public TextureSet Textures;
        public Vector2    Position;
        public Vector2    Scale;
        public Vector2    Origin;
        public Bounds     TextureRegion;
        public float      Rotation;
        public Color      MultiplyColor, AddColor;
        public DrawCallSortKey SortKey;

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

        public BitmapDrawCall (Texture2D texture, Vector2 position, Bounds textureRegion, Color color, Vector2 scale, Vector2 origin, float rotation) {
            if (texture == null)
                throw new ArgumentNullException("texture");
            else if (texture.IsDisposed)
                throw new ObjectDisposedException("texture");

            Textures = new TextureSet(texture);
            Position = position;
            TextureRegion = textureRegion;
            MultiplyColor = color;
            AddColor = new Color(0, 0, 0, 0);
            Scale = scale;
            Origin = origin;
            Rotation = rotation;

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
                    return Textures.Texture1;
                else
                    throw new InvalidOperationException("DrawCall has multiple textures");
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set {
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
            var texSize = new Vector2(Textures.Texture1.Width, Textures.Texture1.Height);
            var texRgn = (TextureRegion.BottomRight - TextureRegion.TopLeft) * texSize * Scale;
            var offset = Origin * texSize;

            return new Bounds(
                Position - offset,
                Position + texRgn - offset
            );
        }

        public bool Crop (Bounds cropBounds) {
            var texSize = new Vector2(Textures.Texture1.Width, Textures.Texture1.Height);
            var drawBounds = EstimateDrawBounds();

            var newBounds_ = Bounds.FromIntersection(drawBounds, cropBounds);
            if (!newBounds_.HasValue)
                return false;
            var newBounds = newBounds_.Value;

            if (newBounds.TopLeft.X > drawBounds.TopLeft.X) {
                Position.X += newBounds.TopLeft.X - drawBounds.TopLeft.X;
                TextureRegion.TopLeft.X += (newBounds.TopLeft.X - drawBounds.TopLeft.X) / texSize.X / Scale.X;
            }
            if (newBounds.TopLeft.Y > drawBounds.TopLeft.Y) {
                Position.Y += newBounds.TopLeft.Y - drawBounds.TopLeft.Y;
                TextureRegion.TopLeft.Y += (newBounds.TopLeft.Y - drawBounds.TopLeft.Y) / texSize.Y / Scale.Y;
            }

            if (newBounds.BottomRight.X < drawBounds.BottomRight.X)
                TextureRegion.BottomRight.X += (newBounds.BottomRight.X - drawBounds.BottomRight.X) / texSize.X / Scale.X;
            if (newBounds.BottomRight.Y < drawBounds.BottomRight.Y)
                TextureRegion.BottomRight.Y += (newBounds.BottomRight.Y - drawBounds.BottomRight.Y) / texSize.Y / Scale.Y;

            return true;
        }

        public ImageReference ImageRef {
            get {
                return new ImageReference(Textures.Texture1, TextureRegion);
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set {
                Textures = new TextureSet(value.Texture);
                TextureRegion = value.TextureRegion;
            }
        }

        public bool IsValid {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                return ((Textures.Texture1 != null) && !Textures.Texture1.IsDisposed);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static BitmapDrawCall operator * (BitmapDrawCall dc, float opacity) {
            dc.MultiplyColor *= opacity;
            dc.AddColor *= opacity;
            return dc;
        }
    }
}