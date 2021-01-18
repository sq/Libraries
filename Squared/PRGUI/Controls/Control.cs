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
using Squared.PRGUI.Layout;
using Squared.Render;
using Squared.Render.Convenience;
using Squared.Render.RasterShape;
using Squared.Render.Text;
using Squared.Util;
using Squared.Util.Event;

namespace Squared.PRGUI {
    public struct ControlDimension {
        public float? Minimum, Maximum, Fixed;

        public void Constrain (ref float? size) {
            if (Minimum.HasValue && size.HasValue)
                size = Math.Max(Minimum.Value, size.Value);
            if (Maximum.HasValue && size.HasValue)
                size = Math.Min(Maximum.Value, size.Value);
        }

        public float? Constrain (float? size) {
            Constrain(ref size);
            return size;
        }
    }
    
    public abstract partial class Control {
        public static bool ShowDebugBoxes = false;

        public static readonly Controls.NullControl None = new Controls.NullControl();

        public string DebugLabel = null;

        internal int TypeID;

        public ControlAppearance Appearance;
        public Margins Margins, Padding;
        public ControlFlags LayoutFlags = ControlFlags.Layout_Fill_Row;
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

        public bool ForceBreak {
            get => LayoutFlags.IsFlagged(ControlFlags.Layout_ForceBreak);
            set => LayoutFlags = (LayoutFlags & ~ControlFlags.Layout_ForceBreak) |
                (value ? ControlFlags.Layout_ForceBreak : default(ControlFlags));
        }

        public bool IsTransparent {
            get {
                if (!Visible)
                    return true;

                var ctx = Context;
                if ((ctx != null) && (GetOpacity(ctx.NowL) <= 0))
                    return true;

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
                OnVisibilityChange(value);
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
        public bool Intangible { get; set; }

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

        protected virtual bool HasChildren => false;
        protected virtual bool ShouldClipContent => false;

        protected WeakReference<UIContext> WeakContext = null;
        protected WeakReference<Control> WeakParent = null;

        protected IControlAnimation ActiveAnimation { get; private set; }
        protected long ActiveAnimationEndWhen;

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
        }

        /// <summary>
        /// Starts an animation on the control. Any animation currently playing will be cancelled.
        /// </summary>
        /// <param name="animation">An animation to apply. If null, this method will return after canceling any existing animation.</param>
        /// <param name="duration">A custom duration for the animation. If unset, the animation's default length will be used.</param>
        public void StartAnimation (IControlAnimation animation, float? duration = null, long? now = null) {
            var _now = now ?? Context.NowL;
            UpdateAnimation(_now);
            ActiveAnimation?.End(this, true);
            ActiveAnimation = null;
            if (animation == null)
                return;
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
                        SetContext(result);
                        return result;
                    }
                }

                return null;
            }
        }

        public static bool IsRecursivelyTransparent (Control control, bool includeSelf = true) {
            if (control.IsTransparent && includeSelf)
                return true;

            var current = control;
            while (true) {
                if (!current.TryGetParent(out Control parent))
                    return false;

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

        protected virtual void OnVisibilityChange (bool newValue) {
            if (newValue == false)
                Context?.NotifyControlBecomingInvalidFocusTarget(this, false);
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
            return Context?.FireEvent(name, this, args, suppressHandler: true) ?? false;
        }

        protected bool FireEvent (string name) {
            return Context?.FireEvent(name, this, suppressHandler: true) ?? false;
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
            return OnEvent(name);
        }

        internal bool HandleEvent<T> (string name, T args) {
            return OnEvent(name, args);
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

            LayoutKey = OnGenerateLayoutTree(context, parent, existingKey);
            if (!LayoutKey.IsInvalid) {
                if (_VisibleHasChanged) {
                    context.RelayoutRequestedForVisibilityChange = true;
                    _VisibleHasChanged = false;
                }

                // TODO: Only register if the control is explicitly interested, to reduce overhead?
                if ((this is IPostLayoutListener listener) && (existingKey == null))
                    context.PostLayoutListeners?.Add(listener);
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
            if (!Appearance.SuppressDecorationMargins && (decorations != null))
                Margins.Add(ref Padding, decorations.Padding, out result);
            else
                result = Padding;
        }

        protected virtual void ComputeFixedSize (out float? fixedWidth, out float? fixedHeight) {
            var sizeScale = Appearance.AutoScaleMetrics ? Context.Decorations.SizeScaleRatio : Vector2.One;
            fixedWidth = Width.Fixed * sizeScale.X;
            fixedHeight = Height.Fixed * sizeScale.Y;
        }

        protected virtual void ComputeSizeConstraints (
            out float? minimumWidth, out float? minimumHeight,
            out float? maximumWidth, out float? maximumHeight
        ) {
            var sizeScale = Appearance.AutoScaleMetrics ? Context.Decorations.SizeScaleRatio : Vector2.One;
            minimumWidth = Width.Minimum * sizeScale.X;
            minimumHeight = Height.Minimum * sizeScale.Y;
            maximumWidth = Width.Maximum * sizeScale.X;
            maximumHeight = Height.Maximum * sizeScale.Y;
        }

        protected virtual ControlKey OnGenerateLayoutTree (UIOperationContext context, ControlKey parent, ControlKey? existingKey) {
            if (!Visible && !context.UIContext.IsUpdatingSubtreeLayout)
                return ControlKey.Invalid;

            var result = existingKey ?? context.Layout.CreateItem();

            var decorations = GetDecorator(context.DecorationProvider);
            ComputeMargins(context, decorations, out Margins computedMargins);
            ComputePadding(context, decorations, out Margins computedPadding);

            MostRecentComputedMargins = computedMargins;

            ComputeFixedSize(out float? fixedWidth, out float? fixedHeight);
            var actualLayoutFlags = ComputeLayoutFlags(fixedWidth.HasValue, fixedHeight.HasValue);

            var spacingScale = context.DecorationProvider.SpacingScaleRatio;
            Margins.Scale(ref computedMargins, ref spacingScale);
            Margins.Scale(ref computedPadding, ref spacingScale);

            context.Layout.SetLayoutFlags(result, actualLayoutFlags);
            context.Layout.SetMargins(result, computedMargins);
            context.Layout.SetPadding(result, computedPadding);
            context.Layout.SetFixedSize(result, fixedWidth ?? LayoutItem.NoValue, fixedHeight ?? LayoutItem.NoValue);

            ComputeSizeConstraints(
                out float? minimumWidth, out float? minimumHeight,
                out float? maximumWidth, out float? maximumHeight
            );

            context.Layout.SetSizeConstraints(
                result, 
                minimumWidth, minimumHeight, 
                maximumWidth, maximumHeight
            );

            if (!parent.IsInvalid && !existingKey.HasValue)
                context.Layout.InsertAtEnd(parent, result);

            return result;
        }

        protected ControlFlags ComputeLayoutFlags (bool hasFixedWidth, bool hasFixedHeight) {
            var result = LayoutFlags;
            // HACK: Clearing the fill flag is necessary for fixed sizes to work,
            //  but clearing both anchors causes the control to end up centered...
            //  and if we only clear one anchor then wrapping breaks. Awesome
            return result;
            if (hasFixedWidth && result.IsFlagged(ControlFlags.Layout_Fill_Row))
                result &= ~ControlFlags.Layout_Fill_Row;
            if (hasFixedHeight && result.IsFlagged(ControlFlags.Layout_Fill_Column))
                result &= ~ControlFlags.Layout_Fill_Column;
            return result;
        }

        protected virtual IDecorator GetDefaultDecorator (IDecorationProvider provider) {
            return null;
        }

        protected IDecorator GetDecorator (IDecorationProvider provider) {
            if (Appearance.Undecorated)
                return provider.None;

            return Appearance.Decorator ?? GetDefaultDecorator(provider);
        }

        protected IDecorator GetTextDecorator (IDecorationProvider provider) {
            return Appearance.TextDecorator ?? GetDefaultDecorator(provider);
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
                    (context.UIContext.TopLevelFocused == this) ||
                    (context.UIContext.ModalFocusDonor == this) ||
                    (context.UIContext.TopLevelModalFocusDonor == this)
                )
                    result |= ControlStates.ContainsFocus;
            }

            if ((context.UIContext.MouseCaptured == this) || (context.ActivateKeyHeld && context.UIContext.Focused == this))
                result |= ControlStates.Pressed;

            return result;
        }

        protected virtual void OnRasterize (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings, IDecorator decorations) {
            UpdateAnimation(context.NowL);
            decorations?.Rasterize(context, ref renderer, settings);
        }

        protected virtual void OnRasterizeChildren (UIOperationContext context, ref RasterizePassSet passSet, DecorationSettings settings) {
        }

        protected virtual void ApplyClipMargins (UIOperationContext context, ref RectF box) {
        }

        protected virtual DecorationSettings MakeDecorationSettings (ref RectF box, ref RectF contentBox, ControlStates state) {
            return new DecorationSettings {
                Box = box,
                ContentBox = contentBox,
                State = state,
                BackgroundColor = GetBackgroundColor(Context.NowL),
                BackgroundImage = Appearance.BackgroundImage
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

        private void RasterizePass (ref UIOperationContext context, RectF box, bool compositing, ref RasterizePassSet passSet, ref ImperativeRenderer renderer, RasterizePasses pass) {
            var contentBox = GetRect(contentRect: true);
            var decorations = GetDecorator(context.DecorationProvider);
            var state = GetCurrentState(context);

            var passContext = context.Clone();
            passContext.Pass = pass;
            // passContext.Renderer = context.Renderer.MakeSubgroup();
            var hasNestedContext = (pass == RasterizePasses.Content) && 
                (ShouldClipContent || HasChildren);
            if (hasNestedContext)
                UpdateVisibleRegion(ref passContext, ref box);

            /*
            if (pass == RasterizePasses.Above)
                renderer.RasterizeRectangle(
                    passContext.VisibleRegion.Position, passContext.VisibleRegion.Extent, 
                    0f, outlineRadius: 1.1f, innerColor: Color.Transparent, outerColor: Color.Transparent,
                    outlineColor: Color.Red
                );
            */

            var contentContext = passContext;
            // FIXME: The memset for these actually burns a measurable amount of time
            ImperativeRenderer contentRenderer = default(ImperativeRenderer);
            RasterizePassSet childrenPassSet = default(RasterizePassSet);

            int previousStackDepth = passSet.StackDepth, newStackDepth = previousStackDepth;

            // For clipping we need to create a separate batch group that contains all the rasterization work
            //  for our children. At the start of it we'll generate the stencil mask that will be used for our
            //  rendering operation(s).
            if (hasNestedContext) {
                renderer.Layer += 1;
                contentContext = passContext.Clone();
                contentRenderer = renderer.MakeSubgroup();
                if (ShouldClipContent) {
                    newStackDepth = previousStackDepth + 1;
                    contentRenderer.DepthStencilState = context.UIContext.GetStencilTest(newStackDepth);
                    childrenPassSet = new RasterizePassSet(ref passSet.Prepass, ref contentRenderer, newStackDepth, passSet.OverlayQueue);
                } else {
                    contentRenderer.DepthStencilState = 
                        (previousStackDepth <= 0)
                        ? DepthStencilState.None
                        : context.UIContext.GetStencilTest(previousStackDepth);
                    childrenPassSet = new RasterizePassSet(ref passSet.Prepass, ref contentRenderer, newStackDepth, passSet.OverlayQueue);
                }
                renderer.Layer += 1;
            }

            var settings = MakeDecorationSettings(ref box, ref contentBox, state);
            if (hasNestedContext)
                OnRasterize(contentContext, ref contentRenderer, settings, decorations);
            else
                OnRasterize(contentContext, ref renderer, settings, decorations);

            if ((pass == RasterizePasses.Content) && HasChildren)
                OnRasterizeChildren(contentContext, ref childrenPassSet, settings);

            if (hasNestedContext) {
                // GROSS OPTIMIZATION HACK: Detect that any rendering operation(s) occurred inside the
                //  group and if so, set up the stencil mask so that they will be clipped.
                if (ShouldClipContent && !contentRenderer.Container.IsEmpty) {
                    // If this is the first stencil pass instead of a nested one, clear the stencil buffer
                    if (passSet.StackDepth < 1)
                        contentRenderer.Clear(stencil: 0, layer: -9999);

                    contentRenderer.DepthStencilState = context.UIContext.GetStencilWrite(previousStackDepth);

                    // FIXME: Separate context?
                    contentContext.Pass = RasterizePasses.ContentClip;

                    // FIXME
                    box = settings.Box;
                    ApplyClipMargins(contentContext, ref box);
                    settings.Box = box;

                    contentRenderer.Layer = -999;
                    settings.State = default(ControlStates);
                    decorations.Rasterize(contentContext, ref contentRenderer, settings);

                    if (passSet.StackDepth > 1) {
                        // If this is a nested stencil pass, erase our stencil data and restore what was there before
                        contentRenderer.DepthStencilState = context.UIContext.GetStencilRestore(newStackDepth);
                        contentRenderer.FillRectangle(new Rectangle(-1, -1, 9999, 9999), Color.Transparent, blendState: RenderStates.DrawNone, layer: 9999);
                    }

                    // passSet.NextReferenceStencil = childrenPassSet.NextReferenceStencil;
                }

                renderer.Layer += 1;
            }
        }

        private void RasterizeAllPasses (ref UIOperationContext context, ref RectF box, ref RasterizePassSet passSet, bool compositing) {
            RasterizePass(ref context, box, compositing, ref passSet, ref passSet.Below, RasterizePasses.Below);
            RasterizePass(ref context, box, compositing, ref passSet, ref passSet.Content, RasterizePasses.Content);
            RasterizePass(ref context, box, compositing, ref passSet, ref passSet.Above, RasterizePasses.Above);
        }

        public bool Rasterize (ref UIOperationContext context, ref RasterizePassSet passSet, float opacity = 1) {
            // HACK: Do this first since it fires opacity change events
            opacity *= GetOpacity(context.NowL);
            if (opacity <= 0)
                return false;

            if (!Visible)
                return false;
            if (LayoutKey.IsInvalid)
                return false;

            var box = GetRect();
            var isInvisible = (box.Extent.X < context.VisibleRegion.Left) ||
                (box.Extent.Y < context.VisibleRegion.Top) ||
                (box.Left > context.VisibleRegion.Extent.X) ||
                (box.Top > context.VisibleRegion.Extent.Y) ||
                (box.Width <= 0) ||
                (box.Height <= 0);
            
            if (ShowDebugBoxes && (context.Pass == RasterizePasses.Content))
                passSet.Content.RasterizeRectangle(box.Position, box.Extent, 0f, 1f, Color.Transparent, Color.Transparent, Color.Red);

            // Only visibility cull controls that have a parent and aren't overlaid.
            if (isInvisible && TryGetParent(out Control parent) && !Appearance.Overlay)
                return false;

            var enableCompositor = Appearance.Compositor?.WillComposite(this, opacity) == true;
            var needsComposition = (opacity < 1) || 
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
                RasterizeAllPasses(ref context, ref box, ref passSet, false);
            } else {
                // HACK: Create padding around the element for drop shadows
                box.SnapAndInset(out Vector2 tl, out Vector2 br, -Context.CompositorPaddingPx);
                // Don't overflow the edges of the canvas with padding, it'd produce garbage pixels
                context.UIContext.CanvasRect.Clamp(ref tl);
                context.UIContext.CanvasRect.Clamp(ref br);

                var compositeBox = new RectF(tl, br - tl);
                var srt = context.UIContext.GetScratchRenderTarget(ref passSet.Prepass, ref compositeBox);
                try {
                    // passSet.Above.RasterizeRectangle(box.Position, box.Extent, 1f, Color.Red * 0.1f);
                    RasterizeIntoPrepass(ref context, passSet, opacity, ref box, ref compositeBox, srt, enableCompositor);
                    // passSet.Above.RasterizeEllipse(box.Center, Vector2.One * 3f, Color.White);
                } finally {
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
            var compositionContext = context.Clone();
            UpdateVisibleRegion(ref compositionContext, ref box);

            // Create nested prepass group before the RT group so that child controls have their prepass operations run before ours
            var nestedPrepass = passSet.Prepass.MakeSubgroup();

            var newPassSet = new RasterizePassSet(ref nestedPrepass, ref rt.Renderer, 0, passSet.OverlayQueue);
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
        }

        public virtual void InvalidateLayout () {
            LayoutKey = ControlKey.Invalid;
        }

        internal void SetContext (UIContext context) {
            if (_CachedContext == context)
                return;

            if ((_CachedContext != null) && (_CachedContext != context))
                throw new InvalidOperationException("UI context already set");

            _CachedContext = context;
            InitializeForContext();
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
            SetContext(parent.Context);
        }

        internal void UnsetParent (Control oldParent) {
            InvalidateLayout();

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