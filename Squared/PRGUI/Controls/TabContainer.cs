using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Squared.PRGUI.Decorations;
using Squared.PRGUI.Layout;
using Squared.Render.Convenience;
using Squared.Util;

namespace Squared.PRGUI.Controls {
    public class TabContainer : ControlParentBase, IControlEventFilter, IEnumerable<Control> {
        private static int NextGroupId = 1;

        private readonly ControlGroup TabStrip;
        private readonly Dictionary<Control, string> Labels = new Dictionary<Control, string>();
        private int SelectedTabIndex = 0;
        private string GroupId;

        private bool SelectedTabIsInvalid = true;
        private bool TabStripIsInvalid = true;

        /// <summary>
        /// If set, layout will occur for all tabs simultaneously and the tab container will
        ///  expand to the maximum size needed to contain a given tab (instead of the size of
        ///  the current tab only).
        /// </summary>
        public bool ExpandToHoldAllTabs = true;

        private bool _TabsOnLeft;
        public bool TabsOnLeft {
            get => _TabsOnLeft;
            set {
                if (_TabsOnLeft == value)
                    return;
                _TabsOnLeft = value;
                InvalidateLayout();
            }
        }

        public Control SelectedTab {
            get =>
                (SelectedTabIndex < (Children.Count - 1))
                    ? Children[SelectedTabIndex + 1] 
                    : null;
            set {
                var idx = Children.IndexOf(value);
                if (idx <= 0)
                    SelectedTabIndex = 0;
                else
                    SelectedTabIndex = idx - 1;
            }
        }

        public int SelectedIndex {
            get => SelectedTabIndex;
            set {
                SelectedTabIndex = Arithmetic.Clamp(value, 0, Children.Count - 2);
            }
        }

        override protected int ChildrenToSkip => 1;

        new public ControlCollection Children {
            get => base.Children;
        }

        public TabContainer () 
            : base () {
            GroupId = $"TabContainer#{NextGroupId++}";
            ClipChildren = false;
            ContainerFlags = ControlFlags.Container_Align_Start | ControlFlags.Container_Row | ControlFlags.Container_Wrap;
            TabStrip = new ControlGroup();
            Children.Add(TabStrip);
            GenerateTabs();
        }

        public void Add (Control tab, string label = null) {
            Children.Add(tab);
            Labels[tab] = label;
        }

        public override void InvalidateLayout () {
            base.InvalidateLayout();
            SelectedTabIsInvalid = true;
            TabStripIsInvalid = true;
        }

        protected void GenerateTabs () {
            TabStrip.LayoutFlags =
                TabsOnLeft
                    ? ControlFlags.Layout_Fill_Column | ControlFlags.Layout_Anchor_Left
                    : ControlFlags.Layout_Fill_Row | ControlFlags.Layout_Anchor_Top;
            TabStrip.ContainerFlags =
                TabsOnLeft
                    ? ControlFlags.Container_Align_Start | ControlFlags.Container_Row | ControlFlags.Container_Prevent_Crush | ControlFlags.Container_Wrap
                    : ControlFlags.Container_Align_Start | ControlFlags.Container_Row | ControlFlags.Container_Prevent_Crush;

            TabStrip.Children.Clear();
            var children = Children;

            for (var i = 1; i < children.Count; i++) {
                var child = children[i];
                Labels.TryGetValue(child, out string label);
                var btn = new RadioButton {
                    GroupId = GroupId,
                    // HACK
                    Text = $"{label ?? (child as IHasDescription)?.Description ?? child.DebugLabel ?? child.ToString()}",
                    EventFilter = this,
                    Appearance = {
                        Decorator = Context?.Decorations?.Tab,
                        DecorationTraits = {
                            TabsOnLeft ? "left" : "top"
                        }
                    },
                    TextAlignment = Render.Text.HorizontalAlignment.Center,
                    AutoSizeIsMaximum = false,
                    LayoutFlags = TabsOnLeft
                        ? ControlFlags.Layout_Fill_Row | ControlFlags.Layout_Anchor_Top | ControlFlags.Layout_ForceBreak
                        : ControlFlags.Layout_Fill_Row | ControlFlags.Layout_Anchor_Top,
                };
                    
                if (i == SelectedTabIndex + 1)
                    btn.Checked = true;
                TabStrip.Add(btn);
            }
            TabStripIsInvalid = false;

            foreach (var t in TabStrip) {
            }
        }

        protected override void EnsureChildrenAreValid () {
        }

        protected override void OnDescendantReceivedFocus (Control control, bool isUserInitiated) {
            // FIXME
        }

        protected override IDecorator GetDefaultDecorator (IDecorationProvider provider) {
            return provider.None;
        }

        protected override void ComputeSizeConstraints (out float? minimumWidth, out float? minimumHeight, out float? maximumWidth, out float? maximumHeight) {
            base.ComputeSizeConstraints(out minimumWidth, out minimumHeight, out maximumWidth, out maximumHeight);
        }

        protected override ControlKey OnGenerateLayoutTree (UIOperationContext context, ControlKey parent, ControlKey? existingKey) {
            var children = Children;
            if (TabStrip.Children.Count != children.Count - 1)
                TabStripIsInvalid = true;

            if (TabStripIsInvalid)
                GenerateTabs();
            if (SelectedTabIsInvalid)
                UpdateSelectedTab();

            var st = SelectedTab;

            var result = base.OnGenerateLayoutTree(context, parent, existingKey);
            if (result.IsInvalid) {
                foreach (var item in children)
                    item.InvalidateLayout();

                return result;
            } else {
                foreach (var item in children) {
                    // item.Visible = (item == st) || (item == TabStrip);
                    item.Visible = true;
                }
            }

            var containerFlags = ContainerFlags | ExtraContainerFlags;
            context.Layout.SetContainerFlags(result, containerFlags);

            {
                var adoc = AbsoluteDisplayOffset; // AbsoluteDisplayOffsetOfChildren;
                // fixme: existing key
                LayoutItem(ref context, existingKey.HasValue, result, adoc, TabStrip);
                ControlKey childBox;
                if (existingKey.HasValue)
                    childBox = context.Layout.GetLastChild(result);
                else {
                    childBox = context.Layout.CreateItem();
                    context.Layout.Append(result, childBox);
                }
                context.Layout.SetContainerFlags(
                    childBox,
                    ControlFlags.Container_Align_Start
                );
                context.Layout.SetLayoutFlags(
                    childBox, TabsOnLeft
                        ? ControlFlags.Layout_Fill
                        : ControlFlags.Layout_Fill | ControlFlags.Layout_ForceBreak
                );
                foreach (var item in children) {
                    if (item == TabStrip)
                        continue;
                    if ((st != item) && !ExpandToHoldAllTabs)
                        continue;
                    item.LayoutFlags = ControlFlags.Layout_Fill; // | ControlFlags.Layout_Floating;
                    LayoutItem(ref context, existingKey.HasValue, childBox, adoc, item);
                }
            }

            return result;
        }

        private static void LayoutItem (ref UIOperationContext context, bool containerHasExistingKey, ControlKey container, Vector2 adoc, Control item) {
            item.AbsoluteDisplayOffset = adoc;

            // If we're performing layout again on an existing layout item, attempt to do the same
            //  for our children
            var childExistingKey = (ControlKey?)null;
            if (containerHasExistingKey && !item.LayoutKey.IsInvalid)
                childExistingKey = item.LayoutKey;

            // FIXME: Don't perform layout for invisible tabs?
            var itemKey = item.GenerateLayoutTree(ref context, container, childExistingKey);
            ;
        }

        protected override void ComputePadding (UIOperationContext context, IDecorator decorations, out Margins result) {
            base.ComputePadding(context, decorations, out result);
        }

        protected override void OnRasterize (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings, IDecorator decorations) {
            // HACK
            var tabPage = Appearance.Decorator ?? context.DecorationProvider?.TabPage;
            if (Appearance.Undecorated || (SelectedTab == null) || (tabPage == null))
                return;
            var stripRect = TabStrip.GetRect();
            var tabContentRect = SelectedTab.GetRect(contentRect: true);
            if (TabsOnLeft) {
                settings.Box.Left += stripRect.Width;
                settings.Box.Width -= stripRect.Width;
            } else {
                settings.Box.Top += stripRect.Height;
                settings.Box.Height -= stripRect.Height;
            }
            settings.ContentBox = tabContentRect;
            tabPage.Rasterize(context, ref renderer, settings);
        }

        protected override void OnRasterizeChildren (UIOperationContext context, ref RasterizePassSet passSet, DecorationSettings settings) {
            if (HideChildren)
                return;

            // FIXME
            /*
            int layer1 = passSet.Below.Layer,
                layer2 = passSet.Content.Layer,
                layer3 = passSet.Above.Layer,
                maxLayer1 = layer1,
                maxLayer2 = layer2,
                maxLayer3 = layer3;
            */

            TabStrip.Rasterize(ref context, ref passSet);
            SelectedTab?.Rasterize(ref context, ref passSet);
        }

        protected override bool OnHitTest (RectF box, Vector2 position, bool acceptsMouseInputOnly, bool acceptsFocusOnly, bool rejectIntangible, ref Control result) {
            if (!HitTestShell(box, position, false, false, rejectIntangible, ref result))
                return false;

            bool success = HitTestInterior(box, position, acceptsMouseInputOnly, acceptsFocusOnly, ref result);
            var tabResult = TabStrip.HitTest(position, acceptsMouseInputOnly, acceptsFocusOnly, rejectIntangible);
            if (tabResult != null) {
                success = true;
                result = tabResult;
            } else if (SelectedTab != null) {
                var childResult = SelectedTab.HitTest(position, acceptsMouseInputOnly, acceptsFocusOnly, rejectIntangible);
                if (childResult != null) {
                    success = true;
                    result = childResult;
                }
            }
            return success;
        }

        private void UpdateSelectedTab () {
            SelectedTabIsInvalid = false;
            foreach (var tab in TabStrip.Children) {
                var rb = (RadioButton)tab;
                if (rb.Checked) {
                    SelectedTabIndex = TabStrip.Children.IndexOf(rb);
                    return;
                }
            }
            SelectedTabIndex = 0;
        }

        bool IControlEventFilter.OnEvent (Control target, string name) {
            return false;
        }

        bool IControlEventFilter.OnEvent<T> (Control target, string name, T args) {
            if (name == UIEvents.RadioButtonSelected)
                SelectedTabIsInvalid = true;
            return false;
        }

        public IEnumerator<Control> GetEnumerator () {
            return ((IEnumerable<Control>)Children).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator () {
            return ((IEnumerable<Control>)Children).GetEnumerator();
        }
    }
}
