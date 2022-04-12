using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using ControlKey = Squared.PRGUI.Layout.ControlKey;
using ControlFlags = Squared.PRGUI.Layout.ControlFlags;
using LayoutDimensions = Squared.PRGUI.Layout.LayoutDimensions;

namespace Squared.PRGUI.NewEngine {
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

        public ControlKey Key => new ControlKey(IndexPlusOne - 1);

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
        public ControlKey Key => _Key.Key;
        public ControlKey Parent => _Parent.Key;
        public ControlKey FirstChild => _FirstChild.Key;
        public ControlKey LastChild => _LastChild.Key;
        public ControlKey PreviousSibling => _PreviousSibling.Key;
        public ControlKey NextSibling => _NextSibling.Key;

        public Layout.ControlFlags Flags;
        public Margins Margins, Padding;
        public ControlDimension Width, Height;
        // TODO: Add a scroll offset value so that Control doesn't need a display offset anymore
        public Vector2 FloatingPosition;
        public Layout.LayoutTags Tag;

#if DEBUG
        // FIXME: Remove this
        public Control Control;
#endif

        public Layout.ControlFlags LayoutFlags {
            get => Flags & Layout.ControlFlagMask.Layout;
            set {
                Flags = (Flags & ~Layout.ControlFlagMask.Layout) | value;
            }
        }

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
            return $"#{Key.ID} {Tag}";
        }
    }

    public struct ControlLayoutResult {
        internal int Version;

        public RectF Rect, ContentRect;
        public Vector2 ContentSize;
        public Layout.LayoutTags Tag;
        internal bool Break;
        internal int Depth;
        internal int FirstRunIndex;

        public override string ToString () {
            var padding = new string(' ', Depth * 2);
            if (ContentSize != default)
                return $"{padding}{Tag} size {Rect.Size} csize {ContentSize} {(Break ? "break" : "")}";
            else
                return $"{padding}{Tag} size {Rect.Size} {(Break ? "break" : "")}";
        }
    }

    internal struct ControlLayoutRun {
        public ControlKeyDefaultInvalid First, Last;
        public int FlowCount, ExpandCountX, ExpandCountY;
        public float TotalWidth, TotalHeight, MaxWidth, MaxHeight;
        public int NextRunIndex;

        public override string ToString () {
            if (First.IsInvalid || Last.IsInvalid)
                return "<invalid>";

            var tail = (NextRunIndex < 0) ? " end" : " ...";
            return $"{First}..{Last} flow={FlowCount} expandX={ExpandCountX} expandY={ExpandCountY}{tail}";
        }
    }
}
