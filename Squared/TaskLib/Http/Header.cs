using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Squared.Task.Http {
    public partial class HttpServer : IDisposable {
        public struct Header {
            private static readonly Regex MyRegex = new Regex(
                @"(?'name'[^:]+)(\s?):(\s?)(?'value'.+)",
                RegexOptions.Compiled | RegexOptions.ExplicitCapture
            );

            public readonly string Name;
            public readonly string Value;

            public Header (string name, string value) {
                if (String.IsNullOrWhiteSpace(name))
                    throw new ArgumentNullException("name");
                if (value == null)
                    throw new ArgumentNullException("value");

                Name = name;
                Value = value;
            }

            internal Header (string line) {
                var m = MyRegex.Match(line);
                if (!m.Success)
                    throw new Exception("Invalid header format");

                Name = m.Groups["name"].Value;
                Value = m.Groups["value"].Value;
            }

            public override string ToString () {
                return String.Format("{0}:{1}", Name, Value);
            }
        }

        public class HeaderCollection : KeyedCollection<string, Header> {
            protected override string GetKeyForItem (Header item) {
                return item.Name;
            }

            public string GetValue (string key) {
                if (Contains(key))
                    return this[key].Value;
                else
                    return null;
            }

            public void SetValue (string key, string value) {
                if (Contains(key))
                    Remove(key);

                if (value != null)
                    Add(new Header(key, value));
            }

            public bool TryGetValue (string key, out Header result) {
                if (Contains(key)) {
                    result = this[key];
                    return true;
                }

                result = default(Header);
                return false;
            }
        }
    }
}
