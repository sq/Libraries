using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Squared.Game;
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

    public class Menu : Container, ICustomTooltipTarget, 
        Accessibility.IReadingTarget, Accessibility.IAcceleratorSource, 
        IModal, ISelectionBearer, IPartiallyIntangibleControl, 
        IFuzzyHitTestTarget, IHasDescription, IAlignedControl
    {
        protected ControlAlignmentHelper<Menu> Aligner;

        public event Action<IModal> Shown;
        public event Action<IModal, ModalCloseReason> Closed;

        public float ItemSpacing = 1;

        // Yuck
        public const int PageSize = 8;

        public const float AutoscrollMarginSize = 24f;

        Vector2 MousePositionWhenShown;
        bool? MouseInsideWhenShown;

        private Future<Control> NextResultFuture = null;

        private Control TooltipTarget => _SelectedItem ?? _HoveringItem;

        protected TooltipTargetSettings TooltipSettings = new TooltipTargetSettings {
            ShowWhileFocused = false,
            ShowWhileMouseIsHeld = false,
            ShowWhileMouseIsNotHeld = false,
            ShowWhileKeyboardFocused = false,
            HideOnMousePress = false,
            HostsChildTooltips = true,
        };

        TooltipTargetSettings ICustomTooltipTarget.TooltipSettings {
            get {
                TooltipSettings.AppearDelay = TooltipContent.Equals(default(AbstractTooltipContent)) &&
                    (TooltipTarget != null) &&
                    !TooltipTarget.TooltipContent.Equals(default(AbstractTooltipContent))
                        ? 0f
                        : (float?)null;
                return TooltipSettings;
            }
        }

        AbstractTooltipContent ICustomTooltipTarget.GetContent () => new AbstractTooltipContent(
            GetTooltipForSelectedItem, settings: GetTooltipSettingsForSelectedItem()
        );

        // FIXME: Attach to the menu item?
        Control ICustomTooltipTarget.Anchor => null;

        public string Description { get; set; }

        private Control _SelectedItem, _HoveringItem, _PendingSnap;

        public bool SnapMouseToNewSelection = false;
        public bool AllowProgrammaticClose { get; set; } = true;
        public bool DeselectOnMouseLeave { get; set; } = true;
        public bool CloseWhenFocusLost { get; set; } = true;
        public bool CloseWhenItemChosen { get; set; } = true;
        public bool CloseOnEscapePress { get; set; } = true;
        public bool CloseOnClickOutside { get; set; } = true;
        public bool BlockInput { get; set; } = true;
        public bool BlockHitTests { get; set; } = false;
        public bool RetainFocus { get; set; } = true;
        public float BackgroundFadeLevel { get; set; } = 0f;

        bool IModal.CanClose (ModalCloseReason reason) {
            switch (reason) {
                case ModalCloseReason.UserCancelled:
                case ModalCloseReason.UserConfirmed:
                    return true;
                case ModalCloseReason.Other:
                    return AllowProgrammaticClose;
                default:
                    return true;
            }
        }

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

        public Control HoveringItem => _HoveringItem;

        public Control SelectedItem {
            get => _SelectedItem;
            set => SetSelectedItem(value, true);
        }

        private void SetSelectedItem (Control value, bool fireEvents, bool snap = false) {
            if (value?.Enabled == false)
                value = null;
            if (_SelectedItem == value)
                return;
            var oldSelection = _SelectedItem;
            _SelectedItem = value;
            OnSelectionChange(oldSelection, value);
            if (snap && SnapMouseToNewSelection)
                _PendingSnap = value;
            else
                _PendingSnap = null;
        }

        public Vector2 Position {
            get => base.Layout.FloatingPosition;
            set {
                Aligner.SetPosition(value, true);
            }
        }

        public bool IsActive { get; private set; }

        private static Func<Control, AbstractString> GetTooltipForSelectedItem = _GetTooltipForSelectedItem;

        protected Control _FocusDonor;
        public Control FocusDonor => _FocusDonor;

        private static AbstractString _GetTooltipForSelectedItem (Control control) {
            var m = control as Menu;
            if (m == null)
                return default(AbstractString);

            AbstractString result = default(AbstractString);
            var item = m.TooltipTarget;
            if (item != null)
                result = item.TooltipContent.Get(item);
            if (result == default(AbstractString))
                result = m.TooltipContent.Get(m);
            return result;
        }

        private TooltipSettings GetTooltipSettingsForSelectedItem () {
            if (TooltipContent != default(AbstractTooltipContent))
                return TooltipContent.Settings;

            return TooltipTarget?.TooltipContent.Settings ?? default(TooltipSettings);
        }

        public Menu ()
            : base () {
            Aligner = new ControlAlignmentHelper<Menu>(this) {
                UpdatePosition = UpdatePosition
            };
            Appearance.Opacity = 0f;
            // HACK: If we don't do this, alignment will be broken when a global scale is set
            Appearance.AutoScaleMetrics = false;
            Visible = false;
            Intangible = true;
            AcceptsMouseInput = true;
            AcceptsFocus = true;
            ContainerFlags = ControlFlags.Container_Column | ControlFlags.Container_Align_Start | ControlFlags.Container_Constrain_Growth;
            LayoutFlags = ControlFlags.Layout_Floating;
            DisplayOrder = 9900;
            ClipChildren = true;
            Scrollable = true;
            ShowHorizontalScrollbar = false;
            ShowVerticalScrollbar = null;
            AutoBreakColumnItems = true;
        }

        protected override IDecorator GetDefaultDecorator (IDecorationProvider provider) {
            return provider?.Menu;
        }

        protected override void ComputeSizeConstraints (ref UIOperationContext context, ref ControlDimension width, ref ControlDimension height, Vector2 sizeScale) {
            base.ComputeSizeConstraints(ref context, ref width, ref height, sizeScale);
            width.Maximum = context.UIContext.CanvasSize.X * 0.5f;
            height.Maximum = context.UIContext.CanvasSize.Y * 0.66f;
        }

        protected override ControlKey OnGenerateLayoutTree (ref UIOperationContext context, ControlKey parent, ControlKey? existingKey) {
            FreezeDynamicContent = Visible;

            if (SelectedItem?.Enabled == false)
                SetSelectedItem(null, true);

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

        protected override bool OnHitTest (RectF box, Vector2 position, ref HitTestState state) {
            var ok = base.OnHitTest(box, position, ref state);
            // HACK: Ensure that hit-test does not pass through to our individual items. We want to handle all events for them
            if (ok && _OverrideHitTestResults && (state.Result?.AcceptsMouseInput == false))
                state.Result = this;
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

        private Control ChildFromGlobalPosition (Vector2 globalPosition, in HitTestOptions options) {
            try {
                var rect = this.GetRect(contentRect: true);
                if (!rect.Contains(globalPosition))
                    return null;

                // TODO: Do this the way ListBox does
                globalPosition.X = rect.Left + 6;
                _OverrideHitTestResults = false;

                var child = HitTest(globalPosition, options);
                if ((child ?? this) == this) {
                    globalPosition.X = rect.Center.X;
                    child = HitTest(globalPosition, options);
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
                // if (((IModal)this).BlockInput)
                    Context.RetainCapture(this);
                return true;
            }

            // Console.WriteLine($"menu.{name}");

            var fireSelectEvent = true;

            var options = new HitTestOptions();
            var item = ChildFromGlobalPosition(args.RelativeGlobalPosition, options);
            if (_HoveringItem != item) {
                var previous = _HoveringItem;
                _HoveringItem = item;
                fireSelectEvent = false;
                OnSelectionChange(previous, item);
            }

            if (name == UIEvents.MouseDown) {
                if (!args.Box.Contains(args.RelativeGlobalPosition))
                    item = null;
                // HACK: Clear the flag that causes us to ignore the next mouseup if the mouse hasn't mvoed
                MouseInsideWhenShown = false;

                if ((item != this) && !Children.Contains(item)) {
                    if (CloseOnClickOutside)
                        ClickedOutside(args);
                    return true;
                }
            }

            if ((Context.MouseOver != this) && (Context.MouseCaptured != this)) {
                if (DeselectOnMouseLeave)
                    SetSelectedItem(null, fireSelectEvent, false);
            } else {
                if (item != null)
                    SetSelectedItem(item, fireSelectEvent, false);
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
                    ClickedOutside(args);
                }
            }

            return true;
        }

        protected override bool OnEvent<T> (string name, T args) {
            if (!IsActive)
                return false;

            if (name == UIEvents.MouseLeave) {
                if (DeselectOnMouseLeave)
                    SetSelectedItem(null, true);
            } else if (args is MouseEventArgs ma)
                return OnMouseEvent(name, ma);
            else if (name == UIEvents.LostFocus) {
                if (CloseWhenFocusLost)
                    Close(ModalCloseReason.Dismissed);
            } else if (args is KeyEventArgs ka)
                return OnKeyEvent(name, ka);
            else
                return base.OnEvent(name, args);

            return false;
        }

        internal void ClickedOutside (MouseEventArgs args) {
            Context.ReleaseCapture(this, FocusDonor, !args.IsSynthetic);
            Close(ModalCloseReason.Dismissed);
        }

        private void SelectItemViaKeyboard (Control item) {
            SetSelectedItem(item, true, snap: true);
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
                    if (CloseOnEscapePress)
                        Close(ModalCloseReason.UserCancelled);
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

        protected override void OnLayoutComplete (ref UIOperationContext context, ref bool relayoutRequested) {
            base.OnLayoutComplete(ref context, ref relayoutRequested);

            Aligner.EnsureAligned(ref context, ref relayoutRequested);

            // Remove ourselves from the top-level context if we've just finished fading out.
            // While this isn't strictly necessary, it's worth doing for many reasons
            var opacity = GetOpacity(context.NowL);
            if ((opacity <= 0) && !IsActive)
                context.UIContext.Controls.Remove(this);

            if (relayoutRequested)
                return;

            // HACK: Once we've finished two layouts...
            if (_PendingSnap != null) {
                // Perform a pending snap-cursor-to-selection
                Context.TryMoveCursor(_PendingSnap.GetRect().Center);
                _PendingSnap = null;
            }

            // Enable tooltips
            TooltipSettings.ShowWhileMouseIsHeld = true;
            TooltipSettings.ShowWhileMouseIsNotHeld = true;
            TooltipSettings.ShowWhileKeyboardFocused = true;
        }

        protected override void OnRasterize (ref UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings, IDecorator decorations) {
            // FIXME: Also generate corner traits if we are aligned to a position/box instead of a control
            Aligner?.AddDecorationTraits(ref settings);
            base.OnRasterize(ref context, ref renderer, settings, decorations);
        }

        protected override void OnRasterizeChildren (ref UIOperationContext context, ref RasterizePassSet passSet, DecorationSettings settings) {
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
                    UniqueId = SelectedItem.ControlIndex
                };
                if (settings.HasStateFlag(ControlStates.ContainsFocus))
                    selectionSettings.State = ControlStates.Hovering | ControlStates.Focused;
                else
                    selectionSettings.State = ControlStates.Hovering;

                var selectionDecorator = context.DecorationProvider.MenuSelection;
                RasterizeSelectionDecorator(
                    ref context, ref passSet, ref selectionSettings, selectionDecorator
                );
                context.Pass = oldPass;

                passSet.Below.Layer += 1;
            }

            base.OnRasterizeChildren(ref context, ref passSet, settings);
        }

        internal static void RasterizeSelectionDecorator (
            ref UIOperationContext context, ref RasterizePassSet passSet, 
            ref DecorationSettings selectionSettings, IDecorator decorator
        ) {
            if (decorator == null)
                return;

            if (!decorator.IsPassDisabled(RasterizePasses.Below)) {
                context.Pass = RasterizePasses.Below;
                decorator.Rasterize(ref context, ref passSet.Below, ref selectionSettings);
            }

            if (!decorator.IsPassDisabled(RasterizePasses.Content)) {
                context.Pass = RasterizePasses.Content;
                decorator.Rasterize(ref context, ref passSet.Content, ref selectionSettings);
            }

            if (decorator.IsPassDisabled(RasterizePasses.Above))
                return;

            context.Pass = RasterizePasses.Above;
            decorator.Rasterize(ref context, ref passSet.Above, ref selectionSettings);
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
                Close(ModalCloseReason.UserConfirmed);
            } else if (((IModal)this).BlockInput) {
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
            Context = context;
            // Ensure we have run a generate pass for our dynamic content before doing anything
            GenerateDynamicContent(true);

            // HACK: Prevent single-frame tooltip glitch when the menu first opens
            TooltipSettings.ShowWhileMouseIsHeld = false;
            TooltipSettings.ShowWhileMouseIsNotHeld = false;
            TooltipSettings.ShowWhileKeyboardFocused = false;

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
            DisplayOrder = context.Controls.PickNewHighestDisplayOrder(this, true);
            if (!context.Controls.Contains(this))
                context.Controls.Add(this);

            MousePositionWhenShown = context.LastInputState.CursorPosition;
            MouseInsideWhenShown = null;
            CalculateScrollable(context);
        }

        private void CalculateScrollable (UIContext context) {
            context.UpdateSubtreeLayout(this);
            if (GetContentBounds(context, out Vector2 contentBounds)) {
                Scrollable = (contentBounds.Y >= Height.Maximum) || (Height.Maximum == null);
                // HACK: Changing the scrollable flag invalidates our layout info, so recalculate it
                // If we don't do this the menu will overhang on the right side
                if (Scrollable)
                    context.UpdateSubtreeLayout(this);
            }
        }

        private Future<Control> ShowInternalEpilogue (UIContext context, Control selectedItem) {
            if (NextResultFuture?.Completed == false)
                NextResultFuture?.SetResult2(null, null);
            NextResultFuture = new Future<Control>();

            // HACK: Prevent the layout info from computing our size from being used to render us next frame
            InvalidateLayout();
            SetSelectedItem(null, false);
            if (selectedItem != null)
                SetSelectedItem(selectedItem, true, snap: true);
            Visible = true;
            Intangible = false;
            if (((IModal)this).BlockInput)
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

        private Vector2 AdjustPosition_Classic (UIContext context, Vector2 desiredPosition) {
            var decorationProvider = context.Decorations;
            var decorator = GetDecorator(decorationProvider, null);

            // HACK city: Need an operation context to compute margins
            context.MakeOperationContext(out var tempContext);
            ComputeEffectiveSpacing(ref tempContext, decorationProvider, decorator, out Margins computedPadding, out Margins computedMargins);
            // Shift ourself up/left to compensate for our decoration margins and align perfectly
            //  with any anchor point
            desiredPosition -= new Vector2(computedMargins.Left, computedMargins.Top);
            // Move ourselves into place and perform layout to figure out how big we are, etc
            Position = desiredPosition;
            context.UpdateSubtreeLayout(this);
            var box = GetRect(context: context);
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

        private bool UpdatePosition (in Vector2 _newPosition, in RectF parentRect, in RectF box, bool updateDesiredPosition) {
            var effectiveSize = box.Size + Margins.Size;
            var availableSpaceX = Math.Max(0, parentRect.Width - effectiveSize.X);
            var availableSpaceY = Math.Max(0, parentRect.Height - effectiveSize.Y);
            var newPosition = new Vector2(
                Arithmetic.Saturate(_newPosition.X, availableSpaceX),
                Arithmetic.Saturate(_newPosition.Y, availableSpaceY)
            ).Floor();

            var changed = Position != newPosition;

            // context.Log($"Menu position {Position} -> {newPosition}");
            Aligner.SetPosition(newPosition, updateDesiredPosition);

            return changed;
        }

        void IModal.Show (UIContext context) {
            Show(context, (Vector2?)null);
        }

        public Future<Control> Show (UIContext context, Vector2? position = null, Control selectedItem = null) {
            ShowInternalPrologue(context);

            // Align the top-left corner of the menu with the target position (compensating for margin),
            //  then shift the menu around if necessary to keep it on screen
            var adjustedPosition = AdjustPosition_Classic(context, (position ?? context.LastInputState.CursorPosition));
            Position = adjustedPosition;
            Aligner.Enabled = false;

            return ShowInternalEpilogue(context, selectedItem);
        }

        public Future<Control> Show (UIContext context, RectF anchorBox, Vector2? anchorAlignment = null, Control selectedItem = null) {
            ShowInternalPrologue(context);
            var idealPosition = anchorBox.Position + (anchorBox.Size * (anchorAlignment ?? new Vector2(0, 1)));
            var adjustedPosition = AdjustPosition_Classic(context, idealPosition);
            Position = adjustedPosition;
            Aligner.Enabled = false;

            return ShowInternalEpilogue(context, selectedItem);
        }

        /// <param name="anchorPoint">Configures what point on the anchor [0 - 1] is used as the center for alignment</param>
        /// <param name="controlAlignmentPoint">Configures what point on the control [0 - 1] is aligned onto the anchor point</param>
        public Future<Control> Show (
            UIContext context, Control anchor, 
            Vector2? anchorPoint = null, 
            Vector2? controlAlignmentPoint = null, 
            Control selectedItem = null
        ) {
            Context = anchor?.Context ?? context;
            ShowInternalPrologue(context);

            _FocusDonor = anchor;
            Aligner.Enabled = true;
            Aligner.Anchor = anchor;
            Aligner.AnchorPoint = anchorPoint ?? new Vector2(0, 1);
            Aligner.ControlAlignmentPoint = controlAlignmentPoint ?? new Vector2(0, 0);
            return ShowInternalEpilogue(context, selectedItem);
        }

        public bool Close (ModalCloseReason reason) {
            if (!IsActive)
                return false;
            if (!AllowProgrammaticClose && (reason == ModalCloseReason.Other))
                return false;
            // HACK: We likely were the tooltip target, so hide it
            // FIXME: The context should really do this automatically
            Context.HideTooltip();
            Aligner.Enabled = false;
            IsActive = false;
            Intangible = true;
            StartAnimation(Context.Animations?.HideMenu);
            var fd = _FocusDonor;
            AcceptsFocus = false;
            Context.NotifyControlBecomingInvalidFocusTarget(this, false);
            if (Context.Focused == this) {
                if (fd != null)
                    Context.SetOrQueueFocus(fd, false, false); // FIXME
            }
            Listener?.Closed(this);
            if (Closed != null)
                Closed(this, reason);
            Context.NotifyModalClosed(this);
            if (NextResultFuture?.Completed == false)
                NextResultFuture?.SetResult2(null, null);
            _FocusDonor = null;
            return true;
        }

        StringBuilder TextBuilder = new StringBuilder();

        AbstractString Accessibility.IReadingTarget.Text {
            get {
                TextBuilder.Clear();
                if (Description != null)
                    TextBuilder.Append(Description);
                else {
                    var ttc = TooltipContent.GetPlainText(this).ToString();
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
        new public void Add (Control child) => Children.Add(child);
        public void Add (string text) => Children.Add(new StaticText { Text = text });

        bool IModal.BlockInput => CloseOnClickOutside && BlockInput;
        bool IModal.RetainFocus => !CloseOnClickOutside && !CloseWhenFocusLost && RetainFocus;
        bool IModal.OnUnhandledEvent (string name, Util.Event.IEventInfo args) => false;
        bool IModal.OnUnhandledKeyEvent (string name, KeyEventArgs args) => false;

        bool IPartiallyIntangibleControl.IsIntangibleAtPosition (Vector2 position) => false;

        int IFuzzyHitTestTarget.WalkTree (
            List<FuzzyHitTest.Result> output, ref FuzzyHitTest.Result thisControl, Vector2 position, Func<Control, bool> predicate, float maxDistanceSquared
        ) => 0;
        bool IFuzzyHitTestTarget.WalkChildren => false;

        Vector2? IAlignedControl.AlignedPosition => Aligner.MostRecentAlignedPosition;

        void IAlignedControl.EnsureAligned (ref UIOperationContext context, ref bool relayoutRequested) {
            if (!Aligner.AlignmentPending) {
                return;
            } else {
                Aligner.EnsureAligned(ref context, ref relayoutRequested);
            }
        }

        Control IAlignedControl.AlignmentAnchor => Aligner.Anchor;
    }
}
