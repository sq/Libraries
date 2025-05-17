using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Util;

namespace Squared.Render {

    public struct MaterialParameterValues : IEnumerable<KeyValuePair<string, object>> {
        // TODO: Store Texture and Array separately so we can fit more values more efficiently,
        //  since it's common to have a few textures + a few uniforms

        public struct Storage {
            internal UnorderedList<Key> Keys;
            internal UnorderedList<Value> Values;

            public Storage (ref MaterialParameterValues source) {
                Keys = source.Keys.GetStorage(true);
                Values = source.Values.GetStorage(true);
            }

            // FIXME: Is this right?
            public Storage EnsureUniqueStorage (ref MaterialParameterValues parameters) {
                if ((Keys != null) && (Values != null))
                    return this;
                else
                    return parameters.GetUniqueStorage();
            }
        }

        [Flags]
        internal enum StateFlags {
            IsCleared = 0b001,
            CopyOnWrite = 0b010,
        }

        internal enum EntryValueType : int {
            None,
            Texture,
            Array,
            B,
            F,
            I,
            V2,
            V3,
            V4,
            Q
        }

        [StructLayout(LayoutKind.Explicit)]
        internal struct EntryUnion {
            [FieldOffset(0)]
            public bool B;
            [FieldOffset(0)]
            public float F;
            [FieldOffset(0)]
            public int I;
            [FieldOffset(0)]
            public Vector2 V2;
            [FieldOffset(0)]
            public Vector3 V3;
            [FieldOffset(0)]
            public Vector4 V4;
            [FieldOffset(0)]
            public Quaternion Q;
        }

        internal struct Key {
            public int HashCode;
            public string Name;
            public int ValueIndex;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public Key (string name) {
                HashCode = name?.GetHashCode() ?? -1;
                Name = name;
                ValueIndex = -1;
            }

            public override string ToString () =>
                $"{Name} @{ValueIndex}";
        }

        internal struct Value {
            public EntryValueType Type;
            public object Reference;
            public EntryUnion Primitive;

            public object BoxedValue {
                get {
                    switch (Type) {
                        case EntryValueType.B:
                            return Primitive.B;
                        case EntryValueType.F:
                            return Primitive.F;
                        case EntryValueType.I:
                            return Primitive.I;
                        case EntryValueType.V2:
                            return Primitive.V2;
                        case EntryValueType.V3:
                            return Primitive.V3;
                        case EntryValueType.V4:
                            return Primitive.V4;
                        case EntryValueType.Q:
                            return Primitive.Q;
                        default:
                            return Reference;
                    }
                }
            }

            public static bool Equals (ref Value lhs, ref Value rhs) {
                if (lhs.Type != rhs.Type)
                    return false;

                switch (lhs.Type) {
                    case EntryValueType.B:
                        return lhs.Primitive.B == rhs.Primitive.B;
                    case EntryValueType.F:
                        return lhs.Primitive.F == rhs.Primitive.F;
                    case EntryValueType.I:
                        return lhs.Primitive.I == rhs.Primitive.I;
                    case EntryValueType.V2:
                        return lhs.Primitive.V2 == rhs.Primitive.V2;
                    case EntryValueType.V3:
                        return lhs.Primitive.V3 == rhs.Primitive.V3;
                    case EntryValueType.V4:
                        return lhs.Primitive.V4 == rhs.Primitive.V4;
                    case EntryValueType.Q:
                        return lhs.Primitive.Q == rhs.Primitive.Q;
                    case EntryValueType.Texture:
                    case EntryValueType.Array:
                        return ReferenceEquals(lhs.Reference, rhs.Reference);
                    default:
                        throw new ArgumentOutOfRangeException("lhs.ValueType");
                }
            }
        }

        private StateFlags State;
        private int KeyHash;
        private DenseList<Key> Keys;
        // TODO: Replace this with a few numbered object slots for references,
        //  and a big pile of UInt64s to pack primitives into. That will make
        //  it possible to fit more small parameters (bools, floats) into the
        //  available space before we need to allocate a backing store
        private DenseList<Value> Values;

        public bool AllocateNewStorageOnWrite {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => GetInternalFlag(StateFlags.CopyOnWrite);
            set => SetInternalFlag(StateFlags.CopyOnWrite, value);
        }

        public bool IsCleared {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => GetInternalFlag(StateFlags.IsCleared);
            private set => SetInternalFlag(StateFlags.IsCleared, value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool GetInternalFlag (StateFlags flag) {
            return (State & flag) == flag;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool? GetInternalFlag (StateFlags isSetFlag, StateFlags valueFlag) {
            if ((State & isSetFlag) != isSetFlag)
                return null;
            else
                return (State & valueFlag) == valueFlag;
        }

        private void SetInternalFlag (StateFlags flag, bool state) {
            if (state)
                State |= flag;
            else
                State &= ~flag;
        }

        private bool ChangeInternalFlag (StateFlags flag, bool newState) {
            if (GetInternalFlag(flag) == newState)
                return false;

            SetInternalFlag(flag, newState);
            return true;
        }

        public Storage GetUniqueStorage () {
            SetInternalFlag(StateFlags.CopyOnWrite, true);
            FlushCopyOnWrite();
            return new Storage(ref this);
        }

        public void UseExistingListStorage (Storage storage, bool preserveContents) {
            SetInternalFlag(StateFlags.CopyOnWrite, false);
            Keys.UseExistingStorage(storage.Keys, preserveContents);
            Values.UseExistingStorage(storage.Values, preserveContents);
            if (!preserveContents)
                KeyHash = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void FlushCopyOnWrite () {
            if (!AllocateNewStorageOnWrite)
                return;

            AllocateNewStorageOnWrite = false;
            if (!Keys.HasList && !Values.HasList)
                return;

            FlushCopyOnWrite_Slow();
        }

        void FlushCopyOnWrite_Slow () {
            var oldKeys = Keys;
            var oldValues = Values;
            oldKeys.Clone(out Keys);
            oldValues.Clone(out Values);
        }
        
        public int Count {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                return IsCleared ? 0 : Keys.Count;
            }
        }

        public bool IsEmpty {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => GetInternalFlag(StateFlags.IsCleared) || (Keys.Count < 1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int FindValue (string name) {
            int count = Count;
            if (count <= 0)
                return -1;

            var hashCode = name.GetHashCode();
            for (int i = 0; i < count; i++) {
                ref var key = ref Keys.Item(i);
                if (key.HashCode != hashCode)
                    continue;
                if (!string.Equals(key.Name, name, StringComparison.Ordinal))
                    continue;

                return key.ValueIndex;
            }

            return -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int FindValue (ref Key needle) {
            int count = Count;
            if (count <= 0)
                return -1;

            for (int i = 0; i < count; i++) {
                ref var key = ref Keys.Item(i);
                if (key.HashCode != needle.HashCode)
                    continue;
                if (!string.Equals(key.Name, needle.Name, StringComparison.Ordinal))
                    continue;

                return key.ValueIndex;
            }

            return -1;
        }

        public void Clear () {
            if (Keys.Count <= 0)
                return;

            KeyHash = 0;
            SetInternalFlag(StateFlags.IsCleared, true);
        }

        private void AddRange_Fast (ref MaterialParameterValues rhs) {
            // If we're empty we can skip the validation built into Set and just copy the other set's keys and values
            if (rhs.Count == 0)
                return;

            KeyHash = rhs.KeyHash;
            Keys.AddRange(ref rhs.Keys);
            Values.AddRange(ref rhs.Values);
        }

        public void AddRange (ref MaterialParameterValues rhs) {
            if (Keys.Count == 0) {
                AddRange_Fast(ref rhs);
                return;
            }

            for (int i = 0, c = rhs.Count; i < c; i++) {
                ref var key = ref rhs.Keys.Item(i);
                ref var value = ref rhs.Values.Item(key.ValueIndex);
                Set(ref key, ref value);
            }
        }

        internal bool TryGet (string name, out Value result) {
            var valueIndex = FindValue(name);
            if (valueIndex < 0) {
                result = default(Value);
                return false;
            }
            Values.GetItem(valueIndex, out result);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AutoClear () {
            if (!IsCleared)
                return;
            if (GetInternalFlag(StateFlags.CopyOnWrite)) {
                Keys = default;
                Values = default;
            } else {
                Keys.Clear();
                Values.Clear();
            }
            SetInternalFlag(StateFlags.IsCleared, false);
        }

        public void ReplaceWith (ref MaterialParameterValues values) {
            if (values.Count == 0) {
                Clear();
                return;
            }

            SetInternalFlag(StateFlags.IsCleared, false);
            FlushCopyOnWrite();
            // HACK: Keys will be interned strings 99% of the time, so leaking them doesn't matter
            Keys.ReplaceWith(ref values.Keys, false);
            // FIXME: Split VT and ref values into separate lists so we don't need to clear the VT list
            Values.ReplaceWith(ref values.Values);
            KeyHash = values.KeyHash;
        }

        private void Set (ref Key key, ref Value value) {
            AutoClear();
            var valueIndex = FindValue(ref key);
            if (valueIndex >= 0) {
                ref var existingValue = ref Values.Item(valueIndex);
                if (Value.Equals(ref existingValue, ref value))
                    return;
                FlushCopyOnWrite();
                Values.SetItem(valueIndex, ref value);
                return;
            }

            FlushCopyOnWrite();
            key.ValueIndex = Count;

            KeyHash ^= key.HashCode;
            Keys.Add(ref key);
            Values.Add(ref value);
        }

        private void Set (string name, Value entry) {
            var key = new Key(name);
            Set(ref key, ref entry);
        }

        public bool Remove (string name) {
            int count = Count;
            if (count <= 0)
                return false;

            var hashCode = name.GetHashCode();
            for (int i = 0; i < count; i++) {
                ref var key = ref Keys.Item(i);
                if (key.HashCode != hashCode)
                    continue;
                if (!string.Equals(key.Name, name, StringComparison.Ordinal))
                    continue;

                FlushCopyOnWrite();
                ref var value = ref Values.Item(key.ValueIndex);
                value = default;
                return true;
            }

            return false;
        }

        public void Add (string name, int value) {
            Set(name, new Value {
                Type = EntryValueType.I,
                Primitive = {
                    I = value
                }
            });
        }

        public void Add (string name, float value) {
            Set(name, new Value {
                Type = EntryValueType.F,
                Primitive = {
                    F = value
                }
            });
        }

        public void Add (string name, Color value) {
            Set(name, new Value {
                Type = EntryValueType.V4,
                Primitive = {
                    V4 = value.ToVector4()
                }
            });
        }

        public void Add (string name, bool value) {
            Set(name, new Value {
                Type = EntryValueType.B,
                Primitive = {
                    B = value
                }
            });
        }

        public void Add (string name, Vector2 value) {
            Set(name, new Value {
                Type = EntryValueType.V2,
                Primitive = {
                    V2 = value
                }
            });
        }

        public void Add (string name, Vector3 value) {
            Set(name, new Value {
                Type = EntryValueType.V3,
                Primitive = {
                    V3 = value
                }
            });
        }

        public void Add (string name, Vector4 value) {
            Set(name, new Value {
                Type = EntryValueType.V4,
                Primitive = {
                    V4 = value
                }
            });
        }

        public void Add (string name, Quaternion value) {
            Set(name, new Value {
                Type = EntryValueType.Q,
                Primitive = {
                    Q = value
                }
            });
        }

        public void Add (string name, Texture texture) {
            Set(name, new Value {
                Type = EntryValueType.Texture,
                Reference = texture
            });
        }

        public void Add (string name, Array array) {
            Set(name, new Value {
                Type = EntryValueType.Array,
                Reference = array
            });
        }

        public void Apply (Material material) {
            if (material.Effect == null)
                return;
            Apply(material.Effect, material.Parameters);
        }

        private void Apply (Effect effect, MaterialEffectParameters cache) {
            for (int i = 0, c = Count; i < c; i++) {
                ref var key = ref Keys.Item(i);
                var p = cache[key.Name];
                if (p == null)
                    continue;
                ApplyEntry(ref Values.Item(key.ValueIndex), p);
            }
        }

        private static void ApplyEntry (ref Value entry, EffectParameter p) {
            var r = entry.Reference;
            switch (entry.Type) {
                case EntryValueType.Texture:
                    p.SetValue((Texture)r);
                    break;
                case EntryValueType.Array:
                    if (r is float[] fa)
                        p.SetValue(fa);
                    else if (r is int[] ia)
                        p.SetValue(ia);
                    else if (r is bool[] ba)
                        p.SetValue(ba);
                    else if (r is Matrix[] ma)
                        p.SetValue(ma);
                    else if (r is Vector2[] v2a)
                        p.SetValue(v2a);
                    else if (r is Vector3[] v3a)
                        p.SetValue(v3a);
                    else if (r is Vector4[] v4a)
                        p.SetValue(v4a);
                    else if (r is Quaternion[] qa)
                        p.SetValue(qa);
                    else
                        throw new ArgumentException("Unsupported array parameter type");
                    break;
                case EntryValueType.B:
                    p.SetValue(entry.Primitive.B);
                    break;
                case EntryValueType.F:
                    p.SetValue(entry.Primitive.F);
                    break;
                case EntryValueType.I:
                    p.SetValue(entry.Primitive.I);
                    break;
                case EntryValueType.V2:
                    p.SetValue(entry.Primitive.V2);
                    break;
                case EntryValueType.V3:
                    p.SetValue(entry.Primitive.V3);
                    break;
                case EntryValueType.V4:
                    p.SetValue(entry.Primitive.V4);
                    break;
                case EntryValueType.Q:
                    p.SetValue(entry.Primitive.Q);
                    break;
                case EntryValueType.None:
                    break;
                default:
                    throw new ArgumentOutOfRangeException("entry.ValueType");
            }
        }

        public bool Equals (MaterialParameterValues pRhs) => Equals(ref pRhs);

        public bool Equals (ref MaterialParameterValues pRhs) {
            if (KeyHash != pRhs.KeyHash)
                return false;
            var count = Count;
            if (count != pRhs.Count)
                return false;
            if (count == 0)
                return true;

            // FIXME: Handle removed items
            for (int i = 0; i < count; i++) {
                ref var lhsKey = ref Keys.Item(i);
                var rhsIndex = pRhs.FindValue(ref lhsKey);
                if (rhsIndex < 0)
                    return false;
                if (!Value.Equals(ref Values.Item(lhsKey.ValueIndex), ref pRhs.Values.Item(rhsIndex)))
                    return false;
            }

            return true;
        }

        public override int GetHashCode () {
            return KeyHash;
        }

        public override bool Equals (object obj) {
            if (obj is MaterialParameterValues mpv)
                return Equals(ref mpv);
            else
                return false;
        }

        public IEnumerator<KeyValuePair<string, object>> GetEnumerator () {
            foreach (var key in Keys)
                yield return new KeyValuePair<string, object>(key.Name, Values[key.ValueIndex].BoxedValue);
        }

        IEnumerator IEnumerable.GetEnumerator () {
            return GetEnumerator();
        }

        internal void CopyTo (ref MaterialParameterValues rhs) {
            int count = Count;
            if (count == 0)
                return;
            for (int i = 0; i < count; i++) {
                ref var key = ref Keys.Item(i);
                ref var value = ref Values.Item(key.ValueIndex);
                if (value.Type == EntryValueType.None)
                    continue;
                rhs.Set(ref key, ref value);
            }
        }
    }
}
