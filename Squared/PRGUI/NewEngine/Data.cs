using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Xna.Framework;
using ControlKey = Squared.PRGUI.Layout.ControlKey;
using ControlFlags = Squared.PRGUI.Layout.ControlFlags;
using LayoutDimensions = Squared.PRGUI.Layout.LayoutDimensions;
using Squared.PRGUI.Layout;
using Squared.PRGUI.NewEngine.Enums;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
#if DEBUG
using System.Xml.Serialization;
using Unserialized = System.Xml.Serialization.XmlIgnoreAttribute;
#else
using Unserialized = Squared.PRGUI.NewEngine.DummyAttribute;
#endif

namespace Squared.PRGUI.NewEngine {
    internal class DummyAttribute : Attribute {
    }

    public static class DataExtensions {
        public static ref ControlDimension Size (this ref BoxRecord record, LayoutDimensions dimension) {
            if (dimension == LayoutDimensions.X)
                return ref record.Width;
            else
                return ref record.Height;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool HasFlag (this ContainerFlag flags, ContainerFlag flag) {
            var masked = (uint)(flags & flag);
            return masked == (int)flag;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool HasFlag (this BoxFlag flags, BoxFlag flag) {
            var masked = (uint)(flags & flag);
            return masked == (int)flag;
        }
    }

    internal struct ControlKeyDefaultInvalid {
        internal int IndexPlusOne;

        public ControlKey Key {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => new ControlKey(IndexPlusOne - 1);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => IndexPlusOne = value.ID + 1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator int (ControlKeyDefaultInvalid key) {
            return key.IndexPlusOne - 1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator ControlKeyDefaultInvalid (int index) {
            return new ControlKeyDefaultInvalid {
                IndexPlusOne = index + 1
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator ControlKeyDefaultInvalid (ControlKey value) {
            return new ControlKeyDefaultInvalid {
                IndexPlusOne = value.ID + 1
            };
        }

        public bool IsInvalid {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (IndexPlusOne <= 0);
        }

        public override string ToString () {
            return IndexPlusOne <= 0
                ? "<invalid>"
                : (IndexPlusOne - 1).ToString();
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct ControlConfiguration {
        public static readonly ControlConfiguration Default = new ControlConfiguration {
            _ContainerFlags = ContainerFlag.DEFAULT,
            _BoxFlags = BoxFlag.DEFAULT,
        };

        [Unserialized]
        internal ContainerFlag _ContainerFlags;
        [Unserialized]
        internal BoxFlag _BoxFlags;

        public ControlConfiguration (ControlFlags value) : this() {
            {
                bool aend = value.IsFlagged(ControlFlags.Container_Align_End),
                    astart = value.IsFlagged(ControlFlags.Container_Align_Start),
                    ajustify = value.IsFlagged(ControlFlags.Container_Align_Justify),
                    acenter = !aend && !astart && !ajustify;

                if ((aend && astart) || (aend && ajustify) || (astart && ajustify))
                    throw new ArgumentException("Invalid child alignment mode");

                if (aend)
                    _ContainerFlags |= ContainerFlag.Align_End;
                else if (acenter)
                    _ContainerFlags |= ContainerFlag.Align_Center;
                else if (ajustify)
                    _ContainerFlags |= ContainerFlag.Align_Justify;
                else if (astart)
                    ; // Default
            }

            {
                bool aleft = value.IsFlagged(ControlFlags.Layout_Anchor_Left),
                    atop = value.IsFlagged(ControlFlags.Layout_Anchor_Top),
                    aright = value.IsFlagged(ControlFlags.Layout_Anchor_Right),
                    abottom = value.IsFlagged(ControlFlags.Layout_Anchor_Bottom);
                if (aleft)
                    _BoxFlags |= BoxFlag.Anchor_Left;
                if (aright)
                    _BoxFlags |= BoxFlag.Anchor_Right;
                if (atop)
                    _BoxFlags |= BoxFlag.Anchor_Top;
                if (abottom)
                    _BoxFlags |= BoxFlag.Anchor_Bottom;
            }

            {
                // Break_Auto already sets this
                // if (value.IsFlagged(ControlFlags.Container_Break_Allow))
                if (value.IsFlagged(ControlFlags.Container_Break_Auto))
                    _ContainerFlags |= ContainerFlag.Arrange_Wrap;
                if (value.IsFlagged(ControlFlags.Container_Clip_Children))
                    _ContainerFlags |= ContainerFlag.Boxes_Clip;
                if (value.IsFlagged(ControlFlags.Container_Constrain_Growth))
                    _ContainerFlags |= ContainerFlag.Boxes_Constrain_Growth;
                if (!value.IsFlagged(ControlFlags.Container_No_Expansion_X))
                    _ContainerFlags |= ContainerFlag.Size_ExpandForContent_X;
                if (!value.IsFlagged(ControlFlags.Container_No_Expansion_Y))
                    _ContainerFlags |= ContainerFlag.Size_ExpandForContent_Y;
                if (value.IsFlagged(ControlFlags.Container_Prevent_Crush_X))
                    _ContainerFlags |= ContainerFlag.Size_PreventCrush_X;
                if (value.IsFlagged(ControlFlags.Container_Prevent_Crush_Y))
                    _ContainerFlags |= ContainerFlag.Size_PreventCrush_Y;
            }

            if (value.IsFlagged(ControlFlags.Container_Column))
                _ContainerFlags |= ContainerFlag.Layout_Column;

            if (value.IsFlagged(ControlFlags.Layout_Floating))
                _BoxFlags |= BoxFlag.Floating;
            if (value.IsFlagged(ControlFlags.Layout_Stacked))
                _BoxFlags |= BoxFlag.Stacked;
            if (value.IsFlagged(ControlFlags.Layout_ForceBreak))
                _BoxFlags |= BoxFlag.Break;
        }

        public uint AllFlags {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ((uint)_ContainerFlags << 0) | ((uint)_BoxFlags << 16);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set {
                _ContainerFlags = (ContainerFlag)(value & 0xFFFFu);
                _BoxFlags = (BoxFlag)((value >> 16) & 0xFFFFu);
            }
        }

        [Unserialized]
        public ChildDirection ChildDirection {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (ChildDirection)_ContainerFlags & ChildDirection.MASK;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _ContainerFlags = (ContainerFlag)
                (((ChildDirection)_ContainerFlags & ~ChildDirection.MASK) | value);
        }

        [Unserialized]
        public ChildAlignment ChildAlign {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (ChildAlignment)_ContainerFlags & ChildAlignment.MASK;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _ContainerFlags = (ContainerFlag)
                (((ChildAlignment)_ContainerFlags & ~ChildAlignment.MASK) | value);
        }

        [Unserialized]
        public ContainerFlags ChildFlags {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (ContainerFlags)_ContainerFlags & ContainerFlags.MASK;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _ContainerFlags = (ContainerFlag)
                (((ContainerFlags)_ContainerFlags & ~ContainerFlags.MASK) | value);
        }

        [Unserialized]
        public BoxAnchorMode Anchor {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (BoxAnchorMode)_BoxFlags & BoxAnchorMode.Fill;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _BoxFlags = (BoxFlag)
                (((BoxAnchorMode)_BoxFlags & ~BoxAnchorMode.Fill) | value);
        }

        [Unserialized]
        public BoxFlags Flags {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (BoxFlags)_BoxFlags & BoxFlags.MASK;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _BoxFlags = (BoxFlag)
                (((BoxFlags)_BoxFlags & ~BoxFlags.MASK) | value);
        }

        [Unserialized]
        public bool ForceBreak {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (_BoxFlags & BoxFlag.Break) != default;
            set => _BoxFlags = (_BoxFlags & ~BoxFlag.Break) | (value ? BoxFlag.Break : default);
        }
        [Unserialized]
        public bool NoMeasurement {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (_BoxFlags & BoxFlag.NoMeasurement) == BoxFlag.NoMeasurement;
            set => _BoxFlags = (_BoxFlags & ~BoxFlag.NoMeasurement) | (value ? BoxFlag.NoMeasurement : default);
        }
        [Unserialized]
        public bool AlignToParentBox {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (_BoxFlags & BoxFlag.AlignToParentBox) == BoxFlag.AlignToParentBox;
            set => _BoxFlags = (_BoxFlags & ~BoxFlag.AlignToParentBox) | (value ? BoxFlag.AlignToParentBox : default);
        }

        // TODO: Consider making these public and add setters
        [Unserialized]
        public bool Clip {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (_ContainerFlags & ContainerFlag.Boxes_Clip) == ContainerFlag.Boxes_Clip;
        }
        [Unserialized]
        public bool ConstrainGrowth {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (_ContainerFlags & ContainerFlag.Boxes_Constrain_Growth) == ContainerFlag.Boxes_Constrain_Growth;
        }
        [Unserialized]
        public bool IsVertical {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (_ContainerFlags & ContainerFlag.Layout_Column) != default;
        }
        [Unserialized]
        public bool IsStacked {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (_BoxFlags & BoxFlag.Stacked) == BoxFlag.Stacked;
        }
        [Unserialized]
        public bool IsStackedOrFloating {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (_BoxFlags & BoxFlag.Stacked) != default;
        }
        [Unserialized]
        public bool IsFloating {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (_BoxFlags & BoxFlag.Floating) == BoxFlag.Floating;
        }
        [Unserialized]
        public bool IsWrap {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (_ContainerFlags & ContainerFlag.Arrange_Wrap) != default;
        }
        [Unserialized]
        public bool FillRow {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (_BoxFlags & BoxFlag.Fill_Row) == BoxFlag.Fill_Row;
        }
        [Unserialized]
        public bool FillColumn {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (_BoxFlags & BoxFlag.Fill_Column) == BoxFlag.Fill_Column;
        }

        public bool Equals (ControlConfiguration rhs) =>
            (_BoxFlags == rhs._BoxFlags) &&
            (_ContainerFlags == rhs._ContainerFlags);

        public override bool Equals (object obj) {
            if (obj is ControlConfiguration cc)
                return Equals(cc);
            else
                return false;
        }

        public override int GetHashCode () {
            return AllFlags.GetHashCode();
        }

        public static bool operator == (ControlConfiguration lhs, ControlConfiguration rhs) => lhs.Equals(rhs);
        public static bool operator != (ControlConfiguration lhs, ControlConfiguration rhs) => !lhs.Equals(rhs);

        public override string ToString () {
            return $"{_BoxFlags} {_ContainerFlags}";
        }
        
        internal void GetAlignmentF (
            float runXAlign, float runYAlign, 
            out float xAlign, out float yAlign
        ) {
            switch (_BoxFlags & BoxFlag.Fill_Row) {
                case BoxFlag.Anchor_Left:
                    xAlign = 0f;
                    break;
                case BoxFlag.Anchor_Right:
                    xAlign = 1f;
                    break;
                case BoxFlag.Fill_Row:
                    // HACK: Flush menu items left
                    xAlign = runXAlign;
                    break;
                default:
                    xAlign = 0.5f;
                    break;
            }

            switch (_BoxFlags & BoxFlag.Fill_Column) {
                case BoxFlag.Anchor_Top:
                    yAlign = 0f;
                    break;
                case BoxFlag.Anchor_Bottom:
                    yAlign = 1f;
                    break;
                case BoxFlag.Fill_Column:
                    yAlign = runYAlign;
                    break;
                default:
                    yAlign = 0.5f;
                    break;
            }
        }

        internal void GetRunAlignmentF (out float xAlign, out float yAlign) {
            // FIXME: Default for secondary axis should be null instead of 0 i think? Or 0.5?
            switch (ChildAlign) {
                default:
                case ChildAlignment.Start:
                    xAlign = 0f;
                    yAlign = IsVertical ? 0 : 0.5f;
                    return;
                case ChildAlignment.End:
                    xAlign = IsVertical ? 0 : 1;
                    yAlign = IsVertical ? 1 : 0.5f;
                    return;
                case ChildAlignment.Center:
                    // HACK: Seems instinctively wrong, but matches layout.h
                    // This ensures that menu items are flush left
                    xAlign = IsVertical ? 0 : 0.5f;
                    yAlign = IsVertical ? 0.5f : 0;
                    return;
                case ChildAlignment.Justify:
                    throw new NotImplementedException();
                    return;
            }
        }
    }

    /// <summary>
    /// Represents a box being laid out by the layout engine
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct BoxRecord {
        // Managed by the layout engine
        // TODO: Use a custom dense backing store and no setters
        [Unserialized]
        internal ControlKeyDefaultInvalid _Key, _Parent, _FirstChild, 
            _LastChild, _PreviousSibling, _NextSibling;

        public ControlKey Key {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _Key.Key;
#if DEBUG
            set => _Key.Key = value;
#endif
        }
        public ControlKey Parent {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _Parent.Key;
#if DEBUG
            set => _Parent.Key = value;
#endif
        }
        public ControlKey FirstChild {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _FirstChild.Key;
#if DEBUG
            set => _FirstChild.Key = value;
#endif
        }
        public ControlKey LastChild {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _LastChild.Key;
#if DEBUG
            set => _LastChild.Key = value;
#endif
        }
        public ControlKey PreviousSibling {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]            
            get => _PreviousSibling.Key;
#if DEBUG
            set => _PreviousSibling.Key = value;
#endif
        }
        public ControlKey NextSibling {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _NextSibling.Key;
#if DEBUG
            set => _NextSibling.Key = value;
#endif
        }

        public ControlConfiguration Config;

        public Margins Margins, Padding;
        public ControlDimension Width, Height;
        // TODO: Add a scroll offset value so that Control doesn't need a display offset anymore
        public Vector2? FloatingPosition;
        public Layout.LayoutTags Tag;

#if DEBUG
        public string DebugLabel;
#endif

        [Unserialized]
        internal ControlFlags _OldFlags;

        public ControlFlags OldFlags {
#if DEBUG
            get => _OldFlags;
#else
            internal get => _OldFlags;
#endif
            set {
                _OldFlags = value;
                Config = new ControlConfiguration(value);
            }
        }

        [Unserialized]
        public ControlFlags OldLayoutFlags {
            internal get => _OldFlags & ControlFlagMask.Layout;
            set => OldFlags = (_OldFlags & ControlFlagMask.Container) | (value & ControlFlagMask.Layout);
        }

        [Unserialized]
        public ControlFlags OldContainerFlags {
            internal get => _OldFlags & ControlFlagMask.Container;
            set => OldFlags = (_OldFlags & ControlFlagMask.Layout) | (value & ControlFlagMask.Container);
        }

        // TODO: When controls are overflowing/being clipped, we want to clip the lowest priority
        //  controls first in order to spare the high priority ones. This mostly is relevant for
        //  toolbars and I haven't needed it yet
        // int Priority;

        public Vector2 FixedSize {
            set {
                Width = value.X;
                Height = value.Y;
            }
        }

        public bool IsValid => !Key.IsInvalid;

        public static implicit operator ControlKey (in BoxRecord rec) => rec.Key;

        public override string ToString () {
            return $"#{Key.ID} {Tag} {Config}";
        }

        internal void ConvertProportionsToMaximums (float parentWidth, float parentHeight, out ControlDimension width, out ControlDimension height) {
            width = Width.ConvertPercentageToMaximum(parentWidth);
            height = Height.ConvertPercentageToMaximum(parentHeight);
        }
    }

    /// <summary>
    /// Represents the state of the layout engine for a given box
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct BoxLayoutResult {
        public Layout.LayoutTags Tag;

        // TODO: Optimize this out
        internal int Version;
        /// <summary>
        /// The index of the first run contained by this control, if any.
        /// </summary>
        internal int FirstRunIndex;
        /// <summary>
        /// The index of the run that contains all this control's floating/stacked children.
        /// </summary>
        internal int FloatingRunIndex;
        internal int ParentRunIndex;
#if DEBUG
        internal int Depth;
        internal bool Break;
#endif
        /// <summary>
        /// Must be true before wrapping can occur
        /// </summary>
        internal bool SizeSetByParent;
        // FIXME: Remove all these
        internal bool Pass1Ready, Pass1Processed, Pass2Processed;

        /// <summary>
        /// The display/layout rectangle of the control.
        /// </summary>
        public RectF Rect;
        /// <summary>
        /// The display/layout rectangle for the control's content (taking into account padding.)
        /// </summary>
        public RectF ContentRect;
        /// <summary>
        /// The size of the control's children.
        /// This will be larger than Rect in scenarios where the control was clipped by its parent,
        ///  in which case you want to constrain it to the parent rect. This will prevent
        ///  clipping from making margins move in too far.
        /// </summary>
        public Vector2 ContentSize;
        /// <summary>
        /// Records the size that this control's parent attempted to set before constraints took
        ///  effect. You can use this to influence control auto-size behavior.
        /// </summary>
        public Vector2 AvailableSpace;

        public override string ToString () {
#if DEBUG
            var padding = new string(' ', Depth * 2);
            var br = Break;
#else
            var padding = string.Empty;
            var br = false;
#endif
            return $"{padding}{Tag} size {Rect.Size} {(br ? "break" : "")}";
        }
    }

    /// <summary>
    /// Represents the state of the layout engine for a given run
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct LayoutRun {
        public int Index;
        public ControlKeyDefaultInvalid First, Last;
        public int FlowCount, ExpandCountX, ExpandCountY, NextRunIndex;
        public float TotalWidth, TotalHeight, MaxOuterWidth, MaxOuterHeight;
        public bool IsVertical, IsFloating;

        public override string ToString () {
            if (First.IsInvalid || Last.IsInvalid)
                return "<invalid>";

            var tail = (NextRunIndex < 0) ? " end" : " ...";
            return $"#{Index} {First}..{Last} flow={FlowCount} expandX={ExpandCountX} expandY={ExpandCountY}{tail}";
        }
    }
}
