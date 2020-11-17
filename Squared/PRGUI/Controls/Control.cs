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
using Squared.Util;

namespace Squared.PRGUI {
    public interface IControlContainer {
        ControlCollection Children { get; }
    }

    public interface IScrollableControl {
        bool AllowDragToScroll { get; }
        Vector2 ScrollOffset { get; set; }
        Vector2? MinScrollOffset { get; }
        Vector2? MaxScrollOffset { get; }
    }

    public interface IPostLayoutListener {
        /// <summary>
        /// This method will be invoked after the full layout pass has been completed, so this control,
        ///  its parent, and its children will all have valid boxes.
        /// </summary>
        /// <param name="relayoutRequested">Request a second layout pass (if you've changed constraints, etc)</param>
        void OnLayoutComplete (UIOperationContext context, ref bool relayoutRequested);
    }

    internal class ControlDataKeyComparer : IEqualityComparer<ControlDataKey> {
        public static readonly ControlDataKeyComparer Instance = new ControlDataKeyComparer();

        public bool Equals (ControlDataKey x, ControlDataKey y) {
            return x.Equals(y);
        }

        public int GetHashCode (ControlDataKey obj) {
            return obj.GetHashCode();
        }
    }

    internal struct ControlDataKey {
        public Type Type;
        public string Key;

        public bool Equals (ControlDataKey rhs) {
            return (Type == rhs.Type) &&
                string.Equals(Key, rhs.Key);
        }

        public override bool Equals (object obj) {
            if (obj is ControlDataKey)
                return Equals((ControlDataKey)obj);
            else
                return false;
        }

        public override int GetHashCode () {
            return Type.GetHashCode();
        }
    }

    public abstract class Control {
        public struct ControlDataCollection : IEnumerable<KeyValuePair<string, object>> {
            Dictionary<ControlDataKey, object> Data;

            public T Get<T> (string name = null) {
                return Get(name, default(T));
            }

            public T Get<T> (string name, T defaultValue) {
                if (Data == null)
                    return defaultValue;

                var key = new ControlDataKey { Type = typeof(T), Key = name };
                object existingValue;
                if (!Data.TryGetValue(key, out existingValue))
                    return defaultValue;
                return (T)existingValue;
            }

            public bool Set<T> (T value) {
                return Set(null, value);
            }

            public bool Set<T> (string name, T value) {
                if (Data == null)
                    Data = new Dictionary<ControlDataKey, object>(ControlDataKeyComparer.Instance);
                var key = new ControlDataKey { Type = typeof(T), Key = name };
                Data[key] = value;
                return true;
            }

            public void Add<T> (string name, T value) {
                Set(name, value);
            }

            public IEnumerator<KeyValuePair<string, object>> GetEnumerator () {
                foreach (var kvp in Data)
                    yield return new KeyValuePair<string, object>(kvp.Key.Key, kvp.Value);
            }

            IEnumerator IEnumerable.GetEnumerator () {
                return this.GetEnumerator();
            }
        }

        public class TabOrderComparer : IComparer<Control> {
            public static readonly TabOrderComparer Instance = new TabOrderComparer();

            public int Compare (Control x, Control y) {
                return x.TabOrder.CompareTo(y.TabOrder);
            }
        }

        public class PaintOrderComparer : IComparer<Control> {
            public static readonly PaintOrderComparer Instance = new PaintOrderComparer();

            public int Compare (Control x, Control y) {
                return x.PaintOrder.CompareTo(y.PaintOrder);
            }
        }

        protected bool HasTransformMatrix { get; private set; }
        protected bool HasInverseTransformMatrix { get; private set; }
        private Matrix _TransformMatrix, _InverseTransformMatrix;

        public Matrix? TransformMatrix {
            get => HasTransformMatrix ? _TransformMatrix : (Matrix?)null;
            set {
                if (value == null) {
                    _TransformMatrix = _InverseTransformMatrix = Matrix.Identity;
                    HasInverseTransformMatrix = HasTransformMatrix = false;
                    return;
                }

                HasTransformMatrix = true;
                _TransformMatrix = value.Value;
                Matrix.Invert(ref _TransformMatrix, out _InverseTransformMatrix);
                var det = _InverseTransformMatrix.Determinant();
                HasInverseTransformMatrix = !float.IsNaN(det) && !float.IsInfinity(det);
            }
        }

        public Matrix? InverseTransformMatrix {
            get {
                if (!HasTransformMatrix)
                    return null;
                if (!HasInverseTransformMatrix)
                    return null;
                return _InverseTransformMatrix;
            }
        }

        public IDecorator CustomDecorations, CustomTextDecorations;
        public Margins Margins, Padding;
        public ControlFlags LayoutFlags = ControlFlags.Layout_Fill_Row;
        public float? FixedWidth, FixedHeight;
        public float? MinimumWidth, MinimumHeight;
        public float? MaximumWidth, MaximumHeight;
        public ColorVariable BackgroundColor;
        public BackgroundImageSettings BackgroundImage = null;
        public Tween<float> Opacity = 1;
        private bool _BackgroundColorEventFired, _OpacityEventFired;

        public ControlDataCollection Data;

        // Accumulates scroll offset(s) from parent controls
        private Vector2 _AbsoluteDisplayOffset;

        internal ControlKey LayoutKey = ControlKey.Invalid;

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
        /// Controls whether textual input (IME composition, etc) should be enabled
        ///  while this control is focused. You will still get key events even if this
        ///  is false, so things like arrow key navigation will work.
        /// </summary>
        public virtual bool AcceptsTextInput { get; protected set; }
        /// <summary>
        /// Intangible controls are ignored by hit-tests
        /// </summary>
        public bool Intangible { get; set; }

        private Control _FocusBeneficiary;
        protected Control _FocusDonor;

        /// <summary>
        /// Focus was transferred to this control from another control, and it will
        ///  be returned when this control goes away. Used for menus and modal dialogs
        /// </summary>
        public Control FocusDonor => _FocusDonor;

        /// <summary>
        /// This control cannot receive focus, but input events that would give it focus will
        ///  direct focus to its beneficiary instead of being ignored
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

        internal bool IsValidFocusTarget => 
            (
                AcceptsFocus || (FocusBeneficiary != null)
            ) && Enabled && Visible;

        internal bool IsValidMouseInputTarget =>
            AcceptsMouseInput && Visible && !Intangible && Enabled;

        const int CompositePadding = 16;

        public int TabOrder { get; set; } = 0;
        public int PaintOrder { get; set; } = 0;

        public AbstractTooltipContent TooltipContent = default(AbstractTooltipContent);
        internal int TooltipContentVersion = 0;

        protected virtual bool HasChildren => false;
        protected virtual bool ShouldClipContent => false;

        protected WeakReference<UIContext> WeakContext = null;
        protected WeakReference<Control> WeakParent = null;

        private RectF LastParentRect;

        protected void InvalidateTooltip () {
            TooltipContentVersion++;
        }

        public UIContext Context {
            get {
                if (WeakContext == null) {
                    if (TryGetParent(out Control parent)) {
                        var result = parent.Context;
                        if (result != null) {
                            SetContext(result);
                            return result;
                        }
                    }
                    return null;
                } else if (WeakContext.TryGetTarget(out UIContext result))
                    return result;
                else
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

            var listener = this as IPostLayoutListener;
            // TODO: Only register if the control is explicitly interested, to reduce overhead?
            if ((listener != null) && (existingKey == null))
                context.PostLayoutListeners?.Add(listener);

            return LayoutKey;
        }

        protected virtual bool OnHitTest (LayoutContext context, RectF box, Vector2 position, bool acceptsMouseInputOnly, bool acceptsFocusOnly, ref Control result) {
            if (Intangible)
                return false;
            if (!AcceptsMouseInput && acceptsMouseInputOnly)
                return false;
            if (!AcceptsFocus && acceptsFocusOnly)
                return false;
            if ((acceptsFocusOnly || acceptsMouseInputOnly) && !Enabled)
                return false;

            if (box.Contains(position)) {
                result = this;
                return true;
            }

            return false;
        }

        public RectF GetRect (LayoutContext context, bool includeOffset = true, bool contentRect = false) {
            var result = contentRect 
                ? context.GetContentRect(LayoutKey) 
                : context.GetRect(LayoutKey);

            if (includeOffset) {
                result.Left += _AbsoluteDisplayOffset.X;
                result.Top += _AbsoluteDisplayOffset.Y;
            }
            
            return result;
        }

        protected float GetOpacity (long now) {
            return AutoFireTweenEvent(now, UIEvents.OpacityTweenEnded, ref Opacity, ref _OpacityEventFired);
        }

        protected pSRGBColor? GetBackgroundColor (long now) {
            var v4 = AutoFireTweenEvent(now, UIEvents.BackgroundColorTweenEnded, ref BackgroundColor.pLinear, ref _BackgroundColorEventFired);
            if (!v4.HasValue)
                return null;
            return pSRGBColor.FromPLinear(v4.Value);
        }

        private void ComputeCenteredTransformMatrix (Vector2 origin, Vector2 finalPosition, out Matrix result) {
            Matrix.CreateTranslation(origin.X, origin.Y, 0, out Matrix centering);
            Matrix.CreateTranslation(finalPosition.X, finalPosition.Y, 0, out Matrix placement);
            result = centering * _TransformMatrix * placement;
        }

        internal Vector2 ApplyLocalTransformToGlobalPosition (LayoutContext context, Vector2 globalPosition, ref RectF box, bool force) {
            if (!HasTransformMatrix || !HasInverseTransformMatrix)
                return globalPosition;

            var localPosition = globalPosition - box.Center;
            // Detect non-invertible transform or other messed up math

            Vector4.Transform(ref localPosition, ref _InverseTransformMatrix, out Vector4 transformedLocalPosition);
            var transformedLocal2 = new Vector2(transformedLocalPosition.X / transformedLocalPosition.W, transformedLocalPosition.Y / transformedLocalPosition.W);
            var result = transformedLocal2 + box.Center;

            if (!force && !box.Contains(result))
                return globalPosition;
            else
                return result;
        }

        public Control HitTest (LayoutContext context, Vector2 position, bool acceptsMouseInputOnly, bool acceptsFocusOnly) {
            if (!Visible)
                return null;
            if (LayoutKey.IsInvalid)
                return null;
            if (GetOpacity(Context.NowL) <= 0)
                return null;

            var result = this;
            var box = GetRect(context);
            position = ApplyLocalTransformToGlobalPosition(context, position, ref box, true);

            if (OnHitTest(context, box, position, acceptsMouseInputOnly, acceptsFocusOnly, ref result))
                return result;

            return null;
        }

        protected virtual Margins ComputeMargins (UIOperationContext context, IDecorator decorations) {
            var result = Margins;
            if (decorations != null)
                result += decorations.Margins;
            return result;
        }

        protected virtual Margins ComputePadding (UIOperationContext context, IDecorator decorations) {
            var result = Padding;
            if (decorations != null)
                result += decorations.Padding;
            return result;
        }

        protected virtual void ComputeFixedSize (out float? fixedWidth, out float? fixedHeight) {
            fixedWidth = FixedWidth;
            fixedHeight = FixedHeight;
        }

        protected virtual void ComputeSizeConstraints (
            out float? minimumWidth, out float? minimumHeight,
            out float? maximumWidth, out float? maximumHeight
        ) {
            minimumWidth = MinimumWidth;
            minimumHeight = MinimumHeight;
            maximumWidth = MaximumWidth;
            maximumHeight = MaximumHeight;
        }

        protected virtual ControlKey OnGenerateLayoutTree (UIOperationContext context, ControlKey parent, ControlKey? existingKey) {
            var result = existingKey ?? context.Layout.CreateItem();

            var decorations = GetDecorations(context.DecorationProvider);
            var computedMargins = ComputeMargins(context, decorations);
            var computedPadding = ComputePadding(context, decorations);

            ComputeFixedSize(out float? fixedWidth, out float? fixedHeight);
            var actualLayoutFlags = ComputeLayoutFlags(fixedWidth.HasValue, fixedHeight.HasValue);

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
            // FIXME: If we do this, fixed-size elements extremely are not fixed size
            if (hasFixedWidth && result.IsFlagged(ControlFlags.Layout_Fill_Row))
                result &= ~ControlFlags.Layout_Fill_Row;
            if (hasFixedHeight && result.IsFlagged(ControlFlags.Layout_Fill_Column))
                result &= ~ControlFlags.Layout_Fill_Column;
            return result;
        }

        protected virtual IDecorator GetDefaultDecorations (IDecorationProvider provider) {
            return null;
        }

        protected IDecorator GetDecorations (IDecorationProvider provider) {
            return CustomDecorations ?? GetDefaultDecorations(provider);
        }

        protected IDecorator GetTextDecorations (IDecorationProvider provider) {
            return CustomTextDecorations ?? GetDefaultDecorations(provider);
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
                if (
                    (context.UIContext.Focused == this) || 
                    (context.UIContext.Focused?.FocusDonor == this)
                ) {
                    result |= ControlStates.Focused;
                    result |= ControlStates.ContainsFocus;
                }

                if (
                    (context.UIContext.TopLevelFocused == this) ||
                    (context.UIContext.TopLevelFocusDonor == this)
                )
                    result |= ControlStates.ContainsFocus;
            }

            if ((context.UIContext.MouseCaptured == this) || (context.SpacebarHeld && context.UIContext.Focused == this))
                result |= ControlStates.Pressed;

            return result;
        }

        protected virtual void OnRasterize (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings, IDecorator decorations) {
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
                BackgroundImage = BackgroundImage
            };
        }

        private void RasterizePass (ref UIOperationContext context, RectF box, bool compositing, ref RasterizePassSet passSet, ref ImperativeRenderer renderer, RasterizePasses pass) {
            var contentBox = GetRect(context.Layout, contentRect: true);
            var decorations = GetDecorations(context.DecorationProvider);
            var state = GetCurrentState(context);

            var passContext = context.Clone();
            passContext.Pass = pass;
            // passContext.Renderer = context.Renderer.MakeSubgroup();
            var hasNestedContext = (pass == RasterizePasses.Content) && (ShouldClipContent || HasChildren);

            var contentContext = passContext;
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
                    childrenPassSet = new RasterizePassSet(ref passSet.Prepass, ref contentRenderer, nextRefStencil, nextRefStencil + 1);
                } else {
                    childrenPassSet = new RasterizePassSet(ref passSet.Prepass, ref contentRenderer, previousRefStencil, nextRefStencil);
                }
                renderer.Layer += 1;
            }

            var settings = MakeDecorationSettings(ref box, ref contentBox, state);
            if (hasNestedContext)
                OnRasterize(contentContext, ref contentRenderer, settings, decorations);
            else
                OnRasterize(contentContext, ref renderer, settings, decorations);

            if (pass == RasterizePasses.Content)
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

        public void Rasterize (ref UIOperationContext context, ref RasterizePassSet passSet, float opacity = 1) {
            // HACK: Do this first since it fires opacity change events
            opacity *= GetOpacity(context.NowL);
            if (opacity <= 0)
                return;

            if (!Visible)
                return;
            if (LayoutKey.IsInvalid)
                return;

            var box = GetRect(context.Layout);
            // HACK: To account for drop shadows and stuff
            const float visibilityPadding = 16;
            var isInvisible = (box.Extent.X < -visibilityPadding) ||
                (box.Extent.Y < -visibilityPadding) ||
                (box.Left > context.UIContext.CanvasSize.X + visibilityPadding) ||
                (box.Top > context.UIContext.CanvasSize.Y + visibilityPadding) ||
                (box.Width <= 0) ||
                (box.Height <= 0);

            // Only visibility cull controls that have a parent.
            if (isInvisible && TryGetParent(out Control parent))
                return;

            var needsComposition = HasTransformMatrix || (opacity < 1);

            if (!needsComposition) {
                RasterizeAllPasses(ref context, ref box, ref passSet, false);
            } else {
                // HACK: Create padding around the element for drop shadows
                box.SnapAndInset(out Vector2 tl, out Vector2 br, -CompositePadding);
                // Don't overflow the edges of the canvas with padding, it'd produce garbage pixels
                if (tl.X < 0)
                    tl.X = 0;
                if (tl.Y < 0)
                    tl.Y = 0;
                if (br.X > context.UIContext.CanvasSize.X)
                    br.X = context.UIContext.CanvasSize.X;
                if (br.Y > context.UIContext.CanvasSize.Y)
                    br.Y = context.UIContext.CanvasSize.Y;

                var compositeBox = new RectF(tl, br - tl);
                var rt = context.UIContext.GetScratchRenderTarget(passSet.Prepass.Container.Coordinator, ref compositeBox, out bool needClear);
                try {
                    // passSet.Above.RasterizeRectangle(box.Position, box.Extent, 1f, Color.Red * 0.1f);
                    RasterizeIntoPrepass(ref context, passSet, opacity, ref box, ref compositeBox, rt, needClear);
                    // passSet.Above.RasterizeEllipse(box.Center, Vector2.One * 3f, Color.White);
                } finally {
                    context.UIContext.ReleaseScratchRenderTarget(rt);
                }
            }
        }

        private static readonly Func<ViewTransform, object, ViewTransform> ApplyLocalTransformMatrix = _ApplyLocalTransformMatrix;
        // HACK
        private RectF MostRecentCompositeBox;

        private static ViewTransform _ApplyLocalTransformMatrix (ViewTransform vt, object _control) {
            var control = (Control)_control;
            control.ComputeCenteredTransformMatrix(control.MostRecentCompositeBox.Size * -0.5f, control.MostRecentCompositeBox.Center, out Matrix transform);
            vt.ModelView *= transform;
            return vt;
        }

        private void RasterizeIntoPrepass (ref UIOperationContext context, RasterizePassSet passSet, float opacity, ref RectF box, ref RectF compositeBox, AutoRenderTarget rt, bool needClear) {
            var compositionContext = context.Clone();

            // Create nested prepass group before the RT group so that child controls have their prepass operations run before ours
            var nestedPrepass = passSet.Prepass.MakeSubgroup();
            var compositionRenderer = passSet.Prepass.ForRenderTarget(rt, name: $"Composite control");
            compositionRenderer.DepthStencilState = DepthStencilState.None;
            compositionRenderer.BlendState = BlendState.AlphaBlend;
            if (needClear)
                compositionRenderer.Clear(color: Color.Transparent, stencil: 0, layer: -1);

            var newPassSet = new RasterizePassSet(ref nestedPrepass, ref compositionRenderer, 0, 1);
            // newPassSet.Above.RasterizeEllipse(box.Center, Vector2.One * 6f, Color.White * 0.7f);
            RasterizeAllPasses(ref compositionContext, ref box, ref newPassSet, true);
            compositionRenderer.Layer += 1;
            var pos = HasTransformMatrix ? Vector2.Zero : compositeBox.Position.Floor();
            // FIXME: Is this the right layer?
            var sourceRect = new Rectangle(
                (int)compositeBox.Left, (int)compositeBox.Top,
                (int)compositeBox.Width, (int)compositeBox.Height
            );
            var dc = new BitmapDrawCall(
                rt.Get(), pos,
                GameExtensionMethods.BoundsFromRectangle(rt.Get(), sourceRect),
                Color.White * opacity
            );

            if (HasTransformMatrix) {
                MostRecentCompositeBox = compositeBox;
                var subgroup = passSet.Above.MakeSubgroup(viewTransformModifier: ApplyLocalTransformMatrix, userData: this);
                subgroup.Draw(ref dc, blendState: BlendState.AlphaBlend);
            } else {
                passSet.Above.Draw(ref dc, blendState: BlendState.AlphaBlend);
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
            InvalidateLayout();

            if (WeakContext != null) {
                if (
                    WeakContext.TryGetTarget(out UIContext existingContext) &&
                    (existingContext != context)
                )
                throw new InvalidOperationException("UI context already set");
            }
            // HACK to handle scenarios where a tree of controls are created without a context
            if (context == null)
                return;

            WeakContext = new WeakReference<UIContext>(context, false);
            InitializeForContext();
        }

        internal void SetParent (Control parent) {
            InvalidateLayout();

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
}
