using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
#if DEBUG
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;
#endif
using Microsoft.Xna.Framework;
using Squared.Game;
using Squared.Util;

namespace Squared.PRGUI.Layout {
#if DEBUG
    public struct ControlKey
        : IXmlSerializable
#else
    public readonly struct ControlKey
#endif
        {
        public static readonly ControlKey Invalid = new ControlKey(-1);
        internal static readonly ControlKey Corrupt = new ControlKey(-2);

#if DEBUG
        internal int ID;
#else
        internal readonly int ID;
#endif

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

#if DEBUG
        public XmlSchema GetSchema () {
            return null;
        }

        public void ReadXml (XmlReader reader) {
            ID = int.Parse(reader["ID"]);
        }

        public void WriteXml (XmlWriter writer) {
            writer.WriteAttributeString("ID", ID.ToString());
        }
#endif
    }

    public sealed class ControlKeyComparer : 
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

        internal ControlKey _Key;
        public ControlKey Key => _Key;

        public ControlFlags Flags;
        public ControlKey Parent, FirstChild, LastChild;
        public ControlKey PreviousSibling, NextSibling;
        public Margins Margins, Padding;
        public Vector2 FixedSize, MinimumSize, MaximumSize, ComputedContentSize, FloatingPosition;
        public LayoutTags Tag;

        public LayoutItem (ControlKey key) {
            _Key = key;
            Flags = default(ControlFlags);
            Parent = FirstChild = LastChild = PreviousSibling = NextSibling = ControlKey.Invalid;
            Margins = Padding = default(Margins);
            FixedSize = MinimumSize = MaximumSize = NoSize;
            FloatingPosition = Vector2.Zero;
            ComputedContentSize = Vector2.Zero;
            Tag = 0;
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

    public unsafe sealed partial class LayoutContext : IDisposable {
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
            if (key.IsInvalid || key.ID >= Boxes.Count)
                return default(RectF);

            return Boxes.DangerousGetItem(key.ID);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool GetRects (ControlKey key, out RectF rect, out RectF contentRect) {
            if (key.IsInvalid || !Boxes.DangerousTryGetItem(key.ID, out rect)) {
                rect = contentRect = default(RectF);
                return false;
            }

            var pItem = LayoutPtr(key);
            GetContentRect(pItem, ref rect, out contentRect);
            return true;
        }

        private unsafe void GetContentRect (LayoutItem * pItem, ref RectF exterior, out RectF interior) {
            Vector2 extent;
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
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe RectF GetContentRect (LayoutItem * pItem) {
            var pExterior = RectPtr(pItem->Key);
            GetContentRect(pItem, ref *pExterior, out RectF interior);
            return interior;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe bool TryGetContentRect (ControlKey key, out RectF result) {
            if ((key.IsInvalid) || (key.ID >= Boxes.Count)) {
                result = default(RectF);
                return false;
            }

            var pItem = LayoutPtr(key);
            var pExterior = RectPtr(key);
            GetContentRect(pItem, ref *pExterior, out result);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe RectF GetContentRect (ControlKey key) {
            if (key.IsInvalid)
                return default(RectF);

            var pItem = LayoutPtr(key);
            var pExterior = RectPtr(key);
            GetContentRect(pItem, ref *pExterior, out RectF interior);
            return interior;
        }

        private void SetRect (ControlKey key, in RectF newRect) {
            if (key.IsInvalid)
                throw new ArgumentOutOfRangeException("key");

            Boxes.DangerousSetItem(key.ID, newRect);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetRect (ControlKey key, out RectF result) {
            if (key.IsInvalid) {
                result = default(RectF);
                return false;
            }

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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal unsafe LayoutItem * LayoutPtr (ControlKey key, bool optional = false) {
            var id = key.ID;
            if (id < 0) {
                if (optional)
                    return null;
                else
                    throw new ArgumentOutOfRangeException(nameof(key));
            }

            if (id >= _Count) {
                if (optional)
                    return null;
                else
                    throw new ArgumentOutOfRangeException(nameof(key));
            }

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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
            Boxes.UnsafeFastClear();
            _Count = 0;
        }

        public void Initialize () {
            Version++;

            IsDisposed = false;
            Root = CreateItem(LayoutTags.Root);
            SetFixedSize(Root, _CanvasSize);
        }
    }
}

namespace Squared.PRGUI {
    [StructLayout(LayoutKind.Explicit)]
    public unsafe struct RectF {
        [FieldOffset(0)]
        public fixed float Values[4];

        [FieldOffset(0)]
        public Vector2 Position;
        [FieldOffset(0)]
        public float Left;
        [FieldOffset(4)]
        public float Top;

        [FieldOffset(8)]
        public Vector2 Size;
        [FieldOffset(8)]
        public float Width;
        [FieldOffset(12)]
        public float Height;

        public RectF (float left, float top, float width, float height) {
            Position = default;
            Size = default;
            Left = left;
            Top = top;
            Width = width;
            Height = height;
        }

        public RectF (Vector2 origin, Vector2 size) {
            Left = Top = Width = Height = default;
            Position = origin;
            Size = size;
        }

        public float this [uint index] {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
#if DEBUG
                if (index > 3)
                    throw new ArgumentOutOfRangeException(nameof(index));
#endif
                return Values[index];
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set {
#if DEBUG
                if (index > 3)
                    throw new ArgumentOutOfRangeException(nameof(index));
#endif
                Values[index] = value;
            }
        }

        public float this [int index] { 
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
#if DEBUG
                if (index > 3)
                    throw new ArgumentOutOfRangeException(nameof(index));
#endif
                return Values[index];
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set {
#if DEBUG
                if (index > 3)
                    throw new ArgumentOutOfRangeException(nameof(index));
#endif
                Values[index] = value;
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

        public Vector2 BottomLeft {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                return new Vector2(Left, Top + Height);
            }
        }

        public Vector2 TopRight {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                return new Vector2(Left + Width, Top);
            }
        }

        public float Right => Left + Width;
        public float Bottom => Top + Height;

        public bool Intersection (in RectF rhs, out RectF result) {
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

        public bool Intersects (in RectF rhs) {
            Vector2 tl, br;
            tl.X = Math.Max(Left, rhs.Left);
            tl.Y = Math.Max(Top, rhs.Top);
            br.X = Math.Min(Left + Width, rhs.Left + rhs.Width);
            br.Y = Math.Min(Top + Height, rhs.Top + rhs.Height);
            return (br.X > tl.X) && (br.Y > tl.Y);
        }

        public static RectF Lerp (in RectF lhs, in RectF rhs, float t) {
            return new RectF(
                Arithmetic.Lerp(lhs.Left, rhs.Left, t),
                Arithmetic.Lerp(lhs.Top, rhs.Top, t),
                Arithmetic.Lerp(lhs.Width, rhs.Width, t),
                Arithmetic.Lerp(lhs.Height, rhs.Height, t)
            );
        }

        public bool Contains (in RectF rhs) {
            return (rhs.Left >= Left) &&
                (rhs.Extent.X <= (Left + Width)) &&
                (rhs.Top >= Top) &&
                (rhs.Extent.Y <= (Top + Height));
        }

        public bool Contains (in Vector2 position) {
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

        public void SnapAndInset (out Vector2 a, out Vector2 b, Margins inset) {
            a = new Vector2(Left + inset.Left, Top + inset.Top);
            b = new Vector2(Left + Width - inset.Right, Top + Height - inset.Bottom);
            // HACK: Snap to integral pixels so that edges don't look uneven
            a = a.Floor();
            b = b.Floor();
            if (a.X > b.X)
                a.X = b.X = Left;
            if (a.Y > b.Y)
                a.Y = b.Y = Top;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals (in RectF rhs) {
            return (Left == rhs.Left) &&
                (Top == rhs.Top) &&
                (Width == rhs.Width) &&
                (Height == rhs.Height);
        }

        public void Clamp (ref Vector2 position) {
            position.X = Arithmetic.Clamp(position.X, Left, Left + Width);
            position.Y = Arithmetic.Clamp(position.Y, Top, Top + Height);
        }

        public Vector2 Clamp (Vector2 position) {
            Clamp(ref position);
            return position;
        }

        public override bool Equals (object obj) {
            if (obj is RectF r)
                return Equals(r);
            else
                return false;
        }

        public static bool operator == (RectF lhs, RectF rhs) {
            return lhs.Equals(rhs);
        }

        public static bool operator != (RectF lhs, RectF rhs) {
            return !lhs.Equals(rhs);
        }

        public static RectF operator * (RectF lhs, float rhs) {
            lhs.Left *= rhs;
            lhs.Width *= rhs;
            lhs.Top *= rhs;
            lhs.Height *= rhs;
            return lhs;
        }

        public static RectF operator * (RectF lhs, Vector2 rhs) {
            lhs.Left *= rhs.X;
            lhs.Width *= rhs.X;
            lhs.Top *= rhs.Y;
            lhs.Height *= rhs.Y;
            return lhs;
        }

        public override int GetHashCode () {
            return Left.GetHashCode() ^ Top.GetHashCode() ^ Width.GetHashCode() ^ Height.GetHashCode();
        }

        public override string ToString () {
            return $"({Left}, {Top}) {Width}x{Height}";
        }

        public static RectF FromPoints (Vector2 position, Vector2 extent) {
            return new RectF(position, extent - position);
        }
    }

    [StructLayout(LayoutKind.Explicit)]
    public unsafe struct Margins {
        [FieldOffset(0)]
#if DEBUG
        [XmlIgnore]
#endif
        public fixed float Values[4];
        [FieldOffset(0)]
        public float Left;
        [FieldOffset(4)]
        public float Top;
        [FieldOffset(8)]
        public float Right;
        [FieldOffset(12)]
        public float Bottom;

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

        public Margins (Vector4 v4) {
            Left = v4.X;
            Top = v4.Y;
            Right = v4.Z;
            Bottom = v4.W;
        }

        public override int GetHashCode () {
            return 0;
        }

        public bool Equals (in Margins rhs) {
            return (Left == rhs.Left) &&
                (Top == rhs.Top) &&
                (Right == rhs.Right) &&
                (Bottom == rhs.Bottom);
        }

        public override bool Equals (object obj) {
            if (obj is Margins m)
                return Equals(in m);
            else
                return false;
        }

        public static bool operator == (Margins lhs, Margins rhs) => lhs.Equals(rhs);
        public static bool operator != (Margins lhs, Margins rhs) => !lhs.Equals(rhs);

        public float this [uint index] {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
#if DEBUG
                if (index > 3)
                    throw new ArgumentOutOfRangeException(nameof(index));
#endif
                return Values[index];
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set {
#if DEBUG
                if (index > 3)
                    throw new ArgumentOutOfRangeException(nameof(index));
#endif
                Values[index] = value;
            }
        }

        public float this [int index] { 
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
#if DEBUG
                if (index > 3)
                    throw new ArgumentOutOfRangeException(nameof(index));
#endif
                return Values[index];
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set {
#if DEBUG
                if (index > 3)
                    throw new ArgumentOutOfRangeException(nameof(index));
#endif
                Values[index] = value;
            }
        }

        public static void Scale (ref Margins margins, float scale) {
            for (int i = 0; i < 4; i++)
                margins.Values[i] *= scale;
        }

        public static void Scale (ref Margins margins, in Vector2 scale) {
            margins.Left *= scale.X;
            margins.Top *= scale.Y;
            margins.Right *= scale.X;
            margins.Bottom *= scale.Y;
        }

        public static Margins operator * (Margins lhs, float rhs) {
            Scale(ref lhs, rhs);
            return lhs;
        }

        public static Margins operator * (Margins lhs, Vector2 rhs) {
            Scale(ref lhs, rhs);
            return lhs;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Add (in Margins lhs, in Margins rhs, out Margins result) {
            result = lhs;
            for (int i = 0; i < 4; i++)
                result.Values[i] += rhs.Values[i];
        }

        public static Margins operator + (Margins lhs, Margins rhs) {
            return new Margins {
                Left = lhs.Left + rhs.Left,
                Top = lhs.Top + rhs.Top,
                Right = lhs.Right + rhs.Right,
                Bottom = lhs.Bottom + rhs.Bottom
            };
        }

        public float X {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Left + Right;
        }
        public float Y {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Top + Bottom;
        }
        public Vector2 TopLeft {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => new Vector2(Left, Top);
        }
        public Vector2 BottomRight {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => new Vector2(Right, Bottom);
        }
        public Vector2 Size {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => new Vector2(Left + Right, Top + Bottom);
        }

        public static implicit operator Vector4 (Margins margins) {
            return new Vector4(margins.Left, margins.Top, margins.Right, margins.Bottom);
        }

        public static explicit operator Margins (Vector4 v4) => new Margins(v4);

        public static bool TryParse (string text, out Margins result) {
            text = text.Trim().Replace("Margins(", "(");
            int i = text.StartsWith("(") ? 1 : 0, count = text.EndsWith(")") ? text.Length - i - 1 : text.Length - i;
            var pieces = text.Substring(i, count).Split(',');
            result = default;
            if (pieces.Length != 4)
                return false;
            if (!float.TryParse(pieces[0], out result.Left) ||
                !float.TryParse(pieces[1], out result.Top) ||
                !float.TryParse(pieces[2], out result.Right) ||
                !float.TryParse(pieces[3], out result.Top))
                return false;
            return true;
        }

        public override string ToString () {
            return $"Margins({Left}, {Top}, {Right}, {Bottom})";
        }

        public Margins Min (ref Margins rhs) {
            return new Margins(
                Math.Min(Left, rhs.Left),
                Math.Min(Top, rhs.Top),
                Math.Min(Right, rhs.Right),
                Math.Min(Bottom, rhs.Bottom)
            );
        }

        public Margins Max (ref Margins rhs) {
            return new Margins(
                Math.Max(Left, rhs.Left),
                Math.Max(Top, rhs.Top),
                Math.Max(Right, rhs.Right),
                Math.Max(Bottom, rhs.Bottom)
            );
        }
    }

    public struct ControlDimension {
        [Flags]
        private enum Flag : uint {
            Minimum = 0b1,
            Maximum = 0b10,
            Fixed   = 0b100
        }

        private Flag Flags;
        private float _Minimum, _Maximum, _Fixed;

        public static ControlDimension operator * (float lhs, ControlDimension rhs) {
            Scale(ref rhs, lhs);
            return rhs;
        }

        public static ControlDimension operator * (ControlDimension lhs, float rhs) {
            Scale(ref lhs, rhs);
            return lhs;
        }

        public bool HasMinimum {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (Flags & Flag.Minimum) == Flag.Minimum;
        }

        public float? Minimum {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => HasMinimum ? _Minimum : (float?)null;
            set {
                if (value == null) {
                    Flags &= ~Flag.Minimum;
                    _Minimum = float.MinValue;
                } else {
                    Flags |= Flag.Minimum;
                    _Minimum = value.Value;
                }
            }
        }

        public bool HasMaximum {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (Flags & Flag.Maximum) == Flag.Maximum;
        }

        public float? Maximum {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => HasMaximum ? _Maximum : (float?)null;
            set {
                if (value == null) {
                    Flags &= ~Flag.Maximum;
                    _Maximum = float.MaxValue;
                } else {
                    Flags |= Flag.Maximum;
                    _Maximum = value.Value;
                }
            }
        }

        public bool HasFixed {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (Flags & Flag.Fixed) == Flag.Fixed;
        }

        public float? Fixed {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => HasFixed ? _Fixed : (float?)null;
            set {
                if (value == null) {
                    Flags &= ~Flag.Fixed;
                    _Fixed = float.NaN;
                } else {
                    Flags |= Flag.Fixed;
                    _Fixed = value.Value;
                }
            }
        }

        internal float EffectiveMinimum {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                float a = (Flags & Flag.Maximum) != default ? _Maximum : float.MaxValue,
                    b = (Flags & Flag.Fixed) != default ? _Fixed : (
                        (Flags & Flag.Minimum) != default ? _Minimum : 0
                    );
                return Math.Min(a, b);
            }
        }

        public ControlDimension AutoComputeFixed () {
            if (
                (_Maximum == _Minimum) && 
                ((Flags & Flag.Maximum) != default) &&
                ((Flags & Flag.Minimum) != default)
            )
                return new ControlDimension {
                    Minimum = _Minimum,
                    Maximum = _Maximum,
                    Fixed = Fixed ?? _Maximum
                };

            return this;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Scale (ref ControlDimension value, float scale) {
            value._Minimum *= scale;
            value._Maximum *= scale;
            value._Fixed *= scale;
        }

        public ControlDimension Scale (float scale) {
            return new ControlDimension {
                Minimum = Minimum * scale,
                Maximum = Maximum * scale,
                Fixed = Fixed * scale
            };
        }

        public static float? Min (float? lhs, float? rhs) {
            if (lhs.HasValue && rhs.HasValue)
                return Math.Min(lhs.Value, rhs.Value);
            else if (lhs.HasValue)
                return lhs;
            else
                return rhs;
        }

        public static float? Max (float? lhs, float? rhs) {
            if (lhs.HasValue && rhs.HasValue)
                return Math.Max(lhs.Value, rhs.Value);
            else if (lhs.HasValue)
                return lhs;
            else
                return rhs;
        }

        /// <summary>
        /// Produces a new dimension with minimum/maximum values that encompass both inputs
        /// </summary>
        public ControlDimension Union (ref ControlDimension rhs) {
            return new ControlDimension {
                Minimum = Min(Minimum, rhs.Minimum),
                Maximum = Max(Maximum, rhs.Maximum),
                // FIXME
                Fixed = Fixed ?? rhs.Fixed
            };
        }

        /// <summary>
        /// Produces a new dimension with minimum/maximum values that only encompass the place where
        ///  the two inputs overlap
        /// </summary>
        public ControlDimension Intersection (ref ControlDimension rhs) {
            return new ControlDimension {
                Minimum = Max(Minimum, rhs.Minimum),
                Maximum = Min(Maximum, rhs.Maximum),
                // FIXME
                Fixed = Fixed ?? rhs.Fixed
            };
        }

        public void Constrain (ref float? size, bool applyFixed, out float delta) {
            var previous = size;
            if (size.HasValue) {
                if ((Flags & Flag.Minimum) != default)
                    size = Math.Max(_Minimum, size.Value);
                if ((Flags & Flag.Maximum) != default)
                    size = Math.Min(_Maximum, size.Value);
            }
            if (applyFixed && (Flags & Flag.Fixed) != default)
                size = _Fixed;

            if (previous.HasValue)
                delta = size.Value - previous.Value;
            else
                delta = size.Value;
        }

        public void Constrain (ref float size, bool applyFixed, out float delta) {
            float? temp = size;
            Constrain(ref temp, applyFixed, out delta);
            size = temp.Value;
        }

        public void Constrain (ref float? size, bool applyFixed) {
            Constrain(ref size, applyFixed, out _);
        }

        public void Constrain (ref float size, bool applyFixed) {
            Constrain(ref size, applyFixed, out _);
        }

        public float? Constrain (float? size, bool applyFixed, out float delta) {
            Constrain(ref size, applyFixed, out delta);
            return size;
        }

        public float Constrain (float size, bool applyFixed, out float delta) {
            float? temp = size;
            Constrain(ref temp, applyFixed, out delta);
            return temp.Value;
        }

        public float? Constrain (float? size, bool applyFixed) {
            Constrain(ref size, applyFixed);
            return size;
        }

        public float Constrain (float size, bool applyFixed) {
            float? temp = size;
            Constrain(ref temp, applyFixed);
            return temp.Value;
        }

        public bool HasValue {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (Flags != default);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator ControlDimension (float fixedSize) {
            return new ControlDimension { Fixed = fixedSize };
        }

        public override string ToString () {
            if (!HasValue)
                return "<unconstrained>";
            else
                return $"Clamp({Fixed?.ToString() ?? "<null>"}, {Minimum?.ToString() ?? "<null>"}, {Maximum?.ToString() ?? "<null>"})";
        }
    }
}