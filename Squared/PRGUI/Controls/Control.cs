using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Game;
using Squared.PRGUI.Decorations;
using Squared.PRGUI.Flags;
using Squared.PRGUI.Layout;
using Squared.Render;
using Squared.Render.Convenience;
using Squared.Render.RasterShape;
using Squared.Render.Text;
using Squared.Threading;
using Squared.Util;
using Squared.Util.Event;

namespace Squared.PRGUI {
    public interface IHasDescription {
        string Description { get; set; }
    }
    
    public abstract partial class Control {
        [Flags]
        protected enum InternalStateFlags : ushort {
            Visible                     = 0b1,
            VisibleHasChanged           = 0b10,
            Enabled                     = 0b100,
            Intangible                  = 0b1000,
            AcceptsFocus                = 0b10000,
            AcceptsMouseInput           = 0b100000,
            AcceptsTextInput            = 0b1000000,
            LayoutInvalid               = 0b10000000,
            EventFiredBackgroundColor   = 0b100000000,
            EventFiredOpacity           = 0b1000000000,
            EventFiredTextColor         = 0b10000000000,
            // TODO
            // EventFiredMatrix      = 0b100000000000,
        }

        private class PendingAnimationRecord {
            public IControlAnimation Animation;
            public float? Duration;
            public long? Now;
        }

        public static readonly Controls.NullControl None = new Controls.NullControl();

        public string DebugLabel = null;
        protected int ControlIndex { get; private set; }
        internal int TypeID;

        public Accessibility.IReadingTarget DelegatedReadingTarget;
        public IControlEventFilter EventFilter;

        public ControlAppearance Appearance;
        public Margins Margins, Padding;
        public ControlFlags LayoutFlags = ControlFlags.Layout_Fill_Row;
        /// <summary>
        /// Overrides LayoutFlags
        /// </summary>
        public LayoutFlags Layout;
        public ControlDimension Width, Height;

        public Controls.ControlDataCollection Data;

        // Accumulates scroll offset(s) from parent controls
        private Vector2 _AbsoluteDisplayOffset;

        private Control _FocusBeneficiary;

        // FIXME
        private InternalStateFlags InternalState = InternalStateFlags.Visible | InternalStateFlags.Enabled;

        protected bool IsLayoutInvalid {
            get => GetInternalFlag(InternalStateFlags.LayoutInvalid) || _LayoutKey.IsInvalid;
            set {
                SetInternalFlag(InternalStateFlags.LayoutInvalid, false);
            }
        }

        private ControlKey _LayoutKey = ControlKey.Invalid;
        public ControlKey LayoutKey {
            get => _LayoutKey;
            private set {
                if (value == _LayoutKey)
                    return;
                _LayoutKey = value;
            }
        }

        public bool IsTransparent {
            get {
                if (!Visible)
                    return true;

                // FIXME: This causes problems when setting up focus for a control that is going to animate into visibility
                /*
                var ctx = Context;
                if ((ctx != null) && (GetOpacity(ctx.NowL) <= 0))
                    return true;
                */

                return false;
            }
        }

        /// <summary>
        /// If false, the control will not participate in layout or rasterization
        /// </summary>
        public bool Visible {
            get => GetInternalFlag(InternalStateFlags.Visible);
            set {
                if (!ChangeInternalFlag(InternalStateFlags.Visible, value))
                    return;
                SetInternalFlag(InternalStateFlags.VisibleHasChanged, true);
                OnVisibleChange(value);
            }
        }
        /// <summary>
        /// If false, the control cannot receive focus or input
        /// </summary>
        public bool Enabled {
            get => GetInternalFlag(InternalStateFlags.Enabled);
            set {
                if (ChangeInternalFlag(InternalStateFlags.Enabled, value) && (value == false))
                    Context?.NotifyControlBecomingInvalidFocusTarget(this, false);
                OnEnabledChange(value);
            }
        }

        /// <summary>
        /// If true, a temporary compositing surface will be used to rasterize this control.
        /// </summary>
        /// <param name="hasOpacity">The control's opacity is not 1.0</param>
        /// <param name="hasTransform">The control has a transform matrix</param>
        protected virtual bool NeedsComposition (bool hasOpacity, bool hasTransform) {            
            return hasOpacity || hasTransform || (Appearance.CompositeMaterial != null) || Appearance.Overlay;
        }

        /// <summary>
        /// Can receive focus even when disabled
        /// </summary>
        public virtual bool AcceptsFocusWhenDisabled => false;
        /// <summary>
        /// Can receive focus via user input
        /// </summary>
        public virtual bool AcceptsFocus {
            get => GetInternalFlag(InternalStateFlags.AcceptsFocus);
            protected set => SetInternalFlag(InternalStateFlags.AcceptsFocus, value);
        }
        /// <summary>
        /// Receives mouse events and can capture the mouse
        /// </summary>
        public virtual bool AcceptsMouseInput {
            get => GetInternalFlag(InternalStateFlags.AcceptsMouseInput);
            protected set => SetInternalFlag(InternalStateFlags.AcceptsMouseInput, value);
        }
        /// <summary>
        /// Controls whether textual input (IME composition, on-screen keyboard, etc) should 
        ///  be enabled while this control is focused. You will still get key events even if 
        ///  this is false, so things like arrow key navigation will work.
        /// </summary>
        public virtual bool AcceptsTextInput {
            get => GetInternalFlag(InternalStateFlags.AcceptsTextInput);
            protected set => SetInternalFlag(InternalStateFlags.AcceptsTextInput, value);
        }
        /// <summary>
        /// Intangible controls are ignored by hit-tests
        /// </summary>
        public bool Intangible {
            get => GetInternalFlag(InternalStateFlags.Intangible);
            set {
                if (ChangeInternalFlag(InternalStateFlags.Intangible, value))
                    OnIntangibleChange(value);
            }
        }

        /// <summary>
        /// Any input events that would deliver focus to this control will instead deliver focus
        ///  to its beneficiary, if set
        /// </summary>
        public Control FocusBeneficiary {
            get => _FocusBeneficiary;
            set {
                if (value != null) {
                    if ((value == this) || (value.FocusBeneficiary == this))
                        throw new ArgumentException("Focus beneficiary must not establish a loop");
                }
                _FocusBeneficiary = value;
            }
        }

        public bool HasParent => (WeakParent != null) && WeakParent.TryGetTarget(out Control temp);

        internal bool IsValidFocusTarget => 
            (
                AcceptsFocus || (FocusBeneficiary != null)
            ) && (Enabled || AcceptsFocusWhenDisabled) && !Control.IsRecursivelyTransparent(this);

        internal bool IsValidMouseInputTarget =>
            AcceptsMouseInput && Visible && !IsTransparent && Enabled;

        // HACK
        internal bool EligibleForFocusRotation => IsValidFocusTarget && (FocusBeneficiary == null);
        internal bool IsFocusProxy => (this is Controls.FocusProxy);

        /// <summary>
        /// Shifts the control forward or backward in the natural tab order. Lower orders come first.
        /// </summary>
        public int TabOrder { get; set; } = 0;
        /// <summary>
        /// Shifts the control forward or backward in the natural painting and hit test orders. Lower orders come first.
        /// </summary>
        public int DisplayOrder { get; set; } = 0;

        public AbstractTooltipContent TooltipContent = default(AbstractTooltipContent);
        internal int TooltipContentVersion = 0;

        protected virtual bool CreateNestedContextForChildren => true;
        protected virtual bool HasPreRasterizeHandler => (ActiveAnimation != null);
        protected virtual bool HasChildren => false;
        protected virtual bool ShouldClipContent => false;

        protected WeakReference<Control> WeakParent = null;

        public Future<bool> ActiveAnimationFuture { get; private set; }
        protected IControlAnimation ActiveAnimation { get; private set; }
        protected long ActiveAnimationEndWhen;
        private PendingAnimationRecord PendingAnimation;

        private static int NextControlIndex = 1;

        public Control () {
            ControlIndex = System.Threading.Interlocked.Increment(ref NextControlIndex);
            TypeID = GetType().GetHashCode();
        }

        private bool GetInternalFlag (InternalStateFlags flag) {
            return (InternalState & flag) == flag;
        }

        private bool? GetInternalFlag (InternalStateFlags isSetFlag, InternalStateFlags valueFlag) {
            if ((InternalState & isSetFlag) != isSetFlag)
                return null;
            else
                return (InternalState & valueFlag) == valueFlag;
        }

        private void SetInternalFlag (InternalStateFlags flag, bool state) {
            if (state)
                InternalState |= flag;
            else
                InternalState &= ~flag;
        }

        private bool ChangeInternalFlag (InternalStateFlags flag, bool newState) {
            if (GetInternalFlag(flag) == newState)
                return false;

            SetInternalFlag(flag, newState);
            return true;
        }

        /// <summary>
        /// If a control is being composited from a temporary surface, this method will be
        ///  called to select a material and blendstate to use when compositing the
        ///  temporary surface. This allows you to ensure premultiplication is handled.
        /// </summary>
        protected virtual void GetMaterialAndBlendStateForCompositing (out Material material, out BlendState blendState) {
            material = null;
            blendState = null;
        }

        private void UpdateAnimation (long now) {
            if (ActiveAnimation == null)
                return;
            if (now < ActiveAnimationEndWhen)
                return;
            ActiveAnimation.End(this, false);
            ActiveAnimation = null;
            if ((ActiveAnimationFuture != null) && !ActiveAnimationFuture.Completed)
                ActiveAnimationFuture.Complete(false);
            ActiveAnimationFuture = null;
        }

        public void CancelActiveAnimation (long? now = null) {
            if (Context == null)
                return;
            if (ActiveAnimation == null)
                return;
            var _now = now ?? Context.NowL;
            UpdateAnimation(_now);
            ActiveAnimation?.End(this, true);
            ActiveAnimation = null;
            if ((ActiveAnimationFuture != null) && !ActiveAnimationFuture.Completed)
                ActiveAnimationFuture.Complete(true);
        }

        /// <summary>
        /// Starts an animation on the control. Any animation currently playing will be cancelled.
        /// </summary>
        /// <param name="animation">An animation to apply. If null, this method will return after canceling any existing animation.</param>
        /// <param name="duration">A custom duration for the animation. If unset, the animation's default length will be used.</param>
        /// <returns>A custom completion future for the animation. When the animation finishes this future will be completed (with true if cancelled).</returns>
        public Future<bool> StartAnimation (IControlAnimation animation, float? duration = null, long? now = null) {
            CancelActiveAnimation(now);
            if (animation == null)
                return new Future<bool>(false);

            ActiveAnimationFuture = new Future<bool>();
            if (Context == null) {
                PendingAnimation = new PendingAnimationRecord {
                    Animation = animation,
                    Duration = duration,
                    Now = now
                };
            } else
                StartAnimationImpl(animation, duration, now);

            return ActiveAnimationFuture;
        }

        private void StartAnimationImpl (IControlAnimation animation, float? duration, long? now) {
            PendingAnimation = null;
            var _now = now ?? Context.NowL;
            var multiplier = (Context?.Animations?.AnimationDurationMultiplier ?? 1f);
            var _duration = (duration ?? animation.DefaultDuration) * multiplier;
            ActiveAnimationEndWhen = _now + (long)((double)_duration * Time.SecondInTicks);
            ActiveAnimation = animation;
            // If the duration is zero end the animation immediately and bias the current time forward
            //  to ensure that the endpoint (end of fade, etc) is applied. Likewise make sure the duration
            //  is never zero since that could produce divide-by-zero effects
            animation.Start(this, _now, Math.Max(_duration, 1f / 1000f));
            if (_duration <= 0)
                UpdateAnimation(_now + Time.MillisecondInTicks);
        }

        protected void InvalidateTooltip () {
            TooltipContentVersion++;
        }

        // FIXME: Potential leak, but you shouldn't be throwing away contexts and keeping controls around
        private UIContext _CachedContext;

        public UIContext Context {
            get {
                if (_CachedContext != null)
                    return _CachedContext;
                if (WeakParent == null)
                    return null;

                if (TryGetParent(out Control parent)) {
                    var result = parent.Context;
                    if (result != null) {
                        Context = result;
                        return result;
                    }
                }

                return null;
            }
            set {
                if (_CachedContext == value)
                    return;

                if ((_CachedContext != null) && (_CachedContext != value))
                    throw new InvalidOperationException("UI context already set");

                _CachedContext = value;
                InitializeForContext();
            }
        }

        public static bool IsRecursivelyTransparent (Control control, bool includeSelf = true) {
            if (control.IsTransparent && includeSelf)
                return true;

            var current = control;
            while (true) {
                if (!current.TryGetParent(out Control parent))
                    return false;

                var icc = (parent as IControlContainer);
                if ((icc != null) && icc.IsControlHidden(current))
                    return true;

                current = parent;
                if (current.IsTransparent)
                    return true;
            }
        }

        public static bool IsEqualOrAncestor (Control control, Control expected) {
            if (expected == control)
                return true;
            if (control == null)
                return false;

            var current = control;
            while (true) {
                if (!current.TryGetParent(out Control parent))
                    return false;

                if (parent == expected)
                    return true;
                current = parent;
            }
        }

        protected virtual void OnVisibleChange (bool newValue) {
            if (newValue == false)
                Context?.NotifyControlBecomingInvalidFocusTarget(this, false);
        }

        protected virtual void OnEnabledChange (bool newValue) {
        }

        protected virtual void OnIntangibleChange (bool newValue) {
        }

        internal void Tick (MouseEventArgs args) {
            OnTick(args);
        }

        /// <summary>
        /// Fired every update as long as this control is fixated or has the mouse captured
        /// </summary>
        protected virtual void OnTick (MouseEventArgs args) {
        }

        protected bool FireEvent<T> (string name, T args) {
            return (EventFilter?.OnEvent(this, name, args) ?? false) || 
                (Context?.FireEvent(name, this, args, suppressHandler: true) ?? false);
        }

        protected bool FireEvent (string name) {
            return (EventFilter?.OnEvent(this, name) ?? false) || 
                (Context?.FireEvent(name, this, suppressHandler: true) ?? false);
        }

        protected T? AutoFireTweenEvent<T> (long now, string name, ref Tween<T>? tween, InternalStateFlags flag)
            where T : struct 
        {
            if (!tween.HasValue)
                return null;

            var v = tween.Value;
            return AutoFireTweenEvent(now, name, ref v, flag);
        }

        protected T AutoFireTweenEvent<T> (long now, string name, ref Tween<T> tween, InternalStateFlags flag)
            where T : struct 
        {
            var eventFired = (InternalState & flag) == flag;

            if (tween.Get(now, out T result)) {
                if (!tween.IsConstant && !eventFired)
                    FireEvent(name);
                InternalState |= flag;
            } else {
                InternalState &= ~flag;
            }
            return result;
        }

        /// <summary>
        /// The total display offset of the control (taking into account scrolling of any parent controls).
        /// </summary>
        public Vector2 AbsoluteDisplayOffset {
            get {
                return _AbsoluteDisplayOffset;
            }
            set {
                if (value == _AbsoluteDisplayOffset)
                    return;
                _AbsoluteDisplayOffset = value;
                OnDisplayOffsetChanged();
            }
        }

        protected virtual void OnDisplayOffsetChanged () {
        }

        internal bool HandleEvent (string name) {
            return (EventFilter?.OnEvent(this, name) ?? false) || OnEvent(name);
        }

        internal bool HandleEvent<T> (string name, T args) {
            return (EventFilter?.OnEvent(this, name, args) ?? false) || OnEvent(name, args);
        }

        protected virtual bool OnEvent (string name) {
            return false;
        }

        protected virtual bool OnEvent<T> (string name, T args) {
            return false;
        }

        internal ControlKey GenerateLayoutTree (ref UIOperationContext context, ControlKey parent, ControlKey? existingKey = null) {
            if (existingKey.HasValue && existingKey.Value.IsInvalid)
                throw new ArgumentOutOfRangeException(nameof(existingKey));

            try {
                if (Appearance.DecorationProvider != null)
                    UIOperationContext.PushDecorationProvider(ref context, Appearance.DecorationProvider);
                LayoutKey = OnGenerateLayoutTree(ref context, parent, existingKey);
                if (!LayoutKey.IsInvalid) {
                    if (ChangeInternalFlag(InternalStateFlags.VisibleHasChanged, false))
                        context.RelayoutRequestedForVisibilityChange = true;

                    // TODO: Only register if the control is explicitly interested, to reduce overhead?
                    if ((this is IPostLayoutListener listener) && (existingKey == null))
                        context.PostLayoutListeners?.Add(listener);

                    if (UIContext.UseNewEngine) {
                        ref var rec = ref context.Engine[LayoutKey];
                        if (context.Layout.TryGetFlags(LayoutKey, out ControlFlags cf))
                            rec.Flags = cf;

#if DEBUG
                        context.Engine.Controls[LayoutKey.ID] = this;
#endif
                    }
                }
            } finally {
                if (Appearance.DecorationProvider != null)
                    UIOperationContext.PopDecorationProvider(ref context);
            }

            return LayoutKey;
        }

        public bool GetRects (out RectF rect, out RectF contentRect, bool applyOffset = true, UIContext context = null) {
            context = context ?? Context;
            if (IsLayoutInvalid || (context == null)) {
                rect = contentRect = default(RectF);
                return false;
            }

            if (UIContext.UseNewEngine) {
                ref var res = ref context.Engine.Result(LayoutKey);
                rect = res.Rect;
                contentRect = res.ContentRect;
            } else {
                if (!context.Layout.GetRects(LayoutKey, out rect, out contentRect))
                    return false;
            }

            if (applyOffset) {
                rect.Left += _AbsoluteDisplayOffset.X;
                rect.Top += _AbsoluteDisplayOffset.Y;
                contentRect.Left += _AbsoluteDisplayOffset.X;
                contentRect.Top += _AbsoluteDisplayOffset.Y;
            }

            return true;
        }

        // HACK
        static readonly ThreadLocal<List<Control>> TempTransformStack = new ThreadLocal<List<Control>>(() => new List<Control>(32));

        // Attempts to apply the full stack of transform matrices to our control's bounding box and produce
        //  a new bounding box that fully contains all four of our transformed corners.
        // The current implementation is broken but mildly broken, and this is really only used for tooltip
        //  anchoring, so I don't care enough to fix it yet. The math is a nightmare.
        private void ApplyCompleteTransform (ref RectF result) {
            List<Control> stack = null;
            {
                // Walk the parent chain to see if any control has a transform
                bool earlyOut = true;
                var current = this;
                while (current != null) {
                    if (current.Appearance.HasTransformMatrix) {
                        earlyOut = false;
                        break;
                    }

                    current.TryGetParent(out current);
                }

                // If we didn't find any transforms we can bail out
                if (earlyOut)
                    return;

                // Otherwise, we need to build a full top-down control chain
                stack = TempTransformStack.Value;
                stack.Clear();
                while (current != null) {
                    stack.Add(current);
                    current.TryGetParent(out current);
                }
                stack.Reverse();
            }

            var matrix = ControlMatrixInfo.IdentityMatrix;
            var nowL = Context.NowL;
            foreach (var item in stack) {
                // Walk down the chain from root to control and apply each transform we find
                if (!item.Appearance.GetTransform(out Matrix itemMatrix, nowL))
                    continue;

                // We need the control's rect so we can align the transform around its transform origin
                var rect = item.GetRect();
                var offset = (rect.Size * item.Appearance.TransformOrigin) + rect.Position;
                Matrix.CreateTranslation(-offset.X, -offset.Y, 0f, out Matrix before);
                Matrix.CreateTranslation(offset.X, offset.Y, 0f, out Matrix after);
                matrix = before * itemMatrix * after * matrix;
            }

            // We've now stacked all the transforms in our parent chain and can apply them to the corners of our rect
            Vector2 tl = result.Position, tr = new Vector2(result.Extent.X, result.Top),
                br = result.Extent, bl = new Vector2(result.Left, result.Extent.Y);
            Vector4 tlX, trX, brX, blX;
            Vector4.Transform(ref tl, ref matrix, out tlX);
            Vector4.Transform(ref tr, ref matrix, out trX);
            Vector4.Transform(ref br, ref matrix, out brX);
            Vector4.Transform(ref bl, ref matrix, out blX);
            tlX /= tlX.W;
            trX /= trX.W;
            brX /= brX.W;
            blX /= blX.W;
            Arithmetic.MinMax(tlX.X, trX.X, brX.X, blX.X, out float minX, out float maxX);
            Arithmetic.MinMax(tlX.Y, trX.Y, brX.Y, blX.Y, out float minY, out float maxY);
            result.Position = new Vector2(minX, minY);
            result.Size = new Vector2(maxX - minX, maxY - minY);
        }

        /// <summary>
        /// Gets the current computed rectangle of the control.
        /// </summary>
        /// <param name="applyOffset">Applies the scroll offset of the control's parent(s).</param>
        /// <param name="contentRect">Insets the rectangle by the control's padding.</param>
        /// <param name="displayRect">Expands the rectangle to include the control's margin and applies its transform (if any).</param>
        public RectF GetRect (bool applyOffset = true, bool contentRect = false, bool displayRect = false, UIContext context = null) {
            if (IsLayoutInvalid)
                return default(RectF);

            context = context ?? Context;
            RectF result;
            if (UIContext.UseNewEngine) {
                ref var res = ref context.Engine.Result(LayoutKey);
                result = contentRect ? res.ContentRect : res.Rect;
            } else {
                if (contentRect)
                    context.Layout.TryGetContentRect(LayoutKey, out result);
                else
                    context.Layout.TryGetRect(LayoutKey, out result);
            }

            if (displayRect) {
                if (contentRect)
                    throw new ArgumentException("Cannot set both contentRect and exteriorRect");
                var margins = MostRecentComputedMargins;
                result.Left -= margins.Left;
                result.Top -= margins.Top;
                result.Width += margins.X;
                result.Height += margins.Y;
                ApplyCompleteTransform(ref result);
            }

            if (applyOffset) {
                result.Left += _AbsoluteDisplayOffset.X;
                result.Top += _AbsoluteDisplayOffset.Y;
            }
            
            return result;
        }

        protected float GetOpacity (long now) {
            if (!Appearance.OpacityIsSet)
                return 1;

            return AutoFireTweenEvent(now, UIEvents.OpacityTweenEnded, ref Appearance._Opacity, InternalStateFlags.EventFiredOpacity);
        }

        protected pSRGBColor? GetBackgroundColor (long now) {
            var v4 = AutoFireTweenEvent(now, UIEvents.BackgroundColorTweenEnded, ref Appearance.BackgroundColor.pLinear, InternalStateFlags.EventFiredBackgroundColor);
            if (!v4.HasValue)
                return null;
            return pSRGBColor.FromPLinear(v4.Value);
        }

        protected pSRGBColor? GetTextColor (long now) {
            var v4 = AutoFireTweenEvent(now, UIEvents.TextColorTweenEnded, ref Appearance.TextColor.pLinear, InternalStateFlags.EventFiredTextColor);
            if (!v4.HasValue)
                return null;
            return pSRGBColor.FromPLinear(v4.Value);
        }

        internal Vector2 ApplyLocalTransformToGlobalPosition (Vector2 globalPosition, ref RectF box, bool force) {
            if (!Appearance.HasTransformMatrix)
                return globalPosition;

            var localPosition = globalPosition;

            // TODO: Throw on non-invertible transform or other messed up math?
            if (!Appearance.GetInverseTransform(box, out Matrix matrix, Context.NowL))
                return globalPosition;

            Vector4.Transform(ref localPosition, ref matrix, out Vector4 transformedLocalPosition);
            localPosition = new Vector2(transformedLocalPosition.X / transformedLocalPosition.W, transformedLocalPosition.Y / transformedLocalPosition.W);

            if (!force && !box.Contains(localPosition))
                return globalPosition;
            else
                return localPosition;
        }

        protected void ComputeEffectiveScaleRatios (IDecorationProvider decorations, out Vector2 padding, out Vector2 margins, out Vector2 size) {
            margins = decorations.SpacingScaleRatio * decorations.MarginScaleRatio;
            padding = decorations.SpacingScaleRatio * decorations.PaddingScaleRatio;

            if (Appearance.AutoScaleMetrics)
                size = decorations.SizeScaleRatio;
            else
                size = Vector2.One;
        }

        protected virtual void ComputeAppearanceSpacing (
            ref UIOperationContext context, IDecorator decorations, 
            out Margins scaledMargins, out Margins scaledPadding, out Margins unscaledPadding
        ) {
            if (!Appearance.SuppressDecorationMargins && (decorations != null))
                Margins.Add(in Margins, decorations.Margins, out scaledMargins);
            else
                scaledMargins = Margins;

            if (!Appearance.SuppressDecorationPadding && (decorations != null))
                Margins.Add(in Padding, decorations.Padding, out scaledPadding);
            else
                scaledPadding = Padding;

            if (!Appearance.SuppressDecorationPadding && (decorations != null))
                unscaledPadding = decorations.UnscaledPadding;
            else
                unscaledPadding = default(Margins);
        }

        protected void ComputeEffectiveSpacing (ref UIOperationContext context, IDecorationProvider decorationProvider, IDecorator decorations, out Margins padding, out Margins margins) {
            ComputeAppearanceSpacing(ref context, decorations, out var scaledMargins, out var scaledPadding, out var unscaledPadding);
            ComputeEffectiveScaleRatios(decorationProvider, out Vector2 paddingScale, out Vector2 marginScale, out Vector2 sizeScale);
            Margins.Scale(ref scaledPadding, in paddingScale);
            Margins.Add(in scaledPadding, in unscaledPadding, out padding);
            Margins.Scale(ref scaledMargins, in marginScale);
            margins = scaledMargins;
        }

        protected virtual void ComputeSizeConstraints (
            ref UIOperationContext context, ref ControlDimension width, ref ControlDimension height, Vector2 sizeScale
        ) {
            width *= sizeScale.X;
            height *= sizeScale.Y;
        }

#if DETECT_DOUBLE_RASTERIZE
        private bool RasterizeIsPending = false;
#endif

        protected virtual ControlKey OnGenerateLayoutTree (ref UIOperationContext context, ControlKey parent, ControlKey? existingKey) {
            if (!Visible && !context.UIContext.IsUpdatingSubtreeLayout)
                return ControlKey.Invalid;

            IsLayoutInvalid = false;

#if DETECT_DOUBLE_RASTERIZE
            RasterizeIsPending = true;
#endif

            var result = existingKey ?? context.Layout.CreateItem();

            var decorationProvider = context.DecorationProvider;
            var decorations = GetDecorator(decorationProvider, context.DefaultDecorator);
            ComputeEffectiveSpacing(ref context, decorationProvider, decorations, out Margins computedPadding, out Margins computedMargins);

            MostRecentComputedMargins = computedMargins;

            var width = Width.AutoComputeFixed();
            var height = Height.AutoComputeFixed();
            var sizeScale = Appearance.AutoScaleMetrics ? Context.Decorations.SizeScaleRatio : Vector2.One;
            ComputeSizeConstraints(ref context, ref width, ref height, sizeScale);

            var actualLayoutFlags = ComputeLayoutFlags(width.Fixed.HasValue, height.Fixed.HasValue);

            context.Layout.SetLayoutFlags(result, actualLayoutFlags);
            context.Layout.SetLayoutData(result, ref Layout.FloatingPosition, ref computedMargins, ref computedPadding);
            context.Layout.SetSizeConstraints(result, in width, in height);

            if (!parent.IsInvalid && !existingKey.HasValue) {
                context.Layout.InsertAtEnd(parent, result);
            }

            return result;
        }

        protected ControlFlags ComputeLayoutFlags (bool hasFixedWidth, bool hasFixedHeight) {
            var result = (LayoutFlags & Layout.Mask) | Layout;
            // HACK: Clearing the fill flag is necessary for fixed sizes to work,
            //  but clearing both anchors causes the control to end up centered...
            //  and if we only clear one anchor then wrapping breaks. Awesome
            /*
            if (hasFixedWidth && result.IsFlagged(ControlFlags.Layout_Fill_Row))
                result &= ~ControlFlags.Layout_Fill_Row;
            if (hasFixedHeight && result.IsFlagged(ControlFlags.Layout_Fill_Column))
                result &= ~ControlFlags.Layout_Fill_Column;
            */
            return result;
        }

        protected virtual IDecorator GetDefaultDecorator (IDecorationProvider provider) {
            return null;
        }

        protected IDecorator GetDecorator (IDecorationProvider provider, IDecorator over) {
            if (Appearance.Undecorated)
                return (provider.None ?? over);

            return Appearance.Decorator ?? (over ?? GetDefaultDecorator(provider));
        }

        protected IDecorator GetTextDecorator (IDecorationProvider provider, IDecorator over) {
            if (Appearance.UndecoratedText)
                return (provider.None ?? over);

            return Appearance.TextDecorator ?? (over ?? GetDefaultDecorator(provider));
        }

        protected ControlStates GetCurrentState (ref UIOperationContext context) {
            var result = default(ControlStates);

            if (!Enabled) {
                result |= ControlStates.Disabled;
            } else {
                if (context.UIContext.Hovering == this)
                    result |= ControlStates.Hovering;

                if (context.UIContext.CurrentTooltipAnchor == this)
                    result |= ControlStates.AnchorForTooltip;

                if (context.UIContext.MouseOver == this)
                    result |= ControlStates.MouseOver;

                // HACK: If a modal has temporarily borrowed focus from us, we should still appear
                //  to be focused.
                var fm = context.UIContext.Focused as IModal;
                if (
                    (context.UIContext.Focused == this) || 
                    (fm?.FocusDonor == this)
                ) {
                    result |= ControlStates.Focused;
                    result |= ControlStates.ContainsFocus;
                }

                if (
                    context.UIContext.FocusChain.Contains(this) ||
                    (context.UIContext.ModalFocusDonor == this) ||
                    (context.UIContext.TopLevelModalFocusDonor == this)
                )
                    result |= ControlStates.ContainsFocus;

                if (context.UIContext.PreviousHovering == this)
                    result |= ControlStates.PreviouslyHovering;

                if (context.UIContext.PreviousFocused == this)
                    result |= ControlStates.PreviouslyFocused;
            }

            if ((context.UIContext.MouseCaptured == this) || (context.ActivateKeyHeld && context.UIContext.Focused == this))
                result |= ControlStates.Pressed;

            return result;
        }

        protected virtual void ApplyClipMargins (ref UIOperationContext context, ref RectF box) {
        }

        public bool TryGetParent (out Control parent) {
            if (WeakParent == null) {
                parent = null;
                return false;
            }

            return WeakParent.TryGetTarget(out parent);
        }

        protected virtual void InitializeForContext () {
            var pa = PendingAnimation;
            PendingAnimation = null;
            if (pa == null)
                return;
            StartAnimationImpl(
                PendingAnimation.Animation, 
                pa.Duration,
                pa.Now
            );
        }

        public virtual void InvalidateLayout () {
            IsLayoutInvalid = true;
        }

        // FIXME: It sucks that this has to be public
        public virtual void ClearLayoutKey () {
            LayoutKey = ControlKey.Invalid;
            // FIXME: Do we need to do this?
            // IsLayoutInvalid = true;
        }

        internal void SetParent (WeakReference<Control> weakParent) {
            if (
                (weakParent == null) ||
                !weakParent.TryGetTarget(out Control parent) || 
                (parent == null)
            ) {
                WeakParent = null;
                return;
            }

            Control currentParent;
            if ((WeakParent != null) && WeakParent.TryGetTarget(out currentParent)) {
                if (currentParent != parent)
                    throw new Exception("This control already has a parent");
                else
                    return;
            }

            ClearLayoutKey();
            WeakParent = weakParent;
            if (parent.Context != null)
                Context = parent.Context;
        }

        internal void UnsetParent (Control oldParent) {
            ClearLayoutKey();

            CancelActiveAnimation();

            if (WeakParent == null)
                return;

            Control actualParent;
            if (!WeakParent.TryGetTarget(out actualParent))
                return;

            if (actualParent != oldParent)
                throw new Exception("Parent mismatch");

            WeakParent = null;
        }

        public override string ToString () {
            return DebugLabel ?? $"{GetType().Name} #{GetHashCode():X8}";
        }
    }
}