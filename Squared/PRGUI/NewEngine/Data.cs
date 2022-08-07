using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using ControlKey = Squared.PRGUI.Layout.ControlKey;
using ControlFlags = Squared.PRGUI.Layout.ControlFlags;
using LayoutDimensions = Squared.PRGUI.Layout.LayoutDimensions;
using Squared.PRGUI.Layout;
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
        public static ref ControlDimension Size (this ref ControlRecord record, LayoutDimensions dimension) {
            if (dimension == LayoutDimensions.X)
                return ref record.Width;
            else
                return ref record.Height;
        }
    }

    internal struct ControlKeyDefaultInvalid {
        public int IndexPlusOne;

        public ControlKey Key {
            get => new ControlKey(IndexPlusOne - 1);
            set => IndexPlusOne = value.ID + 1;
        }

        public static implicit operator ControlKeyDefaultInvalid (int index) {
            return new ControlKeyDefaultInvalid {
                IndexPlusOne = index + 1
            };
        }

        public static implicit operator ControlKeyDefaultInvalid (ControlKey value) {
            return new ControlKeyDefaultInvalid {
                IndexPlusOne = value.ID + 1
            };
        }

        public bool IsInvalid => (IndexPlusOne <= 0);

        public override string ToString () {
            return (IndexPlusOne - 1).ToString();
        }
    }

    /// <summary>
    /// Represents a box being laid out by the layout engine
    /// </summary>
    public struct ControlRecord {
        // Managed by the layout engine
        // TODO: Use a custom dense backing store and no setters
        internal ControlKeyDefaultInvalid _Key, _Parent, _FirstChild, 
            _LastChild, _PreviousSibling, _NextSibling;

        public ControlKey Key {
            get => _Key.Key;
#if DEBUG
            set => _Key.Key = value;
#endif
        }
        public ControlKey Parent {
            get => _Parent.Key;
#if DEBUG
            set => _Parent.Key = value;
#endif
        }
        public ControlKey FirstChild {
            get => _FirstChild.Key;
#if DEBUG
            set => _FirstChild.Key = value;
#endif
        }
        public ControlKey LastChild {
            get => _LastChild.Key;
#if DEBUG
            set => _LastChild.Key = value;
#endif
        }
        public ControlKey PreviousSibling {
            get => _PreviousSibling.Key;
#if DEBUG
            set => _PreviousSibling.Key = value;
#endif
        }
        public ControlKey NextSibling {
            get => _NextSibling.Key;
#if DEBUG
            set => _NextSibling.Key = value;
#endif
        }

        public Layout.ControlFlags Flags;
        public Margins Margins, Padding;
        public ControlDimension Width, Height;
        // TODO: Add a scroll offset value so that Control doesn't need a display offset anymore
        public Vector2 FloatingPosition;
        public Layout.LayoutTags Tag;

        [Unserialized]
        public Layout.ControlFlags LayoutFlags {
            get => Flags & Layout.ControlFlagMask.Layout;
            set {
                Flags = (Flags & ~Layout.ControlFlagMask.Layout) | value;
            }
        }

        [Unserialized]
        public Layout.ControlFlags ContainerFlags {
            get => Flags & Layout.ControlFlagMask.Container;
            set {
                Flags = (Flags & ~Layout.ControlFlagMask.Container) | value;
            }
        }

        public Vector2 FixedSize {
            set {
                Width = value.X;
                Height = value.Y;
            }
        }

        public bool IsValid => !Key.IsInvalid;

        public override string ToString () {
            return $"#{Key.ID} {Tag} {Flags}";
        }
    }

    public struct ControlLayoutResult {
        // TODO: Optimize this out
        internal int Version;

        /// <summary>
        /// The display/layout rectangle of the control.
        /// </summary>
        public RectF Rect;
        /// <summary>
        /// The location of the control's content area or actual content.
        /// This will be larger than Rect in scenarios where the control was clipped by its parent,
        ///  in which case you want to constrain it to the parent rect. This will prevent
        ///  clipping from making margins move in too far.
        /// </summary>
        public RectF ContentRect;
        public Layout.LayoutTags Tag;
        /// <summary>
        /// The control's position within the current run (X for row layout, Y for column layout)
        /// </summary>
        internal float PositionInRun;
        /// <summary>
        /// The index of the first run contained by this control, if any.
        /// </summary>
        internal int FirstRunIndex;
#if DEBUG
        internal bool Break;
        internal int Depth;
#endif

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

    internal struct ControlLayoutRun {
        public ControlKeyDefaultInvalid First, Last;
        public int FlowCount, ExpandCountX, ExpandCountY, NextRunIndex;
        public float TotalWidth, TotalHeight, MaxOuterWidth, MaxOuterHeight;
        public ControlFlags? XAnchor, YAnchor;

        public override string ToString () {
            if (First.IsInvalid || Last.IsInvalid)
                return "<invalid>";

            var tail = (NextRunIndex < 0) ? " end" : " ...";
            return $"{First}..{Last} flow={FlowCount} expandX={ExpandCountX} expandY={ExpandCountY}{tail}";
        }

        public void GetAlignmentF (ControlFlags containerFlags, out float x, out float y) {
            x = y = 0;

            // FIXME: Not sure this is working

            if ((XAnchor ?? default) != default) {
                XAnchor.Value.GetAlignmentF(out x, out _);
            } else if (containerFlags.IsFlagged(ControlFlags.Container_Row))
                x = containerFlags.GetContainerAlignmentF();

            if ((YAnchor ?? default) != default) {
                YAnchor.Value.GetAlignmentF(out x, out y);
            } else if (containerFlags.IsFlagged(ControlFlags.Container_Column))
                y = containerFlags.GetContainerAlignmentF();
        }
    }
}
