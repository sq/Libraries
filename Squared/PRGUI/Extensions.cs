using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Squared.Game;
using Squared.PRGUI.Layout;

namespace Squared.PRGUI {
    public static class PRGUIExtensions {
        // HACK: The most natural default is to flush controls left and center them vertically,
        //  though the oui default seems to have been to center both horizontally and vertically
        const float DefaultXAlignment = 0f, DefaultYAlignment = 0.5f;

        public static void GetAlignmentF (this ControlFlags flags, out float x, out float y) {
            if (flags.IsFlagged(ControlFlags.Layout_Fill_Row))
                x = DefaultXAlignment;
            else if (flags.IsFlagged(ControlFlags.Layout_Anchor_Right))
                x = 1.0f;
            else if (flags.IsFlagged(ControlFlags.Layout_Anchor_Left))
                x = 0f;
            else
                x = DefaultXAlignment;

            if (flags.IsFlagged(ControlFlags.Layout_Fill_Column))
                y = DefaultYAlignment;
            else if (flags.IsFlagged(ControlFlags.Layout_Anchor_Bottom))
                y = 1.0f;
            else if (flags.IsFlagged(ControlFlags.Layout_Anchor_Top))
                y = 0f;
            else
                y = DefaultYAlignment;
        }

        public static float GetContainerAlignmentF (this ControlFlags flags) {
            if (flags.IsFlagged(ControlFlags.Container_Align_End))
                return 1f;
            else if (flags.IsFlagged(ControlFlags.Container_Align_Start))
                return 0f;
            // FIXME: justify
            else // Middle
                return 0.5f;
        }

        public static bool IsBreak (this ControlFlags flags) {
            return IsFlagged(flags, ControlFlags.Internal_Break) ||
                IsFlagged(flags, ControlFlags.Layout_ForceBreak);
        }

        public static bool IsStackedOrFloating (this ControlFlags flags) {
            return IsFlagged(flags, ControlFlags.Layout_Stacked) || IsFlagged(flags, ControlFlags.Layout_Floating);
        }

        public static bool IsBreakDimension (this ControlFlags containerFlags, LayoutDimensions dim) {
            if (dim == LayoutDimensions.X)
                return containerFlags.IsFlagged(ControlFlags.Container_Row);
            else if (dim == LayoutDimensions.Y)
                return containerFlags.IsFlagged(ControlFlags.Container_Column);
            else
                throw new ArgumentOutOfRangeException(nameof(dim));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsFlagged (this ControlFlags flags, ControlFlags flag) {
            var masked = (uint)(flags & flag);
            return masked == (int)flag;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsFlagged (this ControlStates flags, ControlStates flag) {
            var masked = (int)(flags & flag);
            return masked == (int)flag;
        }

        public static float GetOrigin (this in Bounds bounds, LayoutDimensions dimension) {
            return (dimension == LayoutDimensions.X) ? bounds.TopLeft.X : bounds.TopLeft.Y;
        }

        public static float GetSize (this in Bounds bounds, LayoutDimensions dimension) {
            return (dimension == LayoutDimensions.X) ? bounds.Size.X : bounds.Size.Y;
        }

        public static float GetExtent (this in Bounds bounds, LayoutDimensions dimension) {
            return (dimension == LayoutDimensions.X) ? bounds.BottomRight.X : bounds.BottomRight.Y;
        }

        public static Bounds SetOrigin (this Bounds bounds, LayoutDimensions dimension, float value) {
            if (dimension == LayoutDimensions.X)
                bounds.TopLeft.X = value;
            else
                bounds.TopLeft.Y = value;
            return bounds;
        }

        public static Bounds SetSize (this Bounds bounds, LayoutDimensions dimension, float value) {
            if (dimension == LayoutDimensions.X)
                bounds.BottomRight.X = value + bounds.TopLeft.X;
            else
                bounds.BottomRight.Y = value + bounds.TopLeft.Y;
            return bounds;
        }

        public static Bounds SetExtent (this Bounds bounds, LayoutDimensions dimension, float value) {
            if (dimension == LayoutDimensions.X)
                bounds.BottomRight.X = value;
            else
                bounds.BottomRight.Y = value;
            return bounds;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetElement (ref Vector2 v, int index, float value) {
            switch (index) {
                case 0:
                    v.X = value;
                    break;
                case 1:
                    v.Y = value;
                    break;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float GetElement (this in Vector2 v, uint index) {
            return GetElement(in v, (int)index);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float GetElement (this in Vector4 v, uint index) {
            return GetElement(in v, (int)index);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float GetElement (this in Vector2 v, int index) {
            switch (index) {
                case 0:
                    return v.X;
                case 1:
                default:
                    return v.Y;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float GetElement (this in Vector4 v, int index) {
            switch (index) {
                case 0:
                    return v.X;
                case 1:
                    return v.Y;
                case 2:
                    return v.Z;
                case 3:
                default:
                    return v.W;
            }
        }
    }
}
