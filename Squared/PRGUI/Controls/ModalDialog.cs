using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Squared.PRGUI.Accessibility;
using Squared.PRGUI.Decorations;
using Squared.PRGUI.Imperative;
using Squared.PRGUI.Layout;
using Squared.Render.Convenience;
using Squared.Threading;
using Squared.Util;
using Squared.Util.Event;

namespace Squared.PRGUI.Controls {
    public delegate void TypedContainerContentsDelegate<TParameters> (ref ContainerBuilder builder, TParameters parameters);

    public interface IModalDialog : IModal {
        Control AcceptControl { get; set; }
        Control CancelControl { get; set; }
        void SetResultValues (object accept, object cancel);
    }

    public interface IModalDialog<TResult> : IModalDialog {
        void SetResultValues (TResult accept, TResult cancel);
        void SetResultValues (Func<TResult> getAccept, TResult cancel);
        void SetResultValues (Func<TResult> getAccept, Func<TResult> getCancel);
    }

    public class ModalDialog<TParameters, TResult> : Window, IModalDialog<TResult>, IAcceleratorSource {
        public event Action<IModal> Shown;
        public event Action<IModal, ModalCloseReason> Closed;
        public event Func<IModal, bool> BeforeAccept, BeforeCancel;
        public event Action<IModal, TResult> Accepted;

        public TParameters Parameters;

        protected Control _FocusDonor;
        public Control FocusDonor => _FocusDonor;

        public bool CloseOnEnter = false, CloseOnEscape = true;

        private bool ShowNextUpdate;
        private EventSubscription AcceptHandlerRegistered, CancelHandlerRegistered;
        private Control _AcceptControl, _CancelControl;

        public virtual Control BackgroundFadeCutout { get; set; }

        bool IModal.CanClose (ModalCloseReason reason) => (reason) switch {
            ModalCloseReason.UserCancelled => AllowCancel,
            _ => true,
        };

        public bool AllowCancel = true;

        public Control AcceptControl {
            get => _AcceptControl;
            set {
                if (_AcceptControl == value)
                    return;
                if (_AcceptControl != null)
                    AcceptHandlerRegistered.Dispose();
                _AcceptControl = value;
                RegisterWeakHandler(_AcceptControl, _OnAcceptClick, ref AcceptHandlerRegistered);
            }
        }

        public Control CancelControl {
            get => _CancelControl;
            set {
                if (_CancelControl == value)
                    return;
                if (_CancelControl != null)
                    CancelHandlerRegistered.Dispose();
                _CancelControl = value;
                RegisterWeakHandler(_CancelControl, _OnCancelClick, ref CancelHandlerRegistered);
            }
        }

        private Func<TResult> _GetAcceptResult, _GetCancelResult;
        private TResult _AcceptResult, _CancelResult;
        // private bool HasAcceptResult, HasCancelResult;

        void IModalDialog.SetResultValues(object accept, object cancel) {
            if (accept is Func<TResult> getAccept) {
                if (cancel is TResult cv)
                    SetResultValues(getAccept, cv);
                else
                    SetResultValues(getAccept, cancel as Func<TResult>);
            } else
                SetResultValues((TResult)accept, (TResult)cancel);
        }

        /// <summary>
        /// If set, when the modal is accepted it will call this function to compute its result value.
        /// </summary>
        public Func<TResult> GetAcceptResult {
            get => _GetAcceptResult;
            set => _GetAcceptResult = value;
        }

        /// <summary>
        /// If set, when the modal is accepted it will call this function to compute its result value.
        /// </summary>
        public Func<TResult> GetCancelResult {
            get => _GetCancelResult;
            set => _GetCancelResult = value;
        }

        /// <summary>
        /// If set, when the modal is accepted it will have this result value.
        /// </summary>
        public TResult AcceptResult {
            get => (_GetAcceptResult != null) ? _GetAcceptResult() : _AcceptResult;
            set {
                _GetAcceptResult = null;
                _AcceptResult = value;
            }
        }
        /// <summary>
        /// If set, when the modal is cancelled it will have this result value.
        /// </summary>
        public TResult CancelResult {
            get => (_GetCancelResult != null) ? _GetCancelResult() : _CancelResult;
            set {
                _GetCancelResult = null;
                _CancelResult = value;
            }
        }

        public void SetResultValues (TResult accept, TResult cancel) {
            _AcceptResult = accept;
            _GetAcceptResult = null;
            _CancelResult = cancel;
            _GetCancelResult = null;
        }

        public void SetResultValues (Func<TResult> getAccept, TResult cancel) {
            _AcceptResult = default;
            _GetAcceptResult = getAccept;
            _CancelResult = cancel;
            _GetCancelResult = null;
        }

        public void SetResultValues (Func<TResult> getAccept, Func<TResult> getCancel) {
            _AcceptResult = default;
            _GetAcceptResult = getAccept;
            _CancelResult = default;
            _GetCancelResult = getCancel;
        }

        public virtual TResult GetResultForReason (ModalCloseReason reason) {
            if (reason == ModalCloseReason.UserConfirmed)
                return AcceptResult;
            else
                return CancelResult;
        }

        public float BackgroundFadeLevel { get; set; } = 1.0f;

        public bool RetainFocus { get; set; } = true;
        public bool BlockHitTests { get; set; } = true;
        public bool BlockInput { get; set; } = true;
        public bool IsActive { get; protected set; }
        private bool IsFadingOut;

        private Future<TResult> NextResultFuture = null;
        private EventSubscriber _OnAcceptClick, _OnCancelClick;

        public ModalDialog (TParameters parameters)
            : base () {
            Parameters = parameters;
            // Appearance.Opacity = 0f;
            _OnAcceptClick = OnAcceptClick;
            _OnCancelClick = OnCancelClick;
            ElevateOnFocus = true;
        }

        private void RegisterWeakHandler (Control target, EventSubscriber handler, ref EventSubscription subscription) {
            if (Context == null)
                return;
            if (target == null)
                return;

            subscription = Context.EventBus.Subscribe(target, UIEvents.Click, handler, weak: true);
        }

        protected virtual void OnAcceptClick (IEventInfo e) {
            if ((BeforeAccept != null) && BeforeAccept(this)) {
                e.Consume();
                return;
            }

            // FIXME: This stops event listeners from responding to click events for the accept control
            e.Consume();
            Close(AcceptResult, ModalCloseReason.UserConfirmed);
        }

        protected virtual void OnCancelClick (IEventInfo e) {
            if (!AllowCancel) {
                e.Consume();
                return;
            }

            if ((BeforeCancel != null) && BeforeCancel(this)) {
                e.Consume();
                return;
            }

            // FIXME: This stops event listeners from responding to click events for the cancel control
            e.Consume();
            Close(CancelResult, ModalCloseReason.UserCancelled);
        }

        public static Future<TResult> ShowNew (UIContext context, TParameters parameters) {
            var modal = new ModalDialog<TParameters, TResult>(parameters);
            return modal.Show(context);
        }

        public Future<TResult> Show (UIContext context, Control focusDonor = null, Vector2? donorAlignment = null) {
            // Undo freeze that happens in Close
            FreezeDynamicContent = false;

            Context = context;
            // HACK: Prevent the layout info from computing our size from being used to render us next frame
            InvalidateLayout();
            // Force realignment
            if (donorAlignment.HasValue) {
                AlignmentAnchor = focusDonor;
                Alignment = donorAlignment.Value;
                // HACK
                AlignmentAnchorPoint = Vector2.One - donorAlignment.Value;
            } else {
                Aligner.AlignmentPending = true;
            }
            IsFadingOut = false;

            // If the context is busy updating and we're not already active, hide ourselves for a frame
            if (!context.IsUpdating)
                Visible = true;
            else
                Visible = IsActive;

            Intangible = false;
            if (IsActive)
                return NextResultFuture;
            var f = NextResultFuture = new Future<TResult>();
            PlayShowAnimation();
            GenerateDynamicContent(true);
            _FocusDonor = focusDonor ?? context.Focused;
            IsActive = true;
            context.ShowModal(this, false);
            // HACK: Ensure event handlers are registered if they weren't already
            if (AcceptHandlerRegistered.EventFilter.Source != AcceptControl)
                RegisterWeakHandler(AcceptControl, _OnAcceptClick, ref AcceptHandlerRegistered);
            if (CancelHandlerRegistered.EventFilter.Source != CancelControl)
                RegisterWeakHandler(CancelControl, _OnCancelClick, ref CancelHandlerRegistered);
            Elevate();
            if (Shown != null)
                Shown(this);
            ShowNextUpdate = context.IsUpdating;
            return f;
        }

        protected virtual void PlayShowAnimation () {
            var fadeIn = ShowAnimation;
            if (fadeIn != null) {
                Appearance.Opacity = 0f;
                StartAnimation(fadeIn);
            } else
                Appearance.Opacity = 1f;
        }

        protected override void OnLayoutComplete (ref UIOperationContext context, ref bool relayoutRequested) {
            base.OnLayoutComplete(ref context, ref relayoutRequested);

            if (ShowNextUpdate && context.UIContext.IsPerformingRelayout) {
                ShowNextUpdate = false;
                Visible = true;
            }
        }

        void IModal.Show (UIContext context) {
            Show(context);
        }

        void IModal.OnShown () {
            if (!IsActive)
                throw new InvalidOperationException("Call ModalDialog.Show, not Context.ShowModal(...)");
        }

        bool IModal.Close (ModalCloseReason reason) {
            if (!AllowCancel && (reason == ModalCloseReason.UserCancelled))
                return false;
            return Close(reason);
        }

        public bool Close (ModalCloseReason reason) {
            return Close(GetResultForReason(reason), reason);
        }

        protected virtual IControlAnimation HideAnimation => Context?.Animations?.HideModalDialog;
        protected virtual IControlAnimation ShowAnimation => Context?.Animations?.ShowModalDialog;

        public bool Close (TResult result, ModalCloseReason reason) {
            CancelDrag();
            if (!IsActive)
                return false;

            if (!AllowCancel && (reason == ModalCloseReason.UserCancelled))
                return false;

            // Release our event listeners to avoid a cycle
            AcceptHandlerRegistered.Dispose();
            CancelHandlerRegistered.Dispose();
            AcceptHandlerRegistered = CancelHandlerRegistered = default;

            IsActive = false;
            // HACK: Because content callback may permute state
            FreezeDynamicContent = true;
            NextResultFuture?.SetResult(result, null);
            Intangible = true;
            IsFadingOut = (Context.TopLevelFocused == this);
            var f = StartAnimation(HideAnimation);
            FireEvent(UIEvents.Closed, reason);
            Context.NotifyModalClosed(this);
            if ((reason == ModalCloseReason.UserConfirmed) && (Accepted != null))
                Accepted(this, result);
            if (Closed != null)
                Closed(this, reason);
            if ((f == null) || (f.CompletedSuccessfully && f.Result == false))
                Context.Controls.Remove(this);
            AcceptsFocus = false;
            _FocusDonor = null;
            return true;
        }

        public override void UserClose () {
            Close(ModalCloseReason.UserCancelled);
        }

        protected override void OnRasterize (ref UIOperationContext context, ref RasterizePassSet passSet, DecorationSettings settings, IDecorator decorations) {
            // HACK: Normally closing the modal will cause it to lose focus, which creates a 
            //  distracting title bar flicker. We want to suppress that during the close animation
            if (IsFadingOut)
                settings.State |= ControlStates.ContainsFocus;

            base.OnRasterize(ref context, ref passSet, settings, decorations);
        }

        bool IModal.OnUnhandledEvent (string name, Util.Event.IEventInfo args) {
            return false;
        }

        protected override bool OnEvent<T> (string name, T args) {
            if (KeyEventArgs.From(ref args, out var ka))
                return OnKeyEvent(name, ka);

            return base.OnEvent(name, args);
        }

        private bool OnKeyEvent (string name, KeyEventArgs args) {
            if (name != UIEvents.KeyPress)
                return false;

            // HACK
            var ei = new EventInfo<object>(null, this, default(EventCategoryToken), null, null, args);
            if (args.Key == Keys.Enter) {
                if ((AcceptControl != null) && Context.FireSyntheticClick(AcceptControl))
                    return true;

                if (!CloseOnEnter)
                    return false;

                OnAcceptClick(ei);
                return ei.IsConsumed;
            } else if (args.Key == Keys.Escape) {
                if (!CloseOnEscape)
                    return false;

                if ((CancelControl != null) && Context.FireSyntheticClick(CancelControl))
                    return true;

                OnCancelClick(ei);
                return ei.IsConsumed;
            }

            return false;
        }

        bool IModal.OnUnhandledKeyEvent (string name, KeyEventArgs args) {
            if (!ArrowKeyNavigation)
                return false;

            return PerformArrowKeyNavigation(Context, name, args);
        }

        IEnumerable<AcceleratorInfo> IAcceleratorSource.Accelerators {
            get {
                if (AcceptControl != null)
                    yield return new AcceleratorInfo(AcceptControl, Keys.Enter, suppressSyntheticEvents: true);
                if (CancelControl != null)
                    yield return new AcceleratorInfo(CancelControl, Keys.Escape, suppressSyntheticEvents: true);
            }
        }

        public Task<TResult> GetResult () => NextResultFuture.AsTask();
    }

    public class ModalDialog : ModalDialog<object, object> {
        public ModalDialog ()
            : base (null) {
        }

        public ModalDialog (object parameters)
            : base (parameters) {
        }

        public static ModalDialog<object, TResult> New<TResult> () {
            return new ModalDialog<object, TResult>(null);
        }

        public static ModalDialog<TParameters, object> New<TParameters> (
            TParameters parameters, ContainerContentsDelegate dynamicContents = null
        ) {
            return new ModalDialog<TParameters, object>(parameters) {
                DynamicContents = dynamicContents
            };
        }

        public static ModalDialog<TParameters, TResult> New<TParameters, TResult> (
            TParameters parameters, TResult acceptResult, TResult cancelResult, 
            ContainerContentsDelegate dynamicContents = null
        ) {
            return new ModalDialog<TParameters, TResult>(parameters) {
                AcceptResult = acceptResult,
                CancelResult = cancelResult,
                DynamicContents = dynamicContents
            };
        }

        new public void Close (ModalCloseReason reason) {
            base.Close(default, reason);
        }
    }
}
