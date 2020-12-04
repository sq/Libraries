using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Input;
using Squared.PRGUI.Accessibility;
using Squared.PRGUI.Layout;
using Squared.Util;

namespace Squared.PRGUI.Controls {
    public class ModalDialog : Window, IModal, IAcceleratorSource {
        public const float ModalShowSpeed = 0.1f;
        public const float ModalHideSpeed = 0.25f;

        protected Control _FocusDonor;
        public Control FocusDonor => _FocusDonor;

        public Control AcceptControl, CancelControl;

        // FIXME: Customizable?
        bool IModal.FadeBackground => true;
        bool IModal.BlockHitTests => true;
        bool IModal.BlockInput => true;
        bool IModal.RetainFocus => true;

        public void Show (UIContext context) {
            SetContext(context);
            // HACK: Prevent the layout info from computing our size from being used to render us next frame
            LayoutKey = ControlKey.Invalid;
            // Force realignment
            ScreenAlignment = ScreenAlignment;
            Visible = true;
            Intangible = false;
            var now = context.NowL;
            Opacity = Tween<float>.StartNow(0f, 1f, ModalShowSpeed, now: now);
            GenerateDynamicContent(true);
            _FocusDonor = context.TopLevelFocused;
            context.ShowModal(this);
        }

        public void Close () {
            Intangible = true;
            var now = Context.NowL;
            Opacity = Tween<float>.StartNow(Opacity.Get(now), 0f, ModalHideSpeed, now: now);
            Context.NotifyModalClosed(this);
            AcceptsFocus = false;
            _FocusDonor = null;
        }

        bool IModal.OnUnhandledKeyEvent (string name, KeyEventArgs args) {
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
