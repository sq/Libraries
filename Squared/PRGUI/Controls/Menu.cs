using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Squared.PRGUI.Decorations;
using Squared.PRGUI.Layout;
using Squared.Render.Convenience;
using Squared.Threading;
using Squared.Util;
using Squared.Util.Text;

namespace Squared.PRGUI.Controls {
    public interface IMenuListener {
        void Shown (Menu menu);
        void Closed (Menu menu);
        void ItemSelected (Menu menu, Control item);
        void ItemChosen (Menu menu, Control item);
    }

    public class Menu : Container, ICustomTooltipTarget, Accessibility.IReadingTarget, Accessibility.IAcceleratorSource {
        // Yuck
        public const int PageSize = 8;

        public const float AutoscrollMarginSize = 24f;

        public const float MenuShowSpeed = 0.1f;
        public const float MenuHideSpeed = 0.25f;

        Vector2 MousePositionWhenShown;
        bool? MouseInsideWhenShown;

        private Future<Control> NextResultFuture = null;

        float? ICustomTooltipTarget.TooltipDisappearDelay => null;
        float? ICustomTooltipTarget.TooltipAppearanceDelay => TooltipContent.Equals(SelectedItemTooltip) && 
            (SelectedItem != null) &&
            !SelectedItem.TooltipContent.Equals(default(AbstractTooltipContent))
                ? 0f
                : (float?)null;
        bool ICustomTooltipTarget.ShowTooltipWhileMouseIsHeld => true;
        bool ICustomTooltipTarget.ShowTooltipWhileMouseIsNotHeld => true;
        bool ICustomTooltipTarget.ShowTooltipWhileKeyboardFocus => true;
        bool ICustomTooltipTarget.HideTooltipOnMousePress => false;

        public string Description;

        private Control _SelectedItem;

        public bool DeselectOnMouseLeave = true;

        public int SelectedIndex => (_SelectedItem != null)
            ? Children.IndexOf(_SelectedItem)
            : -1;

        public Control SelectedItem {
            get {
                return _SelectedItem;
            }
            set {
                if (value?.Enabled == false)
                    value = null;
                if (_SelectedItem == value)
                    return;
                var oldSelection = _SelectedItem;
                _SelectedItem = value;
                OnSelectionChange(oldSelection, value);
            }
        }

        public Vector2 Position {
            get {
                return new Vector2(Margins.Left, Margins.Top);
            }
            set {
                Margins.Left = value.X;
                Margins.Top = value.Y;
            }
        }

        public bool IsActive { get; private set; }

        public static readonly AbstractTooltipContent SelectedItemTooltip = new AbstractTooltipContent(
            (Control ctl) => {
                var m = ctl as Menu;
                if (m == null)
                    return default(AbstractString);

                AbstractString result = default(AbstractString);
                if (m._SelectedItem != null)
                    result = m._SelectedItem.TooltipContent.Get(m._SelectedItem);
                if (result == default(AbstractString))
                    result = m.TooltipContent.Get(m);
                return result;
            }
        );

        new public AbstractTooltipContent TooltipContent = default(AbstractTooltipContent);

        protected override bool FreezeDynamicContent => Visible;

        public Menu ()
            : base () {
            Visible = false;
            AcceptsMouseInput = true;
            AcceptsFocus = true;
            ContainerFlags = ControlFlags.Container_Row | ControlFlags.Container_Wrap | ControlFlags.Container_Align_Start;
            LayoutFlags = ControlFlags.Layout_Floating;
            PaintOrder = 9900;
            ClipChildren = true;
            ShowHorizontalScrollbar = false;
            base.TooltipContent = SelectedItemTooltip;
        }

        protected override IDecorator GetDefaultDecorator (IDecorationProvider provider) {
            return provider?.Menu;
        }

        protected override ControlKey OnGenerateLayoutTree (UIOperationContext context, ControlKey parent, ControlKey? existingKey) {
            var result = base.OnGenerateLayoutTree(context, parent, existingKey);
            foreach (var child in Children) {
                var lk = child.LayoutKey;
                var cf = context.Layout.GetLayoutFlags(lk);
                context.Layout.SetLayoutFlags(lk, cf | ControlFlags.Layout_ForceBreak);
                var m = context.Layout.GetMargins(lk);
                m.Top = m.Bottom = 0;
                context.Layout.SetMargins(lk, m);
            }
            return result;
        }

        private IMenuListener Listener => FocusDonor as IMenuListener;

        // HACK
        private bool _OverrideHitTestResults = true;

        protected override bool OnHitTest (LayoutContext context, RectF box, Vector2 position, bool acceptsMouseInputOnly, bool acceptsFocusOnly, ref Control result) {
            var ok = base.OnHitTest(context, box, position, acceptsMouseInputOnly, acceptsFocusOnly, ref result);
            // HACK: Ensure that hit-test does not pass through to our individual items. We want to handle all events for them
            if (ok && _OverrideHitTestResults && (result?.AcceptsMouseInput == false))
                result = this;
            return ok;
        }

        private void OnSelectionChange (Control previous, Control newControl) {
            // FIXME: Optimize this for large lists
            foreach (var child in Children) {
                child.CustomTextDecorator = ((child == newControl) && (child.BackgroundColor.pLinear == null))
                    ? Context?.Decorations.Selection 
                    : null;
            }

            Listener?.ItemSelected(this, newControl);
            FireEvent(UIEvents.SelectionChanged, newControl);
        }

        private Control LocateContainingChild (Control control) {
            var current = control;
            while (current != null) {
                if (!control.TryGetParent(out Control parent))
                    return null;
                if (parent == this)
                    return current;
                else
                    current = parent;
            }

            return null;
        }

        private Control ChildFromGlobalPosition (LayoutContext context, Vector2 globalPosition) {
            try {
                var rect = this.GetRect(context, contentRect: true);
                if (!rect.Contains(globalPosition))
                    return null;

                globalPosition.X = rect.Left + 6;
                _OverrideHitTestResults = false;

                var child = HitTest(context, globalPosition, false, false);
                if ((child ?? this) == this) {
                    globalPosition.X = rect.Center.X;
                    child = HitTest(context, globalPosition, false, false);
                }

                if (child == this)
                    return null;
                else
                    return LocateContainingChild(child);
            } finally {
                _OverrideHitTestResults = true;
            }
        }

        private bool OnMouseEvent (string name, MouseEventArgs args) {
            if (ProcessMouseEventForScrollbar(name, args)) {
                Context.RetainCapture(this);
                return true;
            }

            // Console.WriteLine($"menu.{name}");

            var item = ChildFromGlobalPosition(Context.Layout, args.RelativeGlobalPosition);

            if (name == UIEvents.MouseDown) {
                if (!args.Box.Contains(args.RelativeGlobalPosition))
                    item = null;
                // HACK: Clear the flag that causes us to ignore the next mouseup if the mouse hasn't mvoed
                MouseInsideWhenShown = false;

                if ((item != this) && !Children.Contains(item)) {
                    Context.ReleaseCapture(this, FocusDonor);
                    Close();
                    return true;
                }
            }

            if ((Context.MouseOver != this) && (Context.MouseCaptured != this)) {
                if (DeselectOnMouseLeave)
                    SelectedItem = null;
            } else {
                if (item != null)
                    SelectedItem = item;
            }

            if (name == UIEvents.MouseUp) {
                var mouseHasMovedSinceOpening = (args.GlobalPosition - MousePositionWhenShown).Length() > Context.MinimumMouseMovementDistance;
                if (!args.ContentBox.Contains(args.RelativeGlobalPosition)) {
                    // This indicates that the mouse is in our padding zone
                } else if ((MouseInsideWhenShown == true) && !mouseHasMovedSinceOpening) {
                    // The mouse was inside our rect when we first opened, and hasn't moved
                } else if (item != null) {
                    return ChooseItem(item);
                } else
                    Close();
            }

            return true;
        }

        protected override bool OnEvent<T> (string name, T args) {
            if (!IsActive)
                return false;

            if (name == UIEvents.MouseLeave) {
                if (DeselectOnMouseLeave)
                    SelectedItem = null;
            } else if (args is MouseEventArgs)
                return OnMouseEvent(name, (MouseEventArgs)(object)args);
            else if (name == UIEvents.LostFocus)
                Close();
            else if (args is KeyEventArgs)
                return OnKeyEvent(name, (KeyEventArgs)(object)args);
            else
                return base.OnEvent(name, args);

            return false;
        }

        private void SelectItemViaKeyboard (Control item) {
            SelectedItem = item;
            // HACK: Tell the context that the current item is the keyboard selection,
            //  so that autoscroll and tooltips will happen for it.
            Context.OverrideKeyboardSelection(item);
        }

        public bool AdjustSelection (int direction) {
            if (Children.Count == 0)
                return false;

            var selectedIndex = Children.IndexOf(_SelectedItem);
            if (selectedIndex < 0)
                selectedIndex = direction > 0 ? 0 : Children.Count - 1;
            else
                selectedIndex += direction;

            int steps = Children.Count;
            while (steps-- > 0) {
                selectedIndex = Arithmetic.Wrap(selectedIndex, 0, Children.Count - 1);
                var item = Children[selectedIndex];
                if (item.Enabled) {
                    SelectItemViaKeyboard(item);
                    return true;
                } else
                    selectedIndex += direction; 
            }

            return false;
        }

        private bool OnKeyEvent (string name, KeyEventArgs args) {
            if (args.Key != null && UIContext.ModifierKeys.Contains(args.Key.Value))
                return false;
            if (name != UIEvents.KeyPress)
                return true;

            switch (args.Key) {
                case Keys.Escape:
                    Close();
                    return true;
                case Keys.Space:
                case Keys.Enter:
                    if (SelectedItem != null)
                        return ChooseItem(SelectedItem);
                    return true;
                case Keys.Home:
                    SelectItemViaKeyboard(Children.FirstOrDefault());
                    return true;
                case Keys.End:
                    SelectItemViaKeyboard(Children.LastOrDefault());
                    return true;
                case Keys.PageUp:
                case Keys.PageDown:
                    AdjustSelection(args.Key == Keys.PageUp ? -PageSize : PageSize);
                    return true;
                case Keys.Up:
                case Keys.Down:
                    AdjustSelection(args.Key == Keys.Up ? -1 : 1);
                    return true;
                default:
                    return false;
            }
        }

        protected override void OnLayoutComplete (UIOperationContext context, ref bool relayoutRequested) {
            base.OnLayoutComplete(context, ref relayoutRequested);

            if (!MouseInsideWhenShown.HasValue)
                MouseInsideWhenShown = GetRect(context.Layout).Contains(MousePositionWhenShown);

            // Remove ourselves from the top-level context if we've just finished fading out.
            // While this isn't strictly necessary, it's worth doing for many reasons
            var opacity = GetOpacity(context.NowL);
            if ((opacity <= 0) && !IsActive)
                context.UIContext.Controls.Remove(this);
        }

        protected override void OnRasterizeChildren (UIOperationContext context, ref RasterizePassSet passSet, DecorationSettings settings) {
            if (SelectedItem != null) {
                var selectionBox = SelectedItem.GetRect(context.Layout, true, false);
                selectionBox.Left = settings.ContentBox.Left;
                selectionBox.Width = settings.ContentBox.Width;

                // HACK: Selection boxes are normally rasterized on the content layer, but we want to rasterize
                //  the selection on the Below layer beneath items' decorations and content.
                var oldPass = context.Pass;
                context.Pass = RasterizePasses.Content;
                var selectionSettings = new DecorationSettings {
                    Box = selectionBox,
                    ContentBox = selectionBox,
                    State = ControlStates.Hovering | ControlStates.Focused
                };
                context.DecorationProvider.MenuSelection?.Rasterize(context, ref passSet.Below, selectionSettings);
                context.Pass = oldPass;

                passSet.Below.Layer += 1;
            }

            base.OnRasterizeChildren(context, ref passSet, settings);
        }

        private int lastOffset1 = -1,
            lastOffset2 = -1;

        protected override void RasterizeChildrenInOrder (
            ref UIOperationContext context, ref RasterizePassSet passSet, 
            int layer1, int layer2, int layer3, 
            ref int maxLayer1, ref int maxLayer2, ref int maxLayer3
        ) {
            RasterizeChildrenFromCenter(
                ref context, ref passSet, 
                GetRect(context.Layout), Children, _SelectedItem,
                layer1, layer2, layer3, 
                ref maxLayer1, ref maxLayer2, ref maxLayer3,
                ref lastOffset1, ref lastOffset2
            );
        }

        private bool ChooseItem (Control item) {
            if (!item.Enabled)
                return false;

            Listener?.ItemChosen(this, item);
            Context.FireEvent(UIEvents.ItemChosen, this, item);
            var args = Context.MakeMouseEventArgs(item, Context.LastMousePosition, null);
            args.SequentialClickCount = 1;
            Context.FireEvent(UIEvents.Click, item, args);
            NextResultFuture?.SetResult2(item, null);
            Close();
            return true;
        }

        private void ShowInternalPrologue (UIContext context) {
            // Ensure we have run a generate pass for our dynamic content before doing anything
            GenerateDynamicContent(true);

            // If we have existing layout data (like we are in the middle of fading out),
            //  we do not want to re-use it for calculations, it might be wrong
            InvalidateLayout();

            if (!IsActive)
                Opacity = Tween<float>.StartNow(0, 1, MenuShowSpeed, now: context.NowL);
            IsActive = true;

            AcceptsFocus = true;
            if (!context.Controls.Contains(this))
                context.Controls.Add(this);

            MousePositionWhenShown = context.LastMousePosition;
            MouseInsideWhenShown = null;
            MaximumWidth = context.CanvasSize.X * 0.5f;
            MaximumHeight = context.CanvasSize.Y * 0.66f;
            CalculateScrollable(context);
        }

        private void CalculateScrollable (UIContext context) {
            context.UpdateSubtreeLayout(this);
            if (GetContentBounds(context, out RectF contentBounds)) {
                Scrollable = contentBounds.Height >= MaximumHeight;
                // HACK: Changing the scrollable flag invalidates our layout info, so recalculate it
                // If we don't do this the menu will overhang on the right side
                if (Scrollable)
                    context.UpdateSubtreeLayout(this);
            }
        }

        private Future<Control> ShowInternal (UIContext context, Vector2 adjustedPosition, Control selectedItem) {
            if (NextResultFuture?.Completed == false)
                NextResultFuture?.SetResult2(null, null);

            // HACK: Prevent the layout info from computing our size from being used to render us next frame
            LayoutKey = ControlKey.Invalid;
            SelectedItem = selectedItem;
            Position = adjustedPosition;
            Visible = true;
            Intangible = false;
            context.CaptureMouse(this, out _FocusDonor);
            Listener?.Shown(this);
            FireEvent(UIEvents.Shown);
            NextResultFuture = new Future<Control>();
            // FIXME: This doesn't work the first time the menu is shown
            if (selectedItem != null)
                context.PerformAutoscroll(selectedItem, 99999);
            return NextResultFuture;
        }

        private Vector2 AdjustPosition (UIContext context, Vector2 desiredPosition) {
            var margin = context.Decorations.Menu.Margins;
            var box = GetRect(context.Layout);
            // HACK: We'd want to use margin.Right/Bottom here normally, but compensation has already
            //  been applied somewhere in the layout engine for the top/left margins so we need to
            //  cancel them out again
            var maxX = context.CanvasSize.X - box.Width - margin.X;
            var maxY = context.CanvasSize.Y - box.Height - margin.Y;
            var result = new Vector2(
                Arithmetic.Clamp(desiredPosition.X, margin.Left, maxX),
                Arithmetic.Clamp(desiredPosition.Y, margin.Top, maxY)
            );
            return result;
        }

        public Future<Control> Show (UIContext context, Vector2? position = null, Control selectedItem = null) {
            ShowInternalPrologue(context);

            // Align the top-left corner of the menu with the target position (compensating for margin),
            //  then shift the menu around if necessary to keep it on screen
            var adjustedPosition = AdjustPosition(context, (position ?? context.LastMousePosition));

            return ShowInternal(context, adjustedPosition, selectedItem);
        }

        public Future<Control> Show (UIContext context, RectF anchorBox, Control selectedItem = null) {
            var adjustedPosition = AdjustPosition(
                context, new Vector2(anchorBox.Left, anchorBox.Top + anchorBox.Height)
            );

            return ShowInternal(context, adjustedPosition, selectedItem);
        }

        public Future<Control> Show (UIContext context, Control anchor, Control selectedItem = null) {
            ShowInternalPrologue(context);

            var anchorBox = anchor.GetRect(context.Layout);
            return Show(context, anchorBox, selectedItem);
        }

        public void Close () {
            if (!IsActive)
                return;
            IsActive = false;
            Intangible = true;
            Context.ReleaseCapture(this, FocusDonor);
            var now = Context.NowL;
            Opacity = Tween<float>.StartNow(Opacity.Get(now), 0, MenuHideSpeed, now: now);
            Listener?.Closed(this);
            FireEvent(UIEvents.Closed);
            if (NextResultFuture?.Completed == false)
                NextResultFuture?.SetResult2(null, null);
            AcceptsFocus = false;
        }

        StringBuilder TextBuilder = new StringBuilder();

        AbstractString Accessibility.IReadingTarget.Text {
            get {
                TextBuilder.Clear();
                if (Description != null)
                    TextBuilder.Append(Description);
                else {
                    var ttc = TooltipContent.Get(this).ToString();
                    if (ttc != null)
                        TextBuilder.Append(ttc);
                    else {
                        var donorIrt = FocusDonor as Accessibility.IReadingTarget;
                        if (donorIrt != null)
                            TextBuilder.Append($"Menu {donorIrt.Text}");
                    }
                }

                if (SelectedItem != null) {
                    var irt = SelectedItem as Accessibility.IReadingTarget;
                    if (irt != null) {
                        TextBuilder.Append(": ");
                        var existingLength = TextBuilder.Length;
                        irt.FormatValueInto(TextBuilder);
                        if (TextBuilder.Length == existingLength)
                            TextBuilder.Append(irt.Text.ToString());
                    } else {
                        // FIXME: Fallback to something else here?
                    }
                }

                return TextBuilder;
            }
        }

        void Accessibility.IReadingTarget.FormatValueInto (StringBuilder sb) {
            if (SelectedItem == null)
                return;

            var irt = SelectedItem as Accessibility.IReadingTarget;
            if (irt != null) {
                irt.FormatValueInto(sb);
                if (sb.Length == 0)
                    sb.Append(irt.Text);
            } else {
                sb.Append(SelectedItem.ToString());
            }
        }

        public int Count => Children.Count;

        IEnumerable<KeyValuePair<Control, string>> Accessibility.IAcceleratorSource.Accelerators {
            get {
                if (SelectedIndex > 0)
                    yield return new KeyValuePair<Control, string>(this[SelectedIndex - 1], "Up");
                if (SelectedIndex < Children.Count)
                    yield return new KeyValuePair<Control, string>(this[SelectedIndex + 1], "Down");
            }
        }

        public Control this [int index] {
            get => Children[index];
            set => Children[index] = value;
        }
        public void RemoveAt (int index) => Children.RemoveAt(index);
        public void Clear () => Children.Clear();
        public void Add (Control child) => Children.Add(child);
    }
}
