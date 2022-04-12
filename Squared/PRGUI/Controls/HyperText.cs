using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Squared.Game;
using Squared.PRGUI.Accessibility;
using Squared.PRGUI.Decorations;
using Squared.PRGUI.Layout;
using Squared.Render.Convenience;
using Squared.Render.Text;
using Squared.Util;
using Squared.Util.Text;

namespace Squared.PRGUI.Controls {
    public delegate AbstractTooltipContent GetTooltipForMarkedStringHandler (HyperText control, AbstractString text, string id);

    public class HyperText : StaticText, IControlContainer, IControlEventFilter {
        public Action<HyperText, HyperTextHotspot> OnHotSpotClicked;
        public GetTooltipForMarkedStringHandler GetTooltipForString;

        Control IControlContainer.DefaultFocusTarget => null;
        bool IControlContainer.ChildrenAcceptFocus => true;
        public IControlEventFilter ChildEventFilter { get; set; }

        public bool HotspotsAcceptFocus = true;
        public ControlAppearance HotspotAppearance = new ControlAppearance {
            SuppressDecorationMargins = true,
            SuppressDecorationScaling = true,
            AutoScaleMetrics = false
        };
        private ControlCollection _Children;
        protected ControlCollection Children {
            get {
                if (_Children == null)
                    _Children = new ControlCollection(this, Context);
                return _Children;
            }
        }

        public HyperText ()
            : base () {
            RichText = true;
        }

        // HACK: Due to decorators
        protected override bool CanApplyOpacityWithoutCompositing => false;

        protected override bool HasChildren => true;
        int IControlContainer.ChildrenToSkipWhenBuilding => 0;
        bool IControlContainer.ClipChildren {
            get => false;
            set { }
        }
        ControlFlags IControlContainer.ContainerFlags => ControlFlags.Container_Row | ControlFlags.Container_No_Expansion | ControlFlags.Container_Prevent_Crush;
        ControlCollection IControlContainer.Children => Children;

        void IControlContainer.DescendantReceivedFocus (Control descendant, bool isUserInitiated) {
        }

        bool IControlContainer.IsControlHidden (Control child) {
            return false;
        }

        protected override ControlKey OnGenerateLayoutTree (ref UIOperationContext context, ControlKey parent, ControlKey? existingKey) {
            var children = Children;

            if (!Content.IsValid) {
                // HACK: If our content has changed ensure no tooltips are currently attached to one of our children
                foreach (var ht in children)
                    Context.HideTooltip(ht);
            }

            var result = base.OnGenerateLayoutTree(ref context, parent, existingKey);
            var rm = Content.RichMarkers;

            // FIXME: On the first frame our hotspots will be in the wrong place
            if (!existingKey.HasValue) {
                if (rm.Count > 0) {
                    // HACK: This will be bad if for some reason the content is constantly being invalidated,
                    //  but that usually shouldn't happen.
                    if (!Content.IsValid)
                        Content.Get();

                    int numHotspots = rm.Count;
                    while (children.Count > numHotspots)
                        children.RemoveAt(children.Count - 1);
                    while (children.Count < numHotspots)
                        children.Add(new HyperTextHotspot { EventFilter = this });

                    var hsd = HotspotAppearance.Decorator ?? context.DecorationProvider.HyperTextHotspot;
                    var padding = hsd.Padding;

                    for (int i = 0; i < rm.Count; i++) {
                        var m = Content.RichMarkers[i];
                        var hs = (HyperTextHotspot)children[i];
                        hs.Rects = m.Bounds;
                        hs.ActualText = m.MarkedStringActualText;
                        hs.MarkedString = m.MarkedString;
                        hs.MarkedID = m.MarkedID;
                        if (m.Bounds.Count <= 0) {
                            hs.Visible = false;
                            hs.Enabled = false;
                        } else {
                            hs.Visible = true;
                            hs.Enabled = true;
                            hs.Appearance = HotspotAppearance;
                            var b = m.UnionBounds;
                            hs.Layout.FloatingPosition = b.TopLeft + _LastDrawOffset - new Vector2(padding.Left, padding.Top);
                            hs.RectBase = b.TopLeft;
                            hs.Width.Fixed = b.Size.X + padding.X;
                            hs.Height.Fixed = b.Size.Y + padding.Y;
                            hs.SetAcceptsFocus(HotspotsAcceptFocus);
                        }
                    }
                } else {
                    children.Clear();
                }
            }

            for (int i = 0, l = Math.Min(rm.Count, children.Count); i < l; i++) {
                var child = children[i];
                var childExistingKey = (ControlKey?)null;
                if ((existingKey.HasValue) && !child.LayoutKey.IsInvalid)
                    childExistingKey = child.LayoutKey;
                child.GenerateLayoutTree(ref context, result, childExistingKey);
            }

            return result;
        }

        protected override void OnRasterizeChildren (ref UIOperationContext context, ref RasterizePassSet passSet, DecorationSettings settings) {
            foreach (var child in Children)
                child.Rasterize(ref context, ref passSet, 1);
        }

        protected bool HitTestChildren (Vector2 position, bool acceptsMouseInputOnly, bool acceptsFocusOnly, bool rejectIntangible, ref Control result) {
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
            bool success = base.OnHitTest(box, position, acceptsMouseInputOnly, acceptsFocusOnly, rejectIntangible, ref result);
            success |= HitTestChildren(position, acceptsMouseInputOnly, acceptsFocusOnly, rejectIntangible, ref result);
            return success;
        }

        protected override void OnDisplayOffsetChanged () {
            var ado = AbsoluteDisplayOffset;
            var children = Children;
            foreach (var child in children)
                child.AbsoluteDisplayOffset = ado;
        }

        bool IControlEventFilter.OnEvent (Control target, string name) {
            return false;
        }

        bool IControlEventFilter.OnEvent<T> (Control target, string name, T args) {
            if (name == UIEvents.Click) {
                if (OnHotSpotClicked != null) {
                    OnHotSpotClicked(this, (HyperTextHotspot)target);
                    return true;
                }
            }
            return false;
        }
    }

    public class HyperTextHotspot : Control, ICustomTooltipTarget, IReadingTarget, IPartiallyIntangibleControl {
        public AbstractString MarkedString;
        public string MarkedID;
        public AbstractString ActualText;
        public Vector2 RectBase;
        public DenseList<Bounds> Rects;

        public HyperTextHotspot ()
            : base () {
            LayoutFlags = ControlFlags.Layout_Floating;
            AcceptsFocus = true;
            AcceptsMouseInput = true;
            AcceptsTextInput = false;
        }

        internal void SetAcceptsFocus (bool value) {
            AcceptsFocus = value;
        }
        
        Control ICustomTooltipTarget.Anchor => null;
        TooltipTargetSettings ICustomTooltipTarget.TooltipSettings { get; } =
            new TooltipTargetSettings {
                AppearDelay = 0f,
                ShowWhileMouseIsHeld = true,
                ShowWhileMouseIsNotHeld = true,
                ShowWhileFocused = true,
                ShowWhileKeyboardFocused = true,
                HideOnMousePress = false,
            };

        public HyperText Parent {
            get {
                TryGetParent(out Control parent);
                return (HyperText)parent;
            }
        }

        AbstractString IReadingTarget.Text {
            get {
                var result = new StringBuilder();
                result.AppendLine(MarkedString.ToString());
                var ictt = (this as ICustomTooltipTarget);
                var content = ictt.GetContent();
                if (content != default(AbstractTooltipContent)) {
                    var ttt = (content.GetText != null) 
                        ? content.GetText(this)
                        : content.Text;
                    if (ttt != default(AbstractString))
                        result.Append(ttt.ToString());
                }
                return RichText.ToPlainText(result);
            }
        }

        AbstractTooltipContent ICustomTooltipTarget.GetContent () {
            if (Parent.GetTooltipForString != null)
                return Parent.GetTooltipForString(Parent, MarkedString, MarkedID);
            else
                return default(AbstractTooltipContent);
        }

        protected override IDecorator GetDefaultDecorator (IDecorationProvider provider) {
            return provider.HyperTextHotspot;
        }

        protected override bool OnEvent<T> (string name, T args) {
            if (name == UIEvents.Click)
                Context.FireEvent(UIEvents.HotspotClick, Parent, this);

            return base.OnEvent(name, args);
        }

        void IReadingTarget.FormatValueInto (StringBuilder sb) {
            sb.Append(MarkedString.ToString());
        }

        protected override bool OnHitTest (RectF box, Vector2 position, bool acceptsMouseInputOnly, bool acceptsFocusOnly, bool rejectIntangible, ref Control result) {
            var ipic = (IPartiallyIntangibleControl)this;
            if (ipic.IsIntangibleAtPosition(position))
                return false;
            return base.OnHitTest(box, position, acceptsMouseInputOnly, acceptsFocusOnly, rejectIntangible, ref result);
        }

        protected override void OnRasterize (ref UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings, IDecorator decorations) {
            var outerBox = settings.ContentBox;
            foreach (var b in Rects) {
                var r = new RectF(b.TopLeft - RectBase + outerBox.Position, b.Size);
                settings.ContentBox = r;
                r.Position = r.Position - new Vector2(decorations.Margins.Left, decorations.Margins.Top);
                r.Size = r.Size + decorations.Margins.Size;
                settings.Box = r;
                decorations?.Rasterize(ref context, ref renderer, settings);
            }
        }

        bool IPartiallyIntangibleControl.IsIntangibleAtPosition (Vector2 position) {
            var cb = this.GetRect(contentRect: true);
            foreach (var b in Rects) {
                // HACK: Expand the bounds of the hotspots because gamepad snap doesn't work if we don't
                var r = new RectF(b.TopLeft - RectBase + cb.Position - (Vector2.One * 2f), b.Size + (Vector2.One * 4f));
                if (r.Contains(position))
                    return false;
            }
            return true;
        }

        public override string ToString () {
            return $"HotSpot '{MarkedID}' '{ActualText.ToString() ?? MarkedString}'";
        }
    }
}
