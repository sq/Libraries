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
        object AcceptResult { get; set; }
        object CancelResult { get; set; }
    }

    public interface IModalDialog<TResult> : IModalDialog {
        new TResult AcceptResult { get; set; }
        new TResult CancelResult { get; set; }
    }

    public class ModalDialog<TParameters, TResult> : Window, IModalDialog<TResult>, IAcceleratorSource {
        public event Action<IModal> Shown;
        public event Action<IModal, ModalCloseReason> Closed;
        public event Func<IModal, bool> Accepted, Cancelled;

        public TParameters Parameters;

        protected Control _FocusDonor;
        public Control FocusDonor => _FocusDonor;

        public bool CloseOnEnter = true, CloseOnEscape = true;

        private bool IsAcceptHandlerRegistered, IsCancelHandlerRegistered;
        private Control _AcceptControl, _CancelControl;

        bool IModal.CanClose (ModalCloseReason reason) => true;

        public Control AcceptControl {
            get => _AcceptControl;
            set {
                if (_AcceptControl == value)
                    return;
                _AcceptControl = value;
                RegisterHandler(_AcceptControl, OnAcceptClick, ref IsAcceptHandlerRegistered);
            }
        }

        public Control CancelControl {
            get => _CancelControl;
            set {
                if (_CancelControl == value)
                    return;
                _CancelControl = value;
                RegisterHandler(_CancelControl, OnCancelClick, ref IsCancelHandlerRegistered);
            }
        }

        private TResult _AcceptResult, _CancelResult;
        // private bool HasAcceptResult, HasCancelResult;

        object IModalDialog.AcceptResult {
            get => _AcceptResult;
            set => _AcceptResult = (TResult)value;
        }

        object IModalDialog.CancelResult {
            get => _CancelResult;
            set => _CancelResult = (TResult)value;
        }

        public TResult AcceptResult {
            get => _AcceptResult;
            set => _AcceptResult = value;
        }
        public TResult CancelResult {
            get => _CancelResult;
            set => _CancelResult = value;
        }

        public float BackgroundFadeLevel { get; set; } = 1.0f;
        bool IModal.RetainFocus => true;

        public bool BlockHitTests { get; set; } = true;
        public bool BlockInput { get; set; } = true;
        public bool IsActive { get; private set; }
        private bool IsFadingOut;

        private Future<TResult> NextResultFuture = null;

        public ModalDialog (TParameters parameters)
            : base () {
            Parameters = parameters;
            Appearance.Opacity = 0f;
        }

        private void RegisterHandler (Control target, EventSubscriber handler, ref bool isRegistered) {
            if (Context == null)
                return;
            if (target == null)
                return;

            Context.EventBus.Subscribe(target, UIEvents.Click, handler);
            isRegistered = true;
        }

        protected virtual void OnAcceptClick (IEventInfo e) {
            if ((Accepted != null) && Accepted(this)) {
                e.Consume();
                return;
            }

            e.Consume();
            Close(AcceptResult, ModalCloseReason.UserConfirmed);
        }

        protected virtual void OnCancelClick (IEventInfo e) {
            if ((Cancelled != null) && Cancelled(this)) {
                e.Consume();
                return;
            }

            e.Consume();
            Close(CancelResult, ModalCloseReason.UserCancelled);
        }

        public static Future<TResult> ShowNew (UIContext context, TParameters parameters) {
            var modal = new ModalDialog<TParameters, TResult>(parameters);
            return modal.Show(context);
        }

        void IModal.Show (UIContext context) {
            Show(context);
        }

        public Future<TResult> Show (UIContext context, Control focusDonor = null, Vector2? donorAlignment = null) {
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
                Alignment = Alignment;
            }
            IsFadingOut = false;
            Visible = true;
            Intangible = false;
            if (IsActive)
                return NextResultFuture;
            var f = NextResultFuture = new Future<TResult>();
            var fadeIn = context.Animations?.ShowModalDialog;
            if (fadeIn != null)
                StartAnimation(fadeIn);
            else
                Appearance.Opacity = 1f;
            GenerateDynamicContent(true);
            _FocusDonor = focusDonor ?? context.TopLevelFocused;
            context.ShowModal(this, false);
            // HACK: Ensure event handlers are registered if they weren't already
            if (!IsAcceptHandlerRegistered)
                RegisterHandler(AcceptControl, OnAcceptClick, ref IsAcceptHandlerRegistered);
            if (!IsCancelHandlerRegistered)
                RegisterHandler(CancelControl, OnCancelClick, ref IsCancelHandlerRegistered);
            IsActive = true;
            if (Shown != null)
                Shown(this);
            return f;
        }

        bool IModal.Close (ModalCloseReason reason) {
            var value = (reason == ModalCloseReason.UserConfirmed)
                ? AcceptResult
                : CancelResult;
            return Close(value, reason);
        }

        public bool Close (TResult result, ModalCloseReason reason) {
            CancelDrag();
            if (!IsActive)
                return false;

            IsActive = false;
            NextResultFuture?.SetResult(result, null);
            Intangible = true;
            IsFadingOut = (Context.TopLevelFocused == this);
            StartAnimation(Context.Animations?.HideModalDialog);
            Context.NotifyModalClosed(this);
            if (Closed != null)
                Closed(this, reason);
            Context.Controls.Remove(this);
            AcceptsFocus = false;
            _FocusDonor = null;
            return true;
        }

        protected override void OnRasterize (ref UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings, IDecorator decorations) {
            // HACK: Normally closing the modal will cause it to lose focus, which creates a 
            //  distracting title bar flicker. We want to suppress that during the close animation
            if (IsFadingOut)
                settings.State |= ControlStates.ContainsFocus;

            base.OnRasterize(ref context, ref renderer, settings, decorations);
        }

        bool IModal.OnUnhandledEvent (string name, Util.Event.IEventInfo args) {
            return false;
        }

        bool IModal.OnUnhandledKeyEvent (string name, KeyEventArgs args) {
            if (name != UIEvents.KeyUp)
                return false;

            // HACK
            var ei = new EventInfo<object>(null, this, default(EventCategoryToken), null, null, args);
            if (args.Key == Keys.Enter) {
                if (!CloseOnEnter)
                    return false;

                if ((AcceptControl != null) && Context.FireSyntheticClick(AcceptControl))
                    return true;

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

        IEnumerable<AcceleratorInfo> IAcceleratorSource.Accelerators {
            get {
                if (AcceptControl != null)
                    yield return new AcceleratorInfo(AcceptControl, Keys.Enter);
                if (CancelControl != null)
                    yield return new AcceleratorInfo(CancelControl, Keys.Escape);
            }
        }
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

        public void Close (ModalCloseReason reason) {
            base.Close(default, reason);
        }
    }
}
