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
        /// This also enables Layout_ForceBreak on child elements to work in the old engine.
        /// Meaningless in the new engine.
        /// </summary>
        [Obsolete("Use Break_Auto if you actually want to automatically wrap, or Break_Allow otherwise", false)]
        Container_Wrap   = 0b100,

        /// <summary>
        /// Enables Layout_ForceBreak to work on child elements.
        /// </summary>
        Container_Break_Allow   = 0b100,

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
        /// Prevents child elements from growing past the boundaries of this container even if the container's
        ///  content is bigger than the container itself. Has no effect in the old layout engine (use Constrain_Size).
        /// </summary>
        Container_Constrain_Growth  = 0b1000000000000000,
        /// <summary>
        /// Prevents the container from shrinking below the size required to contain its child elements.
        /// This also will prevent child rectangles from being clipped on this axis even if Clip_Children is set.
        /// </summary>
        Container_Prevent_Crush_X = 0b10000000000000000,
        /// <summary>
        /// Prevents the container from shrinking below the size required to contain its child elements.
        /// This also will prevent child rectangles from being clipped on this axis even if Clip_Children is set.
        /// </summary>
        Container_Prevent_Crush_Y = 0b100000000000000000,
        Container_Prevent_Crush = (Container_Prevent_Crush_X | Container_Prevent_Crush_Y),
        /// <summary>
        /// Does not expand the container to hold its children.
        /// </summary>
        Container_No_Expansion_X  = 0b1000000000000000000,
        Container_No_Expansion_Y  = 0b10000000000000000000,
        Container_No_Expansion = (Container_No_Expansion_X | Container_No_Expansion_Y),
        /// <summary>
        /// Any child controls' rectangles will have their width and height reduced to prevent them
        ///  from extending beyond the bottom or right edges of the container.
        /// Their position will not be affected and the container's computed content size will
        ///  also not be affected.
        /// This is the default behavior for the old layout engine.
        /// </summary>
        Container_Clip_Children   = 0b100000000000000000000,
        /// <summary>
        /// Combines the effects of Container_Clip_Children and Container_Constrain_Growth.
        /// </summary>
        Container_Constrain_Size  = Container_Constrain_Growth | Container_Clip_Children,
        /// <summary>
        /// Additional breaks will automatically be inserted if content cannot fit in the container.
        /// Only meaningful in the new engine (but implies Container_Break_Allow for the old engine).
        /// In the new engine this will force the container's size to be set by its parent.
        /// </summary>
        Container_Break_Auto      = 0b1000000000000000000000 | 0b100,

        /// <summary>
        /// Indicates that FloatingPosition has a user-provided value and alignment should be suppressed.
        /// </summary>
        Internal_Has_Position     = 0b10000000000000000000000,
    }

    public static class ControlFlagMask {
        public const ControlFlags 
            BoxModel = ControlFlags.Container_Column | ControlFlags.Container_Row | ControlFlags.Container_Break_Allow,
            Container = ControlFlags.Container_Row |
                ControlFlags.Container_Column |
                ControlFlags.Container_Break_Allow |
                ControlFlags.Container_Align_Start |
                ControlFlags.Container_Align_Middle |
                ControlFlags.Container_Align_End |
                ControlFlags.Container_Align_Justify |
                ControlFlags.Container_Constrain_Growth |
                ControlFlags.Container_Clip_Children |
                ControlFlags.Container_Prevent_Crush |
                ControlFlags.Container_No_Expansion |
                ControlFlags.Container_Break_Auto,
            Layout = ControlFlags.Layout_Fill |
                ControlFlags.Layout_ForceBreak |
                ControlFlags.Layout_Floating |
                ControlFlags.Layout_Stacked,
            Fixed = ControlFlags.Internal_FixedWidth | 
                ControlFlags.Internal_FixedHeight | 
                ControlFlags.Internal_Break;
    }
}

namespace Squared.PRGUI {
    [Flags]
    public enum ControlStates : uint {
        Disabled           = 0b1,
        Hovering           = 0b10,
        Focused            = 0b100,
        Pressed            = 0b1000,
        /// <summary>
        /// If a checkbox or radio button is pressed, this will be set.
        /// </summary>
        Checked            = 0b10000,
        /// <summary>
        /// If this control or one of its children is focused, this will be set.
        /// </summary>
        ContainsFocus      = 0b100000,
        /// <summary>
        /// PreRasterize handlers may be run while the control is invisible. If so, this will be set.
        /// </summary>
        Invisible          = 0b1000000,
        AnchorForTooltip   = 0b10000000,
        PreviouslyFocused  = 0b100000000,
        PreviouslyHovering = 0b1000000000,
        MouseOver          = 0b10000000000,
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