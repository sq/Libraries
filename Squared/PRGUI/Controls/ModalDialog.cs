using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Input;
using Squared.PRGUI.Accessibility;
using Squared.PRGUI.Layout;
using Squared.Threading;
using Squared.Util;

namespace Squared.PRGUI.Controls {
    public class ModalDialog : Window, IModal, IAcceleratorSource {
        protected Control _FocusDonor;
        public Control FocusDonor => _FocusDonor;

        public Control AcceptControl, CancelControl;

        // FIXME: Customizable?
        bool IModal.FadeBackground => true;
        bool IModal.BlockHitTests => true;
        bool IModal.BlockInput => true;
        bool IModal.RetainFocus => true;

        public bool IsActive { get; private set; }

        private Future<object> NextResultFuture = null;

        public static Future<object> ShowNew (UIContext context) {
            var modal = new ModalDialog {
            };
            return modal.Show(context);
        }

        void IModal.Show (UIContext context) {
            this.Show(context);
        }

        public Future<object> Show (UIContext context, Control focusDonor = null) {
            SetContext(context);
            // HACK: Prevent the layout info from computing our size from being used to render us next frame
            InvalidateLayout();
            // Force realignment
            ScreenAlignment = ScreenAlignment;
            Visible = true;
            Intangible = false;
            if (IsActive)
                return NextResultFuture;
            var f = NextResultFuture = new Future<object>();
            PlayAnimation(context.Animations?.ShowModalDialog);
            GenerateDynamicContent(true);
            _FocusDonor = focusDonor ?? context.TopLevelFocused;
            context.ShowModal(this);
            IsActive = true;
            return f;
        }

        void IModal.Close () {
            this.Close(null);
        }

        public void Close (object result = null) {
            if (!IsActive) {
                // FIXME
                /*
                if ((result != null) && (result != NextResultFuture.Result))
                    throw new ArgumentException("This modal was already closed with a different result");
                */
                return;
            }

            IsActive = false;
            NextResultFuture?.SetResult(result, null);
            Intangible = true;
            PlayAnimation(Context.Animations?.HideModalDialog);
            Context.NotifyModalClosed(this);
            AcceptsFocus = false;
            _FocusDonor = null;
        }

        bool IModal.OnUnhandledEvent (string name, Util.Event.IEventInfo args) {
            return false;
        }

        bool IModal.OnUnhandledKeyEvent (string name, KeyEventArgs args) {
            if (name != UIEvents.KeyUp)
                return false;

            if ((args.Key == Keys.Enter) && (AcceptControl != null))
                return Context.FireSyntheticClick(AcceptControl);
            else if ((args.Key == Keys.Escape) && (CancelControl != null))
                return Context.FireSyntheticClick(CancelControl);

            return false;
        }

        IEnumerable<KeyValuePair<Control, string>> IAcceleratorSource.Accelerators {
            get {
                if (AcceptControl != null)
                    yield return new KeyValuePair<Control, string>(AcceptControl, "Enter");
                if (CancelControl != null)
                    yield return new KeyValuePair<Control, string>(CancelControl, "Escape");
            }
        }
    }
}
