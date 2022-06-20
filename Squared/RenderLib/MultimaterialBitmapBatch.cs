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

namespace Squared.Render {
    public struct MaterialBitmapDrawCall {
        public BitmapDrawCall DrawCall;
        public Material Material;
        public SamplerState SamplerState1, SamplerState2;

        public MaterialBitmapDrawCall (
            in BitmapDrawCall drawCall, Material material, 
            SamplerState samplerState1, SamplerState samplerState2
        ) {
            DrawCall = drawCall;
            Material = material;
            SamplerState1 = samplerState1;
            SamplerState2 = samplerState2;
        }

        public override string ToString () {
            return $"{Material} {DrawCall}";
        }
    }

    public class MultimaterialBitmapBatch : BitmapBatchBase<MaterialBitmapDrawCall>, IBitmapBatch  {
        internal sealed class MultimaterialComparer : IRefComparer<MaterialBitmapDrawCall>, IComparer<MaterialBitmapDrawCall> {
            public IRefComparer<BitmapDrawCall> DrawCallComparer;
            public static readonly ReferenceComparer<Material> MaterialComparer = new ReferenceComparer<Material>();

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public int Compare (MaterialBitmapDrawCall lhs, MaterialBitmapDrawCall rhs) {
                return Compare(ref lhs, ref rhs);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public int Compare (ref MaterialBitmapDrawCall lhs, ref MaterialBitmapDrawCall rhs) {
                var result = DrawCallComparer.Compare(ref lhs.DrawCall, ref rhs.DrawCall);

                if (result == 0)
                    result = lhs.Material.MaterialID.CompareTo(rhs.Material.MaterialID);

                if (result == 0)
                    result = lhs.DrawCall.Textures.GetHashCode().CompareTo(rhs.DrawCall.Textures.GetHashCode());

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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add (BitmapDrawCall item) {
            Add(ref item, Material, null, null);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add (ref BitmapDrawCall item) {
            Add(ref item, Material, null, null);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddRange (ArraySegment<BitmapDrawCall> items) {
            for (int i = 0; i < items.Count; i++)
                Add(ref items.Array[i + items.Offset]);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add (ref BitmapDrawCall item, Material material, SamplerState samplerState1 = null, SamplerState samplerState2 = null) {
            if (!BitmapDrawCall.CheckValid(ref item))
                throw new InvalidOperationException("Invalid draw call");

            var dcm = material ?? Material;
            if ((dcm == null) || (dcm.Effect == null))
                throw new InvalidOperationException("Draw call has no material and this batch has no material");

            var dc = new MaterialBitmapDrawCall(in item, dcm, samplerState1, samplerState2);
            _DrawCalls.Add(ref dc);
        }

        public void AddRange (
            BitmapDrawCall[] items, int firstIndex, int count, 
            Vector2? offset = null, Color? multiplyColor = null, Color? addColor = null, 
            DrawCallSortKey? sortKey = null, Vector2? scale = null, Material customMaterial = null,
            SamplerState samplerState1 = null, SamplerState samplerState2 = null, Vector4? userData = null
        ) {
            MaterialBitmapDrawCall dc;
            for (int i = 0; i < count; i++) {
                var item = items[i + firstIndex];
                if (!BitmapDrawCall.CheckValid(ref item))
                    throw new InvalidOperationException("Invalid draw call");

                if (scale.HasValue)
                    item.Position *= scale.Value;
                if (offset.HasValue)
                    item.Position += offset.Value;
                if (multiplyColor.HasValue)
                    item.MultiplyColor = multiplyColor.Value;
                if (addColor.HasValue)
                    item.AddColor = addColor.Value;
                if (userData.HasValue)
                    item.UserData = userData.Value;
                if (sortKey.HasValue)
                    item.SortKey = sortKey.Value;
                if (scale.HasValue)
                    item.Scale *= scale.Value;

                var dcm = customMaterial ?? Material;
                if ((dcm == null) || (dcm.Effect == null))
                    throw new InvalidOperationException("Draw call has no material and this batch has no material");

                dc.DrawCall = item;
                dc.Material = dcm;
                dc.SamplerState1 = samplerState1;
                dc.SamplerState2 = samplerState2;

                _DrawCalls.Add(ref dc);
            }
        }

        public static MultimaterialBitmapBatch New (
            IBatchContainer container, int layer, Material material, 
            bool useZBuffer = false, bool depthPrePass = false, bool worldSpace = false,
            Sorter<BitmapDrawCall> sorter = null
        ) {
            if (container == null)
                throw new ArgumentNullException("container");

            var result = container.RenderManager.AllocateBatch<MultimaterialBitmapBatch>();
            result.Initialize(container, layer, material, sorter, useZBuffer);
            result.DepthPrePassOnly = depthPrePass;
            result.WorldSpace = worldSpace;
            result.CaptureStack(0);
            return result;
        }

        public void Initialize (
            IBatchContainer container, int layer,
            Material material, Sorter<BitmapDrawCall> sorter,
            bool useZBuffer, int? capacity = null
        ) {
            base.Initialize(container, layer, material, true, capacity);

            Sorter = sorter;
            UseZBuffer = useZBuffer;

            if (RangeReservations != null)
                RangeReservations.Clear();

            var rm = container.RenderManager;
            var lp = (ListPool<MaterialBitmapDrawCall>)_DrawCalls.ListPool;
            lp.ThreadGroup = rm.ThreadGroup;
            rm.AddDrainRequiredListPool(lp);

            var prior = (BitmapBatchPrepareState)Interlocked.Exchange(ref _State, (int)BitmapBatchPrepareState.NotPrepared);
            if ((prior == BitmapBatchPrepareState.Issuing) || (prior == BitmapBatchPrepareState.Preparing))
                throw new ThreadStateException("This batch is currently in use");

            _DrawCalls.Clear();
        }

        bool PrepareNativeBatchForRange (
            MaterialBitmapDrawCall[] drawCalls, int[] indexArray, int first, int count,
            LocalObjectCache<object> textureCache
        ) {
            if (count <= 0)
                return true;

            using (var buffer = BufferPool<BitmapDrawCall>.Allocate(count)) {
                var data = buffer.Data;
                var firstDc = drawCalls[first];
                var material = firstDc.Material;
                if (material == null)
                    return false;

                for (int j = 0; j < count; j++)
                    data[j] = drawCalls[j + first].DrawCall;

                var ss1 = firstDc.SamplerState1 ?? BitmapBatch.DefaultSamplerState;
                var ss2 = firstDc.SamplerState2 ?? BitmapBatch.DefaultSamplerState;

                var callSegment = new ArraySegment<BitmapDrawCall>(data, 0, count);
                int drawCallsPrepared = 0;
                bool failed = false;
                var parameters = new BatchBuilderParameters {
                    material = material,
                    samplerState1 = ss1,
                    samplerState2 = ss2,
                    textureCache = textureCache
                };
                while (!failed && !FillOneSoftwareBuffer(
                    indexArray, callSegment, ref drawCallsPrepared, count,
                    ref parameters, out failed
                ));

                return !failed;
            }
        }

        protected override bool PrepareDrawCalls (PrepareManager manager) {
            Squared.Render.NativeBatch.RecordPrimitives(_DrawCalls.Count * 2);

            AllocateNativeBatches();

            var count = _DrawCalls.Count;

            _BufferGenerator = Container.RenderManager.GetBufferGenerator<BufferGenerator<BitmapVertex>>();
            _CornerBuffer = Container.Frame.PrepareData.GetCornerBuffer(Container);

            var textureCache = AbstractTextureReference.Cache.GetCurrentLocalCache();

            using (var b = _DrawCalls.GetBuffer(true)) {
                var drawCalls = b.Data;

                var comparer = Comparer.Value;
                if (Sorter != null)
                    comparer.DrawCallComparer = Sorter.GetComparer(true);
                else
                    comparer.DrawCallComparer = BitmapBatch.DrawCallComparer.Value;

                Sort.FastCLRSortRef(
                    new ArraySegment<MaterialBitmapDrawCall>(b.Data, b.Offset, b.Count), comparer, 0, count
                );

                Material currentMaterial = null;
                SamplerState currentSamplerState1 = null, currentSamplerState2 = null;
                var currentTextures = default(TextureSet);
                int currentRangeStart = -1;

                for (var i = 0; i < count; i++) {
                    ref var dc = ref drawCalls[i + b.Offset];
                    var material = dc.Material;
                    if (material == null)
                        throw new Exception("Missing material for draw call");

                    var ss1 = dc.SamplerState1 ?? BitmapBatch.DefaultSamplerState;
                    var ss2 = dc.SamplerState2 ?? BitmapBatch.DefaultSamplerState;
                    var tex = dc.DrawCall.Textures;

                    var startNewRange = (
                        (material != currentMaterial) ||
                        (currentSamplerState1 != ss1) ||
                        (currentSamplerState2 != ss2) ||
                        !currentTextures.Equals(in dc.DrawCall.Textures)
                    );

                    if ((startNewRange) || (currentRangeStart == -1)) {
                        if (currentRangeStart != -1) {
                            int rangeCount = (i - currentRangeStart);
                            if (!PrepareNativeBatchForRange(drawCalls, null, currentRangeStart + b.Offset, rangeCount, textureCache))
                                return false;
                        }

                        currentRangeStart = i;
                        currentMaterial = material;
                        currentSamplerState1 = ss1;
                        currentSamplerState2 = ss2;
                        currentTextures = dc.DrawCall.Textures;
                    }
                }

                if (currentRangeStart != -1) {
                    int rangeCount = (count - currentRangeStart);
                    if (!PrepareNativeBatchForRange(drawCalls, null, currentRangeStart + b.Offset, rangeCount, textureCache))
                        return false;
                }
            }

            return true;
        }
    }
}