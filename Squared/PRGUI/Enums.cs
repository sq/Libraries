using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Squared.Game;

namespace Squared.PRGUI {
    public enum Dimensions : uint {
        X = 0,
        Y = 1
    }

    [Flags]
    public enum ControlFlags : uint {
        Container_Row = 0x02,
        Container_Column = 0x03,
        Container_NoWrap = 0x00,
        Container_Wrap = 0x04,
        Container_Align_Start = 0x08,
        Container_Align_Middle = 0x00,
        Container_Align_End = 0x10,
        Container_Align_Justify = 0x18,
        Container_Free = 0x00,
        Container_Flex = Container_Row,

        Layout_Left = 0x020,
        Layout_Top = 0x040,
        Layout_Right = 0x080,
        Layout_Bottom = 0x100,
        Layout_Fill_Row = 0x0a0,
        Layout_Fill_Column = 0x140,
        Layout_Center = 0x000,
        Layout_Fill = 0x1e0,
        Layout_Break = 0x200,

        Inserted = 0x400,
        HFixed = 0x800,
        VFixed = 0x1000,
    }

    public static class ControlFlagMask {
        public const ControlFlags BoxModel = (ControlFlags)0x7,
            Container = (ControlFlags)0x1f,
            Layout = (ControlFlags)0x3e0,
            Fixed = ControlFlags.HFixed | ControlFlags.VFixed;
    }

    public static class PRGUIExtensions {
        public static bool IsFlagged (this ControlFlags flags, ControlFlags flag) {
            var masked = (uint)(flags & flag);
            return masked != 0;
        }

        public static float GetOrigin (this Bounds bounds, Dimensions dimension) {
            return (dimension == Dimensions.X) ? bounds.TopLeft.X : bounds.TopLeft.Y;
        }

        public static float GetSize (this Bounds bounds, Dimensions dimension) {
            return (dimension == Dimensions.X) ? bounds.Size.X : bounds.Size.Y;
        }

        public static float GetExtent (this Bounds bounds, Dimensions dimension) {
            return (dimension == Dimensions.X) ? bounds.BottomRight.X : bounds.BottomRight.Y;
        }

        public static Bounds SetOrigin (this Bounds bounds, Dimensions dimension, float value) {
            if (dimension == Dimensions.X)
                bounds.TopLeft.X = value;
            else
                bounds.TopLeft.Y = value;
            return bounds;
        }

        public static Bounds SetSize (this Bounds bounds, Dimensions dimension, float value) {
            if (dimension == Dimensions.X)
                bounds.BottomRight.X = value + bounds.TopLeft.X;
            else
                bounds.BottomRight.Y = value + bounds.TopLeft.Y;
            return bounds;
        }

        public static Bounds SetExtent (this Bounds bounds, Dimensions dimension, float value) {
            if (dimension == Dimensions.X)
                bounds.BottomRight.X = value;
            else
                bounds.BottomRight.Y = value;
            return bounds;
        }

        public static float GetElement (this Vector2 v, uint index) {
            switch (index) {
                case 0:
                    return v.X;
                case 1:
                    return v.Y;
                default:
                    throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        public static float GetElement (this Vector4 v, uint index) {
            switch (index) {
                case 0:
                    return v.X;
                case 1:
                    return v.Y;
                case 2:
                    return v.Z;
                case 3:
                    return v.W;
                default:
                    throw new ArgumentOutOfRangeException(nameof(index));
            }
        }
    }
}
