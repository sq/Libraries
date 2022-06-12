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
    
    public struct ControlAppearance {
        [Flags]
        internal enum AppearanceFlags : ushort {
            Default,
            TextColorIsDefault    = 0b1,
            Overlay               = 0b10,
            Undecorated           = 0b100,
            UndecoratedText       = 0b1000,
            DoNotAutoScaleMetrics = 0b10000,
            SuppressPadding       = 0b100000,
            SuppressMargins       = 0b1000000,
            UnscaledSpacing       = 0b10000000,
            SuppressSpacing       = SuppressPadding | SuppressMargins,
            HasTransformMatrix    = 0b100000000,
            OpacityIsSet          = 0b1000000000,
        }

        /// <summary>
        /// Responsible for deciding whether a control needs to be composited and performing the final
        ///  step of compositing the rendered control from its scratch texture into the scene.
        /// </summary>
        public IControlCompositor Compositor {
            get => _CompositorOrMaterial as IControlCompositor;
            set => _CompositorOrMaterial = value;
        }
        /// <summary>
        /// If no compositor is set, this material will be used to composite the control
        /// </summary>
        public Material CompositeMaterial {
            get => _CompositorOrMaterial as Material;
            set => _CompositorOrMaterial = value;
        }
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

        private object _CompositorOrMaterial;
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

        internal AppearanceFlags Flags;

        private bool GetAppearanceFlag (AppearanceFlags flag) {
            return (Flags & flag) == flag;
        }

        private void SetAppearanceFlag (AppearanceFlags flag, bool value) {
            if (value)
                Flags |= flag;
            else
                Flags &= ~flag;
        }

        /// <summary>
        /// If set, the TextColor will only change the default text color instead of overriding
        ///  the color set by the text decorator.
        /// </summary>
        public bool TextColorIsDefault {
            get => GetAppearanceFlag(AppearanceFlags.TextColorIsDefault);
            set => SetAppearanceFlag(AppearanceFlags.TextColorIsDefault, value);
        }

        /// <summary>
        /// Suppresses clipping of the control and causes it to be rendered above everything
        ///  up until the next modal. You can use this to highlight the control responsible for
        ///  summoning a modal.
        /// </summary>
        public bool Overlay {
            get => GetAppearanceFlag(AppearanceFlags.Overlay);
            set => SetAppearanceFlag(AppearanceFlags.Overlay, value);
        }

        /// <summary>
        /// Forces the Decorator to be None
        /// </summary>
        public bool Undecorated {
            get => GetAppearanceFlag(AppearanceFlags.Undecorated);
            set => SetAppearanceFlag(AppearanceFlags.Undecorated, value);
        }

        /// <summary>
        /// Forces the TextDecorator to be None
        /// </summary>
        public bool UndecoratedText {
            get => GetAppearanceFlag(AppearanceFlags.UndecoratedText);
            set => SetAppearanceFlag(AppearanceFlags.UndecoratedText, value);
        }

        /// <summary>
        /// Disables margins from the control's decorator
        /// </summary>
        public bool SuppressDecorationMargins {
            get => GetAppearanceFlag(AppearanceFlags.SuppressMargins);
            set => SetAppearanceFlag(AppearanceFlags.SuppressMargins, value);
        }

        /// <summary>
        /// Disables padding from the control's decorator
        /// </summary>
        public bool SuppressDecorationPadding {
            get => GetAppearanceFlag(AppearanceFlags.SuppressPadding);
            set => SetAppearanceFlag(AppearanceFlags.SuppressPadding, value);
        }

        /// <summary>
        /// Disables the decoration provider's padding/margin scale ratios
        /// </summary>
        public bool SuppressDecorationScaling {
            get => GetAppearanceFlag(AppearanceFlags.UnscaledSpacing);
            set => SetAppearanceFlag(AppearanceFlags.UnscaledSpacing, value);
        }

        public bool SuppressDecorationSpacing {
            get => GetAppearanceFlag(AppearanceFlags.SuppressSpacing);
            set => SetAppearanceFlag(AppearanceFlags.SuppressSpacing, value);
        }

        public bool HasBackgroundColor => BackgroundColor.HasValue;
        public bool HasTextColor => BackgroundColor.HasValue;

        /// <summary>
        /// If set, the control's fixed size and size constraints will be affected by the
        ///  context's global decoration size scale ratio.
        /// </summary>
        public bool AutoScaleMetrics {
            get => GetAppearanceFlag(AppearanceFlags.DoNotAutoScaleMetrics);
            set => SetAppearanceFlag(AppearanceFlags.DoNotAutoScaleMetrics, !value);
        }

        internal bool OpacityIsSet => GetAppearanceFlag(AppearanceFlags.OpacityIsSet);

        internal Tween<float> _Opacity;
        /// <summary>
        /// Adjusts the opacity of the control [0.0 - 1.0]. Any control configured to be
        ///  semiopaque will automatically become composited.
        /// </summary>
        public Tween<float> Opacity {
            get => GetAppearanceFlag(AppearanceFlags.OpacityIsSet) ? _Opacity : 1f;
            set {
                SetAppearanceFlag(AppearanceFlags.OpacityIsSet, true);
                _Opacity = value;
            }
        }

        public bool Transparent {
            set => Opacity = value ? 0 : 1;
        }

        /// <summary>
        /// If set, the control has a non-identity transform matrix (which may be animated).
        /// </summary>
        public bool HasTransformMatrix => _TransformMatrix?.HasValue == true;

        /// <summary>
        /// Sets the alignment of the transform matrix to allow rotating or scaling the control
        ///  relative to one of its corners instead of the default, its center (0.5)
        /// </summary>
        public Vector2 TransformOrigin {
            get => (_TransformMatrix?.TransformOriginMinusOneHalf + new Vector2(0.5f)) ?? Vector2.Zero;
            set {
                if (_TransformMatrix == null)
                    return;
                _TransformMatrix.TransformOriginMinusOneHalf = value - new Vector2(0.5f);
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

        private ControlMatrixInfo _TransformMatrix;

        /// <summary>
        /// Applies a custom transformation matrix to the control. Any control with a transform matrix
        ///  will automatically become composited. The matrix is applied once the control has been aligned
        ///  around the transform origin (the center of the control, by default) and then the control is
        ///  moved into its normal position afterwards.
        /// </summary>
        public Tween<Matrix>? Transform {
            get => _TransformMatrix?.Matrix;
            set {
                if (
                    (value == null) ||
                    ((value.Value.From == Matrix.Identity) && (value.Value.To == Matrix.Identity))
                ) {
                    _TransformMatrix = null;
                    return;
                }

                // Avoid an unnecessary allocation if this won't change anything
                if (_TransformMatrix?.Matrix == value)
                    return;

                // Unfortunately we have to allocate a new one every time,
                //  because 'this' could have been cloned and we would potentially be trampling a shared
                //  instance.
                _TransformMatrix = new ControlMatrixInfo {
                    HasValue = true,
                    Matrix = value.Value,
                };
            }
        }

        internal void AutoClearTransform (long now) {
            if (!HasTransformMatrix)
                return;
            
            if (_TransformMatrix.Matrix.Get(now, out Matrix m)) {
                if (m == Matrix.Identity)
                    _TransformMatrix.HasValue = false;
            }
        }

        public bool GetTransform (out Matrix matrix, long now) {
            if (!HasTransformMatrix) {
                matrix = default(Matrix);
                return false;
            }

            _TransformMatrix.Matrix.Get(now, out matrix);
            return true;
        }

        public bool GetInverseTransform (out Matrix matrix, long now) {
            if (!HasTransformMatrix) {
                matrix = default(Matrix);
                return false;
            }

            _TransformMatrix.Matrix.Get(now, out Matrix temp);
            Matrix.Invert(ref temp, out matrix);
            var det = matrix.Determinant();
            return !float.IsNaN(det) && !float.IsInfinity(det);
        }
    }

    internal class ControlMatrixInfo {
        public bool HasValue = true;
        public Tween<Matrix> Matrix;
        public Vector2 TransformOriginMinusOneHalf;
    }
}
