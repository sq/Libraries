using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Squared.Game;

namespace Squared.PRGUI.Layout {
    public enum LayoutTags : int {
        Default = 0,
        Spacer,
        Text,
        Window,
        Tooltip,
        TabContainer,
        TabStrip,
        TabChildBox,
        ListBox,
        Column,
        Root,
        Container,
        Group
    }

    public enum LayoutDimensions : uint {
        X = 0,
        Y = 1
    }

    // Note: X values ALWAYS need to come before Y, because we bitshift to adjust masks!
    // Except for row/column, for some reason...
    [Flags]
    public enum ControlFlags : uint {
        /// <summary>
        /// Arrange child elements left-to-right
        /// </summary>
        Container_Row    = 0b10,
        /// <summary>
        /// Arrange child elements top-to-bottom
        /// </summary>
        Container_Column = 0b11,

        /// <summary>
        /// Wrap child elements to additional rows/columns when running out of space.
        /// This also enables Layout_ForceBreak on child elements to work.
        /// </summary>
        Container_Wrap   = 0b100,

        /// <summary>
        /// Place child elements against the start of the row/column
        /// </summary>
        Container_Align_Start  = 0b1000,
        /// <summary>
        /// Center child elements within the row/column
        /// </summary>
        Container_Align_Middle = 0x00,
        /// <summary>
        /// Place child elements against the end of the row/column
        /// </summary>
        Container_Align_End    = 0b10000,
        /// <summary>
        /// Spread child elements across the entire row/column by inserting empty space.
        /// Incompatible with Container_Wrap.
        /// </summary>
        Container_Align_Justify = Container_Align_Start | Container_Align_End,

        /// <summary>
        /// Anchor to left side
        /// </summary>
        Layout_Anchor_Left   = 0b100000,
        /// <summary>
        /// Anchor to top side
        /// </summary>
        Layout_Anchor_Top    = 0b1000000,
        /// <summary>
        /// Anchor to right side
        /// </summary>
        Layout_Anchor_Right  = 0b10000000,
        /// <summary>
        /// Anchor to bottom side
        /// </summary>
        Layout_Anchor_Bottom = 0b100000000,
        /// <summary>
        /// Anchor to both left and right
        /// </summary>
        Layout_Fill_Row = Layout_Anchor_Left | Layout_Anchor_Right,
        /// <summary>
        /// Anchor to both top and bottom
        /// </summary>
        Layout_Fill_Column = Layout_Anchor_Top | Layout_Anchor_Bottom,
        /// <summary>
        /// Center vertically and horizontally (unless otherwise specified), using left/top margins as position offset
        /// </summary>
        Layout_Center = 0x000,
        /// <summary>
        /// Anchor in all four directions
        /// </summary>
        Layout_Fill = Layout_Fill_Row | Layout_Fill_Column,
        /// <summary>
        /// When wrapping, place this item on a new line.
        /// This only works if The container has Container_Wrap set.
        /// </summary>
        Layout_ForceBreak = 0b1000000000,
        /// <summary>
        /// This control does not contribute to its parent's size calculations or its siblings' layout.
        /// </summary>
        Layout_Floating   = 0b10000000000,
        /// <summary>
        /// This control does not contribute to its siblings' layout.
        /// </summary>
        Layout_Stacked    = 0b100000000000,

        Internal_Break       = 0b1000000000000,
        Internal_FixedWidth  = 0b10000000000000,
        Internal_FixedHeight = 0b100000000000000,
        /// <summary>
        /// Prevents child elements from growing past the boundaries of this container.
        /// </summary>
        Container_Constrain_Size  = 0b1000000000000000,
        /// <summary>
        /// Prevents the container from shrinking below the size required to contain its child elements.
        /// </summary>
        Container_Prevent_Crush_X = 0b10000000000000000,
        /// <summary>
        /// Prevents the container from shrinking below the size required to contain its child elements.
        /// </summary>
        Container_Prevent_Crush_Y = 0b100000000000000000,
        Container_Prevent_Crush = (Container_Prevent_Crush_X | Container_Prevent_Crush_Y),
        /// <summary>
        /// Does not expand the container to hold its children.
        /// </summary>
        Container_No_Expansion_X  = 0b1000000000000000000,
        Container_No_Expansion_Y  = 0b10000000000000000000,
        Container_No_Expansion = (Container_No_Expansion_X | Container_No_Expansion_Y),
    }

    public static class ControlFlagMask {
        public const ControlFlags 
            BoxModel = ControlFlags.Container_Column | ControlFlags.Container_Row | ControlFlags.Container_Wrap,
            Container = ControlFlags.Container_Row |
                ControlFlags.Container_Column |
                ControlFlags.Container_Wrap |
                ControlFlags.Container_Align_Start |
                ControlFlags.Container_Align_Middle |
                ControlFlags.Container_Align_End |
                ControlFlags.Container_Align_Justify |
                ControlFlags.Container_Constrain_Size |
                ControlFlags.Container_Prevent_Crush |
                ControlFlags.Container_No_Expansion,
            Layout = ControlFlags.Layout_Fill |
                ControlFlags.Layout_ForceBreak |
                ControlFlags.Layout_Floating |
                ControlFlags.Layout_Stacked,
            Fixed = ControlFlags.Internal_FixedWidth | 
                ControlFlags.Internal_FixedHeight | 
                ControlFlags.Internal_Break;
    }

    public static class PRGUIExtensions {
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

namespace Squared.PRGUI {
    [Flags]
    public enum ControlStates : uint {
        Disabled          = 0b1,
        Hovering          = 0b10,
        Focused           = 0b100,
        Pressed           = 0b1000,
        /// <summary>
        /// If a checkbox or radio button is pressed, this will be set.
        /// </summary>
        Checked           = 0b10000,
        /// <summary>
        /// If this control or one of its children is focused, this will be set.
        /// </summary>
        ContainsFocus     = 0b100000,
        /// <summary>
        /// PreRasterize handlers may be run while the control is invisible. If so, this will be set.
        /// </summary>
        Invisible         = 0b1000000,
        AnchorForTooltip  = 0b10000000,
        PreviouslyFocused = 0b100000000,
        PreviouslyHovering = 0b1000000000,
    }

    public enum RasterizePasses {
        Below,
        Content,
        Above,
        ContentClip
    }

    [Flags]
    public enum MouseButtons : int {
        None = 0,
        Left = 1,
        Right = 2,
        Middle = 4,
        X1 = 8,
        X2 = 16
    }
}