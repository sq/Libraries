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

    public class Menu : Container, ICustomTooltipTarget, Accessibility.IReadingTarget, Accessibility.IAcceleratorSource, 
        IModal, ISelectionBearer, IPartiallyIntangibleControl, 
        IFuzzyHitTestTarget, IHasDescription
    {
        public event Action<IModal> Shown, Closed;

        public float ItemSpacing = 1;

        // Yuck
        public const int PageSize = 8;

        public const float AutoscrollMarginSize = 24f;

        Vector2 MousePositionWhenShown;
        bool? MouseInsideWhenShown;

        private Future<Control> NextResultFuture = null;

        AbstractTooltipContent ICustomTooltipTarget.GetContent () => SelectedItemTooltip;
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

        public string Description { get; set; }

        private Control _SelectedItem;

        public bool DeselectOnMouseLeave = true;
        public bool CloseWhenFocusLost = true;
        public bool CloseWhenItemChosen = true;
        public bool CloseOnClickOutside = true;

        public override int ColumnCount {
            get => base.ColumnCount;
            set {
                if (value != 1)
                    throw new ArgumentOutOfRangeException("ColumnCount");
            }
        }

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

        protected Control _FocusDonor;
        public Control FocusDonor => _FocusDonor;

        public Menu ()
            : base () {
            Appearance.Opacity = 0f;
            // HACK: If we don't do this, alignment will be broken when a global scale is set
            Appearance.AutoScaleMetrics = false;
            Visible = false;
            AcceptsMouseInput = true;
            AcceptsFocus = true;
            ContainerFlags = ControlFlags.Container_Column | ControlFlags.Container_Align_Start;
            LayoutFlags = ControlFlags.Layout_Floating;
            DisplayOrder = 9900;
            ClipChildren = true;
            ShowHorizontalScrollbar = false;
        }

        protected override IDecorator GetDefaultDecorator (IDecorationProvider provider) {
            return provider?.Menu;
        }

        protected override ControlKey OnGenerateLayoutTree (ref UIOperationContext context, ControlKey parent, ControlKey? existingKey) {
            FreezeDynamicContent = Visible;

            var result = base.OnGenerateLayoutTree(ref context, parent, existingKey);
            if (result.IsInvalid)
                return result;

            var hasPushedDecorator = false;
            foreach (var child in Children) {
                var lk = child.LayoutKey;
                SetTextDecorator(ref context, child, ref hasPushedDecorator);
                var m = context.Layout.GetMargins(lk);
                // HACK: Override decorator margins
                m.Top = child.Margins.Top;
                m.Bottom = child.Margins.Bottom + ItemSpacing;
                context.Layout.SetMargins(lk, m);
            }

            if (hasPushedDecorator)
                UIOperationContext.PopTextDecorator(ref context);

            return result;
        }

        private void SetTextDecorator (ref UIOperationContext context, Control child, ref bool hasPushed) {
            var isSelected = SelectedItem == child;
            if (hasPushed) {
                UIOperationContext.PopTextDecorator(ref context);
                hasPushed = false;
            }
            if ((isSelected) && !child.Appearance.HasBackgroundColor) {
                UIOperationContext.PushTextDecorator(ref context, Context?.Decorations.Selection);
                hasPushed = true;
            }
        }

        private IMenuListener Listener => FocusDonor as IMenuListener;

        // HACK
        private bool _OverrideHitTestResults = true;

        protected override bool OnHitTest (RectF box, Vector2 position, bool acceptsMouseInputOnly, bool acceptsFocusOnly, bool rejectIntangible, ref Control result) {
            var ok = base.OnHitTest(box, position, acceptsMouseInputOnly, acceptsFocusOnly, rejectIntangible, ref result);
            // HACK: Ensure that hit-test does not pass through to our individual items. We want to handle all events for them
            if (ok && _OverrideHitTestResults && (result?.AcceptsMouseInput == false))
                result = this;
            return ok;
        }

        private void OnSelectionChange (Control previous, Control newControl) {
            Listener?.ItemSelected(this, newControl);
            FireEvent(UIEvents.SelectionChanged, newControl);
        }

        private Control LocateContainingChild (Control control) {
            var current = control;
            while (current != null) {
                if (!current.TryGetParent(out Control parent))
                    return null;

                if ((parent == this) || (parent == current))
                    return current;
                else
                    current = parent;
            }

            return null;
        }

        private Control ChildFromGlobalPosition (Vector2 globalPosition) {
            try {
                var rect = this.GetRect(contentRect: true);
                if (!rect.Contains(globalPosition))
                    return null;

                // TODO: Do this the way ListBox does
                globalPosition.X = rect.Left + 6;
                _OverrideHitTestResults = false;

                var child = HitTest(globalPosition, false, false);
                if ((child ?? this) == this) {
                    globalPosition.X = rect.Center.X;
                    child = HitTest(globalPosition, false, false);
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
                // Normally an event like mouseup would release our capture and close the menu,
                //  but since the user was interacting with the scrollbar we want to retain
                //  capture so that mouse movement events will still update the selected item
                Context.RetainCapture(this);
                return true;
            }

            // Console.WriteLine($"menu.{name}");

            var item = ChildFromGlobalPosition(args.RelativeGlobalPosition);

            if (name == UIEvents.MouseDown) {
                if (!args.Box.Contains(args.RelativeGlobalPosition))
                    item = null;
                // HACK: Clear the flag that causes us to ignore the next mouseup if the mouse hasn't mvoed
                MouseInsideWhenShown = false;

                if ((item != this) && !Children.Contains(item)) {
                    if (CloseOnClickOutside) {
                        Context.ReleaseCapture(this, FocusDonor);
                        Close();
                    }
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
                } else if (CloseOnClickOutside) {
                    Close();
                }
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
            else if (name == UIEvents.LostFocus) {
                if (CloseWhenFocusLost)
                    Close();
            } else if (args is KeyEventArgs)
                return OnKeyEvent(name, (KeyEventArgs)(object)args);
            else
                return base.OnEvent(name, args);

            return false;
        }

        private void SelectItemViaKeyboard (Control item) {
            SelectedItem = item;
            // HACK: Tell the context that the current item is the keyboard selection,
            //  so that autoscroll and tooltips will happen for it.
            Context.OverrideKeyboardSelection(item, true);
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

            // Remove ourselves from the top-level context if we've just finished fading out.
            // While this isn't strictly necessary, it's worth doing for many reasons
            var opacity = GetOpacity(context.NowL);
            if ((opacity <= 0) && !IsActive)
                context.UIContext.Controls.Remove(this);
        }

        protected override void OnRasterizeChildren (UIOperationContext context, ref RasterizePassSet passSet, DecorationSettings settings) {
            // HACK
            if (!MouseInsideWhenShown.HasValue)
                MouseInsideWhenShown = GetRect().Contains(MousePositionWhenShown);

            if (SelectedItem != null) {
                var selectionBox = SelectedItem.GetRect();
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

        protected override bool RasterizeChild (
            ref UIOperationContext context, Control item, ref RasterizePassSet passSet
        ) {
            bool temp = false;
            SetTextDecorator(ref context, item, ref temp);
            var result = base.RasterizeChild(
                ref context, item, ref passSet
            );
            if (temp)
                UIOperationContext.PopTextDecorator(ref context);
            return result;
        }

        protected override void RasterizeChildrenInOrder (
            ref UIOperationContext context, ref RasterizePassSet passSet
        ) {
            RasterizeChildrenFromCenter(
                ref context, ref passSet, 
                GetRect(), _SelectedItem,
                ref lastOffset1, ref lastOffset2
            );
        }

        private bool ChooseItem (Control item) {
            if (!item.Enabled)
                return false;

            Listener?.ItemChosen(this, item);
            FireEvent(UIEvents.ItemChosen, item);
            var args = Context.MakeMouseEventArgs(item, Context.LastInputState.CursorPosition, null);
            args.SequentialClickCount = 1;
            Context.FireEvent(UIEvents.Click, item, args);
            if (CloseWhenItemChosen) {
                SetResult(item);
                Close();
            } else {
                Context.RetainCapture(this);
            }
            return true;
        }

        /// <summary>
        /// If CloseWhenItemChosen is not set it is your responsibility to invoke this to set a result
        /// </summary>
        public void SetResult (Control item) {
            NextResultFuture?.SetResult2(item, null);
        }

        private void ShowInternalPrologue (UIContext context) {
            SetContext(context);
            // Ensure we have run a generate pass for our dynamic content before doing anything
            GenerateDynamicContent(true);

            // If we have existing layout data (like we are in the middle of fading out),
            //  we do not want to re-use it for calculations, it might be wrong
            InvalidateLayout();

            if (!IsActive) {
                var fadeIn = context.Animations?.ShowMenu;
                if (fadeIn != null)
                    StartAnimation(fadeIn);
                else
                    Appearance.Opacity = 1f;
            }
            IsActive = true;

            AcceptsFocus = true;
            DisplayOrder = context.Controls.PickNewHighestDisplayOrder(this);
            if (!context.Controls.Contains(this))
                context.Controls.Add(this);

            MousePositionWhenShown = context.LastInputState.CursorPosition;
            MouseInsideWhenShown = null;
            Width.Maximum = context.CanvasSize.X * 0.5f;
            Height.Maximum = context.CanvasSize.Y * 0.66f;
            CalculateScrollable(context);
        }

        private void CalculateScrollable (UIContext context) {
            context.UpdateSubtreeLayout(this);
            if (GetContentBounds(context, out RectF contentBounds)) {
                Scrollable = contentBounds.Height >= Height.Maximum;
                // HACK: Changing the scrollable flag invalidates our layout info, so recalculate it
                // If we don't do this the menu will overhang on the right side
                if (Scrollable)
                    context.UpdateSubtreeLayout(this);
            }
        }

        private Future<Control> ShowInternalEpilogue (UIContext context, Vector2 adjustedPosition, Control selectedItem) {
            if (NextResultFuture?.Completed == false)
                NextResultFuture?.SetResult2(null, null);
            NextResultFuture = new Future<Control>();

            // HACK: Prevent the layout info from computing our size from being used to render us next frame
            InvalidateLayout();
            SelectedItem = null;
            SelectedItem = selectedItem;
            Position = adjustedPosition;
            Visible = true;
            Intangible = false;
            context.CaptureMouse(this, out _FocusDonor);
            context.NotifyModalShown(this);
            Listener?.Shown(this);
            if (Shown != null)
                Shown(this);
            SelectItemViaKeyboard(selectedItem);
            // FIXME: This doesn't work the first time the menu is shown
            if (selectedItem != null)
                context.PerformAutoscroll(selectedItem, 99999);
            return NextResultFuture;
        }

        private Vector2 AdjustPosition (UIContext context, Vector2 desiredPosition, Vector2 localAlignment) {
            var decorator = GetDecorator(context.Decorations, null);

            // HACK city: Need an operation context to compute margins
            var tempContext = context.MakeOperationContext();
            // Also our position is stored in margins top/left so we gotta clear that
            var m = Margins;
            Margins = default(Margins);
            // Now we can get the actual margins of our control for layout purposes
            ComputeMargins(tempContext, decorator, out Margins computedMargins);
            // Put back the original margins (even though we'll be changing them potentially)
            Margins = m;
            // Shift ourself up/left to compensate for our decoration margins and align perfectly
            //  with any anchor point
            desiredPosition -= new Vector2(computedMargins.Left, computedMargins.Top);
            // Move ourselves into place and perform layout to figure out how big we are, etc
            // FIXME: localAlignment
            Position = desiredPosition;
            context.UpdateSubtreeLayout(this);
            var box = GetRect(context: context);
            var localAlignmentOffset = localAlignment * box.Size;
            // HACK: We'd want to use margin.Right/Bottom here normally, but compensation has already
            //  been applied somewhere in the layout engine for the top/left margins so we need to
            //  cancel them out again
            var maxX = context.CanvasSize.X - box.Width - computedMargins.X;
            var maxY = context.CanvasSize.Y - box.Height - computedMargins.Y;
            var result = new Vector2(
                Arithmetic.Clamp(desiredPosition.X, computedMargins.Left, maxX),
                Arithmetic.Clamp(desiredPosition.Y, computedMargins.Top, maxY)
            );
            return result;
        }

        bool IModal.FadeBackground => false;

        void IModal.Show (UIContext context) {
            Show(context, (Vector2?)null);
        }

        public Future<Control> Show (UIContext context, Vector2? position = null, Control selectedItem = null) {
            ShowInternalPrologue(context);

            // Align the top-left corner of the menu with the target position (compensating for margin),
            //  then shift the menu around if necessary to keep it on screen
            var adjustedPosition = AdjustPosition(context, (position ?? context.LastInputState.CursorPosition), Vector2.Zero);

            return ShowInternalEpilogue(context, adjustedPosition, selectedItem);
        }

        public Future<Control> Show (UIContext context, RectF anchorBox, Vector2? anchorAlignment = null, Vector2? localAlignment = null, Control selectedItem = null) {
            ShowInternalPrologue(context);
            var idealPosition = anchorBox.Position + (anchorBox.Size * (anchorAlignment ?? new Vector2(0, 1)));
            var adjustedPosition = AdjustPosition(context, idealPosition, localAlignment ?? Vector2.Zero);

            return ShowInternalEpilogue(context, adjustedPosition, selectedItem);
        }

        public Future<Control> Show (UIContext context, Control anchor, Vector2? anchorAlignment = null, Vector2? localAlignment = null, Control selectedItem = null) {
            ShowInternalPrologue(context);

            var anchorBox = anchor.GetRect(context: context);
            return Show(context, anchorBox, anchorAlignment, localAlignment, selectedItem);
        }

        public void Close () {
            if (!IsActive)
                return;
            IsActive = false;
            Intangible = true;
            StartAnimation(Context.Animations?.HideMenu);
            Listener?.Closed(this);
            if (Closed != null)
                Closed(this);
            Context.NotifyModalClosed(this);
            if (NextResultFuture?.Completed == false)
                NextResultFuture?.SetResult2(null, null);
            AcceptsFocus = false;
            _FocusDonor = null;
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

                if (SelectedItem is Accessibility.IReadingTarget irt) {
                    TextBuilder.Append(": ");
                    var existingLength = TextBuilder.Length;
                    irt.FormatValueInto(TextBuilder);
                    if (TextBuilder.Length == existingLength)
                        TextBuilder.Append(irt.Text.ToString());
                } else {
                    // FIXME: Fallback to something else here?
                }

                return TextBuilder;
            }
        }

        void Accessibility.IReadingTarget.FormatValueInto (StringBuilder sb) {
            if (SelectedItem == null)
                return;

            if (SelectedItem is Accessibility.IReadingTarget irt) {
                irt.FormatValueInto(sb);
                if (sb.Length == 0)
                    sb.Append(irt.Text);
            } else {
                sb.Append(SelectedItem.ToString());
            }
        }

        public int Count => Children.Count;

        IEnumerable<Accessibility.AcceleratorInfo> Accessibility.IAcceleratorSource.Accelerators {
            get {
                if (SelectedIndex > 0)
                    yield return new Accessibility.AcceleratorInfo(this[SelectedIndex - 1], "Up");
                if (SelectedIndex < (Children.Count - 1))
                    yield return new Accessibility.AcceleratorInfo(this[SelectedIndex + 1], "Down");
            }
        }

        bool ISelectionBearer.HasSelection => _SelectedItem != null;
        // FIXME: We should expand the width here
        RectF? ISelectionBearer.SelectionRect => SelectedItem?.GetRect();
        Control ISelectionBearer.SelectedControl => SelectedItem;

        public Control this [int index] {
            get => Children[index];
            set => Children[index] = value;
        }
        public void RemoveAt (int index) => Children.RemoveAt(index);
        public void Clear () => Children.Clear();
        public void Add (Control child) => Children.Add(child);

        bool IModal.BlockHitTests => false;
        bool IModal.BlockInput => false;
        bool IModal.RetainFocus => false;
        bool IModal.OnUnhandledEvent (string name, Util.Event.IEventInfo args) => false;
        bool IModal.OnUnhandledKeyEvent (string name, KeyEventArgs args) => false;

        bool IPartiallyIntangibleControl.IsIntangibleAtPosition (Vector2 position) => false;

        int IFuzzyHitTestTarget.WalkTree (
            List<FuzzyHitTest.Result> output, ref FuzzyHitTest.Result thisControl, Vector2 position, Func<Control, bool> predicate, float maxDistanceSquared
        ) => 0;
        bool IFuzzyHitTestTarget.WalkChildren => false;
    }
}
