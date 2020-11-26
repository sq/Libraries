using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Squared.Game;
using Squared.Util;

namespace Squared.PRGUI.Layout {
    public struct ControlKey {
        public static readonly ControlKey Invalid = new ControlKey(-1);

        internal int ID;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ControlKey (int id) {
            ID = id;
        }

        public bool IsInvalid {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                return ID < 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals (ControlKey rhs) {
            return ID == rhs.ID;
        }

        public override bool Equals (object obj) {
            if (obj is ControlKey)
                return Equals((ControlKey)obj);
            else
                return false;
        }

        public override int GetHashCode () {
            return ID.GetHashCode();
        }

        public override string ToString () {
            return $"[control {ID}]";
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator == (ControlKey lhs, ControlKey rhs) {
            return lhs.Equals(rhs);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator != (ControlKey lhs, ControlKey rhs) {
            return !lhs.Equals(rhs);
        }
    }

    public class ControlKeyComparer : 
        IComparer<ControlKey>, IRefComparer<ControlKey>, IEqualityComparer<ControlKey> 
    {
        public static readonly ControlKeyComparer Instance = new ControlKeyComparer();

        public int Compare (ref ControlKey lhs, ref ControlKey rhs) {
            return lhs.ID.CompareTo(rhs.ID);
        }

        public bool Equals (ref ControlKey lhs, ref ControlKey rhs) {
            return lhs.ID == rhs.ID;
        }

        public bool Equals (ControlKey lhs, ControlKey rhs) {
            return lhs.ID == rhs.ID;
        }

        public int GetHashCode (ControlKey key) {
            return key.ID.GetHashCode();
        }

        int IComparer<ControlKey>.Compare (ControlKey lhs, ControlKey rhs) {
            return lhs.ID.CompareTo(rhs.ID);
        }
    }

    public struct LayoutItem {
        public const float NoValue = -1;
        public static readonly Vector2 NoSize = new Vector2(NoValue);

        public readonly ControlKey Key;

        public ControlFlags Flags;
        public ControlKey Parent, FirstChild, LastChild;
        public ControlKey PreviousSibling, NextSibling;
        public Margins Margins, Padding;
        public Vector2 FixedSize, MinimumSize, MaximumSize;

        public LayoutItem (ControlKey key) {
            Key = key;
            Flags = default(ControlFlags);
            Parent = FirstChild = LastChild = PreviousSibling = NextSibling = ControlKey.Invalid;
            Margins = Padding = default(Margins);
            FixedSize = MinimumSize = MaximumSize = NoSize;
        }
    }

    public struct ComputedLayout {
        public readonly ControlKey Key;
        public readonly RectF Rect, ParentRect;

        public Bounds ParentBounds {
            get {
                return (Bounds)ParentRect;
            }
        }

        public Bounds Bounds {
            get {
                return (Bounds)Rect;
            }
        }
    }

    public unsafe partial class LayoutContext : IDisposable {
        public const int DefaultCapacity = 1024;

        public int Count {
            get {
                if (Layout.Count != Boxes.Count)
                    InvalidState();
                return Layout.Count;
            }
        }

        public bool IsDisposed { get; private set; }

        public ControlKey Root = ControlKey.Invalid;

        private int _Count;
        private int Version;
        private int LayoutBufferVersion = -1, BoxesBufferVersion = -1;
        private int LayoutBufferOffset, BoxesBufferOffset;
        private GCHandle LayoutPin, BoxesPin;
        private LayoutItem[] PinnedLayoutArray;
        private RectF[] PinnedBoxesArray;
        private LayoutItem* PinnedLayoutPtr;
        private RectF* PinnedBoxesPtr;
        private readonly UnorderedList<LayoutItem> Layout = new UnorderedList<LayoutItem>(DefaultCapacity);
        private readonly UnorderedList<RectF> Boxes = new UnorderedList<RectF>(DefaultCapacity);
        private Vector2 _CanvasSize;

        public Vector2 CanvasSize {
            get => _CanvasSize;
            set {
                if (value == _CanvasSize)
                    return;
                _CanvasSize = value;
                if (!Root.IsInvalid)
                    SetFixedSize(Root, value);
            }
        }

        public void EnsureCapacity (int capacity) {
            Version++;
            Layout.EnsureCapacity(capacity);
            Boxes.EnsureCapacity(capacity);
        }

        public LayoutItem this [ControlKey key] {
            get {
                return Layout.DangerousGetItem(key.ID);
            }
        }

        public bool TryGetItem (ref ControlKey key, out LayoutItem result) {
            return Layout.DangerousTryGetItem(key.ID, out result);
        }

        public bool TryGetItem (ControlKey key, out LayoutItem result) {
            return Layout.DangerousTryGetItem(key.ID, out result);
        }

        public bool TryGetFirstChild (ControlKey key, out LayoutItem result) {
            if (!Layout.DangerousTryGetItem(key.ID, out result))
                return false;
            var firstChild = result.FirstChild;
            return Layout.DangerousTryGetItem(firstChild.ID, out result);
        }

        public bool TryGetPreviousSibling (ControlKey key, out LayoutItem result) {
            if (!Layout.DangerousTryGetItem(key.ID, out result))
                return false;
            var previousSibling = result.PreviousSibling;
            return Layout.DangerousTryGetItem(previousSibling.ID, out result);
        }

        public bool TryGetNextSibling (ControlKey key, out LayoutItem result) {
            if (!Layout.DangerousTryGetItem(key.ID, out result))
                return false;
            var nextSibling = result.NextSibling;
            return Layout.DangerousTryGetItem(nextSibling.ID, out result);
        }

        public RectF GetRect (ControlKey key) {
            if (key.IsInvalid)
                return default(RectF);

            return Boxes.DangerousGetItem(key.ID);
        }

        private unsafe RectF GetContentRect (LayoutItem * pItem, ref RectF exterior) {
            Vector2 extent;
            RectF interior;
            if (pItem->Key.ID == Root.ID) {
                // FIXME: Why is this necessary?
                extent = CanvasSize;
                // HACK
                exterior = interior = new RectF(Vector2.Zero, extent);
            } else {
                extent = exterior.Extent;
                interior = exterior;
            }
            interior.Left = Math.Min(extent.X, interior.Left + pItem->Padding.Left);
            interior.Top = Math.Min(extent.Y, interior.Top + pItem->Padding.Top);
            interior.Width = Math.Max(0, exterior.Width - pItem->Padding.X);
            interior.Height = Math.Max(0, exterior.Height - pItem->Padding.Y);
            return interior;
        }

        private unsafe RectF GetContentRect (LayoutItem * pItem) {
            var pExterior = RectPtr(pItem->Key);
            return GetContentRect(pItem, ref *pExterior);
        }

        public unsafe RectF GetContentRect (ControlKey key) {
            if (key.IsInvalid)
                return default(RectF);

            var pItem = LayoutPtr(key);
            var pExterior = RectPtr(key);
            return GetContentRect(pItem, ref *pExterior);
        }

        private void SetRect (ControlKey key, ref RectF newRect) {
            if (key.IsInvalid)
                throw new ArgumentOutOfRangeException("key");

            Boxes.DangerousSetItem(key.ID, ref newRect);
        }

        public bool TryGetRect (ControlKey key, out RectF result) {
            return Boxes.DangerousTryGetItem(key.ID, out result);
        }

        public bool TryGetRect (ControlKey key, out float x, out float y, out float width, out float height) {
            x = y = width = height = 0;
            RectF result;
            if (!Boxes.DangerousTryGetItem(key.ID, out result))
                return false;
            x = result.Left;
            y = result.Top;
            width = result.Width;
            height = result.Height;
            return true;
        }

        private unsafe void UpdateLayoutPin () {
            if (LayoutPin.IsAllocated)
                LayoutPin.Free();
            var buffer = Layout.GetBuffer();
            LayoutBufferVersion = Layout.BufferVersion;
            LayoutBufferOffset = buffer.Offset;
            PinnedLayoutArray = buffer.Array;
            LayoutPin = GCHandle.Alloc(buffer.Array, GCHandleType.Pinned);
            PinnedLayoutPtr = ((LayoutItem*)LayoutPin.AddrOfPinnedObject()) + LayoutBufferOffset;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe LayoutItem * LayoutPtr () {
            if (Layout.BufferVersion != LayoutBufferVersion)
                UpdateLayoutPin();
            return (LayoutItem*)PinnedLayoutPtr;
        }

        private unsafe LayoutItem * LayoutPtr (ControlKey key, bool optional = false) {
            var id = key.ID;
            if (id < 0) {
                if (optional)
                    return null;
                else
                    throw new ArgumentOutOfRangeException(nameof(key));
            }

            if (id >= _Count)
                throw new ArgumentOutOfRangeException(nameof(key));

            if (Layout.BufferVersion != LayoutBufferVersion)
                UpdateLayoutPin();

            var buf = (LayoutItem*)PinnedLayoutPtr;
            var result = &buf[id];
            /*
            if (result->Key.ID != id)
                InvalidState();
            */
            return result;
        }

        private unsafe void UpdateBoxesPin () {
            if (BoxesPin.IsAllocated)
                BoxesPin.Free();
            var buffer = Boxes.GetBuffer();
            BoxesBufferVersion = Boxes.BufferVersion;
            BoxesBufferOffset = buffer.Offset;
            PinnedBoxesArray = buffer.Array;
            BoxesPin = GCHandle.Alloc(buffer.Array, GCHandleType.Pinned);
            PinnedBoxesPtr = ((RectF*)BoxesPin.AddrOfPinnedObject()) + BoxesBufferOffset;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe RectF * BoxesPtr () {
            if (Boxes.BufferVersion != BoxesBufferVersion)
                UpdateBoxesPin();
            return (RectF*)PinnedBoxesPtr;
        }

        private unsafe RectF * RectPtr (ControlKey key, bool optional = false) {
            if (key.ID < 0) {
                if (optional)
                    return null;
                else
                    throw new ArgumentOutOfRangeException(nameof(key));
            } else if (key.ID >= _Count)
                throw new ArgumentOutOfRangeException(nameof(key));

            if (Boxes.BufferVersion != BoxesBufferVersion)
                UpdateBoxesPin();
            var buf = (RectF*)PinnedBoxesPtr;
            var result = &buf[key.ID];
            return result;
        }

        public void Clear () {
            Dispose();
            Initialize();
        }

        public void Dispose () {
            Version++;

            if (IsDisposed)
                return;

            IsDisposed = true;
            Root = ControlKey.Invalid;
            LayoutBufferVersion = BoxesBufferVersion = -1;
            if (LayoutPin.IsAllocated)
                LayoutPin.Free();
            if (BoxesPin.IsAllocated)
                BoxesPin.Free();
            Layout.Clear();
            Boxes.Clear();
            _Count = 0;
        }

        public void Initialize () {
            Version++;

            IsDisposed = false;
            Root = CreateItem();
            SetFixedSize(Root, _CanvasSize);
        }
    }
}

namespace Squared.PRGUI {
    public struct RectF {
        public float Left, Top, Width, Height;

        public RectF (float left, float top, float width, float height) {
            Left = left;
            Top = top;
            Width = width;
            Height = height;
        }

        public RectF (Vector2 origin, Vector2 size) {
            Left = origin.X;
            Top = origin.Y;
            Width = size.X;
            Height = size.Y;
        }

        public float this [uint index] {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                return this[(int)index];
            }
            set {
                this[(int)index] = value;
            }
        }

        public float this [int index] { 
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                switch (index) {
                    case 0:
                        return Left;
                    case 1:
                        return Top;
                    case 2:
                        return Width;
                    case 3:
                        return Height;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(index));
                }
            }
            set {
                switch (index) {
                    case 0:
                        Left = value;
                        break;
                    case 1:
                        Top = value;
                        break;
                    case 2:
                        Width = value;
                        break;
                    case 3:
                        Height = value;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(index));
                }
            }
        }

        public Vector2 Position {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                return new Vector2(Left, Top);
            }
            set {
                Left = value.X;
                Top = value.Y;
            }
        }

        public Vector2 Size {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                return new Vector2(Width, Height);
            }
            set {
                Width = value.X;
                Height = value.Y;
            }
        }

        public Vector2 Center {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                return new Vector2(Left + (Width / 2f), Top + (Height / 2f));
            }
        }

        public Vector2 Extent {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                return new Vector2(Left + Width, Top + Height);
            }
        }

        public bool Intersection (ref RectF rhs, out RectF result) {
            var e1 = Extent;
            var e2 = rhs.Extent;
            var x1 = Math.Max(Left, rhs.Left);
            var y1 = Math.Max(Top, rhs.Top);
            var x2 = Math.Min(e1.X, e2.X);
            var y2 = Math.Min(e1.Y, e2.Y);
            if (x2 < x1)
                x1 = x2 = (x2 + x1) / 2f;
            if (y2 < y1)
                y1 = y2 = (y2 + y1) / 2f;
            result = new RectF(x1, y1, x2 - x1, y2 - y1);
            return ((result.Width > 0) && (result.Height > 0));
        }

        public bool Intersects (ref RectF rhs) {
            Vector2 tl, br;
            tl.X = Math.Max(Left, rhs.Left);
            tl.Y = Math.Max(Top, rhs.Top);
            br.X = Math.Min(Left + Width, rhs.Left + rhs.Width);
            br.Y = Math.Min(Top + Height, rhs.Top + rhs.Height);
            return (br.X > tl.X) && (br.Y > tl.Y);
        }

        public bool Contains (Vector2 position) {
            return (position.X >= Left) &&
                (position.X <= (Left + Width)) &&
                (position.Y >= Top) &&
                (position.Y <= (Top + Height));
        }

        public static explicit operator Bounds (RectF self) {
            return new Bounds(
                new Vector2(self.Left, self.Top),
                new Vector2(self.Left + self.Width, self.Top + self.Height)
            );
        }

        public static explicit operator RectF (Bounds self) {
            return new RectF(
                self.TopLeft,
                self.BottomRight - self.TopLeft
            );
        }

        public void SnapAndInset (out Vector2 a, out Vector2 b, float inset = 0) {
            a = new Vector2(Left + inset, Top + inset);
            b = new Vector2(Left + Width - inset, Top + Height - inset);
            // HACK: Snap to integral pixels so that edges don't look uneven
            a = a.Floor();
            b = b.Floor();
            if (a.X > b.X)
                a.X = b.X = Left;
            if (a.Y > b.Y)
                a.Y = b.Y = Top;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals (RectF rhs) {
            return (Left == rhs.Left) &&
                (Top == rhs.Top) &&
                (Width == rhs.Width) &&
                (Height == rhs.Height);
        }

        public override bool Equals (object obj) {
            if (!(obj is RectF))
                return false;

            return Equals((RectF)obj);
        }

        public override int GetHashCode () {
            return Left.GetHashCode() ^ Top.GetHashCode() ^ Width.GetHashCode() ^ Height.GetHashCode();
        }

        public override string ToString () {
            return $"({Left}, {Top}) {Width}x{Height}";
        }
    }

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

        public bool Equals (Margins rhs) {
            return Equals(ref rhs);
        }

        public bool Equals (ref Margins rhs) {
            return (Left == rhs.Left) &&
                (Top == rhs.Top) &&
                (Right == rhs.Right) &&
                (Bottom == rhs.Bottom);
        }

        public override bool Equals (object obj) {
            if (obj is Margins)
                return Equals((Margins)obj);
            else
                return false;
        }

        public float this[int index] {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                return this[(uint)index];
            }
            set {
                this[(uint)index] = value;
            }
        }

        public float this[uint index] {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                switch (index) {
                    case 0:
                        return Left;
                    case 1:
                        return Top;
                    case 2:
                        return Right;
                    case 3:
                        return Bottom;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
            set {
                switch (index) {
                    case 0:
                        Left = value;
                        return;
                    case 1:
                        Top = value;
                        return;
                    case 2:
                        Right = value;
                        return;
                    case 3:
                        Bottom = value;
                        return;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Add (ref Margins lhs, Margins rhs, out Margins result) {
            result.Left = lhs.Left + rhs.Left;
            result.Top = lhs.Top + rhs.Top;
            result.Right = lhs.Right + rhs.Right;
            result.Bottom = lhs.Bottom + rhs.Bottom;
        }

        public static Margins operator + (Margins lhs, Margins rhs) {
            return new Margins {
                Left = lhs.Left + rhs.Left,
                Top = lhs.Top + rhs.Top,
                Right = lhs.Right + rhs.Right,
                Bottom = lhs.Bottom + rhs.Bottom
            };
        }

        public float X => Left + Right;
        public float Y => Top + Bottom;
        public Vector2 Size => new Vector2(Left + Right, Top + Bottom);

        public static implicit operator Vector4 (Margins margins) {
            return new Vector4(margins.Left, margins.Top, margins.Right, margins.Bottom);
        }
    }

    public struct NameAndIndex {
        public string Name;
        public int Index;

        public NameAndIndex (string name, int index = 0) {
            Name = name;
            Index = index;
        }

        public static implicit operator NameAndIndex (string name) {
            return new NameAndIndex(name);
        }
    }
}