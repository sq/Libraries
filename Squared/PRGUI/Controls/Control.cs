using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
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
    public struct ControlDimension {
        public float? Minimum, Maximum, Fixed;

        public static ControlDimension operator * (float lhs, ControlDimension rhs) {
            return rhs.Scale(lhs);
        }

        public static ControlDimension operator * (ControlDimension lhs, float rhs) {
            return lhs.Scale(rhs);
        }

        public ControlDimension AutoComputeFixed () {
            if ((Maximum == Minimum) && Maximum.HasValue)
                return new ControlDimension {
                    Minimum = Minimum,
                    Maximum = Maximum,
                    Fixed = Fixed ?? Maximum
                };

            return this;
        }

        public ControlDimension Scale (float scale) {
            return new ControlDimension {
                Minimum = Minimum * scale,
                Maximum = Maximum * scale,
                Fixed = Fixed * scale
            };
        }

        public static float? Min (float? lhs, float? rhs) {
            if (lhs.HasValue && rhs.HasValue)
                return Math.Min(lhs.Value, rhs.Value);
            else if (lhs.HasValue)
                return lhs;
            else
                return rhs;
        }

        public static float? Max (float? lhs, float? rhs) {
            if (lhs.HasValue && rhs.HasValue)
                return Math.Max(lhs.Value, rhs.Value);
            else if (lhs.HasValue)
                return lhs;
            else
                return rhs;
        }

        /// <summary>
        /// Produces a new dimension with minimum/maximum values that encompass both inputs
        /// </summary>
        public ControlDimension Union (ref ControlDimension rhs) {
            return new ControlDimension {
                Minimum = Min(Minimum, rhs.Minimum),
                Maximum = Max(Maximum, rhs.Maximum),
                // FIXME
                Fixed = Fixed ?? rhs.Fixed
            };
        }

        /// <summary>
        /// Produces a new dimension with minimum/maximum values that only encompass the place where
        ///  the two inputs overlap
        /// </summary>
        public ControlDimension Intersection (ref ControlDimension rhs) {
            return new ControlDimension {
                Minimum = Max(Minimum, rhs.Minimum),
                Maximum = Min(Maximum, rhs.Maximum),
                // FIXME
                Fixed = Fixed ?? rhs.Fixed
            };
        }

        public void Constrain (ref float? size, bool applyFixed) {
            if (Minimum.HasValue && size.HasValue)
                size = Math.Max(Minimum.Value, size.Value);
            if (Maximum.HasValue && size.HasValue)
                size = Math.Min(Maximum.Value, size.Value);
            if (applyFixed && Fixed.HasValue)
                size = Fixed;
        }

        public float? Constrain (float? size, bool applyFixed) {
            Constrain(ref size, applyFixed);
            return size;
        }

        public static implicit operator ControlDimension (float fixedSize) {
            return new ControlDimension { Fixed = fixedSize };
        }

        public override string ToString () {
            return $"Clamp({Fixed?.ToString() ?? "<null>"}, {Minimum?.ToString() ?? "<null>"}, {Maximum?.ToString() ?? "<null>"})";
        }
    }

    public interface IHasDescription {
        string Description { get; set; }
    }

    public interface IControlEventFilter {
        bool OnEvent (Control target, string name);
        bool OnEvent<T> (Control target, string name, T args);
    }
    
    public abstract partial class Control {
        private struct PendingAnimationRecord {
            public IControlAnimation Animation;
            public float? Duration;
            public long? Now;
        }

        public static bool ShowDebugBoxes = false,
            ShowDebugBreakMarkers = false,
            ShowDebugMargins = false,
            ShowDebugPadding = false;

        public static readonly Controls.NullControl None = new Controls.NullControl();

        public string DebugLabel = null;

        internal int TypeID;

        public IControlEventFilter EventFilter;

        public ControlAppearance Appearance;
        public Margins Margins, Padding;
        public ControlFlags LayoutFlags = ControlFlags.Layout_Fill_Row;
        /// <summary>
        /// Overrides LayoutFlags
        /// </summary>
        public LayoutFlags Layout;
        public ControlDimension Width, Height;
        private bool _BackgroundColorEventFired, _OpacityEventFired, _TextColorEventFired;

        public Controls.ControlDataCollection Data;

        // Accumulates scroll offset(s) from parent controls
        private Vector2 _AbsoluteDisplayOffset;

        private ControlKey _LayoutKey = ControlKey.Invalid;
        public ControlKey LayoutKey {
            get => _LayoutKey;
            private set {
                if (value == _LayoutKey)
                    return;
                if (!_LayoutKey.IsInvalid)
                    ;
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

        private bool _Visible = true, _VisibleHasChanged = false, _Enabled = true;

        /// <summary>
        /// If false, the control will not participate in layout or rasterization
        /// </summary>
        public bool Visible {
            get => _Visible;
            set {
                if (_Visible == value)
                    return;
                _Visible = value;
                _VisibleHasChanged = true;
                OnVisibleChange(value);
            }
        }
        /// <summary>
        /// If false, the control cannot receive focus or input
        /// </summary>
        public bool Enabled {
            get => _Enabled;
            set {
                if (_Enabled == value)
                    return;
                _Enabled = value;
                if (value == false)
                    Context?.NotifyControlBecomingInvalidFocusTarget(this, false);
            }
        }

        public virtual bool CanApplyOpacityWithoutCompositing { get; protected set; }
        /// <summary>
        /// Can receive focus via user input
        /// </summary>
        public virtual bool AcceptsFocus { get; protected set; }
        /// <summary>
        /// Receives mouse events and can capture the mouse
        /// </summary>
        public virtual bool AcceptsMouseInput { get; protected set; }
        /// <summary>
        /// Controls whether textual input (IME composition, on-screen keyboard, etc) should 
        ///  be enabled while this control is focused. You will still get key events even if 
        ///  this is false, so things like arrow key navigation will work.
        /// </summary>
        public virtual bool AcceptsTextInput { get; protected set; }
        /// <summary>
        /// Intangible controls are ignored by hit-tests
        /// </summary>
        public bool Intangible {
            get => _Intangible;
            set {
                if (_Intangible == value)
                    return;
                _Intangible = value;
                OnIntangibleChange(value);
            }
        }

        private bool _Intangible;
        private Control _FocusBeneficiary;

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
            ) && Enabled && !Control.IsRecursivelyTransparent(this);

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

        protected bool CreateNestedContextForChildren = true;
        protected virtual bool HasPreRasterizeHandler => (ActiveAnimation != null);
        protected virtual bool HasChildren => false;
        protected virtual bool ShouldClipContent => false;

        protected WeakReference<UIContext> WeakContext = null;
        protected WeakReference<Control> WeakParent = null;

        public Future<bool> ActiveAnimationFuture { get; private set; }
        protected IControlAnimation ActiveAnimation { get; private set; }
        protected long ActiveAnimationEndWhen;
        private PendingAnimationRecord PendingAnimation;

        public Control () {
            TypeID = GetType().GetHashCode();
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
            if (Context == null)
                PendingAnimation = new PendingAnimationRecord {
                    Animation = animation,
                    Duration = duration,
                    Now = now
                };
            else
                StartAnimationImpl(animation, duration, now);

            return ActiveAnimationFuture;
        }

        private void StartAnimationImpl (IControlAnimation animation, float? duration, long? now) {
            PendingAnimation = default(PendingAnimationRecord);
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

        protected T? AutoFireTweenEvent<T> (long now, string name, ref Tween<T>? tween, ref bool eventFired)
            where T : struct {
            if (!tween.HasValue)
                return null;

            var v = tween.Value;
            return AutoFireTweenEvent(now, name, ref v, ref eventFired);
        }

        protected T AutoFireTweenEvent<T> (long now, string name, ref Tween<T> tween, ref bool eventFired)
            where T : struct {

            if (tween.IsConstant) {
                eventFired = true;
            } else if (tween.IsOver(now)) {
                if (!eventFired) {
                    eventFired = true;
                    FireEvent(name);
                }
            } else {
                eventFired = false;
            }
            return tween.Get(now);
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
                    if (_VisibleHasChanged) {
                        context.RelayoutRequestedForVisibilityChange = true;
                        _VisibleHasChanged = false;
                    }

                    // TODO: Only register if the control is explicitly interested, to reduce overhead?
                    if ((this is IPostLayoutListener listener) && (existingKey == null))
                        context.PostLayoutListeners?.Add(listener);
                }
            } finally {
                if (Appearance.DecorationProvider != null)
                    UIOperationContext.PopDecorationProvider(ref context);
            }

            return LayoutKey;
        }

        /// <summary>
        /// Gets the current computed rectangle of the control.
        /// </summary>
        /// <param name="applyOffset">Applies the scroll offset of the control's parent(s).</param>
        /// <param name="contentRect">Insets the rectangle by the control's padding.</param>
        /// <param name="exteriorRect">Expands the rectangle to include the control's margin.</param>
        public RectF GetRect (bool applyOffset = true, bool contentRect = false, bool exteriorRect = false, UIContext context = null) {
            if (LayoutKey.IsInvalid)
                return default(RectF);

            context = context ?? Context;
            var result = contentRect 
                ? context.Layout.GetContentRect(LayoutKey) 
                : context.Layout.GetRect(LayoutKey);

            if (exteriorRect) {
                if (contentRect)
                    throw new ArgumentException("Cannot set both contentRect and exteriorRect");
                var margins = MostRecentComputedMargins;
                result.Left -= margins.Left;
                result.Top -= margins.Top;
                result.Width += margins.X;
                result.Height += margins.Y;
            }

            if (applyOffset) {
                result.Left += _AbsoluteDisplayOffset.X;
                result.Top += _AbsoluteDisplayOffset.Y;
            }
            
            return result;
        }

        protected float GetOpacity (long now) {
            if (!Appearance.HasOpacity)
                return 1;

            return AutoFireTweenEvent(now, UIEvents.OpacityTweenEnded, ref Appearance._Opacity, ref _OpacityEventFired);
        }

        protected pSRGBColor? GetBackgroundColor (long now) {
            var v4 = AutoFireTweenEvent(now, UIEvents.BackgroundColorTweenEnded, ref Appearance.BackgroundColor.pLinear, ref _BackgroundColorEventFired);
            if (!v4.HasValue)
                return null;
            return pSRGBColor.FromPLinear(v4.Value);
        }

        protected pSRGBColor? GetTextColor (long now) {
            var v4 = AutoFireTweenEvent(now, UIEvents.TextColorTweenEnded, ref Appearance.TextColor.pLinear, ref _TextColorEventFired);
            if (!v4.HasValue)
                return null;
            return pSRGBColor.FromPLinear(v4.Value);
        }

        internal Vector2 ApplyLocalTransformToGlobalPosition (Vector2 globalPosition, ref RectF box, bool force) {
            if (!Appearance.HasTransformMatrix)
                return globalPosition;

            var localPosition = globalPosition - box.Center;
            // Detect non-invertible transform or other messed up math

            Appearance.GetInverseTransform(out Matrix matrix, Context.NowL);

            Vector4.Transform(ref localPosition, ref matrix, out Vector4 transformedLocalPosition);
            var transformedLocal2 = new Vector2(transformedLocalPosition.X / transformedLocalPosition.W, transformedLocalPosition.Y / transformedLocalPosition.W);
            var result = transformedLocal2 + box.Center;

            if (!force && !box.Contains(result))
                return globalPosition;
            else
                return result;
        }

        protected virtual void ComputeMargins (UIOperationContext context, IDecorator decorations, out Margins result) {
            if (!Appearance.SuppressDecorationMargins && (decorations != null))
                Margins.Add(ref Margins, decorations.Margins, out result);
            else
                result = Margins;
        }

        protected virtual void ComputePadding (UIOperationContext context, IDecorator decorations, out Margins result) {
            if (!Appearance.SuppressDecorationPadding && (decorations != null))
                Margins.Add(ref Padding, decorations.Padding, out result);
            else
                result = Padding;
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

#if DETECT_DOUBLE_RASTERIZE
            RasterizeIsPending = true;
#endif

            var result = existingKey ?? context.Layout.CreateItem();

            var decorations = GetDecorator(context.DecorationProvider, context.DefaultDecorator);
            ComputeMargins(context, decorations, out Margins computedMargins);
            ComputePadding(context, decorations, out Margins computedPadding);

            MostRecentComputedMargins = computedMargins;

            var width = Width.AutoComputeFixed();
            var height = Height.AutoComputeFixed();
            var sizeScale = Appearance.AutoScaleMetrics ? Context.Decorations.SizeScaleRatio : Vector2.One;
            ComputeSizeConstraints(ref context, ref width, ref height, sizeScale);

            var actualLayoutFlags = ComputeLayoutFlags(width.Fixed.HasValue, height.Fixed.HasValue);

            var spacingScale = context.DecorationProvider.SpacingScaleRatio;
            var paddingScale = spacingScale * context.DecorationProvider.PaddingScaleRatio;
            var marginScale = spacingScale * context.DecorationProvider.MarginScaleRatio;
            if (!Appearance.SuppressDecorationScaling) {
                Margins.Scale(ref computedMargins, ref marginScale);
                Margins.Scale(ref computedPadding, ref paddingScale);
            } else {
                ;
            }

            context.Layout.SetLayoutFlags(result, actualLayoutFlags);
            context.Layout.SetLayoutData(result, ref Layout.FloatingPosition, ref computedMargins, ref computedPadding);
            context.Layout.SetSizeConstraints(result, ref width, ref height);

            if (!parent.IsInvalid && !existingKey.HasValue)
                context.Layout.InsertAtEnd(parent, result);

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

        protected ControlStates GetCurrentState (UIOperationContext context) {
            var result = default(ControlStates);

            if (!Enabled) {
                result |= ControlStates.Disabled;
            } else {
                if (context.UIContext.Hovering == this)
                    result |= ControlStates.Hovering;
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
            }

            if ((context.UIContext.MouseCaptured == this) || (context.ActivateKeyHeld && context.UIContext.Focused == this))
                result |= ControlStates.Pressed;

            return result;
        }

        protected virtual void OnPreRasterize (UIOperationContext context, DecorationSettings settings, IDecorator decorations) {
            UpdateAnimation(context.NowL);
        }

        protected virtual void OnRasterize (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings, IDecorator decorations) {
            decorations?.Rasterize(context, ref renderer, settings);
        }

        protected virtual void OnRasterizeChildren (UIOperationContext context, ref RasterizePassSet passSet, DecorationSettings settings) {
        }

        protected virtual void ApplyClipMargins (UIOperationContext context, ref RectF box) {
        }

        protected virtual DecorationSettings MakeDecorationSettings (ref RectF box, ref RectF contentBox, ControlStates state, bool compositing) {
            return new DecorationSettings {
                Box = box,
                ContentBox = contentBox,
                State = state,
                BackgroundColor = GetBackgroundColor(Context.NowL),
                TextColor = GetTextColor(Context.NowL),
                BackgroundImage = Appearance.BackgroundImage,
                Traits = Appearance.DecorationTraits,
                IsCompositing = compositing
            };
        }

        private void UpdateVisibleRegion (ref UIOperationContext context, ref RectF box) {
            var vr = context.VisibleRegion;
            vr.Left = Math.Max(context.VisibleRegion.Left, box.Left - UIContext.VisibilityPadding);
            vr.Top = Math.Max(context.VisibleRegion.Top, box.Top - UIContext.VisibilityPadding);
            var right = Math.Min(context.VisibleRegion.Extent.X, box.Extent.X + UIContext.VisibilityPadding);
            var bottom = Math.Min(context.VisibleRegion.Extent.Y, box.Extent.Y + UIContext.VisibilityPadding);
            vr.Width = right - vr.Left;
            vr.Height = bottom - vr.Top;
            context.VisibleRegion = vr;
        }

        private void RasterizePass (
            ref UIOperationContext context, 
            ref DecorationSettings settings, IDecorator decorations,
            bool compositing, ref RasterizePassSet passSet, 
            ref ImperativeRenderer renderer, RasterizePasses pass
        ) {
            UIOperationContext passContext;
            context.Clone(out passContext);
            passContext.Pass = pass;
            var hasNestedContext = (pass == RasterizePasses.Content) && 
                (ShouldClipContent || (HasChildren && CreateNestedContextForChildren));
            if (hasNestedContext)
                UpdateVisibleRegion(ref passContext, ref settings.Box);

            // FIXME: The memset for these actually burns a measurable amount of time
            ImperativeRenderer contentRenderer = default(ImperativeRenderer);
            RasterizePassSet childrenPassSet = default(RasterizePassSet);
            UIOperationContext contentContext;

            int previousStackDepth = passSet.StackDepth, newStackDepth = previousStackDepth;

            // For clipping we need to create a separate batch group that contains all the rasterization work
            //  for our children. At the start of it we'll generate the stencil mask that will be used for our
            //  rendering operation(s).
            if (hasNestedContext) {
                renderer.Layer += 1;
                passContext.Clone(out contentContext);
                contentRenderer = renderer.MakeSubgroup();
                if (ShouldClipContent) {
                    newStackDepth = previousStackDepth + 1;
                    contentRenderer.DepthStencilState = context.UIContext.GetStencilTest(newStackDepth);
                    childrenPassSet = new RasterizePassSet(ref contentRenderer, newStackDepth, passSet.OverlayQueue);
                } else {
                    contentRenderer.DepthStencilState = 
                        (previousStackDepth <= 0)
                        ? DepthStencilState.None
                        : context.UIContext.GetStencilTest(previousStackDepth);
                    childrenPassSet = new RasterizePassSet(ref contentRenderer, newStackDepth, passSet.OverlayQueue);
                }
                renderer.Layer += 1;
            } else {
                contentContext = passContext;
            }

            if (HasPreRasterizeHandler && (pass == RasterizePasses.Content))
                OnPreRasterize(contentContext, settings, decorations);

            if (hasNestedContext)
                OnRasterize(contentContext, ref contentRenderer, settings, decorations);
            else
                OnRasterize(contentContext, ref renderer, settings, decorations);

            if ((pass == RasterizePasses.Content) && HasChildren) {
                if (hasNestedContext)
                    OnRasterizeChildren(contentContext, ref childrenPassSet, settings);
                else
                    // FIXME: Save/restore layers?
                    OnRasterizeChildren(contentContext, ref passSet, settings);
            }

            if (hasNestedContext) {
                // GROSS OPTIMIZATION HACK: Detect that any rendering operation(s) occurred inside the
                //  group and if so, set up the stencil mask so that they will be clipped.
                if (ShouldClipContent) {
                    if (!contentRenderer.Container.IsEmpty) {
                        // If this is the first stencil pass instead of a nested one, clear the stencil buffer
                        if (passSet.StackDepth < 1) {
                            contentRenderer.Clear(stencil: 0, layer: -9999);
                        } else {
                            // Erase any siblings' clip regions
                            contentRenderer.DepthStencilState = context.UIContext.GetStencilRestore(previousStackDepth);
                            contentRenderer.FillRectangle(new Rectangle(-1, -1, 9999, 9999), Color.Transparent, blendState: RenderStates.DrawNone, layer: -1000);
                        }

                        contentRenderer.DepthStencilState = context.UIContext.GetStencilWrite(previousStackDepth);

                        // FIXME: Separate context?
                        contentContext.Pass = RasterizePasses.ContentClip;

                        // FIXME
                        var temp = settings;
                        ApplyClipMargins(contentContext, ref temp.Box);

                        var crLayer = contentRenderer.Layer;
                        contentRenderer.Layer = -999;
                        settings.State = default(ControlStates);
                        decorations?.Rasterize(contentContext, ref contentRenderer, temp);

                        contentRenderer.Layer = crLayer;

                        // passSet.NextReferenceStencil = childrenPassSet.NextReferenceStencil;
                    } else {
                        ;
                    }
                } 

                renderer.Layer += 1;
            }
        }

        private void RasterizeAllPasses (ref UIOperationContext context, ref RectF box, ref RasterizePassSet passSet, bool compositing) {
            try {
                if (Appearance.DecorationProvider != null)
                    UIOperationContext.PushDecorationProvider(ref context, Appearance.DecorationProvider);

                var decorations = GetDecorator(context.DecorationProvider, context.DefaultDecorator);
                var contentBox = GetRect(contentRect: true);
                var state = GetCurrentState(context);
                var settings = MakeDecorationSettings(ref box, ref contentBox, state, compositing);
                if (!IsPassDisabled(RasterizePasses.Below, decorations))
                    RasterizePass(ref context, ref settings, decorations, compositing, ref passSet, ref passSet.Below, RasterizePasses.Below);
                if (!IsPassDisabled(RasterizePasses.Content, decorations))
                    RasterizePass(ref context, ref settings, decorations, compositing, ref passSet, ref passSet.Content, RasterizePasses.Content);
                if (!IsPassDisabled(RasterizePasses.Above, decorations))
                    RasterizePass(ref context, ref settings, decorations, compositing, ref passSet, ref passSet.Above, RasterizePasses.Above);
            } finally {
                if (Appearance.DecorationProvider != null)
                    UIOperationContext.PopDecorationProvider(ref context);
            }
        }

        protected virtual bool IsPassDisabled (RasterizePasses pass, IDecorator decorations) {
            // Best not to default this optimization on
            // return decorations.IsPassDisabled(pass);
            return false;
        }

        protected virtual pSRGBColor GetDebugBoxColor (int depth) {
            return Color.Lerp(Color.Red, Color.Orange, depth / 16f);
        }

        private void RasterizeDebugMargins (ref UIOperationContext context, ref RasterizePassSet passSet, ref RectF rect, Margins margins, float direction, Color color) {
            var lineWidth = 1.5f;
            var exteriorRect = rect;
            exteriorRect.Left -= margins.Left * direction;
            exteriorRect.Top -= margins.Top * direction;
            exteriorRect.Width += margins.X * direction;
            exteriorRect.Height += margins.Y * direction;
            var center = rect.Center;

            if (margins.Left > 0)
                passSet.Above.RasterizeRectangle(
                    new Vector2(exteriorRect.Left, center.Y - lineWidth),
                    new Vector2(rect.Left, center.Y + lineWidth),
                    0, color
                );

            if (margins.Top > 0)
                passSet.Above.RasterizeRectangle(
                    new Vector2(center.X - lineWidth, exteriorRect.Top),
                    new Vector2(center.X + lineWidth, rect.Top),
                    0, color
                );

            if (margins.Right > 0)
                passSet.Above.RasterizeRectangle(
                    new Vector2(exteriorRect.Extent.X, center.Y - lineWidth),
                    new Vector2(rect.Extent.X, center.Y + lineWidth),
                    0, color
                );

            if (margins.Bottom > 0)
                passSet.Above.RasterizeRectangle(
                    new Vector2(center.X - lineWidth, exteriorRect.Extent.Y),
                    new Vector2(center.X + lineWidth, rect.Extent.Y),
                    0, color
                );
        }

        private void RasterizeDebugOverlays (ref UIOperationContext context, ref RasterizePassSet passSet, RectF rect) {
            if (!ShowDebugBoxes && !ShowDebugBreakMarkers && !ShowDebugMargins && !ShowDebugPadding)
                return;

            var mouseIsOver = rect.Contains(context.MousePosition);
            var alpha = mouseIsOver ? 1.0f : 0.5f;

            if (ShowDebugBoxes)
                passSet.Above.RasterizeRectangle(
                    rect.Position, rect.Extent, 0f, 1f, Color.Transparent, Color.Transparent, 
                    GetDebugBoxColor(context.Depth) * alpha
                );

            var flags = context.Layout.GetFlags(LayoutKey);

            if (ShowDebugMargins)
                RasterizeDebugMargins(ref context, ref passSet, ref rect, context.Layout.GetMargins(LayoutKey), 1f, Color.Green);

            if (ShowDebugPadding)
                RasterizeDebugMargins(ref context, ref passSet, ref rect, context.Layout.GetPadding(LayoutKey), -1f, Color.Yellow);

            if (ShowDebugBreakMarkers && mouseIsOver && flags.IsBreak()) {
                rect = new RectF(
                    new Vector2(rect.Left - 1.5f, rect.Center.Y - 7.5f),
                    new Vector2(6.5f, 15)
                );
                
                var facingRight = false;
                Vector2 a = !facingRight ? rect.Extent : rect.Position,
                    b = !facingRight 
                        ? new Vector2(rect.Position.X, rect.Center.Y)
                        : new Vector2(rect.Extent.X, rect.Center.Y),
                    c = !facingRight
                        ? new Vector2(rect.Extent.X, rect.Position.Y)
                        : new Vector2(rect.Position.X, rect.Extent.Y);

                var arrowColor =
                    flags.IsFlagged(ControlFlags.Layout_ForceBreak)
                        ? Color.White
                        : Color.Yellow;

                passSet.Above.RasterizeTriangle(
                    a, b, c, radius: 0f, outlineRadius: 1f,
                    innerColor: arrowColor * alpha, outerColor: arrowColor * alpha, 
                    outlineColor: Color.Black * (alpha * 0.8f)
                );
            }
        }


        public bool Rasterize (ref UIOperationContext context, ref RasterizePassSet passSet, float opacity = 1) {
            // HACK: Do this first since it fires opacity change events
            var hidden = false;
            opacity *= GetOpacity(context.NowL);

            if (opacity <= 0)
                hidden = true;
            if (!Visible)
                hidden = true;

            var box = default(RectF);
            bool isZeroSized = false, isInvisible = false;
            if (LayoutKey.IsInvalid) {
                hidden = true;
            } else {
                box = GetRect();
                Vector2 ext = box.Extent,
                    vext = context.VisibleRegion.Extent;
                // HACK: There might be corner cases where you want to rasterize a zero-sized control...
                isZeroSized = (box.Width <= 0) || (box.Height <= 0);
                isInvisible = (ext.X < context.VisibleRegion.Left) ||
                    (ext.Y < context.VisibleRegion.Top) ||
                    (box.Left > vext.X) ||
                    (box.Top > vext.Y) ||
                    isZeroSized;

                if (isInvisible)
                    hidden = true;

                RasterizeDebugOverlays(ref context, ref passSet, box);
            }

#if DETECT_DOUBLE_RASTERIZE
            if (!RasterizeIsPending)
                throw new Exception();
            RasterizeIsPending = false;
#endif

            // Only visibility cull controls that have a parent and aren't overlaid.
            if (isInvisible && TryGetParent(out Control parent) && !Appearance.Overlay)
                hidden = true;

            if (hidden) {
                // HACK: Ensure pre-rasterize handlers run for hidden controls, because the handler
                //  may be doing something important like updating animations or repainting a buffer
                if (HasPreRasterizeHandler) {
                    var decorations = GetDecorator(context.DecorationProvider, context.DefaultDecorator);
                    var state = GetCurrentState(context) | ControlStates.Invisible;
                    var settings = MakeDecorationSettings(ref box, ref box, state, false);
                    OnPreRasterize(context, settings, decorations);
                }
                return false;
            }

            var enableCompositor = Appearance.Compositor?.WillComposite(this, opacity) == true;
            var needsComposition = ((opacity < 1) && !this.CanApplyOpacityWithoutCompositing) || 
                enableCompositor ||
                Appearance.Overlay ||
                (
                    Appearance.HasTransformMatrix && 
                    // HACK: If the current transform matrix is the identity matrix, suppress composition
                    //  this allows simple transform animations that end at the identity matrix to work
                    //  without explicitly clearing the transform after the animation is over.
                    Appearance.GetTransform(out Matrix temp, context.NowL) &&
                    (temp != Matrix.Identity)
                );

            if (!needsComposition) {
                var oldOpacity = context.Opacity;
                context.Opacity *= opacity;
                RasterizeAllPasses(ref context, ref box, ref passSet, false);
                context.Opacity = oldOpacity;
            } else {
                // HACK: Create padding around the element for drop shadows
                box.SnapAndInset(out Vector2 tl, out Vector2 br, -Context.CompositorPaddingPx);
                // Don't overflow the edges of the canvas with padding, it'd produce garbage pixels
                context.UIContext.CanvasRect.Clamp(ref tl);
                context.UIContext.CanvasRect.Clamp(ref br);

                var compositeBox = new RectF(tl, br - tl);
                var srt = context.UIContext.GetScratchRenderTarget(context.Prepass, ref compositeBox);
                if (context.RenderTargetStack.Count > 0)
                    context.RenderTargetStack[context.RenderTargetStack.Count - 1].Dependencies.Add(srt);
                context.RenderTargetStack.Add(srt);
                try {
                    // passSet.Above.RasterizeRectangle(box.Position, box.Extent, 1f, Color.Red * 0.1f);
                    RasterizeIntoPrepass(ref context, passSet, opacity, ref box, ref compositeBox, srt, enableCompositor);
                    // passSet.Above.RasterizeEllipse(box.Center, Vector2.One * 3f, Color.White);
                } finally {
                    context.RenderTargetStack.RemoveTail(1);
                    context.UIContext.ReleaseScratchRenderTarget(srt.Instance);
                }
            }

            return true;
        }

        private static readonly Func<ViewTransform, object, ViewTransform> ApplyLocalTransformMatrix = _ApplyLocalTransformMatrix;
        private static readonly Action<DeviceManager, object> BeforeComposite = _BeforeIssueComposite,
            AfterComposite = _AfterIssueComposite;

        // HACK
        private Margins MostRecentComputedMargins;
        private RectF MostRecentCompositeBox;
        private BitmapDrawCall MostRecentCompositeDrawCall;

        private static void _BeforeIssueComposite (DeviceManager dm, object _control) {
            var control = (Control)_control;
            control.Appearance.Compositor?.BeforeIssueComposite(control, dm, ref control.MostRecentCompositeDrawCall);
        }

        private static void _AfterIssueComposite (DeviceManager dm, object _control) {
            var control = (Control)_control;
            control.Appearance.Compositor?.AfterIssueComposite(control, dm, ref control.MostRecentCompositeDrawCall);
        }

        private static ViewTransform _ApplyLocalTransformMatrix (ViewTransform vt, object _control) {
            var control = (Control)_control;
            control.Appearance.GetFinalTransformMatrix(
                control.MostRecentCompositeBox, control.Context.NowL, out Matrix transform
            );
            vt.ModelView *= transform;
            return vt;
        }

        private void RasterizeIntoPrepass (
            ref UIOperationContext context, RasterizePassSet passSet, float opacity, 
            ref RectF box, ref RectF compositeBox, 
            UIContext.ScratchRenderTarget rt, bool enableCompositor
        ) {
            UIOperationContext compositionContext;
            context.Clone(out compositionContext);
            UpdateVisibleRegion(ref compositionContext, ref box);

            var newPassSet = new RasterizePassSet(ref rt.Renderer, 0, passSet.OverlayQueue);
            // newPassSet.Above.RasterizeEllipse(box.Center, Vector2.One * 6f, Color.White * 0.7f);
            RasterizeAllPasses(ref compositionContext, ref box, ref newPassSet, true);
            rt.Renderer.Layer += 1;
            var pos = Appearance.HasTransformMatrix ? Vector2.Zero : compositeBox.Position.Floor();
            // FIXME: Is this the right layer?
            var sourceRect = new Rectangle(
                (int)compositeBox.Left, (int)compositeBox.Top,
                (int)compositeBox.Width, (int)compositeBox.Height
            );
            var dc = new BitmapDrawCall(
                // FIXME
                rt.Instance.Get(), pos,
                GameExtensionMethods.BoundsFromRectangle((int)Context.CanvasSize.X, (int)Context.CanvasSize.Y, ref sourceRect),
                new Color(opacity, opacity, opacity, opacity), scale: 1.0f / Context.ScratchScaleFactor
            );

            if (Appearance.HasTransformMatrix || enableCompositor) {
                MostRecentCompositeDrawCall = dc;
                MostRecentCompositeBox = compositeBox;
                var subgroup = passSet.Above.MakeSubgroup(
                    before: BeforeComposite, 
                    after: AfterComposite,
                    viewTransformModifier: Appearance.HasTransformMatrix ? ApplyLocalTransformMatrix : null, 
                    userData: this
                );
                subgroup.BlendState = RenderStates.PorterDuffOver;
                if (enableCompositor)
                    Appearance.Compositor.Composite(this, ref subgroup, ref dc);
                else
                    subgroup.Draw(ref dc);
            } else if (Appearance.Overlay) {
                passSet.OverlayQueue.Add(ref dc);
            } else {
                passSet.Above.Draw(ref dc, blendState: RenderStates.PorterDuffOver);
                passSet.Above.Layer += 1;
            }
        }

        public bool TryGetParent (out Control parent) {
            if (WeakParent == null) {
                parent = null;
                return false;
            }

            return WeakParent.TryGetTarget(out parent);
        }

        protected virtual void InitializeForContext () {
            if (PendingAnimation.Animation != null)
                StartAnimationImpl(PendingAnimation.Animation, PendingAnimation.Duration, PendingAnimation.Now);
        }

        public virtual void InvalidateLayout () {
            LayoutKey = ControlKey.Invalid;
        }

        internal void SetParent (Control parent) {
            if (parent == null) {
                WeakParent = null;
                return;
            }

            Control actualParent;
            if ((WeakParent != null) && WeakParent.TryGetTarget(out actualParent)) {
                if (actualParent != parent)
                    throw new Exception("This control already has a parent");
                else
                    return;
            }

            InvalidateLayout();
            WeakParent = new WeakReference<Control>(parent, false);
            Context = parent.Context;
        }

        internal void UnsetParent (Control oldParent) {
            InvalidateLayout();

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