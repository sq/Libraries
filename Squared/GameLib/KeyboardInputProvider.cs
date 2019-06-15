using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Squared.Game {
    public abstract class KeyboardInputProvider : IDisposable {
        public struct Deactivation : IDisposable {
            public KeyboardInputProvider This;

            public void Dispose () {
                This.EnableCount++;
                if (This.EnableCount == 1)
                    This.Install();
            }
        }

        protected int EnableCount;

        public readonly HashSet<char> BlockedCharacters = new HashSet<char>();
        public readonly List<char> Buffer = new List<char>();

        public EventHandler<char> OnCharacter;

        protected KeyboardInputProvider () {
        }

        public Deactivation Deactivate () {
            this.EnableCount--;
            if (this.EnableCount == 0)
                Uninstall();
            return new Deactivation {
                This = this
            };
        }

        protected void PushCharacter (char ch) {
            OnCharacter?.Invoke(this, ch);

            // Workaround for Nuklear losing its mind
            if (ch < 32)
                return;

            if (BlockedCharacters.Contains(ch))
                return;

            Buffer.Add(ch);
        }

        public abstract void Install ();
        public abstract void Uninstall ();

        public void Dispose () {
            Uninstall();
        }
    }

#if XNA
    public class KeyboardInput : KeyboardInputProvider, System.Windows.Forms.IMessageFilter {
        [DllImport("user32.dll")]
        static extern bool TranslateMessage(ref System.Windows.Forms.Message lpMsg);

        const int WM_KEYDOWN = 0x100;
        const int WM_KEYUP = 0x101;
        const int WM_CHAR = 0x102;

        public KeyboardInput () 
            : base () {
        }

        public override void Install () {
            System.Windows.Forms.Application.AddMessageFilter(this);
        }

        public override void Uninstall () {
            System.Windows.Forms.Application.RemoveMessageFilter(this);
        }

        public bool PreFilterMessage (ref System.Windows.Forms.Message m) {
            switch (m.Msg) {
                case WM_KEYDOWN:
                case WM_KEYUP:
                    // XNA normally doesn't invoke TranslateMessage so we don't get any char events
                    TranslateMessage(ref m);
                    return false;
                case WM_CHAR:
                    var ch = (char)m.WParam.ToInt32();
                    // We can get wm_char events for control characters like backspace and Nuklear *does not like that*
                    PushCharacter(ch);
                    return true;
                default:
                    return false;
            }
        }
    }
#elif FNA
    public class KeyboardInput : KeyboardInputProvider {
        public KeyboardInput () 
            : base () {
        }

        public override void Install () {
            Microsoft.Xna.Framework.Input.TextInputEXT.StartTextInput();
            Microsoft.Xna.Framework.Input.TextInputEXT.TextInput += TextInputEXT_TextInput;
        }

        public override void Uninstall () {
            Microsoft.Xna.Framework.Input.TextInputEXT.StopTextInput();
            Microsoft.Xna.Framework.Input.TextInputEXT.TextInput -= TextInputEXT_TextInput;
        }

        private void TextInputEXT_TextInput (char ch) {
            PushCharacter(ch);
        }
    }
#endif
}
