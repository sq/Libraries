using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
        public string SectionName;
        public string Key;
        public string Value;
        public string Comment;
    }

    public class IniReader : IDisposable, IEnumerable<IniLine>, IEnumerator<IniLine> {
        private StreamReader Reader;
        private bool OwnsReader;
        private string SectionName;
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
            var line = Reader.ReadLine();
            if (line == null) {
                SectionName = null;
                return false;
            }

            var index = LinesRead;
            LinesRead++;

            line = line.Replace("\r", "").Trim();
            if (line.Length <= 0) {
                _Current = new IniLine {
                    Index = index,
                    Type = IniLineType.None,
                    SectionName = SectionName,
                };
                return true;
            }

            if (line.StartsWith("#") || line.StartsWith(";")) {
                _Current = new IniLine {
                    Index = index,
                    Type = IniLineType.Comment,
                    Comment = line.Substring(1),
                    SectionName = SectionName,
                };
                return true;
            }

            if (line.StartsWith("[")) {
                _Current = new IniLine {
                    Index = index,
                    SectionName = SectionName,
                };

                if (!line.EndsWith("]")) {
                    _Current.Type = IniLineType.Error;
                    _Current.Value = "Missing ]";
                } else {
                    _Current.Type = IniLineType.Section;
                    SectionName = _Current.SectionName = line.Substring(1, line.Length - 2);
                }

                return true;
            }

            string comment = null;

            var equalsLocation = line.IndexOf("=");
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
            var value = line.Substring(equalsLocation + 1).TrimStart();
            var needEndingQuote = value.StartsWith("\"");
            while (value.EndsWith("\\")) {
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
                value += line;
            }
            value = value.TrimEnd();

            int startsWhere = needEndingQuote ? 1 : 0, endsWhere = value.Length, i = startsWhere;
            var needTrim = !needEndingQuote;
            while ((i < value.Length) && (i >= 0)) {
                var nextImportantCharacter = value.IndexOfAny(ImportantCharacters, i);
                if (nextImportantCharacter < 0)
                    break;

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
                value = value.TrimEnd();

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
                Key = key,
                Value = value,
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
