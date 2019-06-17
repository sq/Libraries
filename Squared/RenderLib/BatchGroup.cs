using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Graphics;

namespace Squared.Render {
    public class BatchGroup : ListBatch<Batch>, IBatchContainer {
        /// <summary>
        /// Set a name for the batch to aid debugging;
        /// </summary>
        public string Name;

        public OcclusionQuery OcclusionQuery;

        Action<DeviceManager, object> _Before, _After;
        private object _UserData;

        public override void Prepare (PrepareContext context) {
            BatchCombiner.CombineBatches(_DrawCalls, context.BatchesToRelease);

            _DrawCalls.Sort(Frame.BatchComparer);
            context.PrepareMany(_DrawCalls);

            OnPrepareDone();
        }

        public override void Issue (DeviceManager manager) {
            if (Name != null)
                Console.WriteLine("Start {0}", Name);

            manager.BatchGroupStack.Push(this);

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

                manager.BatchGroupStack.Pop();

                if (Name != null)
                    Console.WriteLine("End {0}", Name);
            }
        }

        public static RenderTargetBatchGroup ForRenderTarget (
            IBatchContainer container, int layer, AutoRenderTarget renderTarget, 
            Action<DeviceManager, object> before = null, Action<DeviceManager, object> after = null, 
            object userData = null, string name = null
        ) {
            if (container == null)
                throw new ArgumentNullException("container");
            else if (renderTarget?.IsDisposed == true)
                throw new ObjectDisposedException("renderTarget");

            var result = container.RenderManager.AllocateBatch<RenderTargetBatchGroup>();

            result.Initialize(container, layer, before, after, userData);
            result.SingleAuto = renderTarget;
            result.Single = null;
            result.Multiple = null;
            result.Name = name;
            result.CaptureStack(0);

            return result;
        }

        public static RenderTargetBatchGroup ForRenderTarget (
            IBatchContainer container, int layer, RenderTarget2D renderTarget, 
            Action<DeviceManager, object> before = null, Action<DeviceManager, object> after = null, 
            object userData = null, string name = null
        ) {
            if (container == null)
                throw new ArgumentNullException("container");
            else if (renderTarget?.IsDisposed == true)
                throw new ObjectDisposedException("renderTarget");

            var result = container.RenderManager.AllocateBatch<RenderTargetBatchGroup>();

            result.Initialize(container, layer, before, after, userData);
            result.Single = renderTarget;
            result.SingleAuto = null;
            result.Multiple = null;
            result.Name = name;
            result.CaptureStack(0);

            return result;
        }

        public static RenderTargetBatchGroup ForRenderTargets (
            IBatchContainer container, int layer, RenderTarget2D[] renderTargets, 
            Action<DeviceManager, object> before = null, Action<DeviceManager, object> after = null, 
            object userData = null, string name = null
        ) {
            var bindings = new RenderTargetBinding[renderTargets.Length];
            return ForRenderTargets(
                container, layer, bindings,
                before, after, userData
            );
        }

        public static RenderTargetBatchGroup ForRenderTargets (
            IBatchContainer container, int layer, RenderTargetBinding[] renderTargets, 
            Action<DeviceManager, object> before = null, Action<DeviceManager, object> after = null, 
            object userData = null, string name = null
        ) {
            if (container == null)
                throw new ArgumentNullException("container");

            if ((renderTargets == null) || (renderTargets.Length == 0))
                return ForRenderTarget(container, layer, (RenderTarget2D)null, before, after, userData, name: name);

            foreach (var binding in renderTargets)
                if (binding.RenderTarget?.IsDisposed == true)
                    throw new ObjectDisposedException("renderTargets[i]");

            var result = container.RenderManager.AllocateBatch<RenderTargetBatchGroup>();

            result.Initialize(container, layer, before, after, userData);
            result.Single = null;
            result.SingleAuto = null;
            result.Multiple = renderTargets;
            result.Name = name;
            result.CaptureStack(0);

            return result;
        }

        public static BatchGroup New (
            IBatchContainer container, int layer, 
            Action<DeviceManager, object> before = null, Action<DeviceManager, object> after = null, 
            object userData = null, string name = null
        ) {
            if (container == null)
                throw new ArgumentNullException("container");

            var result = container.RenderManager.AllocateBatch<BatchGroup>();
            result.Initialize(container, layer, before, after, userData);
            result.Name = name;
            result.CaptureStack(0);

            return result;
        }

        public void Initialize (
            IBatchContainer container, int layer, 
            Action<DeviceManager, object> before, Action<DeviceManager, object> after, 
            object userData, bool addToContainer = true
        ) {
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

        public override string ToString () {
            return string.Format("{4} {0} #{1} {2} material={3}", Name, Index, StateString, Material, (this is RenderTargetBatchGroup) ? "RT Batch" : "Batch");
        }
    }

    public class RenderTargetBatchGroup : BatchGroup {
        internal RenderTarget2D Single;
        internal AutoRenderTarget SingleAuto;
        internal RenderTargetBinding[] Multiple;

        public override void Issue (DeviceManager manager) {
            var single = Single;
            if (SingleAuto != null)
                single = SingleAuto.Get();

            if (Multiple != null)
                manager.PushRenderTargets(Multiple);
            else
                manager.PushRenderTarget(single);

            var rt = manager.CurrentRenderTarget;
            Console.WriteLine("Push RT {0}", rt?.Format);

            try {
                base.Issue(manager);
            } finally {
                manager.PopRenderTarget();
                rt = manager.CurrentRenderTarget;
                Console.WriteLine("Pop RT {0}", rt?.Format);
            }
        }
    }
}
