using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Remoting.Messaging;
using System.Text;
using System.Text.RegularExpressions;
using Squared.Task.IO;

namespace Squared.Task.Http {
    public partial class HttpServer {
        public class Request {
            public readonly HttpServer Server;
            public readonly RequestLine Line;
            public readonly RequestHeaders Headers;

            internal Request (HttpServer server, RequestLine line, RequestHeaders headers) {
                Server = server;
                Line = line;
                Headers = headers;
            }

            public override string ToString() {
                var sb = new StringBuilder();

                sb.AppendLine(Line.ToString());

                foreach (var header in Headers)
                    sb.AppendLine(header.ToString());

                return sb.ToString();
            }
        }

        public class RequestHeaders : KeyedCollection<string, Header> {
            protected override string GetKeyForItem (Header item) {
                return item.Name;
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

        public struct RequestLine {
            private static readonly Regex MyRegex = new Regex(
                @"(?'method'\w+)\s(?'uri'\S+)(\sHTTP/(?'version'([0-9.]+)))?",
                RegexOptions.Compiled | RegexOptions.ExplicitCapture
            );

            public readonly string Method;
            public readonly string UnparsedUri;
            public readonly Uri Uri;
            public readonly string Version;

            public RequestLine (string host, string line) {
                var m = MyRegex.Match(line);
                if (!m.Success)
                    throw new Exception("Invalid request line format");

                Method = m.Groups["method"].Value.ToUpperInvariant();
                UnparsedUri = m.Groups["uri"].Value;

                Uri hostUri, uri;

                if (!Uri.TryCreate(host, UriKind.Absolute, out hostUri))
                    throw new Exception("Invalid host header");
                
                if (!Uri.TryCreate(hostUri, UnparsedUri, out Uri))
                    throw new Exception("Invalid request URI");

                var g = m.Groups["version"];
                Version = g.Success
                    ? g.Value
                    : null;
            }

            public override string ToString() {
                return String.Format("{0} {1} HTTP/{2}", Method, Uri, Version);
            }
        }

        public struct Header {
            private static readonly Regex MyRegex = new Regex(
                @"(?'name'[^:]+)(\s?):(\s?)(?'value'.+)",
                RegexOptions.Compiled | RegexOptions.ExplicitCapture
            );

            public readonly string Name;
            public readonly string Value;

            public Header (string line) {
                var m = MyRegex.Match(line);
                if (!m.Success)
                    throw new Exception("Invalid header format");

                Name = m.Groups["name"].Value;
                Value = m.Groups["value"].Value;
            }

            public override string ToString() {
                return String.Format("{0}:{1}", Name, Value);
            }
        }

        private IEnumerator<object> RequestTask (ListenerContext context, Socket socket) {
            const int bufferSize = 8192;

            Encoding headerEncoding;
            try {
                headerEncoding = Encoding.GetEncoding("ISO-8859-1");
            } catch {
                headerEncoding = Encoding.ASCII;
            }

            using (var adapter = new SocketDataAdapter(socket))
            using (var reader = new AsyncTextReader(adapter, headerEncoding, bufferSize)) {
                var headers = new RequestHeaders();

                var fRequestLine = reader.ReadLine();
                yield return fRequestLine;

                while (true) {
                    var fHeaderLine = reader.ReadLine();
                    yield return fHeaderLine;

                    if (String.IsNullOrWhiteSpace(fHeaderLine.Result))
                        break;

                    headers.Add(new Header(fHeaderLine.Result));
                }

                string hostName;
                if (headers.Contains("Host")) {
                    hostName = String.Format("http://{0}", headers["Host"].Value);
                } else {
                    var lep = (IPEndPoint)socket.LocalEndPoint;
                    hostName = String.Format("http://{0}:{1}", lep.Address, lep.Port);
                }

                var request = new Request(
                    this,
                    new RequestLine(hostName, fRequestLine.Result),
                    headers
                );
                IncomingRequests.Enqueue(request);
            }
        }
    }
}
