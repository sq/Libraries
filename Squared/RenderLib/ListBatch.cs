using System;
using System.Collections.Generic;
using Squared.Util;

namespace Squared.Render {
    public interface IListBatch {
        int Count { get; }
    }

    public abstract class ListBatch<T> : Batch, IListBatch {
        public const int BatchCapacityLimit = 512;

        private static ListPool<T> _ListPool = new ListPool<T>(
            320, 16, 64, BatchCapacityLimit, 10240
        );

        protected DenseList<T> _DrawCalls = new DenseList<T>();
        private static bool _CanFastClearDrawCalls = false;

        public ListBatch ()
            : base () 
        {
            _DrawCalls.ListPool = _ListPool;
        }

        public T FirstDrawCall {
            get {
                return _DrawCalls.FirstOrDefault();
            }
        }

        public T LastDrawCall {
            get {
                return _DrawCalls.LastOrDefault();
            }
        }

        protected void Initialize (
            IBatchContainer container, int layer, Material material,
            bool addToContainer, int? capacity = null
        ) {
            if (_CanFastClearDrawCalls)
                _DrawCalls.UnsafeFastClear();
            else
                _DrawCalls.Clear();
            base.Initialize(container, layer, material, addToContainer);
        }

        public static void AdjustPoolCapacities (
            int? itemSizeLimit, int? largeItemSizeLimit,
            int? smallPoolCapacity, int? largePoolCapacity
        ) {
            _ListPool.SmallPoolMaxItemSize = itemSizeLimit.GetValueOrDefault(_ListPool.SmallPoolMaxItemSize);
            _ListPool.LargePoolMaxItemSize = largeItemSizeLimit.GetValueOrDefault(_ListPool.LargePoolMaxItemSize);
            _ListPool.SmallPoolCapacity = smallPoolCapacity.GetValueOrDefault(_ListPool.SmallPoolCapacity);
            _ListPool.LargePoolCapacity = largePoolCapacity.GetValueOrDefault(_ListPool.LargePoolCapacity);
        }

        public static void ConfigureClearBehavior (bool enableFastClear) {
            _CanFastClearDrawCalls = enableFastClear;
            _ListPool.FastClearEnabled = enableFastClear;
        }

        public int Count {
            get {
                return _DrawCalls.Count;
            }
        }

        public void EnsureCapacity (int capacity, bool lazy = false) {
            _DrawCalls.EnsureCapacity(capacity, lazy);
        }

        protected void Add (ref T item) {
            _DrawCalls.Add(ref item);
        }

        protected override void OnReleaseResources() {
            _DrawCalls.Dispose();
            base.OnReleaseResources();
        }

        public override string ToString () {
            if (Name != null)
                return string.Format("{0} x{6} '{5}' #{1} {2} layer={4} material={3}", GetType().Name, InstanceId, StateString, Material, Layer, FormatName(), Count);
            else
                return string.Format("{0} x{5} #{1} {2} layer={4} material={3}", GetType().Name, InstanceId, StateString, Material, Layer, Count);
        }
    }
}
