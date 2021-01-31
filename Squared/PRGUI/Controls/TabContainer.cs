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

namespace Squared.PRGUI.Controls {
    public class TabContainer : ControlParentBase, IControlEventFilter, IEnumerable<Control> {
        private static int NextGroupId = 1;

        private readonly ControlGroup TabStrip;
        private readonly Dictionary<Control, string> Labels = new Dictionary<Control, string>();
        private bool SelectedTabIsInvalid = true;
        private int SelectedTabIndex = 0;
        private string GroupId;

        public Control SelectedTab => 
            (SelectedTabIndex < (Children.Count - 1))
                ? Children[SelectedTabIndex + 1] 
                : null;

        new public ControlCollection Children {
            get => base.Children;
        }

        public TabContainer () 
            : base () {
            GroupId = $"TabContainer#{NextGroupId++}";
            ClipChildren = false;
            ContainerFlags = ControlFlags.Container_Align_Start | ControlFlags.Container_Row | ControlFlags.Container_Wrap;
            TabStrip = new ControlGroup {
                LayoutFlags = ControlFlags.Layout_Fill_Row | ControlFlags.Layout_Anchor_Top,
                ContainerFlags = ControlFlags.Container_Align_Start | ControlFlags.Container_Row | ControlFlags.Container_No_Expansion | ControlFlags.Container_Prevent_Crush
            };
            Children.Add(TabStrip);
            GenerateTabs();
        }

        public void Add (Control tab, string label = null) {
            Children.Add(tab);
            Labels[tab] = label;
        }

        protected void GenerateTabs () {
            TabStrip.Children.Clear();
            for (var i = 1; i < _Children.Count; i++) {
                var child = _Children[i];
                var btn = new RadioButton {
                    GroupId = GroupId,
                    Text = $"{Labels[child] ?? child.ToString()}",
                    EventFilter = this,
                    Appearance = {
                        Decorator = Context?.Decorations?.Tab
                    }
                };
                if (i == SelectedTabIndex + 1)
                    btn.Checked = true;
                TabStrip.Add(btn);
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

        protected override ControlKey OnGenerateLayoutTree (UIOperationContext context, ControlKey parent, ControlKey? existingKey) {
            if (TabStrip.Children.Count != _Children.Count - 1)
                GenerateTabs();
            if (SelectedTabIsInvalid)
                UpdateSelectedTab();

            var st = SelectedTab;

            var result = base.OnGenerateLayoutTree(context, parent, existingKey);
            if (result.IsInvalid) {
                foreach (var item in _Children)
                    item.InvalidateLayout();

                return result;
            } else {
                foreach (var item in _Children) {
                    item.Visible = (item == st) || (item == TabStrip);
                }
            }

            var containerFlags = ContainerFlags | ExtraContainerFlags;
            context.Layout.SetContainerFlags(result, containerFlags);

            {
                var adoc = AbsoluteDisplayOffset; // AbsoluteDisplayOffsetOfChildren;
                LayoutItem(ref context, existingKey, result, adoc, TabStrip);
                LayoutItem(ref context, existingKey, result, adoc, st);
            }

            return result;
        }

        private static void LayoutItem (ref UIOperationContext context, ControlKey? existingKey, ControlKey self, Vector2 adoc, Control item) {
            item.LayoutFlags = ControlFlags.Layout_Anchor_Top | ControlFlags.Layout_Fill_Row |
                ControlFlags.Layout_ForceBreak;
            item.AbsoluteDisplayOffset = adoc;

            // If we're performing layout again on an existing layout item, attempt to do the same
            //  for our children
            var childExistingKey = (ControlKey?)null;
            if ((existingKey.HasValue) && !item.LayoutKey.IsInvalid)
                childExistingKey = item.LayoutKey;

            // FIXME: Don't perform layout for invisible tabs?
            var itemKey = item.GenerateLayoutTree(ref context, self, childExistingKey);
        }

        protected override void ComputePadding (UIOperationContext context, IDecorator decorations, out Margins result) {
            base.ComputePadding(context, decorations, out result);
        }

        protected override void OnRasterize (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings, IDecorator decorations) {
            var tabPage = context.DecorationProvider?.TabPage;
            if (SelectedTab == null)
                return;
            var stripRect = TabStrip.GetRect();
            var tabContentRect = SelectedTab.GetRect(contentRect: true);
            settings.Box.Top += stripRect.Height;
            settings.Box.Height -= stripRect.Height;
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
