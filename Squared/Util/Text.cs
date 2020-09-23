using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Squared.Util.Text {
    public static class Unicode {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool DecodeSurrogatePair (char ch1, char ch2, out uint codepoint) {
            codepoint = (uint)ch1;
            if (char.IsSurrogatePair(ch1, ch2)) {
                codepoint = (uint)char.ConvertToUtf32(ch1, ch2);
                return true;
            } else if (char.IsHighSurrogate(ch1) || char.IsLowSurrogate(ch1)) {
                // if we have a corrupt partial surrogate pair, it's not meaningful to return the first half.
                codepoint = 0xFFFD;
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Next (this AbstractString str, int offset) {
            var ch = str[offset];
            if (char.IsHighSurrogate(ch))
                return offset + 2;
            else
                return offset + 1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Previous (this AbstractString str, int offset) {
            var ch = str[offset - 1];
            if (char.IsLowSurrogate(ch))
                return offset - 2;
            else
                return offset - 1;
        }

        public static CodepointEnumerable Codepoints (this AbstractString str, int startOffset = 0) {
            return new CodepointEnumerable(str, startOffset);
        }

        public static CodepointEnumerable Codepoints (this string str, int startOffset = 0) {
            return new CodepointEnumerable(str, startOffset);
        }

        public static CodepointEnumerable Codepoints (this StringBuilder str, int startOffset = 0) {
            return new CodepointEnumerable(str, startOffset);
        }

        public static CodepointEnumerable Codepoints (this ArraySegment<char> str, int startOffset = 0) {
            return new CodepointEnumerable(str, startOffset);
        }

        public static uint? NthCodepoint (AbstractString str, int codepointIndex, int relativeToCharacterIndex = 0) {
            foreach (var cp in str.Codepoints(relativeToCharacterIndex)) {
                if (codepointIndex == 0)
                    return cp.Codepoint;
                codepointIndex--;
            }

            return null;
        }

        public static bool IsWhiteSpace (uint codepoint) {
            if (codepoint > 0xFFFF)
                return false;
            else
                return char.IsWhiteSpace((char)codepoint);
        }

        public static Pair<int> FindWordBoundary (AbstractString str, int searchFromCodepointIndex, int relativeToCharacterIndex = 0) {
            int firstWhitespaceCharacter = -1, 
                lastWhitespaceCharacter = -1, 
                firstWordCharacter = -1, 
                lastWordCharacter = -1;

            bool searchStartedInWhiteSpace = false, inWord = false;
            foreach (var cp in str.Codepoints(relativeToCharacterIndex)) {
                bool transitioned = false;
                var isWhiteSpace = IsWhiteSpace(cp.Codepoint);
                if (cp.CodepointIndex == searchFromCodepointIndex)
                    searchStartedInWhiteSpace = isWhiteSpace;

                if (isWhiteSpace) {
                    if (inWord || firstWhitespaceCharacter < 0) {
                        transitioned = inWord;
                        inWord = false;
                        firstWhitespaceCharacter = cp.CharacterIndex;
                    }
                    lastWhitespaceCharacter = cp.CharacterIndex;
                } else {
                    if (!inWord || firstWordCharacter < 0) {
                        transitioned = !inWord;
                        inWord = true;
                        firstWordCharacter = cp.CharacterIndex;
                    }
                    lastWordCharacter = cp.CharacterIndex;
                }

                if (transitioned && (cp.CodepointIndex > searchFromCodepointIndex))
                    break;
            }

            if (searchStartedInWhiteSpace)
                return new Pair<int>(firstWhitespaceCharacter, lastWhitespaceCharacter + 1);
            else {
                if (char.IsHighSurrogate(str[lastWordCharacter]))
                    lastWordCharacter++;
                return new Pair<int>(firstWordCharacter, lastWordCharacter + 1);
            }
        }
    }

    public struct CodepointEnumerable : IEnumerable<CodepointEnumerant> {
        public AbstractString String;
        public int StartOffset;

        public CodepointEnumerable (AbstractString str, int startOffset = 0) {
            String = str;
            StartOffset = startOffset;
        }

        public CodepointEnumerator GetEnumerator () {
            return new CodepointEnumerator(String, StartOffset);
        }

        IEnumerator IEnumerable.GetEnumerator () {
            return new CodepointEnumerator(String, StartOffset);
        }

        IEnumerator<CodepointEnumerant> IEnumerable<CodepointEnumerant>.GetEnumerator () {
            return new CodepointEnumerator(String, StartOffset);
        }
    }

    public struct CodepointEnumerant {
        public int CharacterIndex, CodepointIndex;
        public uint Codepoint;

        public static explicit operator uint (CodepointEnumerant value) {
            return value.Codepoint;
        }
    }

    public struct CodepointEnumerator : IEnumerator<CodepointEnumerant> {
        public AbstractString String;
        private int Length;
        private int Offset, StartOffset, _CurrentCharacterIndex, _CurrentCodepointIndex;
        private uint _CurrentCodepoint;
        private bool InSurrogatePair;

        public CodepointEnumerator (AbstractString str, int startOffset) {
            String = str;
            Length = str.Length;
            StartOffset = startOffset;
            Offset = startOffset - 1;
            _CurrentCharacterIndex = -1;
            _CurrentCodepointIndex = -1;
            _CurrentCodepoint = 0;
            InSurrogatePair = false;
        }

        public CodepointEnumerant Current => new CodepointEnumerant {
            CharacterIndex = _CurrentCharacterIndex,
            CodepointIndex = _CurrentCodepointIndex,
            Codepoint = _CurrentCodepoint
        };
        object IEnumerator.Current => Current;

        public void Dispose () {
            String = default(AbstractString);
            Offset = -1;
            _CurrentCharacterIndex = -1;
            _CurrentCodepointIndex = -1;
            _CurrentCodepoint = 0;
            InSurrogatePair = false;
        }

        public bool MoveNext () {
            Offset++;
            if (Offset >= Length)
                return false;

            if (InSurrogatePair)
                _CurrentCharacterIndex++;

            char ch1 = String[Offset],
                ch2 = (Offset >= Length - 1)
                    ? '\0' : String[Offset + 1];
            if (Unicode.DecodeSurrogatePair(ch1, ch2, out _CurrentCodepoint)) {
                Offset++;
                InSurrogatePair = true;
            } else {
                InSurrogatePair = false;
            }

            _CurrentCodepointIndex++;
            _CurrentCharacterIndex++;

            return true;
        }

        public void Reset () {
            Length = String.Length;
            Offset = StartOffset - 1;
            _CurrentCharacterIndex = -1;
            _CurrentCodepointIndex = -1;
            _CurrentCodepoint = 0;
            InSurrogatePair = false;
        }
    }

    public struct AbstractString : IEquatable<AbstractString> {
        private readonly string String;
        private readonly StringBuilder StringBuilder;
        private readonly ArraySegment<char> ArraySegment;

        public AbstractString (string text) {
            String = text;
            StringBuilder = null;
            ArraySegment = default(ArraySegment<char>);
        }

        public AbstractString (StringBuilder stringBuilder) {
            String = null;
            StringBuilder = stringBuilder;
            ArraySegment = default(ArraySegment<char>);
        }

        public AbstractString (char[] array) {
            String = null;
            StringBuilder = null;
            ArraySegment = new ArraySegment<char>(array);
        }

        public AbstractString (ArraySegment<char> array) {
            String = null;
            StringBuilder = null;
            ArraySegment = array;
        }

        public static implicit operator AbstractString (string text) {
            return new AbstractString(text);
        }

        public static implicit operator AbstractString (StringBuilder stringBuilder) {
            return new AbstractString(stringBuilder);
        }

        public static implicit operator AbstractString (char[] array) {
            return new AbstractString(array);
        }

        public static implicit operator AbstractString (ArraySegment<char> array) {
            return new AbstractString(array);
        }

        public bool Equals (AbstractString other) {
            return (String == other.String) &&
                (StringBuilder == other.StringBuilder) &&
                (ArraySegment == other.ArraySegment);
        }

        public bool Equals (AbstractString other, StringComparison comparison) {
            // FIXME: Optimize this
            return ToString().Equals(other.ToString(), comparison);
        }

        public char this[int index] {
            get {
                if (String != null)
                    return String[index];
                else if (StringBuilder != null)
                    return StringBuilder[index];
                else if (ArraySegment.Array != null) {
                    if ((index < 0) || (index >= ArraySegment.Count))
                        throw new ArgumentOutOfRangeException("index");

                    return ArraySegment.Array[index + ArraySegment.Offset];
                } else
                    throw new NullReferenceException("This string contains no text");
            }
        }

        public int Length {
            get {
                if (String != null)
                    return String.Length;
                else if (StringBuilder != null)
                    return StringBuilder.Length;
                else // Default fallback to 0 characters
                    return ArraySegment.Count;
            }
        }

        public bool IsNull {
            get {
                return
                    (String == null) &&
                    (StringBuilder == null) &&
                    (ArraySegment.Array == null);
            }
        }

        public override string ToString () {
            if (String != null)
                return String;
            else if (StringBuilder != null)
                return StringBuilder.ToString();
            else if (ArraySegment.Array != null)
                return new string(ArraySegment.Array, ArraySegment.Offset, ArraySegment.Count);
            else
                throw new NullReferenceException("This string contains no text");
        }
    }
}
