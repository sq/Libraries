using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Squared.PRGUI.Decorations;
using Squared.Render;
using Squared.Render.Text;
using Squared.Util;

namespace Squared.PRGUI {
    public struct ColorVariable {
        public Tween<Vector4>? pLinear;

        public bool HasValue => pLinear.HasValue;

        public bool IsTransparent => !HasValue ||
            (pLinear.Value.IsConstant && (pLinear.Value.From.W <= 0));

        public Tween<Color>? Color {
            set => Update(ref pLinear, value);
        }

        public Tween<pSRGBColor>? pSRGB {
            set => Update(ref pLinear, value);
        }

        public Vector4? Get (long now) {
            return pLinear?.Get(now);
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
            var pLinear = new pSRGBColor(c).ToPLinear();
            return new ColorVariable { pLinear = pLinear };
        }

        public static implicit operator ColorVariable (pSRGBColor c) {
            return new ColorVariable { pLinear = c.ToPLinear() };
        }
    }

    [Flags]
    public enum ControlAppearanceFlags : byte {
        Default,
        TextColorIsDefault = 0b1,
        Overlay = 0b10,
        Undecorated = 0b100,
        UndecoratedText = 0b1000
    }

    [Flags]
    public enum DecorationSpacingMode : byte {
        Default,
        SuppressPadding = 0b1,
        SuppressMargins = 0b10,
        SuppressSpacing = SuppressPadding | SuppressMargins,
        UnscaledSpacing = 0b100
    }
    
    public struct ControlAppearance {
        /// <summary>
        /// Responsible for deciding whether a control needs to be composited and performing the final
        ///  step of compositing the rendered control from its scratch texture into the scene.
        /// </summary>
        public IControlCompositor Compositor;
        /// <summary>
        /// Specifies a custom decoration provider to use instead of the current default.
        /// Inheritable.
        /// </summary>
        public IDecorationProvider DecorationProvider;
        /// <summary>
        /// Specifies a custom decorator to use instead of the current default.
        /// </summary>
        public IDecorator Decorator;
        /// <summary>
        /// Specifies a custom decorator to use for text instead of the current default;
        /// </summary>
        public IDecorator TextDecorator;
        /// <summary>
        /// Specifies a custom list of traits to pass into the decorator during rendering.
        /// </summary>
        public DenseList<string> DecorationTraits;

        private object _GlyphSourceOrProvider;

        /// <summary>
        /// Specifies a custom glyph source to use when rendering text.
        /// </summary>
        public IGlyphSource GlyphSource {
            get {
                if (_GlyphSourceOrProvider is Func<IGlyphSource> provider)
                    return provider();
                else
                    return _GlyphSourceOrProvider as IGlyphSource;
            }
            set {
                if ((value == null) && !(_GlyphSourceOrProvider is IGlyphSource))
                    return;
                _GlyphSourceOrProvider = value;
            }
        }

        public Func<IGlyphSource> GlyphSourceProvider {
            get => _GlyphSourceOrProvider as Func<IGlyphSource>;
            set {
                if ((value == null) && !(_GlyphSourceOrProvider is Func<IGlyphSource>))
                    return;
                _GlyphSourceOrProvider = value;
            }
        }

        public ColorVariable BackgroundColor;
        public ColorVariable TextColor;
        public BackgroundImageSettings BackgroundImage;

        private ControlAppearanceFlags MiscFlags;
        public DecorationSpacingMode SpacingMode;

        /// <summary>
        /// If set, the TextColor will only change the default text color instead of overriding
        ///  the color set by the text decorator.
        /// </summary>
        public bool TextColorIsDefault {
            get => (MiscFlags & ControlAppearanceFlags.TextColorIsDefault) != default;
            set {
                MiscFlags = value
                    ? MiscFlags | ControlAppearanceFlags.TextColorIsDefault
                    : MiscFlags & ~ControlAppearanceFlags.TextColorIsDefault;
            }
        }
        /// <summary>
        /// Suppresses clipping of the control and causes it to be rendered above everything
        ///  up until the next modal. You can use this to highlight the control responsible for
        ///  summoning a modal.
        /// </summary>
        public bool Overlay {
            get => (MiscFlags & ControlAppearanceFlags.Overlay) != default;
            set {
                MiscFlags = value
                    ? MiscFlags | ControlAppearanceFlags.Overlay
                    : MiscFlags & ~ControlAppearanceFlags.Overlay;
            }
        }
        /// <summary>
        /// Forces the Decorator to be None
        /// </summary>
        public bool Undecorated {
            get => (MiscFlags & ControlAppearanceFlags.Undecorated) != default;
            set {
                MiscFlags = value
                    ? MiscFlags | ControlAppearanceFlags.Undecorated
                    : MiscFlags & ~ControlAppearanceFlags.Undecorated;
            }
        }
        /// <summary>
        /// Forces the TextDecorator to be None
        /// </summary>
        public bool UndecoratedText {
            get => (MiscFlags & ControlAppearanceFlags.UndecoratedText) != default;
            set {
                MiscFlags = value
                    ? MiscFlags | ControlAppearanceFlags.UndecoratedText
                    : MiscFlags & ~ControlAppearanceFlags.UndecoratedText;
            }
        }
        /// <summary>
        /// Disables margins from the control's decorator
        /// </summary>
        public bool SuppressDecorationMargins {
            get => (SpacingMode & DecorationSpacingMode.SuppressMargins) != default;
            set {
                SpacingMode = value
                    ? SpacingMode | DecorationSpacingMode.SuppressMargins
                    : SpacingMode & ~DecorationSpacingMode.SuppressMargins;
            }
        }
        /// <summary>
        /// Disables padding from the control's decorator
        /// </summary>
        public bool SuppressDecorationPadding {
            get => (SpacingMode & DecorationSpacingMode.SuppressPadding) != default;
            set {
                SpacingMode = value
                    ? SpacingMode | DecorationSpacingMode.SuppressPadding
                    : SpacingMode & ~DecorationSpacingMode.SuppressPadding;
            }
        }

        /// <summary>
        /// Disables the decoration provider's padding/margin scale ratios
        /// </summary>
        public bool SuppressDecorationScaling {
            get => (SpacingMode & DecorationSpacingMode.UnscaledSpacing) != default;
            set {
                SpacingMode = value
                    ? SpacingMode | DecorationSpacingMode.UnscaledSpacing
                    : SpacingMode & ~DecorationSpacingMode.UnscaledSpacing;
            }
        }

        public bool SuppressDecorationSpacing {
            get => (SpacingMode & DecorationSpacingMode.SuppressSpacing) == DecorationSpacingMode.SuppressSpacing;
            set {
                SpacingMode = value
                    ? SpacingMode | DecorationSpacingMode.SuppressSpacing
                    : SpacingMode & ~DecorationSpacingMode.SuppressSpacing;
            }
        }

        public bool HasBackgroundColor => BackgroundColor.HasValue;
        public bool HasTextColor => BackgroundColor.HasValue;

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

        public bool Transparent {
            set => Opacity = value ? 0 : 1;
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

        // TODO: Pull this out into an on-demand heap allocation since most controls won't have a transform
        //  and the size of this thing is like 160 bytes per control
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

        internal void AutoClearTransform (long now) {
            if (!HasTransformMatrix)
                return;
            
            if (_TransformMatrix.Get(now, out Matrix m)) {
                if (m == Matrix.Identity)
                    HasTransformMatrix = false;
            }
        }

        public bool GetTransform (out Matrix matrix, long now) {
            if (!HasTransformMatrix) {
                matrix = default(Matrix);
                return false;
            }

            _TransformMatrix.Get(now, out matrix);
            return true;
        }

        public bool GetInverseTransform (out Matrix matrix, long now) {
            if (!HasTransformMatrix) {
                matrix = default(Matrix);
                return false;
            }

            _TransformMatrix.Get(now, out Matrix temp);
            Matrix.Invert(ref temp, out matrix);
            var det = matrix.Determinant();
            return !float.IsNaN(det) && !float.IsInfinity(det);
        }
    }
}
