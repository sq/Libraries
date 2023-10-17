using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using System.Runtime.CompilerServices;
using Squared.PRGUI.Layout;
using Squared.Util;
using Squared.PRGUI.NewEngine.Enums;
using System.Reflection;
#if DEBUG
using System.Xml.Serialization;
using System.IO;
#endif

namespace Squared.PRGUI.NewEngine {
    public partial class LayoutEngine {
        public static BoxRecord Invalid = new BoxRecord {
            _Key = -1,
        };

        private int Version;
        private const int Capacity = 65536;

        // TODO: Much better sizing implementation
        // We need to make sure that once we hand out a reference to Records[n], we never transition that item
        //  into a new array, otherwise a reference to the old array could be persistent on the stack.
        // The obvious solution is a huge buffer, but another option is a chain of smaller buffers so we grow by
        //  allocating a new buffer, which won't invalidate old references.
        private SegmentedArray<BoxRecord> Records = new SegmentedArray<BoxRecord>(Capacity);
        private SegmentedArray<BoxLayoutResult> Results = new SegmentedArray<BoxLayoutResult>(Capacity);
        // The worst case size is one run per control
        private SegmentedArray<LayoutRun> RunBuffer = new SegmentedArray<LayoutRun>(Capacity);

#if DEBUG
        internal Control[] Controls = new Control[Capacity];
#endif

        private Vector2 _CanvasSize;
        public Vector2 CanvasSize {
            get => _CanvasSize;
            set {
                _CanvasSize = value;
                Root().FixedSize = value;
            }
        }

        public int Count => Records.Count;
        public void Clear () {
            Records.Clear();
            Results.Clear();
            RunBuffer.Clear();
            Version++;
            // Initialize root
            ref var root = ref Create(tag: Layout.LayoutTags.Root);
            root.FixedSize = _CanvasSize;
        }

        public LayoutEngine () {
            Clear();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref BoxRecord Root () {
            return ref Records[0];
        }

        /// <summary>
        /// Returns a reference to the control's configuration. Throws if key is invalid.
        /// </summary>
        public ref BoxRecord this [ControlKey key] {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                if (key.ID < 0)
                    return ref Invalid;
                else
                    return ref Records[key.ID];
            }
        }

        /// <summary>
        /// Returns a reference to the control's configuration. Throws if key is invalid.
        /// </summary>
        public ref BoxRecord this [int index] {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                if (index < 0)
                    return ref Invalid;
                else
                    return ref Records[index];
            }
        }

        public ref BoxRecord Create (out ControlKey result, Layout.LayoutTags tag = default, ControlFlags flags = default, ControlKey? parent = null) {
            ref BoxRecord record = ref Create(tag, flags);
            result = record.Key;
            if (parent.HasValue)
                InsertAtEnd(parent.Value, result);
            return ref record;
        }

        public ref BoxRecord Create (Layout.LayoutTags tag = default, ControlFlags flags = default, ControlKey? parent = null) {
            ref var result = ref Records.New(out int index);
            result._Key = new ControlKey(index);
            // FIXME
            result.Config.GridColumnCount = 0;
            result.OldFlags = flags;
            result.Tag = tag;
            if (parent.HasValue)
                InsertAtEnd(parent.Value, result.Key);
            // HACK: Cannot return 'ref result' in older versions of C#
            return ref Records[index];
        }

        public ref BoxRecord GetOrCreate (ControlKey? existingKey, Layout.LayoutTags tag = default, ControlFlags flags = default) {
            var index = existingKey?.ID ?? -1;
            if (index < 0)
                return ref Create(tag, flags);

            ref var result = ref Records[index];
            result.OldFlags = flags;
            result.Tag = tag;
            return ref result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private RunEnumerable Runs (ControlKey parent) {
            Assert(!parent.IsInvalid);
            return new RunEnumerable(this, parent);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ChildrenEnumerable Children (ControlKey parent, bool reverse = false) {
            Assert(!parent.IsInvalid);
            return new ChildrenEnumerable(this, parent, reverse);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SiblingsEnumerable Enumerate (ControlKey first, ControlKey? last = null) {
            Assert(!first.IsInvalid);
            return new SiblingsEnumerable(this, first, last);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ref BoxLayoutResult UnsafeResult (ControlKey key) {
            return ref Results.UnsafeItem(key.ID);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref BoxLayoutResult Result (ControlKey key) {
            return ref Results[key.ID];
        }

        #region Diagnostic internals

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void InvalidState () {
            throw new Exception("Invalid internal state");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void AssertionFailed (string message) {
            throw new Exception(
                message != null
                    ? $"Assertion failed: {message}"
                    : "Assertion failed"
                );
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Assert (bool b, string message = null) {
            if (b)
                return;

            AssertionFailed(message);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AssertNotRoot (ControlKey key) {
            Assert(!key.IsInvalid, "Invalid key");
            Assert(key.ID != 0, "Key must not be the root");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AssertNotEqual (ControlKey lhs, ControlKey rhs) {
            Assert(lhs != rhs, "Keys must not be equal");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AssertMasked (ControlFlags flags, ControlFlags mask) {
            Assert((flags & mask) == flags, "Flags must be compatible with mask");
        }

        #endregion

        #region Child manipulation

        public void Remove (ControlKey key) {
            Assert(!key.IsInvalid);
            AssertNotRoot(key);

            var deadItem = this[key];
            if (!deadItem.PreviousSibling.IsInvalid) {
                ref var prev = ref this[deadItem.PreviousSibling];
                prev._NextSibling = deadItem.NextSibling;
            }
            if (!deadItem.NextSibling.IsInvalid) {
                ref var next = ref this[deadItem.NextSibling];
                next._PreviousSibling = deadItem.PreviousSibling;
            }

            if (!deadItem.Parent.IsInvalid) {
                ref var parent = ref this[deadItem.Parent];
                if (parent.FirstChild == key)
                    parent._FirstChild = deadItem.NextSibling;
                if (parent.LastChild == key)
                    parent._LastChild = deadItem.PreviousSibling;
            }
        }

        public void InsertBefore (ControlKey newSibling, ControlKey later) {
            AssertNotRoot(newSibling);
            Assert(!later.IsInvalid);
            Assert(!newSibling.IsInvalid);
            AssertNotEqual(later, newSibling);

            ref var pLater = ref this[later];
            ref var pParent = ref this[pLater.Parent];
            ref var pNewItem = ref this[newSibling];
            ref var pPreviousSibling = ref this[pLater.PreviousSibling];

            pNewItem._NextSibling = later;
            pNewItem._Parent = pLater.Parent;
            pNewItem._PreviousSibling = pLater.PreviousSibling;

            if (pPreviousSibling.IsValid)
                pPreviousSibling._NextSibling = pNewItem.Key;

            if (pParent.FirstChild == later)
                pParent._FirstChild = newSibling;

            pLater._PreviousSibling = pNewItem.Key;
        }

        public void InsertAfter (ControlKey earlier, ControlKey newSibling) {
            AssertNotRoot(newSibling);
            Assert(!earlier.IsInvalid);
            Assert(!newSibling.IsInvalid);
            AssertNotEqual(earlier, newSibling);

            ref var pEarlier = ref this[earlier];
            ref var pNewItem = ref this[newSibling];
            ref var pNextSibling = ref this[pEarlier.NextSibling];

            pNewItem._Parent = pEarlier.Parent;
            pNewItem._PreviousSibling = pEarlier.Key;
            pNewItem._NextSibling = pEarlier.NextSibling;

            pEarlier._NextSibling = pNewItem.Key;

            if (pNextSibling.IsValid) {
                pNextSibling._PreviousSibling = pNewItem.Key;
            } else {
                ref var pParent = ref this[pEarlier.Parent];
                Assert(pParent.LastChild == pEarlier.Key);
                pParent._LastChild = pNewItem.Key;
            }
        }

        /// <summary>
        /// Alias for InsertAtEnd
        /// </summary>
        public void Append (ControlKey parent, ControlKey child) {
            InsertAtEnd(parent, child);
        }

        public unsafe void InsertAtEnd (ControlKey parent, ControlKey newLastChild) {
            AssertNotRoot(newLastChild);
            AssertNotEqual(parent, newLastChild);

            ref var pParent = ref this[parent];
            ref var pChild = ref this[newLastChild];

            Assert(pChild.Parent.IsInvalid, "is not inserted");

            if (pParent._FirstChild.IsInvalid) {
                Assert(pParent._LastChild.IsInvalid);
                pParent._FirstChild = newLastChild;
                pParent._LastChild = newLastChild;
                pChild._Parent = parent;
            } else {
                Assert(!pParent._LastChild.IsInvalid);
                InsertAfter(pParent.LastChild, newLastChild);
            }
        }

        public unsafe void InsertAtStart (ControlKey parent, ControlKey newFirstChild) {
            AssertNotRoot(newFirstChild);
            AssertNotEqual(parent, newFirstChild);
            ref var pParent = ref this[parent];
            ref var pChild = ref this[newFirstChild];

            Assert(pChild.Parent.IsInvalid, "is not inserted");

            pChild._Parent = parent;
            pChild._PreviousSibling = ControlKey.Invalid;
            pChild._NextSibling = pParent.FirstChild;

            if (pParent._FirstChild.IsInvalid) {
                Assert(pParent._LastChild.IsInvalid);
                pParent._FirstChild = newFirstChild;
                pParent._LastChild = newFirstChild;
                pChild._Parent = parent;
            } else {
                Assert(!pParent._LastChild.IsInvalid);
                InsertBefore(newFirstChild, pParent.FirstChild);
            }
        }
        #endregion

        public void Update () {
            Results.Clear();
            RunBuffer.Clear();
            PerformLayout(ref Root());
        }

        public void LoadRecords (string filename) {
#if DEBUG
            var serializer = new XmlSerializer(typeof(BoxRecord[]));
            using (var stream = File.OpenRead(filename)) {
                var temp = (BoxRecord[])serializer.Deserialize(stream);
                Records.Clear();
                Records.AddRange(temp);
            }
#else
            throw new NotImplementedException();
#endif
        }

        public void SaveRecords (string filename) {
#if DEBUG
            var serializer = new XmlSerializer(typeof(BoxRecord[]));
            var temp = Records.ToArray();
            using (var stream = File.Open(filename, FileMode.Create))
                serializer.Serialize(stream, temp);
#else
            throw new NotImplementedException();
#endif
        }
    
        public bool DebugHitTest (Vector2 position, out BoxRecord record, out BoxLayoutResult result, bool exhaustive) {
            record = default;
            result = default;
            // We perform the top level hit test in reverse order so that controls last in the Children list block hit tests
            //  to children at the front of the list. This matches how it normally works
            return DebugHitTest(in Root(), position, ref record, ref result, exhaustive, true);
        }

        private bool DebugHitTest (in BoxRecord control, Vector2 position, ref BoxRecord record, ref BoxLayoutResult result, bool exhaustive, bool reverse) {
            ref var testResult = ref Result(control.Key);
            var inside = testResult.Rect.Contains(position);

            if (inside || exhaustive) {
                foreach (var ckey in Children(control.Key, reverse)) {
                    if (DebugHitTest(in this[ckey], position, ref record, ref result, exhaustive, false))
                        return true;
                }
            }

            if (inside) {
                record = control;
                result = testResult;
                return true;
            } else
                return false;
        }
    }
}
