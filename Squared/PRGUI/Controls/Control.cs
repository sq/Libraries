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
    public interface IModal {
        /// <summary>
        /// Focus was transferred to this control from another control, and it will
        ///  be returned when this control goes away. Used for menus and modal dialogs
        /// </summary>
        Control FocusDonor { get; }
        /// <summary>
        /// While this modal is active, any tests that would normally hit a control below it will hit nothing.
        /// </summary>
        bool BlockHitTests { get; }
        /// <summary>
        /// While this modal is active, controls below it cannot receive input.
        /// </summary>
        bool BlockInput { get; }
        /// <summary>
        /// Focus will not be allowed to leave this modal while it is active.
        /// </summary>
        bool RetainFocus { get; }
        /// <summary>
        /// While this modal is active, any controls beneath it will fade out.
        /// </summary>
        bool FadeBackground { get; }
        void Show (UIContext context);
        void Close ();
        bool OnUnhandledKeyEvent (string name, KeyEventArgs args);
        bool OnUnhandledEvent (string name, IEventInfo args);
    }

    public interface IControlCompositor {
        /// <summary>
        /// Decides whether the control needs to be composited given its current state at the specified opacity.
        /// </summary>
        bool WillComposite (Control control, float opacity);
        /// <summary>
        /// Invoked immediately before the compositor draw operation is issued to the GPU. This is the appropriate
        ///  time to update material uniforms for the composite operation.
        /// </summary>
        void BeforeIssueComposite (Control control, DeviceManager dm, ref BitmapDrawCall drawCall);
        /// <summary>
        /// Composites the control into the scene using the provided renderer and draw call data.
        /// </summary>
        void Composite (Control control, ref ImperativeRenderer renderer, ref BitmapDrawCall drawCall);
        /// <summary>
        /// Invoked immediately after the compositor draw operation is issued to the GPU.
        /// If you made any state changes in BeforeIssueComposite, you should undo them here.
        /// </summary>
        void AfterIssueComposite (Control control, DeviceManager dm, ref BitmapDrawCall drawCall);
    }

    public interface IControlContainer {
        bool ClipChildren { get; set; }
        ControlFlags ContainerFlags { get; set; }
        ControlCollection Children { get; }
        /// <summary>
        /// Invoked to notify a container that one of its descendants (either a direct child or
        ///  indirect child) has received focus either as a result of user input or some other
        ///  automatic state change.
        /// </summary>
        void DescendantReceivedFocus (Control descendant, bool isUserInitiated);
    }

    public interface IScrollableControl {
        bool AllowDragToScroll { get; }
        bool Scrollable { get; set; }
        Vector2 ScrollOffset { get; }
        Vector2? MinScrollOffset { get; }
        Vector2? MaxScrollOffset { get; }
        bool TrySetScrollOffset (Vector2 value, bool forUser);
    }

    public interface IPartiallyIntangibleControl {
        bool IsIntangibleAtPosition (Vector2 position);
    }

    public interface IPostLayoutListener {
        /// <summary>
        /// This method will be invoked after the full layout pass has been completed, so this control,
        ///  its parent, and its children will all have valid boxes.
        /// </summary>
        /// <param name="relayoutRequested">Request a second layout pass (if you've changed constraints, etc)</param>
        void OnLayoutComplete (UIOperationContext context, ref bool relayoutRequested);
    }

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
    
    public abstract class Control {
        public static readonly Controls.NullControl None = new Controls.NullControl();

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

        private bool _Visible = true;
        public bool Visible {
            get {
                if (!_Visible)
                    return false;
                // HACK
                var ctx = Context;
                if (ctx != null)
                    if (GetOpacity(ctx.NowL) <= 0)
                        return false;
                return true;
            }
            set {
                _Visible = value;
            }
        }

        public bool Enabled { get; set; } = true;
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
            ) && Enabled && Visible;

        internal bool IsValidMouseInputTarget =>
            AcceptsMouseInput && Visible && !Intangible && Enabled;

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
            LayoutKey = OnGenerateLayoutTree(context, parent, existingKey);

            // TODO: Only register if the control is explicitly interested, to reduce overhead?
            if ((this is IPostLayoutListener listener) && (existingKey == null))
                context.PostLayoutListeners?.Add(listener);

            return LayoutKey;
        }

        protected virtual bool OnHitTest (RectF box, Vector2 position, bool acceptsMouseInputOnly, bool acceptsFocusOnly, ref Control result) {
            if (Intangible)
                return false;
            if (!AcceptsMouseInput && acceptsMouseInputOnly)
                return false;
            if (!AcceptsFocus && acceptsFocusOnly)
                return false;
            if ((acceptsFocusOnly || acceptsMouseInputOnly) && !Enabled)
                return false;
            if (!box.Contains(position))
                return false;

            result = this;
            return true;
        }

        public RectF GetRect (bool includeOffset = true, bool contentRect = false, UIContext context = null) {
            context = context ?? Context;
            var result = contentRect 
                ? context.Layout.GetContentRect(LayoutKey) 
                : context.Layout.GetRect(LayoutKey);

            if (includeOffset) {
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

        public Control HitTest (Vector2 position, bool acceptsMouseInputOnly, bool acceptsFocusOnly, bool ignoreIntangible = false) {
            if (!Visible)
                return null;
            if (LayoutKey.IsInvalid)
                return null;
            if (GetOpacity(Context.NowL) <= 0)
                return null;

            var result = this;
            var box = GetRect();
            position = ApplyLocalTransformToGlobalPosition(position, ref box, true);

            if (OnHitTest(box, position, acceptsMouseInputOnly, acceptsFocusOnly, ref result)) {
                var ipic = result as IPartiallyIntangibleControl;
                if (ignoreIntangible && (ipic?.IsIntangibleAtPosition(position) == true))
                    return null;

                return result;
            }

            return null;
        }

        protected virtual void ComputeMargins (UIOperationContext context, IDecorator decorations, out Margins result) {
            if (decorations != null)
                Margins.Add(ref Margins, decorations.Margins, out result);
            else
                result = Margins;
        }

        protected virtual void ComputePadding (UIOperationContext context, IDecorator decorations, out Margins result) {
            if (decorations != null)
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
            var result = existingKey ?? context.Layout.CreateItem();

            var decorations = GetDecorator(context.DecorationProvider);
            ComputeMargins(context, decorations, out Margins computedMargins);
            ComputePadding(context, decorations, out Margins computedPadding);

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
            var hasNestedContext = (pass == RasterizePasses.Content) && (ShouldClipContent || HasChildren);
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

            int previousRefStencil = passSet.ReferenceStencil;
            int nextRefStencil = passSet.NextReferenceStencil;

            // For clipping we need to create a separate batch group that contains all the rasterization work
            //  for our children. At the start of it we'll generate the stencil mask that will be used for our
            //  rendering operation(s).
            if (hasNestedContext) {
                renderer.Layer += 1;
                contentContext = passContext.Clone();
                contentRenderer = renderer.MakeSubgroup();
                if (ShouldClipContent) {
                    contentRenderer.DepthStencilState = context.UIContext.GetStencilTest(nextRefStencil);
                    childrenPassSet = new RasterizePassSet(ref passSet.Prepass, ref contentRenderer, nextRefStencil, nextRefStencil + 1, passSet.OverlayQueue);
                } else {
                    childrenPassSet = new RasterizePassSet(ref passSet.Prepass, ref contentRenderer, previousRefStencil, nextRefStencil, passSet.OverlayQueue);
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
                    if (passSet.ReferenceStencil == 0)
                        contentRenderer.Clear(stencil: 0, layer: -9999);

                    contentRenderer.DepthStencilState = context.UIContext.GetStencilWrite(previousRefStencil);

                    // FIXME: Separate context?
                    contentContext.Pass = RasterizePasses.ContentClip;

                    // FIXME
                    box = settings.Box;
                    ApplyClipMargins(contentContext, ref box);
                    settings.Box = box;

                    contentRenderer.Layer = -999;
                    settings.State = default(ControlStates);
                    decorations.Rasterize(contentContext, ref contentRenderer, settings);

                    if (passSet.ReferenceStencil != 0) {
                        // If this is a nested stencil pass, erase our stencil data and restore what was there before
                        contentRenderer.DepthStencilState = context.UIContext.GetStencilRestore(passSet.ReferenceStencil);
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

            /*
            if (context.Pass == RasterizePasses.Content)
                passSet.Content.RasterizeRectangle(box.Position, box.Extent, 0f, 1f, Color.Transparent, Color.Transparent, Color.Red);
            */

            // Only visibility cull controls that have a parent and aren't overlaid.
            if (isInvisible && TryGetParent(out Control parent) && !Appearance.Overlay)
                return false;

            var enableCompositor = Appearance.Compositor?.WillComposite(this, opacity) == true;
            var needsComposition = Appearance.HasTransformMatrix || 
                (opacity < 1) || 
                enableCompositor ||
                Appearance.Overlay;

            if (!needsComposition) {
                RasterizeAllPasses(ref context, ref box, ref passSet, false);
            } else {
                // HACK: Create padding around the element for drop shadows
                box.SnapAndInset(out Vector2 tl, out Vector2 br, -Context.CompositorPaddingPx);
                // Don't overflow the edges of the canvas with padding, it'd produce garbage pixels
                tl.X = Math.Max(tl.X, 0);
                tl.Y = Math.Max(tl.Y, 0);
                br.X = Math.Min(br.X, context.UIContext.CanvasSize.X);
                br.Y = Math.Min(br.Y, context.UIContext.CanvasSize.Y);

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

            var newPassSet = new RasterizePassSet(ref nestedPrepass, ref rt.Renderer, 0, 1, passSet.OverlayQueue);
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
                new Color(1f, 1f, 1f, opacity), scale: 1.0f / Context.ScratchScaleFactor
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
                if (enableCompositor)
                    Appearance.Compositor.Composite(this, ref subgroup, ref dc);
                else
                    subgroup.Draw(ref dc, blendState: BlendState.NonPremultiplied);
            } else if (Appearance.Overlay) {
                passSet.OverlayQueue.Add(ref dc);
            } else {
                passSet.Above.Draw(ref dc, blendState: BlendState.NonPremultiplied);
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

        internal virtual void InvalidateLayout () {
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
            return $"{GetType().Name} #{GetHashCode():X8}";
        }
    }

    public struct ColorVariable {
        public Tween<Vector4>? pLinear;

        public Tween<Color>? Color {
            set => Update(ref pLinear, value);
        }

        public Tween<pSRGBColor>? pSRGB {
            set => Update(ref pLinear, value);
        }

        internal static void Update (ref Tween<Vector4>? v4, Tween<Color>? value) {
            if (value == null) {
                v4 = null;
                return;
            }

            var v = value.Value;
            v4 = v.CloneWithNewValues(((pSRGBColor)v.From).ToPLinear(), ((pSRGBColor)v.To).ToPLinear());
        }

        internal static void Update (ref Tween<Vector4>? v4, Tween<pSRGBColor>? value) {
            if (value == null) {
                v4 = null;
                return;
            }

            var v = value.Value;
            v4 = v.CloneWithNewValues(v.From.ToPLinear(), v.To.ToPLinear());
        }

        public static implicit operator ColorVariable (Tween<Color> c) {
            var result = new ColorVariable();
            Update(ref result.pLinear, c);
            return result;
        }

        public static implicit operator ColorVariable (Color c) {
            return new ColorVariable { Color = c };
        }

        public static implicit operator ColorVariable (pSRGBColor c) {
            return new ColorVariable { pSRGB = c };
        }
    }
    
    public struct ControlAppearance {
        /// <summary>
        /// Responsible for deciding whether a control needs to be composited and performing the final
        ///  step of compositing the rendered control from its scratch texture into the scene.
        /// </summary>
        public IControlCompositor Compositor;
        public IDecorator Decorator, TextDecorator;
        public IGlyphSource Font;
        public ColorVariable BackgroundColor;
        public ColorVariable TextColor;
        public BackgroundImageSettings BackgroundImage;
        /// <summary>
        /// Suppresses clipping of the control and causes it to be rendered above everything
        ///  up until the next modal. You can use this to highlight the control responsible for
        ///  summoning a modal.
        /// </summary>
        public bool Overlay;

        internal bool HasOpacity { get; private set; }

        /// <summary>
        /// If set, the control has a non-identity transform matrix (which may be animated).
        /// </summary>
        public bool HasTransformMatrix { get; private set; }

        private bool _DoNotAutoScaleMetrics;
        private Vector2 _TransformOriginMinusOneHalf;

        /// <summary>
        /// Sets the alignment of the transform matrix to allow rotating or scaling the control
        ///  relative to one of its corners instead of the default, its center (0.5)
        /// </summary>
        public Vector2 TransformOrigin {
            get => _TransformOriginMinusOneHalf + new Vector2(0.5f);
            set => _TransformOriginMinusOneHalf = value - new Vector2(0.5f);
        }

        /// <summary>
        /// If set, the control's fixed size and size constraints will be affected by the
        ///  context's global decoration size scale ratio.
        /// </summary>
        public bool AutoScaleMetrics {
            get => !_DoNotAutoScaleMetrics;
            set => _DoNotAutoScaleMetrics = !value;
        }

        internal Tween<float> _Opacity;
        /// <summary>
        /// Adjusts the opacity of the control [0.0 - 1.0]. Any control configured to be
        ///  semiopaque will automatically become composited.
        /// </summary>
        public Tween<float> Opacity {
            get => HasOpacity ? _Opacity : 1f;
            set {
                HasOpacity = true;
                _Opacity = value;
            }
        }

        public void GetFinalTransformMatrix (RectF sourceRect, long now, out Matrix result) {
            var origin = sourceRect.Size * -TransformOrigin;
            var finalPosition = sourceRect.Position + (sourceRect.Size * TransformOrigin);
            Matrix.CreateTranslation(origin.X, origin.Y, 0, out Matrix centering);
            Matrix.CreateTranslation(finalPosition.X, finalPosition.Y, 0, out Matrix placement);
            if (GetTransform(out Matrix xform, now))
                result = centering * xform * placement;
            else
                result = Matrix.Identity;
        }

        internal Tween<Matrix> _TransformMatrix;

        /// <summary>
        /// Applies a custom transformation matrix to the control. Any control with a transform matrix
        ///  will automatically become composited. The matrix is applied once the control has been aligned
        ///  around the transform origin (the center of the control, by default) and then the control is
        ///  moved into its normal position afterwards.
        /// </summary>
        public Tween<Matrix>? Transform {
            get => _TransformMatrix;
            set {
                if (
                    (value == null) ||
                    ((value.Value.From == Matrix.Identity) && (value.Value.To == Matrix.Identity))
                ) {
                    _TransformMatrix = Matrix.Identity;
                    HasTransformMatrix = false;
                    return;
                }

                HasTransformMatrix = true;
                _TransformMatrix = value.Value;
            }
        }

        public bool GetTransform (out Matrix matrix, long now) {
            if (!HasTransformMatrix) {
                matrix = default(Matrix);
                return false;
            }

            matrix = _TransformMatrix.Get(now);
            return true;
        }

        public bool GetInverseTransform (out Matrix matrix, long now) {
            if (!HasTransformMatrix) {
                matrix = default(Matrix);
                return false;
            }

            var temp = _TransformMatrix.Get(now);
            Matrix.Invert(ref temp, out matrix);
            var det = matrix.Determinant();
            return !float.IsNaN(det) && !float.IsInfinity(det);
        }
    }

    public class FuzzyHitTest : IEnumerable<FuzzyHitTest.Result> {
        public struct Result {
            public int Depth;
            public float Distance;
            public Control Control;
            public RectF Rect;
            public Vector2 ClosestPoint;
        }

        public readonly UIContext Context;
        public Vector2 Position { get; private set; }
        private readonly List<Result> Results = new List<Result>();

        public FuzzyHitTest (UIContext context) {
            Context = context;
        }

        public void Run (Vector2 position, Func<Control, bool> predicate = null, float maxDistance = 64) {
            Results.Clear();
            Position = position;

            WalkTree(Context.Controls, position, 0, predicate, maxDistance * maxDistance);

            if (Results.Count > 1) {
                Results.Sort(ResultComparer);
                ;
            }
        }

        // Sort lowest distance first, then if the distances are similar, highest depth first
        private static int ResultComparer (Result lhs, Result rhs) {
            var result = rhs.Depth.CompareTo(lhs.Depth);
            if (result == 0) {
                double d1 = Math.Round(lhs.Distance, 1, MidpointRounding.AwayFromZero),
                    d2 = Math.Round(rhs.Distance, 1, MidpointRounding.AwayFromZero);
                result = d1.CompareTo(d2);
            }
                
            return result;
        }

        private int WalkTree (ControlCollection controls, Vector2 position, int depth, Func<Control, bool> predicate, float maxDistanceSquared) {
            var totalMatches = 0;

            var ordered = controls.InDisplayOrder(Context.FrameIndex);
            var stop = false;
            for (int i = ordered.Count - 1; (i >= 0) && !stop; i--) {
                var control = ordered[i];
                var result = new Result {
                    Depth = depth,
                    Control = control,
                    Rect = control.GetRect(context: Context)
                };

                var inside = result.Rect.Contains(position);
                stop = stop || inside;

                int localMatches = 0;
                var icc = control as IControlContainer;
                if (icc != null) {
                    localMatches = WalkTree(icc.Children, position, depth + 1, predicate, maxDistanceSquared);
                    totalMatches += localMatches;
                }

                if (localMatches > 0)
                    continue;

                if ((predicate != null) && !predicate(control))
                    continue;

                float distanceSquared;
                if (inside) {
                    result.Distance = distanceSquared = 0f;
                    result.ClosestPoint = position;
                } else {
                    result.ClosestPoint = new Vector2(
                        Arithmetic.Clamp(position.X, result.Rect.Left, result.Rect.Extent.X),
                        Arithmetic.Clamp(position.Y, result.Rect.Top,  result.Rect.Extent.Y)
                    );
                    distanceSquared = (position - result.ClosestPoint).LengthSquared();
                    if (distanceSquared > maxDistanceSquared)
                        continue;
                    result.Distance = (float)Math.Sqrt(distanceSquared);
                }

                Results.Add(result);
                totalMatches += 1;
            }

            return totalMatches;
        }

        public int Count => Results.Count;
        public Result this[int index] => Results[index];

        IEnumerator<Result> IEnumerable<Result>.GetEnumerator () => ((IEnumerable<Result>)Results).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator () => ((IEnumerable<Result>)Results).GetEnumerator();
        public List<Result>.Enumerator GetEnumerator => Results.GetEnumerator();
    }
}
