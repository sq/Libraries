using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Squared.PRGUI.Decorations;
using Squared.PRGUI.Layout;
using Squared.Render.Convenience;

namespace Squared.PRGUI.Controls {
    public class TabContainer : ControlParentBase {
        private readonly ControlGroup TabStrip;
        private readonly Dictionary<Control, string> Labels = new Dictionary<Control, string>();
        private int SelectedTabIndex = 0;

        public Control SelectedTab => 
            (SelectedTabIndex < (Children.Count - 1))
                ? Children[SelectedTabIndex + 1] 
                : null;

        public ControlCollection Tabs {
            get => base.Children;
        }

        public TabContainer () 
            : base () {
            Appearance.BackgroundColor = Color.Red;
            ClipChildren = true;
            TabStrip = new ControlGroup {
                LayoutFlags = ControlFlags.Layout_Fill_Row | ControlFlags.Layout_Anchor_Top,
                ContainerFlags = ControlFlags.Container_Align_Start | ControlFlags.Container_Row | ControlFlags.Container_No_Expansion | ControlFlags.Container_Prevent_Crush
            };
            Children.Add(TabStrip);
            TabStrip.Add(new Button { Text = "Tab 1" });
            TabStrip.Add(new Button { Text = "Tab 2" });
        }

        protected override void EnsureChildrenAreValid () {
        }

        protected override void OnDescendantReceivedFocus (Control control, bool isUserInitiated) {
            // FIXME
        }

        protected override IDecorator GetDefaultDecorator (IDecorationProvider provider) {
            return provider.Container;
        }

        protected override ControlKey OnGenerateLayoutTree (UIOperationContext context, ControlKey parent, ControlKey? existingKey) {
            var result = base.OnGenerateLayoutTree(context, parent, existingKey);
            if (result.IsInvalid) {
                foreach (var item in _Children)
                    item.InvalidateLayout();

                return result;
            }

            var containerFlags = ContainerFlags | ExtraContainerFlags;
            context.Layout.SetContainerFlags(result, containerFlags);

            var adoc = AbsoluteDisplayOffset; // AbsoluteDisplayOffsetOfChildren;
            for (int i = 0, c = _Children.Count; i < c; i++) {
                var item = _Children[i];
                item.ForceBreak = true;
                item.AbsoluteDisplayOffset = adoc;

                // If we're performing layout again on an existing layout item, attempt to do the same
                //  for our children
                var childExistingKey = (ControlKey?)null;
                if ((existingKey.HasValue) && !item.LayoutKey.IsInvalid)
                    childExistingKey = item.LayoutKey;

                // FIXME: Don't perform layout for invisible tabs?
                var itemKey = item.GenerateLayoutTree(ref context, result, childExistingKey);
            }

            return result;
        }

        protected override void ComputePadding (UIOperationContext context, IDecorator decorations, out Margins result) {
            base.ComputePadding(context, decorations, out result);
        }

        protected override void OnRasterize (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings, IDecorator decorations) {
            base.OnRasterize(context, ref renderer, settings, decorations);
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
    }
}
