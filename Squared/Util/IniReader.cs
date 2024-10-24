using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Squared.Util.Text;

namespace Squared.Util.Ini {
    public enum IniLineType {
        None,
        /// <summary>
        /// [sectionname] ; optional-comment
        /// </summary>
        Section,
        /// <summary>
        /// key=value ; optional-comment
        /// </summary>
        Value,
        /// <summary>
        /// ; full-line-comment
        /// </summary>
        Comment,
        Error
    }

    public struct IniLine {
        public IniLineType Type;
        public int Index;
        public AbstractString SectionName;
        public AbstractString Key;
        public AbstractString Value;
        public AbstractString Comment;
    }

    public class IniReader : IDisposable, IEnumerable<IniLine>, IEnumerator<IniLine> {
        private StreamReader Reader;
        private bool OwnsReader;
        private AbstractString SectionName;
        private int LinesRead = 0;
        private IniLine _Current;

        IniLine IEnumerator<IniLine>.Current => _Current;
        object IEnumerator.Current => _Current;

        public IniReader (string filename)
            : this (new StreamReader(filename, Encoding.UTF8), true) {
        }

        public IniReader (Stream stream, bool ownsStream)
            : this (new StreamReader(stream, Encoding.UTF8, false, 1024, !ownsStream), true) {
        }

        public IniReader (StreamReader reader, bool ownsReader) {
            Reader = reader;
            OwnsReader = ownsReader;
        }

        public IEnumerator<IniLine> GetEnumerator () {
            return this;
        }

        static readonly char[] ImportantCharacters = new[] { '\"', ';', '#' };

        bool IEnumerator.MoveNext () {
            var _line = Reader.ReadLine();
            if (_line == null) {
                SectionName = AbstractString.Empty;
                return false;
            }

            var line = new AbstractString(_line);

            var index = LinesRead;
            LinesRead++;

            if (line.EndsWith('\r'))
                line = line.Substring(0, line.Length - 1);
            line = line.Trim();
            if (line.EndsWith('\r')) {
                line = line.Substring(0, line.Length - 1);
                line = line.Trim();
            }

            if (line.Length <= 0) {
                _Current = new IniLine {
                    Index = index,
                    Type = IniLineType.None,
                    SectionName = SectionName,
                };
                return true;
            }

            if (line.StartsWith('#') || line.StartsWith(';')) {
                _Current = new IniLine {
                    Index = index,
                    Type = IniLineType.Comment,
                    Comment = line.Substring(1),
                    SectionName = SectionName,
                };
                return true;
            }

            if (line.StartsWith('[')) {
                _Current = new IniLine {
                    Index = index,
                    SectionName = SectionName,
                };

                if (!line.EndsWith(']')) {
                    _Current.Type = IniLineType.Error;
                    _Current.Value = "Missing ]";
                } else {
                    _Current.Type = IniLineType.Section;
                    SectionName = _Current.SectionName = line.Substring(1, line.Length - 2).ToString();
                }

                return true;
            }

            var comment = AbstractString.Empty;

            var equalsLocation = line.IndexOf('=');
            if (equalsLocation <= 0) {
                _Current = new IniLine {
                    Index = index,
                    Type = IniLineType.Error,
                    Value = "Missing key or =",
                    SectionName = SectionName,
                    Comment = comment
                };
                return true;
            }

            var key = line.Substring(0, equalsLocation).Trim();
            // FIXME: We want TrimStart here, but the whole line was already trimmed earlier.
            var value = line.Substring(equalsLocation + 1).Trim();
            var needEndingQuote = value.StartsWith("\"");
            while (value.EndsWith('\\')) {
                line = Reader.ReadLine();
                if (line == null) {
                    _Current = new IniLine {
                        Index = index,
                        Type = IniLineType.Error,
                        Value = "End of file encountered after \\ line extender",
                        SectionName = SectionName,
                        Comment = comment,
                    };
                    return true;
                }
                value = value.ToString() + line;
            }
            // FIXME: We want TrimEnd here
            value = value.Trim();

            int startsWhere = needEndingQuote ? 1 : 0, endsWhere = value.Length, i = startsWhere;
            var needTrim = !needEndingQuote;
            while ((i < value.Length) && (i >= 0)) {
                var nextImportantCharacter = value.Substring(i).IndexOfAny(ImportantCharacters);
                if (nextImportantCharacter < 0)
                    break;
                nextImportantCharacter += i;

                var ch = value[nextImportantCharacter];
                switch (ch) {
                    case '\"': {
                        if (needEndingQuote) {
                            needEndingQuote = false;
                            endsWhere = Math.Min(endsWhere, nextImportantCharacter);
                        }
                        break;
                    }

                    case ';':
                    case '#': {
                        if (!needEndingQuote) {
                            comment = value.Substring(nextImportantCharacter);
                            endsWhere = Math.Min(endsWhere, nextImportantCharacter);
                            i = -1;
                        }
                        break;
                    }

                    default:
                        throw new Exception();
                }

                if (i >= 0)
                    i = nextImportantCharacter + 1;
                else
                    break;
            }

            value = value.Substring(startsWhere, endsWhere - startsWhere);
            if (needTrim)
                // FIXME: Only TrimEnd wanted here
                value = value.Trim();

            if (needEndingQuote) {
                _Current = new IniLine {
                    Index = index,
                    Type = IniLineType.Error,
                    Value = "Unterminated double-quoted string",
                    SectionName = SectionName,
                    Comment = comment,
                };
                return true;
            }

            _Current = new IniLine {
                Index = index,
                Type = IniLineType.Value,
                Key = key.ToString(),
                Value = value.ToString(),
                SectionName = SectionName,
                Comment = comment,
            };
            return true;
        }

        void IEnumerator.Reset () {
            throw new NotImplementedException();
        }

        public void Dispose () {
            if (OwnsReader)
                Reader?.Dispose();
            Reader = null;
        }

        IEnumerator IEnumerable.GetEnumerator () {
            return this;
        }
    }
}
