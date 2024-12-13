﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Squared.Game;
using Squared.PRGUI.Accessibility;
using Squared.PRGUI.Decorations;
using Squared.PRGUI.Flags;
using Squared.PRGUI.Imperative;
using Squared.PRGUI.Layout;
using Squared.PRGUI.NewEngine;
using Squared.Render.Convenience;
using Squared.Util;

namespace Squared.PRGUI.Controls {
    /// <summary>
    /// A control that owns child controls with no particular layout or rendering behavior
    /// </summary>
    public abstract class ControlParentBase : Control, IControlContainer {
        private ControlCollection _Children;
        protected ControlCollection Children {
            get {
                var context = Context;
                if (_Children == null)
                    _Children = new ControlCollection(this, context);
                EnsureChildrenAreValid(context);
                return _Children;
            }
        }

        protected Control DefaultFocusTarget = null;
        Control IControlContainer.DefaultFocusTarget => DefaultFocusTarget;

        ControlCollection IControlContainer.Children => Children;
        int IControlContainer.ChildrenToSkipWhenBuilding => ChildrenToSkipWhenBuilding;

        public IAcceleratorSource AcceleratorSource { get; set; }
        public IControlEventFilter ChildEventFilter { get; set; }

        protected ControlParentBase () 
            : base () {
        }

        protected virtual int ChildrenToSkipWhenBuilding => 0;

        /// <summary>
        /// If set, children will only be rendered within the volume of this container
        /// </summary>
        protected bool ClipChildren { get; set; } = false;

        bool IControlContainer.ChildrenAcceptFocus => true;

        bool IControlContainer.ClipChildren {
            get => ClipChildren;
            set => ClipChildren = value;
        }

        public ControlFlags ContainerFlags { get; set; } =
            ControlFlags.Container_Align_Start | ControlFlags.Container_Row |
            ControlFlags.Container_Break_Allow;
        // If we set Break_Auto here more things in the new engine act like the old engine,
        //  but it's a very bad default
        // ControlFlags.Container_Break_Auto;

        /// <summary>
        /// Overrides ContainerFlags
        /// </summary>
        public ContainerFlags Container;

        /// <summary>
        /// Extra container flags that will be or'd in at layout time
        /// </summary>
        protected ControlFlags ExtraContainerFlags = default(ControlFlags);

        protected override bool ShouldClipContent => ClipChildren && (Children.Count > 0);
        // FIXME: Always true?
        protected override bool HasChildren => (Context != null) && (Children.Count > 0);

        /// <summary>
        /// If false, child hit testing will occur even if a point lies outside of our bounds
        /// </summary>
        protected virtual bool ConstrainChildHitTests => true;

        protected virtual bool HideChildren => false;

        public override void InvalidateLayout () {
            base.InvalidateLayout();
            var children = Children;
            foreach (var ch in children)
                ch.InvalidateLayout();
        }
        
        public override void ClearLayoutKey () {
            base.ClearLayoutKey();
            var children = Children;
            foreach (var ch in children)
                ch.ClearLayoutKey();
        }

        bool IControlContainer.IsControlHidden (Control child) => false;

        protected abstract void EnsureChildrenAreValid (UIContext context);
        protected abstract void OnDescendantReceivedFocus (Control control, bool isUserInitiated);

        /// <summary>
        /// Rasterizes a child control and updates the pass layer data
        /// </summary>
        /// <returns>Whether the child was successfully rasterized</returns>
        protected virtual bool RasterizeChild (
            ref UIOperationContext context, Control item, ref RasterizePassSet passSet
        ) {
            return item.Rasterize(ref context, ref passSet);
        }

        protected override void OnVisibleChange (bool newValue) {
            base.OnIntangibleChange(newValue);

            if (newValue)
                return;

            ReleaseChildFocus();
        }

        protected void ReleaseChildFocus () {
            if (IsEqualOrAncestor(Context?.Focused, this))
                Context.NotifyControlBecomingInvalidFocusTarget(Context.Focused, false);
        }

        void IControlContainer.DescendantReceivedFocus (Control descendant, bool isUserInitiated) {
            OnDescendantReceivedFocus(descendant, isUserInitiated);
        }

        protected bool HitTestShell (RectF box, Vector2 position, ref HitTestState state) {
            return base.OnHitTest(box, position, ref state);
        }

        protected virtual bool HitTestInterior (RectF box, Vector2 position, ref HitTestState state) {
            return (state.Options.AcceptsMouseInput == null) || (state.Options.AcceptsMouseInput == AcceptsMouseInput);
        }

        public T Child<T> (Func<T, bool> predicate)
            where T : Control {

            foreach (var child in Children) {
                if (!(child is T))
                    continue;
                var t = (T)child;
                if (predicate(t))
                    return t;
            }

            return null;
        }
    }

    /// <summary>
    /// A control that can host multiple child controls with support for scrolling, columns, etc
    /// Child controls are arranged based on its ContainerFlags
    /// </summary>
    public abstract class ContainerBase : ControlParentBase, IControlContainer {
        protected virtual Vector2 AbsoluteDisplayOffsetOfChildren => AbsoluteDisplayOffset;

        private int _ColumnCount = 1;

        public bool[] ColumnExpansion;

        protected DenseList<ControlKey> ColumnKeys;
        protected bool NeedToInvalidateChildLayout;

        /// <summary>
        /// Splits the container into multiple columns arranged left-to-right.
        /// Children will automatically be distributed across the columns, and
        ///  each column will inherit this container's ContainerFlags.
        /// </summary>
        public virtual int ColumnCount {
            get => _ColumnCount;
            set {
                if (value == _ColumnCount)
                    return;
                _ColumnCount = value;
                InvalidateLayout();
            }
        }

        /// <summary>
        /// If set, this control will never be a valid result from HitTest, but its
        ///  children may be
        /// </summary>
        protected bool DisableSelfHitTests = false;
        /// <summary>
        /// If set, children will not be processed by HitTest
        /// </summary>
        protected bool DisableChildHitTests = false;

        /// <summary>
        /// Enables DynamicContents. Disable this if you plan to generate children yourself in a derived type
        /// </summary>
        protected bool AllowDynamicContent = true;

        protected ContainerBuilder DynamicBuilder;
        protected ContainerContentsDelegate _DynamicContents;
        /// <summary>
        /// If set, every update this delegate will be invoked to reconstruct the container's children
        /// </summary>        
        public ContainerContentsDelegate DynamicContents {
            get => _DynamicContents;
            set {
                if (!AllowDynamicContent)
                    value = null;
                if (_DynamicContents == value)
                    return;
                _DynamicContents = value;
                DynamicContentIsInvalid = true;
            }
        }
        /// <summary>
        /// If true, dynamic contents will only be updated when this container is invalidated
        /// </summary>
        public bool CacheDynamicContent;

        protected bool DynamicContentIsInvalid = true;
        protected bool FreezeDynamicContent = false;
        protected bool SuppressChildLayout = false;

        public ContainerBase () 
            : base () {
        }

        protected override void EnsureChildrenAreValid (UIContext context) {
            if (context == null)
                return;
            if (DynamicContentIsInvalid)
                GenerateDynamicContent(false || DynamicContentIsInvalid);
        }

        protected override void OnDescendantReceivedFocus (Control control, bool isUserInitiated) {
        }

        public void InvalidateDynamicContent () {
            DynamicContentIsInvalid = true;
        }

        private bool IsGeneratingDynamicContent = false,
            IsNewInstanceForDynamicContent = true;

        internal void EnsureDynamicBuilderInitialized (out ContainerBuilder result) {
            if (
                (DynamicContents == null) && 
                (DynamicBuilder.Container == this)
            ) {
                DynamicBuilder.PreviousRemovedControls.EnsureList();
                DynamicBuilder.CurrentRemovedControls.EnsureList();
                result = DynamicBuilder;
            } else {
                result = DynamicBuilder = new ContainerBuilder(this, IsNewInstanceForDynamicContent);
                IsNewInstanceForDynamicContent = false;
            }
        }

        protected void GenerateDynamicContent (bool force) {
            DynamicContentIsInvalid = false;

            if (DynamicContents == null)
                return;

            if ((FreezeDynamicContent || CacheDynamicContent) && !force)
                return;

            if (IsGeneratingDynamicContent)
                return;

            IsGeneratingDynamicContent = true;
            try {
                if (DynamicBuilder.Container != this)
                    DynamicBuilder = new ContainerBuilder(this, true);
                else
                    DynamicBuilder.Reset();
                DynamicContents(ref DynamicBuilder);
                DynamicBuilder.Finish();
                // FIXME: Is this right?
                DynamicContentIsInvalid = false;
            } finally {
                IsGeneratingDynamicContent = false;
            }
        }

        protected ControlFlags ComputeContainerFlags () {
            var declarative = (ControlFlags)Container;
            var expl = (ContainerFlags & Container.Mask);
            // HACK: If the declarative properties were used to set column or row, clear the arrangement flag from ContainerFlags
            // If this isn't done, doing Container.Column will break if Container.Row is also set
            if (((declarative & ControlFlags.Container_Column) | (declarative & ControlFlags.Container_Row)) != default)
                expl &= ~(ControlFlags.Container_Row | ControlFlags.Container_Column);
            return declarative | expl;
        }

        protected virtual ControlKey CreateColumn (ref UIOperationContext context, ControlKey parent, int columnIndex) {
            ref var result = ref context.Engine.Create(LayoutTags.Column, parent: parent);
            // FIXME
            var cf = ComputeContainerFlags();
            var resultFlags = cf | ControlFlags.Container_Prevent_Crush_Y;
            // FIXME
            if (cf.IsFlagged(ControlFlags.Container_No_Expansion))
                resultFlags |= ControlFlags.Container_No_Expansion;

            ControlFlags layoutFlags;
            if ((ColumnExpansion == null) || (columnIndex >= ColumnExpansion.Length) || ColumnExpansion[columnIndex])
                layoutFlags = ControlFlags.Layout_Fill_Row | ControlFlags.Layout_Anchor_Top;
            else
                layoutFlags = columnIndex >= (ColumnCount - 1)
                    ? ControlFlags.Layout_Anchor_Right | ControlFlags.Layout_Anchor_Top
                    : ControlFlags.Layout_Anchor_Left | ControlFlags.Layout_Anchor_Top;

            result.OldFlags = layoutFlags | resultFlags;
            // context.Engine.SetContainerFlags(parent, );
            return result.Key;
        }
        
        protected override ref BoxRecord OnGenerateLayoutTree (ref UIOperationContext context, ControlKey parent, ControlKey? existingKey) {
            var wasInvalid = IsLayoutInvalid;
            ref var result = ref base.OnGenerateLayoutTree(ref context, parent, existingKey);
            result.Tag = LayoutTags.Container;

            var children = Children;
            if (result.IsInvalid || SuppressChildLayout) {
                NeedToInvalidateChildLayout = true;

                return ref result;
            } else if (NeedToInvalidateChildLayout) {
                NeedToInvalidateChildLayout = false;
                foreach (var item in children)
                    item.InvalidateLayout();
            }

            var containerFlags = ComputeContainerFlags() | ExtraContainerFlags;
            var multiColumn = (ColumnCount > 1) || false;
            if (multiColumn) {
                containerFlags = ComputeContainerFlags()
                    & ~ControlFlags.Container_Column
                    & ~ControlFlags.Container_Break_Allow;
                containerFlags |= ControlFlags.Container_Row;
            }

            result.OldContainerFlags = containerFlags;

            if ((context.HiddenCount <= 0) && Visible)
                GenerateDynamicContent(DynamicContentIsInvalid);

            if (!existingKey.HasValue)
                ColumnKeys.Clear();

            if (multiColumn) {
                if (!existingKey.HasValue)
                    for (int i = 0; i < ColumnCount; i++)
                        ColumnKeys.Add(CreateColumn(ref context, result, i));
            } else {
                ColumnKeys.Add(result);
            }

            var adoc = AbsoluteDisplayOffsetOfChildren;
            for (int i = 0, c = children.Count; i < c; i++) {
                var item = children[i];
                var columnIndex = i % ColumnCount;
                // FIXME: We shouldn't need to do this every frame
                item.AbsoluteDisplayOffset = DoesIgnoreScrolling(item) ? AbsoluteDisplayOffset : adoc;

                // If we're performing layout again on an existing layout item, attempt to do the same
                //  for our children
                var childExistingKey = (ControlKey?)null;
                if ((existingKey.HasValue) && !item.LayoutKey.IsInvalid)
                    childExistingKey = item.LayoutKey;

                item.GenerateLayoutTree(ref context, ColumnKeys[columnIndex], childExistingKey);
            }

            return ref result;
        }

        protected override void OnDisplayOffsetChanged () {
            var adoc = AbsoluteDisplayOffsetOfChildren;
            var children = Children;
            foreach (var child in children)
                child.AbsoluteDisplayOffset = DoesIgnoreScrolling(child) ? AbsoluteDisplayOffset : adoc;
        }

        protected override void OnRasterizeChildren (ref UIOperationContext context, ref RasterizePassSet passSet, DecorationSettings settings) {
            if (HideChildren)
                return;

            RasterizeChildrenInOrder(ref context, ref passSet);
        }

        internal static ref RasterizePassSet PickPassSet (bool cond, ref RasterizePassSet t, ref RasterizePassSet f) {
            if (cond)
                return ref t;
            else
                return ref f;
        }

        protected virtual void RasterizeChildrenInOrder (
            ref UIOperationContext context, ref RasterizePassSet passSet
        ) {
            var sequence = Children.InDisplayOrder(Context.FrameIndex, out ControlCollection.OrderRange range);
            if (sequence.Count <= 0)
                return;

            int firstSplitPlaneLayer = passSet.Content.Layer;
            int layerB = passSet.Below.Layer,
                layerC = passSet.Content.Layer,
                layerA = passSet.Above.Layer;

#if !NOSPAN
            RasterizePassSet currentLayerContext;
            System.Runtime.CompilerServices.Unsafe.SkipInit(out currentLayerContext);
#else
            var currentLayerContext = default(RasterizePassSet);
#endif
            
            var currentContextOrder = int.MinValue;
            var hasSplitPlane = false;
            // FIXME: In rare circumstances a container could try to paint overlay content into its Above pass,
            //  but that content would inconsistently cover children's Above passes depending on whether this
            //  per-layer-context mode is true or not
            var isUsingPerLayerContext = (range.Min != range.Max);

            foreach (var item in sequence) {
                // HACK: If we don't do this, invisible controls can cause a plane split and prevent neighboring controls from sharing layers
                if (!item.Visible)
                    continue;

                if (
                    isUsingPerLayerContext && 
                    (currentContextOrder != item.DisplayOrder) ||
                    // HACK: If we are ever going to split a plane, we need to split once right away at the start.
                    // This ensures that the 'above' layer of a control with DisplayOrder 0 cannot paint over the 
                    //  'above' layer of a control with DisplayOrder 1. The old behavior (only splitting after the
                    //  first plane is done) would have placed the split entirely in the Content layer, below the
                    //  Above content of the first layer.
                    !hasSplitPlane
                ) {
                    var newBaseLayer = firstSplitPlaneLayer++;
                    // FIXME: In the case where we have a nested hierarchy like:
                    // A { B { C D } E { F G } }
                    // we want to put B+E in the same contexts, along with putting C+F and D+G into the same contexts
                    // this would require us to find the batch containers we generated for C/D when constructing them for F and G.
                    // FIXME: Should we use the constructor that nests the new Below/Content/Above inside of the outer
                    //  Below/Content/Above?
                    currentLayerContext = new RasterizePassSet(ref passSet.Content, this, passSet.StackDepth, ref newBaseLayer);
                    currentContextOrder = item.DisplayOrder;
                    hasSplitPlane = true;
                    layerB = layerC = layerA = 0;
                }

                ref var targetPassSet = ref PickPassSet(hasSplitPlane, ref currentLayerContext, ref passSet);

                targetPassSet.Below.Layer = layerB;
                targetPassSet.Content.Layer = layerC;
                targetPassSet.Above.Layer = layerA;
                RasterizeChild(ref context, item, ref targetPassSet);
            }
        }

        /// <summary>
        /// Intelligently rasterize children starting from an automatically selected
        ///  midpoint, instead of rasterizing all of our children.
        /// May draw an unnecessarily large number of children in some cases, but will
        ///  typically only draw slightly more than the number of children currently
        ///  in view.
        /// </summary>
        /// <returns>The number of child controls rasterization was attempted for</returns>
        public int RasterizeChildrenFromCenter (
            ref UIOperationContext context, ref RasterizePassSet passSet, 
            RectF box, Control selectedItem,
            ref int lastOffset1, ref int lastOffset2,
            DenseList<Control> extraControls = default
        ) {
            // FIXME: This does not handle display order correctly.
            var children = Children;
            if (children.Count <= 0)
                return 0;

            RectF childRect =
                (selectedItem != null)
                    ? selectedItem.GetRect()
                    : default(RectF);

            int count = children.Count, 
                selectedIndex = children.IndexOf(selectedItem), 
                startOffset = (
                    (selectedIndex >= 0) &&
                    box.Intersects(in childRect)
                )
                    // If we have a selected item and the selected item is visible, begin painting
                    //  from its position
                    ? selectedIndex
                    : (
                        (
                            (lastOffset1 >= 0) &&
                            (lastOffset2 >= 0) &&
                            (lastOffset2 < count)
                        )
                            // Otherwise, start painting from the midpoint of the last paint region
                            ? (lastOffset1 + lastOffset2) / 2
                            // And if we don't have a last paint region, start from our midpoint
                            : count / 2
                    );
            bool hasRenderedAny = false;
            startOffset = Arithmetic.Clamp(startOffset, 0, count - 1);

            int itemsAttempted = 0;
            for (int i = startOffset, j = startOffset; (i >= 0) || (j < count); i--, j++) {
                if (i >= 0) {
                    itemsAttempted++;
                    // Stop searching upward once an item fails to render
                    var item1 = children[i];
                    // HACK
                    if (item1.DisplayOrder != 0) {
                        if (!extraControls.Contains(item1))
                            extraControls.Add(item1);
                    } else {
                        extraControls.Remove(item1);
                        var ok = RasterizeChild(
                            ref context, item1, ref passSet
                        );
                        if (!item1.Visible) {
                            ;
                        } else if (!ok && hasRenderedAny) {
                            lastOffset1 = i;
                            i = -1;
                        } else if (ok) {
                            hasRenderedAny = true;
                        }
                    }
                }

                if ((i != j) && (j < count)) {
                    itemsAttempted++;
                    var item2 = children[j];
                    // HACK
                    if (item2.DisplayOrder != 0) {
                        if (!extraControls.Contains(item2))
                            extraControls.Add(item2);
                    } else {
                        extraControls.Remove(item2);
                        var ok = RasterizeChild(
                            ref context, item2, ref passSet
                        );
                        if (!item2.Visible) {
                            ;
                        } else if (!ok && hasRenderedAny) {
                            lastOffset2 = j;
                            j = count;
                        } else if (ok) {
                            hasRenderedAny = true;
                        }
                    }
                }
            }

            extraControls.Sort(IndexPreservingPaintOrderComparer.Instance);
            var lastOrder = 0;
            foreach (var ctl in extraControls) {
                // FIXME
                if (ctl == null)
                    continue;
                // HACK: Do a clumsy plane split so paint order isn't completely broken.
                // This ensures that menu filter boxes actually paint over menu items.
                if (ctl.DisplayOrder != lastOrder)
                    // HACK: stackDepth needs to == passSet.StackDepth, otherwise clipping will break when controls are filtered.
                    // I'm not really sure why.
                    passSet = new RasterizePassSet(ref passSet.Content, this, passSet.StackDepth);
                RasterizeChild(ref context, ctl, ref passSet);
            }

            return itemsAttempted;
        }

        protected override void OnVisibleChange (bool newValue) {
            DynamicContentIsInvalid = true;
            base.OnIntangibleChange(newValue);
        }

        protected bool HitTestChildren (Vector2 position, ref HitTestState state) {
            if (DisableChildHitTests)
                return false;

            // FIXME: Should we only perform the hit test if the position is within our boundaries?
            // This doesn't produce the right outcome when a container's computed size is zero
            var sorted = Children.InDisplayOrder(Context.FrameIndex);
            if (sorted.Count <= 0)
                return false;

            for (int i = sorted.Count - 1; i >= 0; i--) {
                var item = sorted[i];
                var newResult = item.HitTest(position, in state.Options);
                if (newResult != null) {
                    state.Result = newResult;
                    return true;
                }
            }

            return false;
        }

        protected override bool OnHitTest (RectF box, Vector2 position, ref HitTestState state) {
            var temp = state;
            temp.Options.AcceptsMouseInput = temp.Options.AcceptsFocus = null;
            var shell = HitTestShell(box, position, ref temp);
            if (shell)
                state.Result = temp.Result;

            if (!shell && ConstrainChildHitTests)
                return false;

            bool success = !DisableSelfHitTests && HitTestInterior(box, position, ref state) && shell;
            success |= HitTestChildren(position, ref state);
            return success;
        }
    }

    public class ControlGroup : ContainerBase, IEnumerable<Control> {
        new public ControlCollection Children {
            get => base.Children;
        }
        new public bool ClipChildren {
            get => base.ClipChildren;
            set => base.ClipChildren = value;
        }
        new public bool DisableSelfHitTests {
            get => base.DisableSelfHitTests;
            set => base.DisableSelfHitTests = value;
        }
        new public bool DisableChildHitTests {
            get => base.DisableChildHitTests;
            set => base.DisableChildHitTests = value;
        }

        protected override bool CreateNestedContextForChildren => false;

        public ControlGroup ()
            : this (false, false) {
        }

        public ControlGroup (bool forceBreak = false, bool preventCrush = false, bool fill = false)
            : base () {
            Appearance.Undecorated = true;
            Layout.ForceBreak = forceBreak;
            Container.PreventCrush = preventCrush;
            DisableSelfHitTests = true;
            if (fill)
                Layout.Fill = true;
        }

        // For simple initializers
        public void Add (Control control) {
            Children.Add(control);
        }

        protected override ref BoxRecord OnGenerateLayoutTree (ref UIOperationContext context, ControlKey parent, ControlKey? existingKey) {
            ref var result = ref base.OnGenerateLayoutTree(ref context, parent, existingKey);
            result.Tag = LayoutTags.Group;
            return ref result;
        }

        DenseList<Control>.Enumerator GetEnumerator () => Children.GetEnumerator();
        IEnumerator<Control> IEnumerable<Control>.GetEnumerator () => GetEnumerator();            
        IEnumerator IEnumerable.GetEnumerator () => GetEnumerator();
    }
}
