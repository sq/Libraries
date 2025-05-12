﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Graphics;
using Squared.Util;

namespace Squared.Render {
    public delegate void ViewTransformModifier (ref ViewTransform vt, object userData);

    public class BatchGroup : ListBatch<Batch>, IBatchContainer {
        /// <summary>
        /// A material set view transforms are pushed to.
        /// </summary>
        public DefaultMaterialSet MaterialSet;

        /// <summary>
        /// A view transform that is pushed for the duration of the batch group.
        /// </summary>        
        public ViewTransform? ViewTransform {
            get {
                if (_HasViewTransform)
                    return _ViewTransform;
                else
                    return null;
            }
            set {
                if (value == null)
                    _HasViewTransform = false;
                else {
                    _HasViewTransform = true;
                    _ViewTransform = value.Value;
                }
            }
        }

        private bool _HasViewTransform;
        private ViewTransform _ViewTransform;

        /// <summary>
        /// A function that is called to process and modify the current view transform for the duration of the batch group.
        /// </summary>
        public ViewTransformModifier ViewTransformModifier;

        public Frame Frame { get; private set;  }
        int IBatchContainer.FrameIndex {
            get {
                return Frame.Index;
            }
        }

        /// <summary>
        /// If set, contained batches will not have their Issue method called.
        /// </summary>
        public bool DisableIssue = false;

        /// <summary>
        /// If set, DeviceManager.PushStates and PopStates will automatically be called around this batch.
        /// </summary>
        public bool SaveAndRestoreStates = false;

        Action<DeviceManager, object> _Before, _After;
        private object _UserData;

        private static readonly int BatchGroupTypeId = IdForType<BatchGroup>.Id;

        static BatchGroup () {
        }

        void IBatchContainer.PrepareChildren (ref PrepareContext context) {
            _DrawCalls.Sort(Frame.BatchComparer);
            context.PrepareMany(ref _DrawCalls);
        }

        public override void Prepare (PrepareContext context) {
            OnPrepareDone();
        }

        public override void Issue (DeviceManager manager) {
            manager.BatchGroupStack.Push(this);
            if (SaveAndRestoreStates)
                manager.PushStates();

            base.Issue(manager);

            // HACK: If the user set a material on us explicitly, apply our parameters to it
            if (Material != null)
                Material.Flush(manager, ref MaterialParameters);

            bool pop = false;

            if (_HasViewTransform || ViewTransformModifier != null) {
                if (MaterialSet == null)
                    throw new NullReferenceException("MaterialSet must be set if ViewTransform or ViewTransformModifier are set");

                ViewTransform oldVt = MaterialSet.ViewTransform,
                    newVt = _HasViewTransform ? _ViewTransform : oldVt;
                if (ViewTransformModifier != null)
                    ViewTransformModifier(ref newVt, _UserData);

                if (!newVt.Equals(ref oldVt)) {
                    // FIXME: We shouldn't need force: true
                    MaterialSet.PushViewTransform(ref newVt, force: false);
                    if (Material != null)
                        MaterialSet.ApplyViewTransformToMaterial(Material, ref newVt);
                    pop = true;
                }
            }

            if (_Before != null)
                _Before(manager, _UserData);

            try {
                if (!DisableIssue) {
                    var items = _DrawCalls.Items;
                    for (int i = 0, c = items.Count; i < c; i++)
                        items.Array[items.Offset + i]?.IssueAndWrapExceptions(manager);
                }
            } finally {
                if (_After != null)
                    _After(manager, _UserData);

                if (pop) {
                    MaterialSet.PopViewTransform(force: false);
                    // FIXME: Do we need to?
                    /*
                    if (Material != null)
                        MaterialSet.ApplyViewTransformToMaterial(Material, ref MaterialSet.ViewTransformMutable);
                    */
                }

                if (SaveAndRestoreStates)
                    manager.PopStates();
                manager.BatchGroupStack.Pop();
            }
        }

        protected override string FormatName () {
            if (_UserData == null)
                return Name;

            return Name?.Replace("{userData}", _UserData.ToString());
        }

        public static RenderTargetBatchGroup ForRenderTarget (
            IBatchContainer container, int layer, AutoRenderTarget renderTarget, 
            Action<DeviceManager, object> before = null, Action<DeviceManager, object> after = null, 
            object userData = null, 
            DefaultMaterialSet materialSet = null, in ViewTransform? viewTransform = null, 
            string name = null, bool? ignoreInvalidTargets = null
        ) {
            if (container == null)
                throw new ArgumentNullException("container");
            else if (renderTarget?.IsDisposed == true)
                throw new ObjectDisposedException("renderTarget");

            var result = container.RenderManager.AllocateBatch<RenderTargetBatchGroup>();

            result.Initialize(container, layer, before, after, userData, materialSet);
            if (viewTransform.HasValue)
                result.SetViewTransform(in viewTransform);
            result.SingleAuto = renderTarget;
            result.Single = null;
            result.Multiple = null;
            result.Name = name;
            result.IgnoreInvalidRenderTargets = ignoreInvalidTargets ?? result.IgnoreInvalidRenderTargets;
            result.CaptureStack(0);

            return result;
        }

        public static RenderTargetBatchGroup ForRenderTarget (
            IBatchContainer container, int layer, RenderTarget2D renderTarget, 
            Action<DeviceManager, object> before = null, Action<DeviceManager, object> after = null, 
            object userData = null, 
            DefaultMaterialSet materialSet = null, in ViewTransform? viewTransform = null, 
            string name = null, bool? ignoreInvalidTargets = null
        ) {
            if (container == null)
                throw new ArgumentNullException("container");
            else if (renderTarget?.IsDisposed == true)
                throw new ObjectDisposedException("renderTarget");

            var result = container.RenderManager.AllocateBatch<RenderTargetBatchGroup>();

            result.Initialize(container, layer, before, after, userData, materialSet);
            if (viewTransform.HasValue)
                result.SetViewTransform(in viewTransform);
            result.Single = renderTarget;
            result.SingleAuto = null;
            result.Multiple = null;
            result.Name = name;
            result.IgnoreInvalidRenderTargets = ignoreInvalidTargets ?? result.IgnoreInvalidRenderTargets;
            result.CaptureStack(0);

            return result;
        }

        public static RenderTargetBatchGroup ForRenderTargets (
            IBatchContainer container, int layer, RenderTarget2D[] renderTargets, 
            Action<DeviceManager, object> before = null, Action<DeviceManager, object> after = null, 
            object userData = null, 
            DefaultMaterialSet materialSet = null, in ViewTransform? viewTransform = null, 
            string name = null, bool? ignoreInvalidTargets = null
        ) {
            var bindings = new RenderTargetBinding[renderTargets.Length];
            return ForRenderTargets(
                container, layer, bindings,
                before, after, userData,
                materialSet, viewTransform,
                name, ignoreInvalidTargets
            );
        }

        public static RenderTargetBatchGroup ForRenderTargets (
            IBatchContainer container, int layer, RenderTargetBinding[] renderTargets, 
            Action<DeviceManager, object> before = null, Action<DeviceManager, object> after = null, 
            object userData = null, 
            DefaultMaterialSet materialSet = null, in ViewTransform? viewTransform = null, 
            string name = null, bool? ignoreInvalidTargets = null
        ) {
            if (container == null)
                throw new ArgumentNullException("container");

            if ((renderTargets == null) || (renderTargets.Length == 0))
                return ForRenderTarget(container, layer, (RenderTarget2D)null, before, after, userData, name: name);

            foreach (var binding in renderTargets)
                if (binding.RenderTarget?.IsDisposed == true)
                    throw new ObjectDisposedException("renderTargets[i]");

            var result = container.RenderManager.AllocateBatch<RenderTargetBatchGroup>();

            result.Initialize(container, layer, before, after, userData, materialSet);
            if (viewTransform.HasValue)
                result.SetViewTransform(in viewTransform);
            result.Single = null;
            result.SingleAuto = null;
            result.Multiple = renderTargets;
            result.Name = name;
            result.IgnoreInvalidRenderTargets = ignoreInvalidTargets ?? result.IgnoreInvalidRenderTargets;
            result.CaptureStack(0);

            return result;
        }

        public static BatchGroup New (
            IBatchContainer container, int layer, 
            Action<DeviceManager, object> before = null, Action<DeviceManager, object> after = null,
            object userData = null, DefaultMaterialSet materialSet = null, string name = null
        ) {
            if (container == null)
                throw new ArgumentNullException("container");

            var result = container.RenderManager.AllocateBatch<BatchGroup>(BatchGroupTypeId);
            result.Initialize(container, layer, before, after, userData, materialSet);
            result.Name = name;
            result.CaptureStack(0);

            return result;
        }

        public void Initialize (
            IBatchContainer container, int layer, 
            Action<DeviceManager, object> before, Action<DeviceManager, object> after, 
            object userData, DefaultMaterialSet materialSet = null, bool addToContainer = true
        ) {
            Name = "Group";

            base.Initialize(container, layer, null, addToContainer);

            Frame = container.Frame;
            Coordinator = container.Coordinator;
            RenderManager = container.RenderManager;
            _Before = before;
            _After = after;
            _UserData = userData;
            MaterialSet = materialSet;
            _HasViewTransform = false;
            ViewTransformModifier = null;
            IsReleased = false;
            DisableIssue = false;
            SaveAndRestoreStates = false;
        }

        public void SetViewTransform (
            ViewTransformModifier modifier
        ) {
            if ((modifier != null) && (MaterialSet == null))
                throw new ArgumentException("No view transform can be applied without a material set");

            _HasViewTransform = false;
            ViewTransformModifier = modifier;
        }

        public void SetViewTransform (
            in ViewTransform viewTransform
        ) {
            if (MaterialSet == null)
                throw new ArgumentException("No view transform can be applied without a material set");

            _HasViewTransform = true;
            _ViewTransform = viewTransform;
            ViewTransformModifier = null;
        }

        public void SetViewTransform (
            in ViewTransform? viewTransform
        ) {
            if (viewTransform.HasValue && (MaterialSet == null))
                throw new ArgumentException("No view transform can be applied without a material set");

            _HasViewTransform = viewTransform.HasValue;
            if (viewTransform.HasValue) {
                _ViewTransform = viewTransform.Value;
                ViewTransformModifier = null;
            }
        }

        public RenderCoordinator Coordinator {
            get;
            private set;
        }

        public RenderManager RenderManager {
            get;
            private set;
        }

        public bool IsEmpty => Count == 0;

        new public bool IsReleased { get; private set; }

        public void Remove (Batch batch) {
            if (batch == null)
                return;

            var items = _DrawCalls.Items;
            var offset = Array.IndexOf(items.Array, batch, items.Offset, items.Count);
            if (offset >= 0)
                // FIXME: Unordered?
                _DrawCalls.RemoveAtOrdered(offset);
        }

        public void Add (Batch batch) {
            if (batch == null)
                throw new ArgumentNullException("batch");
            if (batch.Container != null)
                throw new InvalidOperationException("This batch is already in another container.");

            batch.Container = this;
            base.Add(ref batch);
        }

        protected override void OnReleaseResources () {
            IsReleased = true;

            _DrawCalls.Clear();

            base.OnReleaseResources();

            // Avoid retaining objects while sitting in the pool
            _Before = _After = null;
            _UserData = null;
            ViewTransformModifier = null;
        }

        public override string ToString () {
            if (Name != null)
                return string.Format("{4} '{0}' #{1} {2} layer={5} material={3}", FormatName(), InstanceId, StateString, Material, (this is RenderTargetBatchGroup) ? "RT Batch" : "Batch", Layer);
            else
                return string.Format("{4} #{1} {2} layer={5} material={3}", FormatName(), InstanceId, StateString, Material, (this is RenderTargetBatchGroup) ? "RT Batch" : "Batch", Layer);
        }
    }

    public sealed class RenderTargetBatchGroup : BatchGroup {
#if DEBUG
        public bool IgnoreInvalidRenderTargets = false;
#else
        public bool IgnoreInvalidRenderTargets = true;
#endif

        internal RenderTarget2D Single;
        internal AutoRenderTarget SingleAuto;
        internal RenderTargetBinding[] Multiple;

        public override void Issue (DeviceManager manager) {
            var single = Single;
            if (SingleAuto != null)
                single = SingleAuto.Get();

            var isValid = true;

            if (Multiple != null) {
                foreach (var rt in Multiple)
                    if (!AutoRenderTarget.IsRenderTargetValid(rt.RenderTarget))
                        isValid = false;

                if (!isValid) {
                    if (IgnoreInvalidRenderTargets) {
                        MarkAsIssued(manager);
                        return;
                    } else
                        throw new ObjectDisposedException("Invalid render target for batch group " + Name);
                }

                manager.PushRenderTargets(Multiple);
            } else {
                if (single != null && (!AutoRenderTarget.IsRenderTargetValid(single)))
                    isValid = false;

                if (!isValid) {
                    if (IgnoreInvalidRenderTargets) {
                        MarkAsIssued(manager);
                        return;
                    } else
                        throw new ObjectDisposedException("Invalid render target for batch group " + Name);
                }

                manager.PushRenderTarget(single);
            }

            try {
                base.Issue(manager);
            } finally {
                manager.PopRenderTarget();
            }
        }

        protected override void OnReleaseResources () {
            base.OnReleaseResources();

            // Avoid retaining objects while sitting in the pool
            Single = null;
            SingleAuto = null;
            Multiple = null;
        }
    }
}
