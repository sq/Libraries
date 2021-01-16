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
            var pLinear = new pSRGBColor(c).ToPLinear();
            return new ColorVariable { pLinear = pLinear };
        }

        public static implicit operator ColorVariable (pSRGBColor c) {
            return new ColorVariable { pLinear = c.ToPLinear() };
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
        /// <summary>
        /// Forces the Decorator to be None
        /// </summary>
        public bool Undecorated;
        /// <summary>
        /// Overrides margins and padding to be 0
        /// </summary>
        public bool SuppressMargins;

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
}
