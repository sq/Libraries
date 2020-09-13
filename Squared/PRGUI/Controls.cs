using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Squared.PRGUI.Layout;
using Squared.Render.Convenience;

namespace Squared.PRGUI {
    public struct Margins {
        public float Left, Top, Right, Bottom;

        public Margins (float value) {
            Left = Top = Right = Bottom = value;
        }

        public Margins (float x, float y) {
            Left = Right = x;
            Top = Bottom = y;
        }

        public Margins (float left, float top, float right, float bottom) {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }

        public static implicit operator Vector4 (Margins margins) {
            return new Vector4(margins.Left, margins.Top, margins.Right, margins.Bottom);
        }
    }

    public struct LayoutTemplate {
        public Margins Margins;
        public Vector2? Size;
    }

    public class RasterizeContext {
        public DecorationProvider DecorationProvider;
        public LayoutContext Layout;
        public ImperativeRenderer Renderer;
        public RasterizePasses Pass;
    }
}
