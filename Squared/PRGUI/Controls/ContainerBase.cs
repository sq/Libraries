using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Squared.Game;
using Squared.PRGUI.Decorations;
using Squared.PRGUI.Imperative;
using Squared.PRGUI.Layout;
using Squared.Render.Convenience;
using Squared.Util;

namespace Squared.PRGUI.Controls {
    public abstract class ContainerBase : Control, IControlContainer {
        protected ControlCollection _Children;
        protected ControlCollection Children {
            get {
                if (DynamicContentIsInvalid)
                    GenerateDynamicContent(false || DynamicContentIsInvalid);
                return _Children;
            }
        }

        ControlCollection IControlContainer.Children => Children;

        protected Vector2 AbsoluteDisplayOffsetOfChildren;

        private int _ColumnCount = 1;
        public virtual int ColumnCount {
            get => _ColumnCount;
            set => _ColumnCount = value;
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
        /// If set, children will only be rendered within the volume of this container
        /// </summary>
        protected bool ClipChildren { get; set; } = false;

        bool IControlContainer.ClipChildren {
            get => ClipChildren;
            set => ClipChildren = value;
        }

        public ControlFlags ContainerFlags { get; set; } =
            ControlFlags.Container_Align_Start | ControlFlags.Container_Row | 
            ControlFlags.Container_Wrap;

        protected ControlFlags ExtraContainerFlags = default(ControlFlags);

        public bool PreventCrush {
            get => ContainerFlags.IsFlagged(ControlFlags.Container_Prevent_Crush);
            set => ContainerFlags = (ContainerFlags & ~ControlFlags.Container_Prevent_Crush) |
                (value ? ControlFlags.Container_Prevent_Crush : default(ControlFlags));
        }

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
            _Children = new ControlCollection(this);
        }

        public void InvalidateDynamicContent () {
            DynamicContentIsInvalid = true;
        }

        public override void InvalidateLayout () {
            base.InvalidateLayout();
            for (int i = 0, c = (ColumnKeys?.Length ?? 0); i < c; i++)
                ColumnKeys[i] = ControlKey.Invalid;
            foreach (var ch in _Children)
                ch.InvalidateLayout();
        }

        private bool IsGeneratingDynamicContent = false;

        internal void EnsureDynamicBuilderInitialized (out ContainerBuilder result) {
            if (
                (DynamicContents == null) && 
                (DynamicBuilder.Container == this)
            ) {
                DynamicBuilder.PreviousRemovedControls.EnsureList();
                DynamicBuilder.CurrentRemovedControls.EnsureList();
                result = DynamicBuilder;
            } else {
                result = DynamicBuilder = new ContainerBuilder(this);
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
                    DynamicBuilder = new ContainerBuilder(this);
                DynamicBuilder.Reset();
                DynamicContents(ref DynamicBuilder);
                DynamicBuilder.Finish();
                // FIXME: Is this right?
                DynamicContentIsInvalid = false;
            } finally {
                IsGeneratingDynamicContent = false;
            }
        }

        protected virtual ControlKey CreateColumn (UIOperationContext context, ControlKey parent, int columnIndex) {
            var result = context.Layout.CreateItem();
            context.Layout.InsertAtEnd(parent, result);
            context.Layout.SetLayoutFlags(result, ControlFlags.Layout_Fill);
            context.Layout.SetContainerFlags(result, ContainerFlags | ControlFlags.Container_Prevent_Crush_Y);
            // context.Layout.SetContainerFlags(parent, );
            return result;
        }

        protected ControlKey[] ColumnKeys;
        
        protected override ControlKey OnGenerateLayoutTree (UIOperationContext context, ControlKey parent, ControlKey? existingKey) {
            var result = base.OnGenerateLayoutTree(context, parent, existingKey);
            if (result.IsInvalid) {
                foreach (var item in _Children)
                    item.InvalidateLayout();

                return result;
            }

            var containerFlags = ContainerFlags | ExtraContainerFlags;
            var multiColumn = (ColumnCount > 1) || false;
            if (multiColumn) {
                containerFlags = ContainerFlags
                    & ~ControlFlags.Container_Column
                    & ~ControlFlags.Container_Wrap;
                containerFlags |= ControlFlags.Container_Row;
            }

            context.Layout.SetContainerFlags(result, containerFlags);

            if (SuppressChildLayout) {
                // FIXME: We need to also lock our minimum width in this case
                // HACK
                foreach (var item in _Children)
                    item.InvalidateLayout();

                return result;
            } else {
                GenerateDynamicContent(false || DynamicContentIsInvalid);

                if (ColumnCount != (ColumnKeys?.Length ?? 0))
                    ColumnKeys = new ControlKey[ColumnCount];

                if (multiColumn) {
                    if (!existingKey.HasValue)
                        for (int i = 0; i < ColumnCount; i++)
                            ColumnKeys[i] = CreateColumn(context, result, i);
                } else {
                    ColumnKeys[0] = result;
                }

                for (int i = 0, c = _Children.Count; i < c; i++) {
                    var item = _Children[i];
                    var columnIndex = i % ColumnCount;
                    item.AbsoluteDisplayOffset = AbsoluteDisplayOffsetOfChildren;

                    // If we're performing layout again on an existing layout item, attempt to do the same
                    //  for our children
                    var childExistingKey = (ControlKey?)null;
                    if ((existingKey.HasValue) && !item.LayoutKey.IsInvalid)
                        childExistingKey = item.LayoutKey;

                    var itemKey = item.GenerateLayoutTree(ref context, ColumnKeys[columnIndex], childExistingKey);
                }
                return result;
            }
        }

        protected override void OnDisplayOffsetChanged () {
            AbsoluteDisplayOffsetOfChildren = AbsoluteDisplayOffset.Floor();

            foreach (var child in _Children)
                child.AbsoluteDisplayOffset = AbsoluteDisplayOffsetOfChildren;
        }

        protected override bool ShouldClipContent => ClipChildren && (_Children.Count > 0);
        // FIXME: Always true?
        protected override bool HasChildren => (Children.Count > 0);

        protected virtual bool HideChildren => false;

        protected override void OnRasterizeChildren (UIOperationContext context, ref RasterizePassSet passSet, DecorationSettings settings) {
            if (HideChildren)
                return;

            // FIXME
            int layer1 = passSet.Below.Layer,
                layer2 = passSet.Content.Layer,
                layer3 = passSet.Above.Layer,
                maxLayer1 = layer1,
                maxLayer2 = layer2,
                maxLayer3 = layer3;

            RasterizeChildrenInOrder(
                ref context, ref passSet, 
                layer1, layer2, layer3,
                ref maxLayer1, ref maxLayer2, ref maxLayer3
            );

            passSet.Below.Layer = maxLayer1;
            passSet.Content.Layer = maxLayer2;
            passSet.Above.Layer = maxLayer3;
        }

        protected virtual void RasterizeChildrenInOrder (
            ref UIOperationContext context, ref RasterizePassSet passSet, 
            int layer1, int layer2, int layer3, 
            ref int maxLayer1, ref int maxLayer2, ref int maxLayer3
        ) {
            var sequence = Children.InDisplayOrder(Context.FrameIndex);
            foreach (var item in sequence)
                RasterizeChild(ref context, item, ref passSet, layer1, layer2, layer3, ref maxLayer1, ref maxLayer2, ref maxLayer3);
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
            int layer1, int layer2, int layer3, 
            ref int maxLayer1, ref int maxLayer2, ref int maxLayer3,
            ref int lastOffset1, ref int lastOffset2
        ) {
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
                    box.Intersects(ref childRect)
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

            int itemsAttempted = 0;
            for (int i = startOffset, j = startOffset; (i >= 0) || (j < count); i--, j++) {
                if (i >= 0) {
                    itemsAttempted++;
                    // Stop searching upward once an item fails to render
                    var item1 = children[i];
                    var ok = RasterizeChild(
                        ref context, item1, ref passSet,
                        layer1, layer2, layer3,
                        ref maxLayer1, ref maxLayer2, ref maxLayer3
                    );
                    if (item1.IsTransparent) {
                        ;
                    } else if (!ok && hasRenderedAny) {
                        lastOffset1 = i;
                        i = -1;
                    } else if (ok) {
                        hasRenderedAny = true;
                    }
                }

                if (j < count) {
                    itemsAttempted++;
                    var item2 = children[j];
                    var ok = RasterizeChild(
                        ref context, item2, ref passSet,
                        layer1, layer2, layer3,
                        ref maxLayer1, ref maxLayer2, ref maxLayer3
                    );
                    if (item2.IsTransparent) {
                        ;
                    } else if (!ok && hasRenderedAny) {
                        lastOffset2 = j;
                        j = count;
                    } else if (ok) {
                        hasRenderedAny = true;
                    }
                }
            }

            return itemsAttempted;
        }

        /// <summary>
        /// Rasterizes a child control and updates the pass layer data
        /// </summary>
        /// <returns>Whether the child was successfully rasterized</returns>
        protected virtual bool RasterizeChild (
            ref UIOperationContext context, Control item, ref RasterizePassSet passSet, 
            int layer1, int layer2, int layer3, ref int maxLayer1, 
            ref int maxLayer2, ref int maxLayer3
        ) {
            passSet.Below.Layer = layer1;
            passSet.Content.Layer = layer2;
            passSet.Above.Layer = layer3;

            var result = item.Rasterize(ref context, ref passSet);

            maxLayer1 = Math.Max(maxLayer1, passSet.Below.Layer);
            maxLayer2 = Math.Max(maxLayer2, passSet.Content.Layer);
            maxLayer3 = Math.Max(maxLayer3, passSet.Above.Layer);

            return result;
        }

        protected override void OnVisibilityChange (bool newValue) {
            base.OnVisibilityChange(newValue);

            DynamicContentIsInvalid = true;
            if (newValue)
                return;

            ReleaseChildFocus();
        }

        protected void ReleaseChildFocus () {
            if (IsEqualOrAncestor(Context?.Focused, this))
                Context.NotifyControlBecomingInvalidFocusTarget(Context.Focused, false);
        }

        protected bool HitTestShell (RectF box, Vector2 position, bool acceptsMouseInputOnly, bool acceptsFocusOnly, bool rejectIntangible, ref Control result) {
            return base.OnHitTest(box, position, acceptsMouseInputOnly, acceptsFocusOnly, rejectIntangible, ref result);
        }

        protected virtual bool HitTestInterior (RectF box, Vector2 position, bool acceptsMouseInputOnly, bool acceptsFocusOnly, ref Control result) {
            return AcceptsMouseInput || !acceptsMouseInputOnly;
        }

        protected bool HitTestChildren (Vector2 position, bool acceptsMouseInputOnly, bool acceptsFocusOnly, bool rejectIntangible, ref Control result) {
            if (DisableChildHitTests)
                return false;

            // FIXME: Should we only perform the hit test if the position is within our boundaries?
            // This doesn't produce the right outcome when a container's computed size is zero
            var sorted = Children.InDisplayOrder(Context.FrameIndex);
            for (int i = sorted.Count - 1; i >= 0; i--) {
                var item = sorted[i];
                var newResult = item.HitTest(position, acceptsMouseInputOnly, acceptsFocusOnly, rejectIntangible);
                if (newResult != null) {
                    result = newResult;
                    return true;
                }
            }

            return false;
        }

        protected override bool OnHitTest (RectF box, Vector2 position, bool acceptsMouseInputOnly, bool acceptsFocusOnly, bool rejectIntangible, ref Control result) {
            if (!HitTestShell(box, position, false, false, rejectIntangible, ref result))
                return false;

            bool success = !DisableSelfHitTests && HitTestInterior(box, position, acceptsMouseInputOnly, acceptsFocusOnly, ref result);
            success |= HitTestChildren(position, acceptsMouseInputOnly, acceptsFocusOnly, rejectIntangible, ref result);
            return success;
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

        protected virtual void OnDescendantReceivedFocus (Control control, bool isUserInitiated) {
        }

        void IControlContainer.DescendantReceivedFocus (Control descendant, bool isUserInitiated) {
            OnDescendantReceivedFocus(descendant, isUserInitiated);
        }
    }

    public sealed class ControlGroup : ContainerBase, IEnumerable<Control> {
        new public ControlCollection Children {
            get => base.Children;
        }
        new public bool ClipChildren {
            get => base.ClipChildren;
            set => base.ClipChildren = value;
        }
        new public bool DisableSelfHitTests {
            get => base.DisableSelfHitTests;
            set => base.DisableSelfHitTests = true;
        }
        new public bool DisableChildHitTests {
            get => base.DisableChildHitTests;
            set => base.DisableChildHitTests = value;
        }

        public ControlGroup ()
            : this (false, false) {
        }

        public ControlGroup (bool forceBreak = false, bool preventCrush = false)
            : base () {
            Appearance.Undecorated = true;
            ForceBreak = forceBreak;
            PreventCrush = preventCrush;
            DisableSelfHitTests = true;
        }

        // For simple initializers
        public void Add (Control control) {
            Children.Add(control);
        }

        public IEnumerator<Control> GetEnumerator () {
            return ((IEnumerable<Control>)Children).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator () {
            return ((IEnumerable<Control>)Children).GetEnumerator();
        }
    }
}
