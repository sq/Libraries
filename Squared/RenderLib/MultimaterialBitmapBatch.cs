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
    public struct MaterialBitmapDrawCall {
        public BitmapDrawCall DrawCall;
        public Material Material;
        public SamplerState SamplerState1, SamplerState2;

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

    public class MultimaterialBitmapBatch : BitmapBatchBase<MaterialBitmapDrawCall>, IBitmapBatch  {
        internal class MultimaterialComparer : IRefComparer<MaterialBitmapDrawCall>, IComparer<MaterialBitmapDrawCall> {
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
        public void Add (ref BitmapDrawCall item) {
            Add(ref item, Material, null, null);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddRange (ArraySegment<BitmapDrawCall> items) {
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

            var dc = new MaterialBitmapDrawCall(ref item, dcm, samplerState1, samplerState2);
            _DrawCalls.Add(ref dc);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add (ref BitmapDrawCall item, Material material, SamplerState samplerState1 = null, SamplerState samplerState2 = null) {
            if (!item.IsValid)
                throw new InvalidOperationException("Invalid draw call");

            var dcm = material ?? Material;
            if ((dcm == null) || (dcm.Effect == null))
                throw new InvalidOperationException("Draw call has no material and this batch has no material");

            var dc = new MaterialBitmapDrawCall(ref item, dcm, samplerState1, samplerState2);
            _DrawCalls.Add(ref dc);
        }

        public void AddRange (
            BitmapDrawCall[] items, int firstIndex, int count, 
            Vector2? offset = null, Color? multiplyColor = null, Color? addColor = null, 
            DrawCallSortKey? sortKey = null, Vector2? scale = null, Material customMaterial = null,
            SamplerState samplerState1 = null, SamplerState samplerState2 = null
        ) {
            MaterialBitmapDrawCall dc;
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

                dc.DrawCall = item;
                dc.Material = dcm;
                dc.SamplerState1 = samplerState1;
                dc.SamplerState2 = samplerState2;

                _DrawCalls.Add(ref dc);
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

        public void Initialize (
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

        public override void Prepare (PrepareContext context) {
            using (var b = _DrawCalls.GetBuffer(true)) {
                var drawCalls = b.Data;
                var count = b.Count;

                var comparer = Comparer.Value;
                if (Sorter != null)
                    comparer.DrawCallComparer = Sorter.GetComparer(true);
                else
                    comparer.DrawCallComparer = BitmapBatch.DrawCallComparer;

                Sort.FastCLRSortRef(
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
                context.Prepare(_Group);

                OnPrepareDone();
            }
        }

        public override void Issue (DeviceManager manager) {
            manager.ApplyMaterial(Material);
            _Group.Issue(manager);

            base.Issue(manager);
        }
    }
}