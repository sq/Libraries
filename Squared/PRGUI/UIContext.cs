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
        internal struct UnhandledEvent {
            internal class Comparer : IEqualityComparer<UnhandledEvent> {
                public static readonly Comparer Instance = new Comparer();

                public bool Equals (UnhandledEvent x, UnhandledEvent y) {
                    return x.Equals(y);
                }

                public int GetHashCode (UnhandledEvent obj) {
                    return obj.GetHashCode();
                }
            }

            public Control Source;
            public string Name;

            public bool Equals (UnhandledEvent rhs) {
                return (Source == rhs.Source) &&
                    (Name == rhs.Name);
            }

            public override bool Equals (object obj) {
                if (obj is UnhandledEvent)
                    return Equals((UnhandledEvent)obj);
                else
                    return false;
            }
        }

        internal class ScratchRenderTarget : IDisposable {
            public readonly UIContext Context;
            public readonly AutoRenderTarget Instance;
            public readonly UnorderedList<RectF> UsedRectangles = new UnorderedList<RectF>();
            public ImperativeRenderer Renderer;
            public List<ScratchRenderTarget> Dependencies = new List<ScratchRenderTarget>();
            internal bool VisitedByTopoSort;

            public ScratchRenderTarget (RenderCoordinator coordinator, UIContext context) {
                Context = context;
                int width = (int)(context.CanvasSize.X * context.ScratchScaleFactor),
                    height = (int)(context.CanvasSize.Y * context.ScratchScaleFactor);
                Instance = new AutoRenderTarget(
                    coordinator, width, height,
                    false, Context.SurfaceFormat, DepthFormat.Depth24Stencil8
                );
            }

            public void Update () {
                int width = (int)(Context.CanvasSize.X * Context.ScratchScaleFactor),
                    height = (int)(Context.CanvasSize.Y * Context.ScratchScaleFactor);
                Instance.Resize(width, height);
            }

            public void Reset () {
                UsedRectangles.Clear();
                Dependencies.Clear();
                VisitedByTopoSort = false;
            }

            public void Dispose () {
                Instance?.Dispose();
            }

            internal bool IsSpaceAvailable (ref RectF rectangle) {
                foreach (var used in UsedRectangles) {
                    if (used.Intersects(ref rectangle))
                        return false;
                }

                return true;
            }
        }

        public static readonly HashSet<Keys> ModifierKeys = new HashSet<Keys> {
            Keys.LeftAlt,
            Keys.LeftControl,
            Keys.LeftShift,
            Keys.RightAlt,
            Keys.RightControl,
            Keys.RightShift,
            Keys.LeftWindows,
            Keys.RightWindows
        };

        public static readonly HashSet<Keys> SuppressRepeatKeys = new HashSet<Keys> {
            Keys.Escape,
            Keys.CapsLock,
            Keys.Scroll,
            Keys.NumLock,
            Keys.Insert
        };

        public bool LogRelayoutRequests = false;

        /// <summary>
        /// Reserves empty space around composited controls on all sides to create room for drop shadows and
        ///  any other decorations that may extend outside the control's rectangle.
        /// </summary>
        public int CompositorPaddingPx = 16;

        public float BackgroundFadeOpacity = 0.2f;
        public float BackgroundFadeDuration = 0.2f;

        // Allocate scratch rendering buffers (for composited controls) at a higher or lower resolution
        //  than the canvas, to improve the quality of transformed imagery
        public readonly float ScratchScaleFactor = 1.0f;

        // Full occlusion tests are performed with this padding region (in pixels) to account for things like
        //  drop shadows being visible even if the control itself is not
        public const float VisibilityPadding = 16;

        /// <summary>
        /// If set, it is possible for Focused to become null. Otherwise, the context will attempt to ensure
        ///  that a control is focused at all times
        /// </summary>
        public bool AllowNullFocus = true;

        /// <summary>
        /// Globally tracks whether text editing should be in insert or overwrite mode
        /// </summary>
        public bool TextInsertionMode = true;

        /// <summary>
        /// Tooltips will only appear after the mouse remains over a single control for this long (in seconds)
        /// </summary>
        public float TooltipAppearanceDelay = 0.3f;
        /// <summary>
        /// If the mouse leaves tooltip-bearing controls for this long (in seconds) the tooltip appearance delay will reset
        /// </summary>
        public float TooltipDisappearDelay = 0.2f;
        public float TooltipFadeDurationFast = 0.1f;
        public float TooltipFadeDuration = 0.2f;

        public float TooltipSpacing = 8;

        /// <summary>
        /// Double-clicks will only be tracked if this far apart or less (in seconds)
        /// </summary>
        public double DoubleClickWindowSize = 0.4;
        /// <summary>
        /// If the mouse is only moved this far (in pixels) it will be treated as no movement for the purposes of click detection
        /// </summary>
        public float MinimumMouseMovementDistance = 10;
        /// <summary>
        /// If the mouse has moved more than the minimum movement distance, do not generate a click event if a scroll occurred.
        /// Otherwise, a click event will be generated as long as the mouse is still within the target control.
        /// If this is not set, drag-to-scroll may misbehave.
        /// </summary>
        public bool SuppressSingleClickOnMovementWhenAppropriate = true;
        /// <summary>
        /// If the mouse moves more than the minimum movement distance, convert it into scrolling instead of
        ///  a click, when appropriate
        /// </summary>
        public bool EnableDragToScroll = true;
        /// <summary>
        /// Drag-to-scroll will have its movement speed increased by this factor
        /// </summary>
        public float DragToScrollSpeed = 1.0f;

        public float AutoscrollMargin = 4;
        public float AutoscrollSpeedSlow = 6;
        public float AutoscrollSpeedFast = 80;
        public float AutoscrollFastThreshold = 512;
        public float AutoscrollInstantThreshold = 1200;

        /// <summary>
        /// A key must be held for this long (in seconds) before repeating begins
        /// </summary>
        public double FirstKeyRepeatDelay = 0.4;
        /// <summary>
        /// Key repeating begins at this rate (in seconds)
        /// </summary>
        public double KeyRepeatIntervalSlow = 0.09;
        /// <summary>
        /// Key repeating accelerates to this rate (in seconds) over time
        /// </summary>
        public double KeyRepeatIntervalFast = 0.03;
        /// <summary>
        /// The key repeating rate accelerates over this period of time (in seconds)
        /// </summary>
        public double KeyRepeatAccelerationDelay = 4.5;

        /// <summary>
        /// Performance stats
        /// </summary>
        public int LastPassCount;

        /// <summary>
        /// Configures the size of the rendering canvas
        /// </summary>
        public Vector2 CanvasSize;

        public RectF CanvasRect => new RectF(0, 0, CanvasSize.X, CanvasSize.Y);

        /// <summary>
        /// Control events are broadcast on this bus
        /// </summary>
        public readonly EventBus EventBus;

        internal List<UnhandledEvent> UnhandledEvents = new List<UnhandledEvent>();
        internal List<UnhandledEvent> PreviousUnhandledEvents = new List<UnhandledEvent>();

        internal List<IModal> ModalStack = new List<IModal>();

        public IModal ActiveModal =>
            (ModalStack.Count > 0)
                ? ModalStack[ModalStack.Count - 1]
                : null;

        /// <summary>
        /// The layout engine used to compute control sizes and positions
        /// </summary>
        public readonly LayoutContext Layout = new LayoutContext();

        /// <summary>
        /// Configures the appearance and size of controls
        /// </summary>
        public IDecorationProvider Decorations;
        public IAnimationProvider Animations;

        internal int FrameIndex;

        /// <summary>
        /// The top-level controls managed by the layout engine. Each one gets a separate rendering layer
        /// </summary>
        public ControlCollection Controls { get; private set; }

        private Control _Focused, _MouseCaptured, _Hovering, _KeyboardSelection;

        private ConditionalWeakTable<Control, Control> TopLevelFocusMemory = new ConditionalWeakTable<Control, Control>();

        private Vector2 MousePositionWhenKeyboardSelectionWasLastUpdated;
        public IScrollableControl DragToScrollTarget { get; private set; }
        private Vector2? DragToScrollInitialOffset;
        private Vector2 DragToScrollInitialPosition;

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

        internal void ClearKeyboardSelection () {
            SuppressAutoscrollDueToInputScroll = false;
            _KeyboardSelection = null;
            MousePositionWhenKeyboardSelectionWasLastUpdated = LastMousePosition;
        }

        internal void SetKeyboardSelection (Control control, bool forUser) {
            if (control == _KeyboardSelection)
                return;
            if (forUser)
                SuppressAutoscrollDueToInputScroll = false;
            _KeyboardSelection = control;
            MousePositionWhenKeyboardSelectionWasLastUpdated = LastMousePosition;
        }

        bool SuppressAutoscrollDueToInputScroll = false;

        /// <summary>
        /// The control most recently interacted with by the user
        /// </summary>
        public Control FixatedControl => MouseCaptured ?? KeyboardSelection ?? Hovering;

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
        /// Indicates that the context is currently being interacted with by the user
        /// </summary>
        public bool IsActive {
            get =>
                (MouseOverLoose != null) ||
                    _LastInput.AreAnyKeysHeld ||
                    (KeyboardSelection != null) ||
                    (MouseCaptured != null) ||
                    AcceleratorOverlayVisible ||
                    (ModalStack.Count > 0);
        }

        /// <summary>
        /// Indicates that input is currently in progress (a key or button is held)
        /// </summary>
        public bool IsInputInProgress {
            get =>
                _LastInput.AreAnyKeysHeld ||
                    (MouseCaptured != null) ||
                    AcceleratorOverlayVisible;
        }

        public Control TopLevelFocused { get; private set; }
        public Control TopLevelModalFocusDonor { get; private set; }

        public Control ModalFocusDonor { get; private set; }
        public Control PreviousFocused { get; private set; }
        public Control PreviousTopLevelFocused { get; private set; }

        private Control PreviousMouseDownTarget = null;

        /// <summary>
        /// This control is currently being scrolled via implicit scroll input
        /// </summary>
        public Control CurrentImplicitScrollTarget { get; private set; }

        internal readonly HashSet<Control> FocusChain = new HashSet<Control>(new ReferenceComparer<Control>());

        public RichTextConfiguration RichTextConfiguration;
        public DefaultMaterialSet Materials { get; private set; }
        private ITimeProvider TimeProvider;

        private readonly List<IInputSource> ScratchInputSources = new List<IInputSource>();
        public readonly List<IInputSource> InputSources = new List<IInputSource>();
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
        private Control LastClickTarget;
        private double LastMouseDownTime, LastClickTime;
        private int SequentialClickCount;

        private double? FirstTooltipHoverTime;
        private double LastTooltipHoverTime;

        private Tooltip CachedTooltip;
        private Control PreviousTooltipAnchor;
        private bool IsTooltipVisible;
        private int CurrentTooltipContentVersion;
        private Controls.StaticText CachedCompositionPreview;

        private UnorderedList<ScratchRenderTarget> ScratchRenderTargets = new UnorderedList<ScratchRenderTarget>();
        private readonly static Dictionary<int, DepthStencilState> StencilEraseStates = new Dictionary<int, DepthStencilState>();
        private readonly static Dictionary<int, DepthStencilState> StencilWriteStates = new Dictionary<int, DepthStencilState>();
        private readonly static Dictionary<int, DepthStencilState> StencilTestStates = new Dictionary<int, DepthStencilState>();

        internal bool IsCompositionActive = false;

        /// <summary>
        /// The surface format used for scratch compositor textures. Update this if you want to use sRGB.
        /// </summary>
        public SurfaceFormat SurfaceFormat = SurfaceFormat.Color;

        public float Now { get; private set; }
        public long NowL { get; private set; }

        [System.Diagnostics.Conditional("DEBUG")]
        internal void Log (string text) {
            if (System.Diagnostics.Debugger.IsAttached)
                System.Diagnostics.Debug.WriteLine(text);
            else
                Console.WriteLine(text);
        }

        internal DepthStencilState GetStencilRestore (int targetReferenceStencil) {
            DepthStencilState result;
            if (StencilEraseStates.TryGetValue(targetReferenceStencil, out result))
                return result;

            result = new DepthStencilState {
                StencilEnable = true,
                StencilFunction = CompareFunction.Less,
                StencilPass = StencilOperation.Replace,
                StencilFail = StencilOperation.Keep,
                ReferenceStencil = targetReferenceStencil,
                DepthBufferEnable = false
            };

            StencilEraseStates[targetReferenceStencil] = result;
            return result;
        }

        internal DepthStencilState GetStencilWrite (int previousReferenceStencil) {
            DepthStencilState result;
            if (StencilWriteStates.TryGetValue(previousReferenceStencil, out result))
                return result;

            result = new DepthStencilState {
                StencilEnable = true,
                StencilFunction = CompareFunction.Equal,
                StencilPass = StencilOperation.IncrementSaturation,
                StencilFail = StencilOperation.Keep,
                ReferenceStencil = previousReferenceStencil,
                DepthBufferEnable = false
            };

            StencilWriteStates[previousReferenceStencil] = result;
            return result;
        }

        internal DepthStencilState GetStencilTest (int referenceStencil) {
            DepthStencilState result;
            if (StencilTestStates.TryGetValue(referenceStencil, out result))
                return result;

            result = new DepthStencilState {
                StencilEnable = true,
                StencilFunction = CompareFunction.LessEqual,
                StencilPass = StencilOperation.Keep,
                StencilFail = StencilOperation.Keep,
                ReferenceStencil = referenceStencil,
                StencilWriteMask = 0,
                DepthBufferEnable = false
            };

            StencilTestStates[referenceStencil] = result;
            return result;
        }

        public UIContext (DefaultMaterialSet materials, IGlyphSource font = null, ITimeProvider timeProvider = null)
            : this (
                materials: materials,
                decorations: new DefaultDecorations(materials) {
                    DefaultFont = font
                },
                timeProvider: timeProvider
            ) {
        }

        public UIContext (
            DefaultMaterialSet materials, IDecorationProvider decorations, 
            IAnimationProvider animations = null, ITimeProvider timeProvider = null
        ) {
            EventBus = new EventBus();
            EventBus.AfterBroadcast += EventBus_AfterBroadcast;
            Controls = new ControlCollection(this);
            Decorations = decorations;
            Animations = animations ?? (decorations as IAnimationProvider);
            TimeProvider = TimeProvider ?? new DotNetTimeProvider();
            Materials = materials;
            TTS = new Accessibility.TTS(this);
            _LastInput = _CurrentInput = new InputState {
                CursorPosition = new Vector2(-99999)
            };
            _LastInput.HeldKeys = _LastHeldKeys;
            _CurrentInput.HeldKeys = _CurrentHeldKeys;
            CreateInputIDs();
        }

        public InputID GetInputID (Keys key, KeyboardModifiers modifiers) {
            foreach (var iid in InputIDs) {
                if ((iid.Key == key) && iid.Modifiers.Equals(modifiers))
                    return iid;
            }
            var result = new InputID { Key = key, Modifiers = modifiers };
            InputIDs.Add(result);
            return result;
        }

        private Vector2 LastMousePosition => _LastInput.CursorPosition;
        private MouseButtons LastMouseButtons => _LastInput.Buttons;
        private KeyboardModifiers CurrentModifiers => _CurrentInput.Modifiers;
        private MouseButtons CurrentMouseButtons => _CurrentInput.Buttons;

        private void EventBus_AfterBroadcast (EventBus sender, object eventSource, string eventName, object eventArgs, bool eventWasHandled) {
            if (eventWasHandled)
                return;

            UnhandledEvents.Add(new UnhandledEvent {
                Source = eventSource as Control,
                Name = eventName
            });
        }

        private Tooltip GetTooltipInstance () {
            if (CachedTooltip == null) {
                CachedTooltip = new Tooltip {
                    Appearance = { Opacity = 0 }
                };
                Controls.Add(CachedTooltip);
            }

            return CachedTooltip;
        }

        private Controls.StaticText GetCompositionPreviewInstance () {
            if (CachedCompositionPreview == null) {
                CachedCompositionPreview = new Controls.StaticText {
                    DisplayOrder = int.MaxValue,
                    Wrap = false,
                    Multiline = false,
                    Intangible = true,
                    LayoutFlags = ControlFlags.Layout_Floating,
                    Appearance = {
                        BackgroundColor = Color.White,
                        TextColor = Color.Black,
                        Decorator = Decorations.CompositionPreview
                    }
                };
                Controls.Add(CachedCompositionPreview);
            }

            return CachedCompositionPreview;
        }

        private void AutomaticallyTransferFocusOnTopLevelChange (Control target) {
            if (target.AcceptsFocus)
                return;

            var previousTopLevel = FindTopLevelAncestor(Focused);
            var newTopLevel = FindTopLevelAncestor(target);
            if ((newTopLevel != previousTopLevel) && (newTopLevel != null)) {
                Log($"Automatically transfering focus to new top level ancestor {newTopLevel}");
                TrySetFocus(newTopLevel, isUserInitiated: false);
            }
        }

        public bool CaptureMouse (Control target) {
            return CaptureMouse(target, out Control temp);
        }

        public bool CaptureMouse (Control target, out Control previous) {
            previous = Focused;
            if ((MouseCaptured != null) && (MouseCaptured != target))
                RetainCaptureRequested = target;
            // HACK: If we used IsValidFocusTarget here, it would break scenarios where a control is capturing
            //  focus before being shown or being enabled
            AutomaticallyTransferFocusOnTopLevelChange(target);
            if (target.AcceptsFocus)
                TrySetFocus(target, true, true);
            MouseCaptured = target;
            return (MouseCaptured == target);
        }

        public bool ReleaseFocus (Control target, bool forward) {
            if (Focused != target)
                return false;

            // FIXME
            var isUserInitiated = false;
            if (!RotateFocus(false, forward ? 1 : -1, isUserInitiated)) {
                // Forward is a best-effort request, go backward instead if necessary
                if (forward && RotateFocus(false, -1, isUserInitiated))
                    return true;
                return TrySetFocus(null);
            }

            return true;
        }

        private void RemoveFromFocusMemory (Control control) {
            foreach (var topLevel in Controls) {
                if (!TopLevelFocusMemory.TryGetValue(topLevel, out Control memory))
                    continue;
                if (control == memory)
                    TopLevelFocusMemory.Remove(topLevel);
            }
        }

        // FIXME: This operation can shift the focus out of view, should it perform auto-scroll?
        public bool ReleaseDescendantFocus (Control container, bool forward) {
            if (Focused == null)
                return false;
            if (container == null)
                return false;

            // FIXME
            var isUserInitiated = false;

            if (TopLevelFocused == container)
                return RotateFocus(true, forward ? 1 : -1, isUserInitiated);
            
            var chain = Focused;
            while (chain != null) {
                // If focus memory points to this control we're defocusing, clear it
                RemoveFromFocusMemory(chain);

                if (chain == container) {
                    if (!RotateFocusFrom(container, forward ? 1 : -1, isUserInitiated)) {
                        if (forward)
                            return RotateFocusFrom(container, -1, isUserInitiated);
                        else
                            return TrySetFocus(null, true);
                    } else
                        return true;
                }

                if (!chain.TryGetParent(out Control parent) || (parent == null))
                    return false;
                chain = parent;
            }

            return false;
        }

        public void RetainCapture (Control target) {
            RetainCaptureRequested = target;
        }

        public void ReleaseCapture (Control target, Control focusDonor) {
            if (Focused == target)
                Focused = focusDonor;
            if (Hovering == target)
                Hovering = null;
            if (MouseCaptured == target)
                MouseCaptured = null;
            ReleasedCapture = target;
            if (RetainCaptureRequested == target)
                RetainCaptureRequested = null;
        }

        UnorderedList<IPostLayoutListener> _PostLayoutListeners = new UnorderedList<IPostLayoutListener>();
        List<Control> _TopLevelControls = new List<Control>();

        private void DoUpdateLayoutInternal (UIOperationContext context, bool secondTime) {
            Layout.CanvasSize = CanvasSize;
            Layout.SetContainerFlags(Layout.Root, ControlFlags.Container_Row | ControlFlags.Container_Constrain_Size);

            _TopLevelControls.Clear();
            Controls.CopyTo(_TopLevelControls);

            foreach (var control in _TopLevelControls)
                control.GenerateLayoutTree(
                    ref context, Layout.Root, 
                    (secondTime && !control.LayoutKey.IsInvalid) 
                        ? control.LayoutKey 
                        : (ControlKey?)null
                );
        }

        private bool NotifyLayoutListeners (UIOperationContext context) {
            bool relayoutRequested = context.RelayoutRequestedForVisibilityChange;
            if (relayoutRequested && LogRelayoutRequests)
                Log($"Relayout requested due to visibility change");

            foreach (var listener in context.PostLayoutListeners) {
                var wasRequested = relayoutRequested;
                listener.OnLayoutComplete(context, ref relayoutRequested);
                if (relayoutRequested != wasRequested) {
                    var ctl = (Control)listener;
                    if (LogRelayoutRequests)
                        Log($"Relayout requested by {ctl.DebugLabel ?? listener.GetType().Name}");
                }
            }
            return relayoutRequested;
        }

        public void Update () {
            FrameIndex++;

            var context = MakeOperationContext();
            var pll = Interlocked.Exchange(ref _PostLayoutListeners, null);
            if (pll == null)
                pll = new UnorderedList<IPostLayoutListener>();
            else
                pll.Clear();
            context.PostLayoutListeners = pll;

            try {
                Layout.Clear();

                DoUpdateLayoutInternal(context, false);
                Layout.Update();

                if (NotifyLayoutListeners(context)) {
                    DoUpdateLayoutInternal(context, true);
                    Layout.Update();
                    NotifyLayoutListeners(context);
                }
            } finally {
                Interlocked.CompareExchange(ref _PostLayoutListeners, pll, null);
            }

            UpdateAutoscroll();
        }

        private void UpdateCaptureAndHovering (Vector2 mousePosition, Control exclude = null) {
            // FIXME: This breaks drag-to-scroll
            // MouseOver = HitTest(mousePosition, rejectIntangible: true);
            MouseOver = MouseOverLoose = HitTest(mousePosition, rejectIntangible: false);

            if ((MouseOver != MouseCaptured) && (MouseCaptured != null))
                Hovering = null;
            else
                Hovering = MouseOver;
        }

        public void ShowModal (IModal modal) {
            if (ModalStack.Contains(modal))
                throw new InvalidOperationException("Modal already visible");
            var ctl = (Control)modal;
            ctl.DisplayOrder = Controls.PickNewHighestDisplayOrder(ctl);
            if (!Controls.Contains(ctl))
                Controls.Add(ctl);
            NotifyModalShown(modal);
        }

        public bool CloseActiveModal () {
            if (ModalStack.Count <= 0)
                return false;
            return CloseModal(ModalStack[ModalStack.Count - 1]);
        }

        public bool CloseModal (IModal modal) {
            if (!ModalStack.Contains(modal))
                return false;
            modal.Close();
            return true;
        }

        public void NotifyModalShown (IModal modal) {
            if (ModalStack.Contains(modal))
                throw new InvalidOperationException("Modal already visible");
            var ctl = (Control)modal;
            if (!Controls.Contains(ctl))
                throw new InvalidOperationException("Modal not in top level controls list");
            TopLevelFocusMemory.Remove(ctl);
            ModalStack.Add(modal);
            TrySetFocus(ctl, false, false);
            FireEvent(UIEvents.Shown, ctl);
        }

        public void NotifyModalClosed (IModal modal) {
            if (modal == null)
                return;

            var ctl = (Control)modal;
            var newFocusTarget = (TopLevelFocused == ctl)
                ? modal.FocusDonor
                : null;
            ReleaseCapture(ctl, modal.FocusDonor);
            if (!ModalStack.Contains(modal))
                return;
            ModalStack.Remove(modal);
            FireEvent(UIEvents.Closed, ctl);
            if (newFocusTarget != null)
                TrySetFocus(newFocusTarget, false, false);
        }

        private readonly Dictionary<Control, Control> InvalidFocusTargets = 
            new Dictionary<Control, Control>(new ReferenceComparer<Control>());

        private void DefocusInvalidFocusTargets () {
            while ((Focused != null) && !Focused.IsValidFocusTarget && InvalidFocusTargets.TryGetValue(Focused, out Control idealNewTarget)) {
                InvalidFocusTargets.Remove(Focused);
                var current = Focused;
                var ok = (idealNewTarget == null) && TrySetFocus(idealNewTarget);

                if (!ok) {
                    var interim = Focused;
                    idealNewTarget = PickIdealNewFocusTargetForInvalidFocusTarget(Focused);
                    if (!TrySetFocus(idealNewTarget)) {
                        // Log($"Could not move focus from invalid target {current}");
                        break;
                    } else
                        ; // Log($"Moved focus from invalid target {current} to {Focused} through {interim}");
                } else {
                    // Log($"Moved focus from invalid target {current} to {Focused}");
                }
            }
            InvalidFocusTargets.Clear();
        }

        private Control PickIdealNewFocusTargetForInvalidFocusTarget (Control control) {
            var fm = Focused as IModal;
            Control idealNewTarget = null;
            // FIXME: TopLevelFocused fixes some behaviors here but breaks others :(
            if ((fm?.FocusDonor != null) && Control.IsEqualOrAncestor(Focused, control))
                idealNewTarget = fm.FocusDonor;

            // Attempt to auto-shift focus as long as our parent chain is focusable
            if (!Control.IsRecursivelyTransparent(control, includeSelf: false))
                idealNewTarget = PickNextFocusTarget(control, 1, true);
            else
                // Auto-shifting failed, so try to return to the most recently focused control
                idealNewTarget = PreviousFocused ?? PreviousTopLevelFocused;

            return idealNewTarget;
        }

        // Clean up when a control is removed in case it has focus or mouse capture,
        //  and attempt to return focus to the most recent place it occupied (for modals)
        public void NotifyControlBecomingInvalidFocusTarget (Control control, bool removed) {
            RemoveFromFocusMemory(control);

            if (PreviousFocused == control)
                PreviousFocused = null;
            if (PreviousTopLevelFocused == control)
                PreviousTopLevelFocused = null;
            if (Control.IsEqualOrAncestor(_MouseCaptured, control))
                MouseCaptured = null;

            InvalidFocusTargets[control] = 
                PickIdealNewFocusTargetForInvalidFocusTarget(control);

            if (Control.IsEqualOrAncestor(KeyboardSelection, control))
                ClearKeyboardSelection();

            if (PreviousFocused == control)
                PreviousFocused = null;
            if (PreviousTopLevelFocused == control)
                PreviousTopLevelFocused = null;

            if (removed)
                NotifyModalClosed(control as IModal);
        }

        public bool IsPriorityInputSource (IInputSource source) {
            if (ScratchInputSources.Count > 0)
                return ScratchInputSources.IndexOf(source) == 0;
            else
                return InputSources.IndexOf(source) == 0;
        }

        public void PromoteInputSource (IInputSource source) {
            var existingIndex = InputSources.IndexOf(source);
            if (existingIndex == 0)
                return;
            else if (existingIndex > 0)
                InputSources.RemoveAt(existingIndex);
            InputSources.Insert(0, source);
        }

        private void EnsureValidFocus () {
            DefocusInvalidFocusTargets();

            if ((Focused == null) && !AllowNullFocus)
                Focused = PickNextFocusTarget(null, 1, true);
        }

        public void UpdateInput (bool processEvents = true) {
            Now = (float)TimeProvider.Seconds;
            NowL = TimeProvider.Ticks;

            if ((_CurrentInput.CursorPosition.X < -999) ||
                (_CurrentInput.CursorPosition.Y < -999))
                _CurrentInput.CursorPosition = CanvasSize / 2f;

            _LastInput = _CurrentInput;
            _LastInput.HeldKeys = _LastHeldKeys;
            _LastHeldKeys.Clear();
            foreach (var k in _CurrentHeldKeys)
                _LastHeldKeys.Add(k);

            _CurrentHeldKeys.Clear();
            _CurrentInput = new InputState {
                HeldKeys = _CurrentHeldKeys,
                CursorPosition = _LastInput.CursorPosition,
                WheelValue = _LastInput.WheelValue
            };

            ScratchInputSources.Clear();
            foreach (var src in InputSources)
                ScratchInputSources.Add(src);

            foreach (var src in ScratchInputSources) {
                src.SetContext(this);
                src.Update(ref _LastInput, ref _CurrentInput);
            }

            ScratchInputSources.Clear();

            _CurrentInput.AreAnyKeysHeld = _CurrentInput.HeldKeys.Count > 0;

            if (!processEvents)
                return;

            var mousePosition = _CurrentInput.CursorPosition;

            PreviousUnhandledEvents.Clear();
            PreviousUnhandledEvents.AddRange(UnhandledEvents);
            UnhandledEvents.Clear();

            if (
                (Focused != null) && 
                (!Focused.IsValidFocusTarget || (FindTopLevelAncestor(Focused) == null))
            ) {
                // If the current focused control is no longer enabled or present, attempt to
                //  focus something else in the current top level control, if possible
                if (Controls.Contains(TopLevelFocused))
                    Focused = TopLevelFocused;
                else
                    NotifyControlBecomingInvalidFocusTarget(Focused, false);
            }

            EnsureValidFocus();

            var previouslyFixated = FixatedControl;

            var previouslyHovering = Hovering;

            UpdateCaptureAndHovering(_CurrentInput.CursorPosition);
            var mouseEventTarget = MouseCaptured ?? Hovering;
            var topLevelTarget = (mouseEventTarget != null)
                ? FindTopLevelAncestor(mouseEventTarget)
                : null;

            var activeModal = ActiveModal;
            var wasInputBlocked = false;
            if (
                (activeModal?.BlockInput == true) && 
                (activeModal != topLevelTarget) &&
                (topLevelTarget?.DisplayOrder < int.MaxValue)
            ) {
                mouseEventTarget = null;
                wasInputBlocked = true;
            }

            // If the mouse moves after the keyboard selection was updated, clear it
            if (KeyboardSelection != null) {
                var movedDistance = mousePosition - MousePositionWhenKeyboardSelectionWasLastUpdated;
                if (movedDistance.Length() > MinimumMouseMovementDistance) {
                    if (_CurrentInput.KeyboardNavigationEnded)
                        ClearKeyboardSelection();
                }
            }

            var mouseOverLoose = MouseOverLoose;

            if (LastMousePosition != mousePosition) {
                if (DragToScrollTarget != null)
                    HandleMouseDrag((Control)DragToScrollTarget, mousePosition);
                else if (CurrentMouseButtons != MouseButtons.None)
                    HandleMouseDrag(mouseEventTarget, mousePosition);
                else
                    HandleMouseMove(mouseEventTarget ?? mouseOverLoose, mousePosition);
            }

            var mouseDownPosition = MouseDownPosition;
            var previouslyCaptured = MouseCaptured;
            var processClick = false;

            if ((LastMouseButtons == MouseButtons.None) && (CurrentMouseButtons != MouseButtons.None)) {
                if (
                    (mouseEventTarget == null) && 
                    (mouseOverLoose != null)
                ) {
                    HandleMouseDownPrologue();
                    HandleMouseDownEpilogue(false, mouseOverLoose, mousePosition, CurrentMouseButtons);
                } else {
                    HandleMouseDown(mouseEventTarget, mousePosition, CurrentMouseButtons);
                }
                mouseDownPosition = mouseDownPosition ?? mousePosition;
            } else if ((LastMouseButtons != MouseButtons.None) && (CurrentMouseButtons == MouseButtons.None)) {
                bool scrolled = false;
                if (Hovering != null)
                    scrolled = HandleMouseUp(mouseEventTarget, mousePosition, mouseDownPosition, LastMouseButtons);
                else if (DragToScrollTarget != null)
                    scrolled = TeardownDragToScroll(mouseEventTarget, mousePosition);
                else /* if (MouseCaptured != null) */
                    scrolled = HandleMouseUp(mouseEventTarget, mousePosition, mouseDownPosition, LastMouseButtons);

                // if (MouseCaptured != null) {
                var movedDistance = mousePosition - mouseDownPosition;
                var hasMoved = movedDistance.HasValue &&
                        (movedDistance.Value.Length() >= MinimumMouseMovementDistance);
                if (
                    !hasMoved &&
                    (!scrolled || !SuppressSingleClickOnMovementWhenAppropriate)
                )
                    processClick = true;
                // }

                if (MouseCaptured != null) {
                    if (RetainCaptureRequested == MouseCaptured) {
                        RetainCaptureRequested = null;
                    } else {
                        MouseCaptured = null;
                    }
                }

                // FIXME: Clear LastMouseDownTime?
            } else if (LastMouseButtons != CurrentMouseButtons) {
                FireEvent(UIEvents.MouseButtonsChanged, mouseEventTarget, MakeMouseEventArgs(mouseEventTarget, mousePosition, mouseDownPosition));
            }

            if (processClick && !wasInputBlocked) {
                // FIXME: if a menu is opened by a mousedown event, this will
                //  fire a click on the menu in response to its mouseup
                if (
                    (Hovering == previouslyCaptured) ||
                    ((previouslyCaptured == null) && (Hovering == PreviousMouseDownTarget))
                ) {
                    if ((LastMouseButtons & MouseButtons.Left) == MouseButtons.Left)
                        // FIXME: Is this ?? right
                        HandleClick(previouslyCaptured ?? PreviousMouseDownTarget, mousePosition, mouseDownPosition ?? mousePosition);
                    else
                        ; // FIXME: Fire another event here?
                } else
                    HandleDrag(previouslyCaptured, Hovering);
            }

            var mouseWheelDelta = _CurrentInput.WheelValue - _LastInput.WheelValue;

            if (mouseWheelDelta != 0)
                HandleScroll(previouslyCaptured ?? MouseOverLoose, mouseWheelDelta);

            TickControl(KeyboardSelection, mousePosition, mouseDownPosition);
            if (Hovering != KeyboardSelection)
                TickControl(Hovering, mousePosition, mouseDownPosition);
            if ((MouseCaptured != KeyboardSelection) && (MouseCaptured != Hovering))
                TickControl(MouseCaptured, mousePosition, mouseDownPosition);

            UpdateTooltip((CurrentMouseButtons != MouseButtons.None));

            if (CurrentInputState.ScrollDistance.Length() >= 0.5f) {
                var implicitScrollTarget = CurrentImplicitScrollTarget;
                if ((implicitScrollTarget == null) || Control.IsRecursivelyTransparent(implicitScrollTarget, true))
                    implicitScrollTarget = KeyboardSelection ?? Hovering ?? MouseOverLoose ?? Focused;

                if (implicitScrollTarget != null) {
                    if (AttemptTargetedScroll(implicitScrollTarget, CurrentInputState.ScrollDistance, recursive: false))
                        CurrentImplicitScrollTarget = implicitScrollTarget;
                }
            } else {
                CurrentImplicitScrollTarget = null;
            }

            EnsureValidFocus();

            if (FixatedControl != previouslyFixated)
                HandleFixationChange(previouslyFixated, FixatedControl);
        }

        private void TickControl (Control control, Vector2 globalPosition, Vector2? mouseDownPosition) {
            if (control == null)
                return;
            control.Tick(MakeMouseEventArgs(control, globalPosition, mouseDownPosition));
        }

        private bool IsTooltipActive {
            get {
                return (CachedTooltip != null) && !CachedTooltip.IsTransparent;
            }
        }

        private void ResetTooltipShowTimer () {
            FirstTooltipHoverTime = null;
        }

        private bool IsTooltipAllowedToAppear (bool leftButtonPressed) {
            var target = FixatedControl;
            var cttt = target as ICustomTooltipTarget;
            if (cttt == null)
                return !leftButtonPressed;

            var result = (leftButtonPressed
                ? cttt.ShowTooltipWhileMouseIsHeld
                : cttt.ShowTooltipWhileMouseIsNotHeld);
            if (FixatedControl == KeyboardSelection)
                result |= cttt.ShowTooltipWhileKeyboardFocus;
            return result;
        }

        private void UpdateTooltip (bool leftButtonPressed) {
            var target = FixatedControl;
            if (!IsTooltipAllowedToAppear(leftButtonPressed))
                return;

            var cttt = target as ICustomTooltipTarget;

            var now = Now;
            var tooltipContent = default(AbstractString);
            if (target != null) {
                if (cttt != null)
                    tooltipContent = cttt.GetContent().Get(target);
                else
                    tooltipContent = target.TooltipContent.Get(target);
            }

            var disappearDelay = (cttt?.TooltipDisappearDelay ?? TooltipDisappearDelay);

            if (!tooltipContent.IsNull) {
                if (!FirstTooltipHoverTime.HasValue)
                    FirstTooltipHoverTime = now;

                if (IsTooltipActive)
                    LastTooltipHoverTime = now;

                var hoveringFor = now - FirstTooltipHoverTime;
                var disappearTimeout = now - LastTooltipHoverTime;
                var version = target.TooltipContentVersion + target.TooltipContent.Version;

                if (
                    (hoveringFor >= (cttt?.TooltipAppearanceDelay ?? TooltipAppearanceDelay)) || 
                    (disappearTimeout < disappearDelay)
                ) {
                    ShowTooltip(target, tooltipContent, CurrentTooltipContentVersion != version);
                    CurrentTooltipContentVersion = version;
                }
            } else {
                var shouldDismissInstantly = (target != null) && IsTooltipActive && 
                    GetTooltipInstance().GetRect(context: this).Contains(LastMousePosition);
                // TODO: Instead of instantly hiding, maybe just fade the tooltip out partially?
                HideTooltip(shouldDismissInstantly);

                var elapsed = now - LastTooltipHoverTime;
                if (elapsed >= disappearDelay)
                    ResetTooltipShowTimer();
            }
        }

        /// <summary>
        /// Updates key/click repeat state for the current timestamp and returns true if a click should be generated
        /// </summary>
        public bool UpdateRepeat (double now, double firstTime, ref double mostRecentTime, double speedMultiplier = 1, double accelerationMultiplier = 1) {
            // HACK: Handle cases where mostRecentTime has not been initialized by the initial press
            if (mostRecentTime < firstTime)
                mostRecentTime = firstTime;

            double repeatSpeed = Arithmetic.Lerp(KeyRepeatIntervalSlow, KeyRepeatIntervalFast, (float)((now - firstTime) / KeyRepeatAccelerationDelay * accelerationMultiplier)) / speedMultiplier;
            if (
                ((now - firstTime) >= FirstKeyRepeatDelay) &&
                ((now - mostRecentTime) >= repeatSpeed)
            ) {
                mostRecentTime = now;
                return true;
            }

            return false;
        }

        private void HideTooltipForMouseInput (bool isMouseDown) {
            var cttt = FixatedControl as ICustomTooltipTarget;
            if (cttt != null) {
                if (!cttt.HideTooltipOnMousePress)
                    return;
            }

            ResetTooltipShowTimer();
            HideTooltip(true);
            FirstTooltipHoverTime = null;
            LastTooltipHoverTime = 0;
        }

        private void HideTooltip (bool instant) {
            if (CachedTooltip == null)
                return;

            if (instant)
                CachedTooltip.Appearance.Opacity = 0;
            else if (IsTooltipVisible)
                CachedTooltip.Appearance.Opacity = Tween.StartNow(
                    CachedTooltip.Appearance.Opacity.Get(Now), 0, now: NowL, 
                    seconds: TooltipFadeDuration * (Animations?.AnimationDurationMultiplier ?? 1)
                );
            IsTooltipVisible = false;
        }

        // HACK: Suppress the 'if not Visible then don't perform layout' behavior
        internal bool IsUpdatingSubtreeLayout;

        /// <summary>
        /// Use at your own risk! Performs immediate layout of a control and its children.
        /// The results of this are not necessarily accurate, but can be used to infer its ideal size for positioning.
        /// </summary>
        public void UpdateSubtreeLayout (Control subtreeRoot) {
            var tempCtx = MakeOperationContext();

            var pll = Interlocked.Exchange(ref _PostLayoutListeners, null);
            if (pll == null)
                pll = new UnorderedList<IPostLayoutListener>();
            else
                pll.Clear();
            tempCtx.PostLayoutListeners = pll;

            var wasUpdatingSubtreeLayout = IsUpdatingSubtreeLayout;
            try {
                IsUpdatingSubtreeLayout = true;
                UpdateSubtreeLayout(tempCtx, subtreeRoot);

                if (NotifyLayoutListeners(tempCtx)) {
                    DoUpdateLayoutInternal(tempCtx, true);
                    UpdateSubtreeLayout(tempCtx, subtreeRoot);
                    NotifyLayoutListeners(tempCtx);
                }
            } finally {
                IsUpdatingSubtreeLayout = wasUpdatingSubtreeLayout;
                Interlocked.CompareExchange(ref _PostLayoutListeners, pll, null);
            }
        }

        private void UpdateSubtreeLayout (UIOperationContext context, Control subtreeRoot) {
            ControlKey parentKey;
            Control parent;
            if (!subtreeRoot.TryGetParent(out parent))
                parentKey = Layout.Root;
            else if (!parent.LayoutKey.IsInvalid)
                parentKey = parent.LayoutKey;
            else {
                // Just in case for some reason the control's parent also hasn't had layout happen...
                UpdateSubtreeLayout(context, parent);
                return;
            }

            subtreeRoot.GenerateLayoutTree(
                ref context, parentKey, 
                subtreeRoot.LayoutKey.IsInvalid 
                    ? (ControlKey?)null 
                    : subtreeRoot.LayoutKey
            );
            Layout.UpdateSubtree(subtreeRoot.LayoutKey);
        }

        private void ShowTooltip (Control anchor, AbstractString text, bool textIsInvalidated) {
            var instance = GetTooltipInstance();

            var textChanged = !instance.Text.TextEquals(text, StringComparison.Ordinal) || 
                textIsInvalidated;

            var rect = anchor.GetRect(context: this);
            // HACK: Clip the anchor's rect to its parent's rect to ensure that
            //  in the event that a container is scrolling, the tooltip doesn't shift outside
            //  of the container too far
            if (anchor.TryGetParent(out Control anchorParent)) {
                var parentRect = anchorParent.GetRect(context: this, contentRect: true);

                // If the anchor is entirely invisible, hide the tooltip to prevent visible glitches
                if (!rect.Intersection(ref parentRect, out rect)) {
                    instance.Visible = false;
                    return;
                }
            }

            instance.Visible = true;
            instance.DisplayOrder = int.MaxValue;

            if (textChanged || !IsTooltipVisible) {
                var idealMaxWidth = CanvasSize.X * 0.35f;

                instance.Text = text;
                // FIXME: Shift it around if it's already too close to the right side
                instance.Width.Maximum = idealMaxWidth;
                instance.Invalidate();

                UpdateSubtreeLayout(instance);
            }

            var instanceBox = instance.GetRect();
            var newX = rect.Left + (rect.Width / 2f) - (instanceBox.Width / 2f);
            /*
            // We want to make sure the tooltip is at least roughly aligned with the mouse horizontally
            // FIXME: This doesn't work because we're performing this position update every frame while a tooltip is open, oops
            if ((newX > LastMousePosition.X) || (newX + instanceBox.Width) < LastMousePosition.X)
                newX = LastMousePosition.X;
            */

            newX = Arithmetic.Clamp(newX, TooltipSpacing, CanvasSize.X - instanceBox.Width - (TooltipSpacing * 2));
            var newY = rect.Extent.Y + TooltipSpacing;
            if ((instanceBox.Height + rect.Extent.Y) >= CanvasSize.Y)
                newY = rect.Top - instanceBox.Height - TooltipSpacing;
            instance.Margins = new Margins(newX, newY, 0, 0);

            var currentOpacity = instance.Appearance.Opacity.Get(Now);
            if (!IsTooltipVisible)
                instance.Appearance.Opacity = Tween.StartNow(
                    currentOpacity, 1f, 
                    seconds: (currentOpacity > 0.1 ? TooltipFadeDurationFast : TooltipFadeDuration) * (Animations?.AnimationDurationMultiplier ?? 1), 
                    now: NowL
                );
            if ((anchor != PreviousTooltipAnchor) && (currentOpacity > 0))
                instance.Appearance.Opacity = 1f;

            PreviousTooltipAnchor = anchor;
            IsTooltipVisible = true;
            UpdateSubtreeLayout(instance);
        }

        // Position is relative to the top-left corner of the canvas
        public Control HitTest (Vector2 position, bool acceptsMouseInputOnly = false, bool acceptsFocusOnly = false, bool rejectIntangible = false) {
            var areHitTestsBlocked = false;
            foreach (var m in ModalStack)
                if (m.BlockHitTests)
                    areHitTestsBlocked = true;

            var sorted = Controls.InDisplayOrder(FrameIndex);
            for (var i = sorted.Count - 1; i >= 0; i--) {
                var control = sorted[i];
                if (
                    areHitTestsBlocked && 
                    !ModalStack.Contains(control as IModal) &&
                    // HACK to allow floating controls over the modal stack
                    (control.DisplayOrder != int.MaxValue)
                )
                    continue;
                var result = control.HitTest(position, acceptsMouseInputOnly, acceptsFocusOnly, rejectIntangible);
                if (result != null)
                    return result;
            }

            return null;
        }

        internal UIOperationContext MakeOperationContext () {
            return new UIOperationContext {
                UIContext = this,
                Opacity = 1,
                Now = Now,
                NowL = NowL,
                Modifiers = CurrentModifiers,
                ActivateKeyHeld = _LastInput.ActivateKeyHeld,
                MouseButtonHeld = (LastMouseButtons != MouseButtons.None),
                MousePosition = LastMousePosition,
                VisibleRegion = new RectF(-VisibilityPadding, -VisibilityPadding, CanvasSize.X + (VisibilityPadding * 2), CanvasSize.Y + (VisibilityPadding * 2))
            };
        }

        internal ScratchRenderTarget GetScratchRenderTarget (BatchGroup prepass, ref RectF rectangle) {
            ScratchRenderTarget result = null;

            foreach (var rt in ScratchRenderTargets) {
                if (rt.IsSpaceAvailable(ref rectangle)) {
                    result = rt;
                    break;
                }
            }

            if (result == null) {
                result = new ScratchRenderTarget(prepass.Coordinator, this);
                ScratchRenderTargets.Add(result);
            }

            if (result.UsedRectangles.Count == 0) {
                var group = BatchGroup.ForRenderTarget(prepass, 0, result.Instance, name: "Scratch Prepass");
                result.Renderer = new ImperativeRenderer(group, Materials);
                result.Renderer.DepthStencilState = DepthStencilState.None;
                result.Renderer.BlendState = BlendState.AlphaBlend;
                result.Renderer.Clear(-9999, color: Color.Transparent /* FrameColors[FrameIndex % FrameColors.Length] * 0.5f */, stencil: 0);
            }

            result.UsedRectangles.Add(ref rectangle);
            return result;
        }

        internal void ReleaseScratchRenderTarget (AutoRenderTarget rt) {
            // FIXME: Do we need to do anything here?
        }

        private bool WasBackgroundFaded = false;
        private Tween<float> BackgroundFadeTween = new Tween<float>(0f);
        private UnorderedList<BitmapDrawCall> OverlayQueue = new UnorderedList<BitmapDrawCall>();

        /*
        private Color[] FrameColors = new[] {
            Color.Red,
            Color.Green,
            Color.Blue,
            Color.Yellow
        };
        */

        private void FlushOverlayQueue (ref ImperativeRenderer renderer) {
            foreach (var dc in OverlayQueue) {
                renderer.Draw(dc);
                renderer.Layer += 1;
            }

            OverlayQueue.Clear();
        }

        public void Rasterize (Frame frame, AutoRenderTarget renderTarget, int layer) {
            FrameIndex++;

            Now = (float)TimeProvider.Seconds;
            NowL = TimeProvider.Ticks;

            var context = MakeOperationContext();

            foreach (var srt in ScratchRenderTargets) {
                srt.Update();
                srt.Reset();
            }

            var seq = Controls.InDisplayOrder(FrameIndex);

            var activeModal = ActiveModal;
            int fadeBackgroundAtIndex = -1;
            for (int i = 0; i < ModalStack.Count; i++) {
                var modal = ModalStack[i];
                if (modal.FadeBackground) {
                    if (!WasBackgroundFaded) {
                        BackgroundFadeTween = Tween.StartNow(
                            BackgroundFadeTween.Get(NowL), 1f,
                            seconds: BackgroundFadeDuration * (Animations?.AnimationDurationMultiplier ?? 1), now: NowL
                        );
                    }

                    fadeBackgroundAtIndex = seq.IndexOf((Control)modal);
                    WasBackgroundFaded = true;
                }
            }

            if (fadeBackgroundAtIndex < 0 && WasBackgroundFaded) {
                BackgroundFadeTween = new Tween<float>(0f);
                WasBackgroundFaded = false;
            }

            var topLevelHovering = FindTopLevelAncestor(Hovering);

            OverlayQueue.Clear();

            using (var outerGroup = BatchGroup.New(frame, layer, name: "Rasterize UI"))
            using (var prepassGroup = BatchGroup.New(outerGroup, -999, name: "Prepass"))
            using (var rtBatch = BatchGroup.ForRenderTarget(outerGroup, 1, renderTarget, name: "Final Pass")) {
                context.Prepass = prepassGroup;
                var renderer = new ImperativeRenderer(rtBatch, Materials) {
                    BlendState = BlendState.AlphaBlend,
                    DepthStencilState = DepthStencilState.None
                };
                renderer.Clear(color: Color.Transparent, stencil: 0, layer: -999);

                var topLevelFocusIndex = seq.IndexOf(TopLevelFocused);
                for (int i = 0; i < seq.Count; i++) {
                    var control = seq[i];
                    if (i == fadeBackgroundAtIndex) {
                        var opacity = BackgroundFadeTween.Get(NowL) * BackgroundFadeOpacity;
                        renderer.FillRectangle(
                            Game.Bounds.FromPositionAndSize(Vector2.One * -9999, Vector2.One * 99999), 
                            Color.White * opacity, blendState: RenderStates.SubtractiveBlend
                        );
                        renderer.Layer += 1;
                    }

                    var m = control as IModal;
                    if ((m != null) && ModalStack.Contains(m))
                        FlushOverlayQueue(ref renderer);

                    // When the accelerator overlay is visible, fade out any top-level controls
                    //  that cover the currently focused top-level control so that the user can see
                    //  any controls that might be active
                    var fadeForKeyboardFocusVisibility = AcceleratorOverlayVisible ||
                    // HACK: Also do this if gamepad input is active so that it's easier to tell what's going on
                    //  when the dpad is used to move focus around
                        ((InputSources[0] is GamepadVirtualKeyboardAndCursor) && (KeyboardSelection != null));

                    var opacityModifier = (fadeForKeyboardFocusVisibility && (topLevelFocusIndex >= 0))
                        ? (
                            (i == topLevelFocusIndex) || (i < topLevelFocusIndex)
                                ? 1.0f
                                // Mousing over an inactive control that's being faded will make it more opaque
                                //  so that you can see what it is
                                : (
                                    (topLevelHovering == control)
                                        // FIXME: oh my god
                                        // HACK: When the accelerator overlay is visible we want to make any top-level control
                                        //  that the mouse is currently over more opaque, so you can see what you're about to
                                        //  focus by clicking on it
                                        // If it's not visible and we're using a virtual cursor, we want to make top-level controls
                                        //  that are currently covering the keyboard selection *less visible* since the user is
                                        //  currently interacting with something underneath it
                                        // he;lp
                                        ? (AcceleratorOverlayVisible ? 0.9f : 0.33f)
                                        : (AcceleratorOverlayVisible ? 0.65f : 0.95f)
                                )
                        )
                        : 1.0f;
                    // HACK: Each top-level control is its own group of passes. This ensures that they cleanly
                    //  overlap each other, at the cost of more draw calls.
                    var passSet = new RasterizePassSet(ref renderer, 0, OverlayQueue);
                    passSet.Below.DepthStencilState =
                        passSet.Content.DepthStencilState =
                        passSet.Above.DepthStencilState = DepthStencilState.None;
                    control.Rasterize(ref context, ref passSet, opacityModifier);
                }

                FlushOverlayQueue(ref renderer);

                LastPassCount = prepassGroup.Count + 1;

                if (AcceleratorOverlayVisible) {
                    renderer.Layer += 1;
                    RasterizeAcceleratorOverlay(context, ref renderer);
                }

                {
                    var subRenderer = renderer.MakeSubgroup();
                    subRenderer.BlendState = BlendState.NonPremultiplied;
                    // HACK
                    context.Pass = RasterizePasses.Below;
                    foreach (var isrc in InputSources) {
                        isrc.SetContext(this);
                        isrc.Rasterize(context, ref subRenderer);
                    }
                    subRenderer.Layer += 1;
                    context.Pass = RasterizePasses.Content;
                    foreach (var isrc in InputSources)
                        isrc.Rasterize(context, ref subRenderer);
                    subRenderer.Layer += 1;
                    context.Pass = RasterizePasses.Above;
                    foreach (var isrc in InputSources)
                        isrc.Rasterize(context, ref subRenderer);
                }
            }

            // Now that we have a dependency graph for the scratch targets, use it to
            //  reorder their batches so that the dependencies come first
            {
                TopoSortTable.Clear();

                foreach (var srt in ScratchRenderTargets)
                    PushRecursive(srt);

                int i = -9999;
                foreach (var item in TopoSortTable) {
                    ((Batch)item.Renderer.Container).Layer = i++;
                }
            }
        }

        private void PushRecursive (ScratchRenderTarget srt) {
            if (srt.VisitedByTopoSort)
                return;

            srt.VisitedByTopoSort = true;
            foreach (var dep in srt.Dependencies)
                PushRecursive(dep);

            TopoSortTable.Add(srt);
        }

        private List<ScratchRenderTarget> TopoSortTable = new List<ScratchRenderTarget>();

        public void Dispose () {
            Layout.Dispose();

            foreach (var rt in ScratchRenderTargets)
                rt.Dispose();

            ScratchRenderTargets.Clear();
        }
    }

    public struct RasterizePassSet {
        public ImperativeRenderer Below, Content, Above;
        public UnorderedList<BitmapDrawCall> OverlayQueue;
        public int StackDepth;

        public RasterizePassSet (ref ImperativeRenderer container, int stackDepth, UnorderedList<BitmapDrawCall> overlayQueue) {
            // FIXME: Order them?
            Below = container.MakeSubgroup(name: "Below");
            Content = container.MakeSubgroup(name: "Content");
            Above = container.MakeSubgroup(name: "Above");
            StackDepth = stackDepth;
            OverlayQueue = overlayQueue;
        }

        public RasterizePassSet (ref ImperativeRenderer container, int stackDepth, UnorderedList<BitmapDrawCall> overlayQueue, ref int layer) {
            Below = container.MakeSubgroup(name: "Below", layer: layer);
            Content = container.MakeSubgroup(name: "Content", layer: layer + 1);
            Above = container.MakeSubgroup(name: "Above", layer: layer + 2);
            StackDepth = stackDepth;
            OverlayQueue = overlayQueue;
            layer = layer + 3;
        }
    }

    public struct UIOperationContext {
        public UIContext UIContext;
        public DefaultMaterialSet Materials => UIContext?.Materials;
        public LayoutContext Layout => UIContext?.Layout;
        public RasterizePasses Pass;
        public float Opacity { get; internal set; }
        public float Now { get; internal set; }
        public long NowL { get; internal set; }
        public KeyboardModifiers Modifiers { get; internal set; }
        public bool ActivateKeyHeld { get; internal set; }
        public bool MouseButtonHeld { get; internal set; }
        public Vector2 MousePosition { get; internal set; }
        public RectF VisibleRegion { get; internal set; }
        public BatchGroup Prepass;
        private DenseList<IDecorator> DecoratorStack, TextDecoratorStack;
        private DenseList<IDecorationProvider> DecorationProviderStack;
        internal DenseList<UIContext.ScratchRenderTarget> RenderTargetStack;
        internal UnorderedList<IPostLayoutListener> PostLayoutListeners;
        internal bool RelayoutRequestedForVisibilityChange;
        internal int HiddenCount;
        internal int Depth;

        private T GetStackTop<T> (ref DenseList<T> stack) {
            if (stack.Count <= 0)
                return default(T);
            stack.GetItem(stack.Count - 1, out T result);
            return result;
        }

        private static void StackPush<T> (ref DenseList<T> stack, T value) {
            stack.Add(value);
        }

        private static void StackPop<T> (ref DenseList<T> stack) {
            if (stack.Count <= 0)
                throw new InvalidOperationException("Stack empty");
            stack.RemoveAt(stack.Count - 1);
        }

        public IDecorationProvider DecorationProvider => GetStackTop(ref DecorationProviderStack) ?? UIContext?.Decorations;
        public static void PushDecorationProvider (ref UIOperationContext context, IDecorationProvider value) => 
            StackPush(ref context.DecorationProviderStack, value);
        public static void PopDecorationProvider (ref UIOperationContext context) => 
            StackPop(ref context.DecorationProviderStack);
        public IDecorator DefaultDecorator => GetStackTop(ref DecoratorStack);
        public static void PushDecorator (ref UIOperationContext context, IDecorator value) => 
            StackPush(ref context.DecoratorStack, value);
        public static void PopDecorator (ref UIOperationContext context) => 
            StackPop(ref context.DecoratorStack);
        public IDecorator DefaultTextDecorator => GetStackTop(ref TextDecoratorStack);
        public static void PushTextDecorator (ref UIOperationContext context, IDecorator value) => 
            StackPush(ref context.TextDecoratorStack, value);
        public static void PopTextDecorator (ref UIOperationContext context) => 
            StackPop(ref context.TextDecoratorStack);

        public void Clone (out UIOperationContext result) {
            result = new UIOperationContext {
                UIContext = UIContext,
                Pass = Pass,
                Now = Now,
                NowL = NowL,
                Modifiers = Modifiers,
                ActivateKeyHeld = ActivateKeyHeld,
                MouseButtonHeld = MouseButtonHeld,
                MousePosition = MousePosition,
                VisibleRegion = VisibleRegion,
                RelayoutRequestedForVisibilityChange = RelayoutRequestedForVisibilityChange,
                Depth = Depth + 1,
                HiddenCount = HiddenCount,
                Opacity = Opacity,
                Prepass = Prepass,
            };
            RenderTargetStack.Clone(out result.RenderTargetStack);
            DecoratorStack.Clone(out result.DecoratorStack);
            TextDecoratorStack.Clone(out result.TextDecoratorStack);
            DecorationProviderStack.Clone(out result.DecorationProviderStack);
        }
    }
}
