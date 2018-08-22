using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Graphics;

namespace Squared.Render {

    public class BatchGroup : ListBatch<Batch>, IBatchContainer {
        private class SetRenderTargetDataPool : BaseObjectPool<SetRenderTargetData> {
            protected override SetRenderTargetData AllocateNew () {
                return new SetRenderTargetData();
            }
        }

        private static readonly SetRenderTargetDataPool _Pool = new SetRenderTargetDataPool();

        private class SetRenderTargetData {
            public RenderTarget2D RenderTarget;
            public Action<DeviceManager, object> Before;
            public Action<DeviceManager, object> After;
            public object UserData;
        }

        public OcclusionQuery OcclusionQuery;

        Action<DeviceManager, object> _Before, _After;
        private object _UserData;

        public override void Prepare (PrepareContext context) {
            BatchCombiner.CombineBatches(ref _DrawCalls, context.BatchesToRelease);

            _DrawCalls.Sort(Frame.BatchComparer);
            context.PrepareMany(_DrawCalls);

            OnPrepareDone();
        }

        public override void Issue (DeviceManager manager) {
            if (OcclusionQuery != null)
                OcclusionQuery.Begin();
            if (_Before != null)
                _Before(manager, _UserData);

            try {
                using (var b = _DrawCalls.GetBuffer(false)) {
                    for (int i = 0; i < b.Count; i++)
                        if (b.Data[i] != null)
                            b.Data[i].IssueAndWrapExceptions(manager);
                }
            } finally {
                if (_After != null)
                    _After(manager, _UserData);
                if (OcclusionQuery != null)
                    OcclusionQuery.End();

                base.Issue(manager);
            }
        }

        private static readonly Action<DeviceManager, object> SetRenderTargetCallback = _SetRenderTargetCallback;

        private static void _SetRenderTargetCallback (DeviceManager dm, object userData) {
            var data = (SetRenderTargetData)userData;
            if (Tracing.RenderTrace.EnableTracing)
                Tracing.RenderTrace.ImmediateMarker("Set   RT {0}", Tracing.ObjectNames.ToObjectID(data.RenderTarget));

            dm.PushRenderTarget(data.RenderTarget);
            if (data.Before != null)
                data.Before(dm, data.UserData);
        }

        private static readonly Action<DeviceManager, object> RestoreRenderTargetCallback = _RestoreRenderTargetCallback;

        private static void _RestoreRenderTargetCallback (DeviceManager dm, object userData) {
            var data = (SetRenderTargetData)userData;
            if (Tracing.RenderTrace.EnableTracing)
                Tracing.RenderTrace.ImmediateMarker("Unset RT {0}", Tracing.ObjectNames.ToObjectID(data.RenderTarget));

            dm.PopRenderTarget();
            if (data.After != null)
                data.After(dm, data.UserData);

            _Pool.Release(data);
        }

        public static BatchGroup ForRenderTarget (IBatchContainer container, int layer, RenderTarget2D renderTarget, Action<DeviceManager, object> before = null, Action<DeviceManager, object> after = null, object userData = null) {
            if (container == null)
                throw new ArgumentNullException("container");

            var result = container.RenderManager.AllocateBatch<BatchGroup>();
            var data = _Pool.Allocate();

            data.RenderTarget = renderTarget;
            data.Before = before;
            data.After = after;
            data.UserData = userData;
            result.Initialize(container, layer, SetRenderTargetCallback, RestoreRenderTargetCallback, data);
            result.CaptureStack(0);

            return result;
        }

        public static BatchGroup New (IBatchContainer container, int layer, Action<DeviceManager, object> before = null, Action<DeviceManager, object> after = null, object userData = null) {
            if (container == null)
                throw new ArgumentNullException("container");

            var result = container.RenderManager.AllocateBatch<BatchGroup>();
            result.Initialize(container, layer, before, after, userData);
            result.CaptureStack(0);

            return result;
        }

        public void Initialize (IBatchContainer container, int layer, Action<DeviceManager, object> before, Action<DeviceManager, object> after, object userData, bool addToContainer = true) {
            base.Initialize(container, layer, null, addToContainer);

            RenderManager = container.RenderManager;
            _Before = before;
            _After = after;
            _UserData = userData;
            IsReleased = false;
            OcclusionQuery = null;
        }

        public RenderManager RenderManager {
            get;
            private set;
        }

        new public bool IsReleased { get; private set; }

        new public void Add (Batch batch) {
            if (batch == null)
                throw new ArgumentNullException("batch");
            if (batch.Container != null)
                throw new InvalidOperationException("This batch is already in another container.");

            batch.Container = this;
            base.Add(ref batch);
        }

        protected override void OnReleaseResources () {
            IsReleased = true;
            OcclusionQuery = null;

            for (int i = 0, c = _DrawCalls.Count; i < c; i++) {
                var batch = _DrawCalls[i];
                if (batch != null)
                    batch.ReleaseResources();
            }

            _DrawCalls.Clear();

            base.OnReleaseResources();
        }
    }
}
