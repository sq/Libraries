using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Squared.PRGUI.Decorations;
using Squared.PRGUI.Layout;
using Squared.Render.Convenience;
using Squared.Util;

namespace Squared.PRGUI.Controls {
    public class TabContainer : ControlParentBase, IControlEventFilter, IEnumerable<Control>, IControlContainer {
        private static int NextGroupId = 1;

        private readonly ControlGroup TabStrip;
        private readonly Dictionary<Control, string> Labels = new Dictionary<Control, string>();
        private int SelectedTabIndex = 0;
        private string GroupId;

        private bool IdentifySelectedTabFromUIState = true;
        private bool TabStripIsInvalid = true;

        /// <summary>
        /// If set, layout will occur for all tabs simultaneously and the tab container will
        ///  expand to the maximum size needed to contain a given tab (instead of the size of
        ///  the current tab only).
        /// </summary>
        public bool ExpandToHoldAllTabs = true;

        public bool ExpandTabsX = true, ExpandTabsY = true;

        float _TabScale = 1;
        public float TabScale {
            get => _TabScale;
            set {
                value = Arithmetic.Clamp(value, 0.1f, 2.0f);
                _TabScale = value;
                TabStripIsInvalid = true;
            }
        }

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
                var children = Children;
                var c = children.Count - 2;
                SelectedTabIndex = Arithmetic.Clamp(value, 0, c);
                if ((TabStrip == null) || (TabStrip.Children.Count != children.Count - 1))
                    return;

                for (var i = 0; i <= c; i++) {
                    var btn = TabStrip.Children[i] as RadioButton;
                    btn.Checked = i == SelectedTabIndex;
                }
            }
        }

        override protected int ChildrenToSkipWhenBuilding => 1;

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
            IdentifySelectedTabFromUIState = true;
            TabStripIsInvalid = true;
        }

        bool IControlContainer.IsControlHidden (Control child) => (child != SelectedTab) && (child != TabStrip); 

        protected void GenerateTabs () {
            TabStripIsInvalid = false;

            TabStrip.Layout.Fill.Column = TabsOnLeft;
            TabStrip.Layout.Fill.Row = !TabsOnLeft;
            TabStrip.Layout.Anchor.Left = TabsOnLeft;
            TabStrip.Layout.Anchor.Top = !TabsOnLeft;
            TabStrip.LayoutFlags =
                TabsOnLeft
                    ? ControlFlags.Layout_Fill_Column | ControlFlags.Layout_Anchor_Left
                    : ControlFlags.Layout_Fill_Row | ControlFlags.Layout_Anchor_Top;
            TabStrip.ContainerFlags =
                ControlFlags.Container_Align_Start | ControlFlags.Container_Row;
            TabStrip.Container.Wrap = TabsOnLeft;

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
                    AutoSizeWidth = TabsOnLeft,
                    AutoSizeIsMaximum = false,
                    ScaleToFit = true,
                    LayoutFlags = TabsOnLeft
                        ? ControlFlags.Layout_Fill_Row | ControlFlags.Layout_Anchor_Top | ControlFlags.Layout_ForceBreak
                        : ControlFlags.Layout_Fill_Row | ControlFlags.Layout_Anchor_Top,
                    Scale = _TabScale
                };
                    
                if (i == SelectedTabIndex + 1)
                    btn.Checked = true;
                TabStrip.Add(btn);
            }
        }

        protected override void EnsureChildrenAreValid () {
            if (TabStripIsInvalid)
                GenerateTabs();
        }

        protected override void OnDescendantReceivedFocus (Control control, bool isUserInitiated) {
            // FIXME
        }

        protected override IDecorator GetDefaultDecorator (IDecorationProvider provider) {
            return provider.None;
        }

        protected override ControlKey OnGenerateLayoutTree (ref UIOperationContext context, ControlKey parent, ControlKey? existingKey) {
            var children = Children;
            if (TabStrip.Children.Count != children.Count - 1)
                TabStripIsInvalid = true;

            if (TabStripIsInvalid)
                GenerateTabs();
            if (IdentifySelectedTabFromUIState)
                UpdateSelectedTab();

            var st = SelectedTab;

            var result = base.OnGenerateLayoutTree(ref context, parent, existingKey);
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
                    ControlFlags.Container_Align_Start | ControlFlags.Container_Column
                );
                context.Layout.SetLayoutFlags(
                    childBox, TabsOnLeft
                        ? ControlFlags.Layout_Fill
                        : ControlFlags.Layout_Fill | ControlFlags.Layout_ForceBreak
                );
                var childLayoutFlags = default(ControlFlags) |
                    (ExpandTabsX ? ControlFlags.Layout_Fill_Row : default(ControlFlags)) |
                    (ExpandTabsY ? ControlFlags.Layout_Fill_Column : default(ControlFlags)) | 
                    ControlFlags.Layout_Stacked;
                foreach (var item in children) {
                    if (item == TabStrip)
                        continue;
                    if ((st != item) && !ExpandToHoldAllTabs)
                        continue;
                    item.LayoutFlags = childLayoutFlags; // | ControlFlags.Layout_Floating;
                    LayoutItem(ref context, existingKey.HasValue, childBox, adoc, item);
                }
            }

            return result;
        }

        private void LayoutItem (ref UIOperationContext context, bool containerHasExistingKey, ControlKey container, Vector2 adoc, Control item) {
            item.AbsoluteDisplayOffset = adoc;

            // If we're performing layout again on an existing layout item, attempt to do the same
            //  for our children
            var childExistingKey = (ControlKey?)null;
            if (containerHasExistingKey && !item.LayoutKey.IsInvalid)
                childExistingKey = item.LayoutKey;

            // FIXME: Don't perform layout for invisible tabs?
            if (item != SelectedTab)
                context.HiddenCount++;
            item.GenerateLayoutTree(ref context, container, childExistingKey);
            if (item != SelectedTab)
                context.HiddenCount--;
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
            IdentifySelectedTabFromUIState = false;
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
                IdentifySelectedTabFromUIState = true;
            else if (name == UIEvents.KeyPress)
                return OnTabKeyPress((KeyEventArgs)(object)args);

            return false;
        }

        private bool FocusCurrentTab () {
            var tab = SelectedTab;
            if (tab == null)
                return false;

            return Context.TrySetFocus(tab);
        }

        private bool RotateFocusedTab (int direction) {
            var index = TabStrip.Children.IndexOf(Context.Focused);
            // FIXME: This should be impossible
            if (index < 0)
                return false;
            var newIndex = Arithmetic.Wrap(index + direction, 0, Children.Count - 2);
            if (index == newIndex)
                return false;
            var newControl = TabStrip.Children[newIndex];
            return Context.TrySetFocus(newControl);
        }

        private bool OnTabKeyPress (KeyEventArgs args) {
            if (TabsOnLeft) {
                switch (args.Key) {
                    case Keys.Up:
                    case Keys.Down:
                        return RotateFocusedTab(args.Key == Keys.Up ? -1 : 1);
                    case Keys.Right:
                        return FocusCurrentTab();
                }
            } else {
                switch (args.Key) {
                    case Keys.Left:
                    case Keys.Right:
                        return RotateFocusedTab(args.Key == Keys.Left ? -1 : 1);
                    case Keys.Down:
                        return FocusCurrentTab();
                }
            }

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
