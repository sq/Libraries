using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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
    public sealed partial class UIContext : IDisposable {
        private class FocusMemoryCell {
            public GCHandle Handle;

            public Control Value {
                get => Handle.IsAllocated ? Handle.Target as Control : null;
                set {
                    var existingValue = Value;
                    if (existingValue == value)
                        return;
                    if (Handle.IsAllocated)
                        Handle.Free();
                    if (value != null)
                        Handle = GCHandle.Alloc(value, GCHandleType.Weak);
                    else
                        Handle = default;
                }
            }

            ~FocusMemoryCell() {
                if (Handle.IsAllocated)
                    Handle.Free();
            }
        }

        internal List<UnhandledEvent> UnhandledEvents = new List<UnhandledEvent>();
        internal List<UnhandledEvent> PreviousUnhandledEvents = new List<UnhandledEvent>();

        internal List<IModal> ModalStack = new List<IModal>();

        public IModal ActiveModal =>
            (ModalStack.Count > 0)
                ? ModalStack[ModalStack.Count - 1]
                : null;

        private Control _Focused, _MouseCaptured, _Hovering, _KeyboardSelection,
            _PreviouslyFocusedForTimestampUpdate, _PreferredTooltipSource;
        
        private IModal _FocusedModal;

        /// <summary>
        /// This tween's value will be applied to the tooltip so you can conditionally hide it based on application state
        ///  without actually causing it to go away or having to call HideTooltip repeatedly
        /// </summary>
        public Tween<float> GlobalTooltipOpacity = 1f;

        /// <summary>
        /// The control most recently interacted with by the user
        /// </summary>
        public Control FixatedControl => MouseCaptured ?? KeyboardSelection ?? Hovering;
        
        // We track this across updates so that programmatic focus changes will still fire the fixation change event
        private Control PreviouslyFixated;

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

        public long LastFocusChange { get; private set; }
        public long LastHoverLoss { get; private set; }
        public long LastHoverGain { get; private set; }

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

        public IModal FocusedModal => _FocusedModal;

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
                if (previous != value) {
                    // If hovering moves from ControlA to null, then later moves from null to ControlB, we don't
                    //  want to restart the fade for ControlA
                    if (previous != null)
                        LastHoverLoss = NowL;
                    // If the mouse moves off of a control and then back onto it, we shouldn't restart the fade-in
                    //  animation, it'll look really bad
                    if ((PreviousHovering != value) && (value != null))
                        LastHoverGain = NowL;
                    // We want to always keep a control in PreviousHovering so that fade-outs will work
                    if (previous != null)
                        PreviousHovering = previous;
                    HandleHoverTransition(previous, value);
                }
            }
        }

        public Control KeyboardSelection {
            get => _KeyboardSelection;
        }

        public Control TopLevelFocused { get; private set; }
        public Control TopLevelModalFocusDonor { get; private set; }

        public Control ModalFocusDonor { get; private set; }
        /// <summary>
        /// The control that most recently held focus before the current control, if any.
        /// Note that if focus was acquired from a focus donor, the focus donor will not be recorded in this property.
        /// </summary>
        // FIXME: This can cause a reference leak
        public Control PreviousFocused { get; private set; }
        /// <summary>
        /// The control that was most recently being hovered over before the current control, if any.
        /// Note that this will rarely become null.
        /// </summary>
        // FIXME: This can cause a reference leak
        public Control PreviousHovering { get; private set; }
        // FIXME: This can cause a reference leak
        public Control PreviousTopLevelFocused { get; private set; }

        private Control PreviousMouseDownTarget = null, FocusedAtStartOfUpdate = null;

        private ConditionalWeakTable<Control, FocusMemoryCell> TopLevelFocusMemory = new ConditionalWeakTable<Control, FocusMemoryCell>();

        public Control CurrentTooltipAnchor => (IsTooltipActive && IsTooltipVisible) ? PreviousTooltipAnchor : null;

        private Vector2 MousePositionWhenKeyboardSelectionWasLastUpdated;
        public IScrollableControl DragToScrollTarget { get; private set; }
        private Vector2? DragToScrollInitialOffset;
        private Vector2 DragToScrollInitialPosition;

        bool SuppressAutoscrollDueToInputScroll = false,
            SuppressFocusChangeAnimationsThisStep = false;

        /// <summary>
        /// This control is currently being scrolled via implicit scroll input
        /// </summary>
        public Control CurrentImplicitScrollTarget { get; private set; }

        internal readonly HashSet<Control> FocusChain = new HashSet<Control>(Control.Comparer.Instance);

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

        internal bool IsCompositionActive = false,
            // HACK: Suppress the 'if not Visible then don't perform layout' behavior
            IsUpdatingSubtreeLayout;

        public bool IsUpdating { get; private set; }
        public bool IsPerformingRelayout { get; private set; }

        private Vector2 LastMousePosition => _LastInput.CursorPosition;
        private MouseButtons LastMouseButtons => _LastInput.Buttons;
        private KeyboardModifiers CurrentModifiers => _CurrentInput.Modifiers;
        private MouseButtons CurrentMouseButtons => _CurrentInput.Buttons;

        UnorderedList<IPostLayoutListener> _PostLayoutListeners = new UnorderedList<IPostLayoutListener>();
        List<Control> _TopLevelControls = new List<Control>();

        private int ScratchRenderTargetsUsedThisFrame;

        public float Now { get; private set; }
        public long NowL { get; private set; }

        /// <summary>
        /// You can use this to explicitly request that textures for static images etc be treated as not premultiplied
        /// </summary>
        public BlendState DefaultTextureBlendState = null;
        internal ConditionalWeakTable<Texture2D, BlendState> TextureBlendStateTable = new ConditionalWeakTable<Texture2D, BlendState>();

        public void SetDefaultBlendState (Texture2D texture, BlendState blendState) {
            TextureBlendStateTable.Remove(texture);
            TextureBlendStateTable.Add(texture, blendState);
        }

        public BlendState PickDefaultBlendState (Texture2D texture) {
            if ((texture != null) && TextureBlendStateTable.TryGetValue(texture, out BlendState result))
                return result;
            return DefaultTextureBlendState;
        }

        public bool HasBeenFocusedSinceStartOfUpdate (Control control) {
            return (Focused == control) && (FocusedAtStartOfUpdate == control);
        }

        public Vector2 GetLocalCursorPosition (Control relativeTo) {
            var temp = MakeMouseEventArgs(relativeTo, CurrentInputState.CursorPosition, null, false);
            return temp.LocalPosition;
        }
    }
}
