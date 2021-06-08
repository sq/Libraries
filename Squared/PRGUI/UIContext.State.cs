using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Squared.PRGUI.Controls;
using Squared.PRGUI.Decorations;
using Squared.PRGUI.Input;
using Squared.PRGUI.Layout;
using Squared.Render;
using Squared.Render.Convenience;
using Squared.Render.Text;
using Squared.Util;
using Squared.Util.Event;
using Squared.Util.Text;

namespace Squared.PRGUI {
    public partial class UIContext : IDisposable {
        internal List<UnhandledEvent> UnhandledEvents = new List<UnhandledEvent>();
        internal List<UnhandledEvent> PreviousUnhandledEvents = new List<UnhandledEvent>();

        internal List<IModal> ModalStack = new List<IModal>();

        public IModal ActiveModal =>
            (ModalStack.Count > 0)
                ? ModalStack[ModalStack.Count - 1]
                : null;

        private Control _Focused, _MouseCaptured, _Hovering, _KeyboardSelection;

        private Control _PreferredTooltipSource;

        /// <summary>
        /// The control most recently interacted with by the user
        /// </summary>
        public Control FixatedControl => MouseCaptured ?? KeyboardSelection ?? Hovering;

        /// <summary>
        /// The control that most recently received a click from the user
        /// </summary>
        public Control PreviousClickTarget { get; private set; }

        /// <summary>
        /// The control currently underneath the mouse cursor
        /// </summary>
        public Control MouseOver { get; private set; }

        /// <summary>
        /// The control currently underneath the mouse cursor, even if it is intangible.
        /// Used as a scroll target
        /// </summary>
        public Control MouseOverLoose { get; private set; }

        /// <summary>
        /// The control that currently has keyboard input focus
        /// </summary>
        public Control Focused {
            get => _Focused;
            set {
                if (!TrySetFocus(value, false))
                    TrySetFocus(null, true);
            }
        }

        /// <summary>
        /// The control that currently has the mouse captured (if a button is pressed)
        /// </summary>
        public Control MouseCaptured {
            get => _MouseCaptured;
            private set {
                if ((value != null) && !value.AcceptsMouseInput)
                    throw new InvalidOperationException("Control cannot accept mouse input");
                var previous = _MouseCaptured;
                _MouseCaptured = value;
                if (value != null)
                    ClearKeyboardSelection();
                if (previous != value)
                    FireEvent(UIEvents.MouseCaptureChanged, value, previous);
            }
        }

        /// <summary>
        /// The control currently underneath the mouse cursor, as long as the mouse is not captured by another control
        /// </summary>
        public Control Hovering {
            get => _Hovering;
            private set {
                var previous = _Hovering;
                _Hovering = value;
                if (previous != value)
                    HandleHoverTransition(previous, value);
            }
        }

        public Control KeyboardSelection {
            get => _KeyboardSelection;
        }

        public Control TopLevelFocused { get; private set; }
        public Control TopLevelModalFocusDonor { get; private set; }

        public Control ModalFocusDonor { get; private set; }
        public Control PreviousFocused { get; private set; }
        public Control PreviousTopLevelFocused { get; private set; }

        private Control PreviousMouseDownTarget = null;

        private ConditionalWeakTable<Control, Control> TopLevelFocusMemory = new ConditionalWeakTable<Control, Control>();

        public Control CurrentTooltipAnchor => (IsTooltipActive && IsTooltipVisible) ? PreviousTooltipAnchor : null;

        private Vector2 MousePositionWhenKeyboardSelectionWasLastUpdated;
        public IScrollableControl DragToScrollTarget { get; private set; }
        private Vector2? DragToScrollInitialOffset;
        private Vector2 DragToScrollInitialPosition;

        bool SuppressAutoscrollDueToInputScroll = false;

        /// <summary>
        /// This control is currently being scrolled via implicit scroll input
        /// </summary>
        public Control CurrentImplicitScrollTarget { get; private set; }

        internal readonly HashSet<Control> FocusChain = new HashSet<Control>(new ReferenceComparer<Control>());

        private readonly List<IInputSource> ScratchInputSources = new List<IInputSource>();
        private readonly List<InputID> InputIDs = new List<InputID>();

        private InputState _CurrentInput, _LastInput;
        private List<Keys> _LastHeldKeys = new List<Keys>(), 
            _CurrentHeldKeys = new List<Keys>();

        public InputState CurrentInputState => _CurrentInput;
        public InputState LastInputState => _LastInput;

        private Vector2? MouseDownPosition;
        private Control ReleasedCapture = null;
        private Control RetainCaptureRequested = null;

        private Vector2 LastClickPosition;
        private double LastMouseDownTime, LastClickTime;
        private int SequentialClickCount;

        private double? FirstTooltipHoverTime;
        private double LastTooltipHoverTime;

        private Tooltip CachedTooltip;
        private Control PreviousTooltipAnchor;
        private bool IsTooltipVisible;
        private int CurrentTooltipContentVersion;
        private Controls.StaticText CachedCompositionPreview;

        internal bool IsCompositionActive = false;

        private Vector2 LastMousePosition => _LastInput.CursorPosition;
        private MouseButtons LastMouseButtons => _LastInput.Buttons;
        private KeyboardModifiers CurrentModifiers => _CurrentInput.Modifiers;
        private MouseButtons CurrentMouseButtons => _CurrentInput.Buttons;

        UnorderedList<IPostLayoutListener> _PostLayoutListeners = new UnorderedList<IPostLayoutListener>();
        List<Control> _TopLevelControls = new List<Control>();

        // HACK: Suppress the 'if not Visible then don't perform layout' behavior
        internal bool IsUpdatingSubtreeLayout;

        private int ScratchRenderTargetsUsedThisFrame;

        public float Now { get; private set; }
        public long NowL { get; private set; }
    }
}
