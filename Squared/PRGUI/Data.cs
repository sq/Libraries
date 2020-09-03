using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Squared.Game;
using Squared.Util;

namespace Squared.PRGUI {
    public struct ControlKey {
        public static readonly ControlKey Invalid = new ControlKey(-1);

        internal int ID;

        internal ControlKey (int id) {
            ID = id;
        }

        public bool IsInvalid {
            get {
                return ID < 0;
            }
        }

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

        public static bool operator == (ControlKey lhs, ControlKey rhs) {
            return lhs.Equals(rhs);
        }

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

    public struct ControlLayout {
        public readonly ControlKey Key;

        public ControlFlags Flags;
        public ControlKey FirstChild;
        public ControlKey NextSibling;
        public Vector4 Margins;
        public Vector2 Size;

        public ControlLayout (ControlKey key) {
            Key = key;
            Flags = default(ControlFlags);
            FirstChild = ControlKey.Invalid;
            NextSibling = ControlKey.Invalid;
            Margins = default(Vector4);
            Size = default(Vector2);
        }
    }

    public partial class Context : IDisposable {
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

        private GCHandle LayoutPin, BoxesPin;
        private readonly UnorderedList<ControlLayout> Layout = new UnorderedList<ControlLayout>();
        private readonly UnorderedList<Bounds> Boxes = new UnorderedList<Bounds>();

        private void InvalidState () {
            throw new Exception("Invalid internal state");
        }

        private void Assert (bool b) {
            throw new Exception("Assertion failed");
        }

        private void AssertNotRoot (ControlKey key) {
            if (key.IsInvalid)
                throw new Exception("Invalid key");
            else if (key == Root)
                throw new Exception("Key must not be the root");
        }

        private void AssertNotEqual (ControlKey lhs, ControlKey rhs) {
            if (lhs == rhs)
                throw new Exception("Keys must not be equal");
        }

        private void AssertMasked (ControlFlags flags, ControlFlags mask, string maskName) {
            if ((flags & mask) != mask)
                throw new Exception("Flags must be compatible with mask " + maskName);
        }

        public ControlLayout this [ControlKey key] {
            get {
                return Layout.DangerousGetItem(key.ID);
            }
        }

        public bool TryGetItem (ref ControlKey key, out ControlLayout result) {
            return Layout.DangerousTryGetItem(key.ID, out result);
        }

        public bool TryGetItem (ControlKey key, out ControlLayout result) {
            return Layout.DangerousTryGetItem(key.ID, out result);
        }

        public bool TryGetFirstChild (ControlKey key, out ControlLayout result) {
            if (!Layout.DangerousTryGetItem(key.ID, out result))
                return false;
            var firstChild = result.FirstChild;
            return Layout.DangerousTryGetItem(firstChild.ID, out result);
        }

        public bool TryGetNextSibling (ControlKey key, out ControlLayout result) {
            if (!Layout.DangerousTryGetItem(key.ID, out result))
                return false;
            var nextSibling = result.NextSibling;
            return Layout.DangerousTryGetItem(nextSibling.ID, out result);
        }

        public Bounds GetRect (ControlKey key) {
            return Boxes.DangerousGetItem(key.ID);
        }

        public bool TryGetRect (ControlKey key, out Bounds result) {
            return Boxes.DangerousTryGetItem(key.ID, out result);
        }

        public bool TryGetRect (ControlKey key, out float x, out float y, out float width, out float height) {
            x = y = width = height = 0;
            Bounds result;
            if (!Boxes.DangerousTryGetItem(key.ID, out result))
                return false;
            x = result.TopLeft.X;
            y = result.TopLeft.Y;
            var size = result.Size;
            width = size.X;
            height = size.Y;
            return true;
        }

        private unsafe ControlLayout * LayoutPtr () {
            var buffer = Layout.GetBuffer();
            if (!LayoutPin.IsAllocated || (buffer.Array != LayoutPin.Target)) {
                LayoutPin.Free();
                LayoutPin = GCHandle.Alloc(buffer.Array, GCHandleType.Pinned);
            }
            return ((ControlLayout*)LayoutPin.AddrOfPinnedObject()) + buffer.Offset;
        }

        private unsafe ControlLayout * LayoutPtr (ControlKey key) {
            if ((key.ID < 0) || (key.ID >= Layout.Count))
                throw new ArgumentOutOfRangeException(nameof(key));

            var result = &LayoutPtr()[key.ID];
            if (result->Key != key)
                InvalidState();
            return result;
        }

        private unsafe Bounds * BoxesPtr () {
            var buffer = Boxes.GetBuffer();
            if (!BoxesPin.IsAllocated || (buffer.Array != BoxesPin.Target)) {
                BoxesPin.Free();
                BoxesPin = GCHandle.Alloc(buffer.Array, GCHandleType.Pinned);
            }
            return ((Bounds *)BoxesPin.AddrOfPinnedObject()) + buffer.Offset;
        }

        private unsafe Bounds * BoxPtr (ControlKey key) {
            if ((key.ID < 0) || (key.ID >= Boxes.Count))
                throw new ArgumentOutOfRangeException(nameof(key));

            var result = &BoxesPtr()[key.ID];
            return result;
        }

        public void Dispose () {
            if (IsDisposed)
                return;

            IsDisposed = true;
            Root = ControlKey.Invalid;
            LayoutPin.Free();
            BoxesPin.Free();
            Layout.Clear();
            Boxes.Clear();
        }

        public void Initialize () {
            IsDisposed = false;
            Root = CreateItem();
        }
    }
}
