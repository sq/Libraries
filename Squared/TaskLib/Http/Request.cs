using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Squared.Task.IO;

namespace Squared.Task.Http {
    public partial class HttpServer {
        public class Request {
            public readonly HttpServer Server;
            public readonly RequestLine Line;

            internal Request (HttpServer server, RequestLine line) {
                Server = server;
                Line = line;
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

            public RequestLine (string baseUri, string line) {
                var m = MyRegex.Match(line);
                if (!m.Success)
                    throw new Exception("Invalid format");

                Method = m.Groups["method"].Value.ToUpperInvariant();
                UnparsedUri = m.Groups["uri"].Value;

                if (UnparsedUri.StartsWith("/"))
                    Uri = new Uri(new Uri(baseUri, UriKind.Absolute), UnparsedUri);
                else
                    Uri = new Uri(UnparsedUri, UriKind.Absolute);

                var g = m.Groups["version"];
                Version = g.Success
                    ? g.Value
                    : null;
            }

            public override string ToString() {
                return String.Format("{0} {1} HTTP/{2}", Method, Uri, Version);
            }
        }

        private IEnumerator<object> RequestTask (ListenerContext context, Socket socket) {
            const int bufferSize = 8192;

            using (var adapter = new SocketDataAdapter(socket))
            using (var reader = new AsyncTextReader(adapter, Encoding.UTF8, bufferSize)) {
                var fRequestLine = reader.ReadLine();
                yield return fRequestLine;

                var lep = (IPEndPoint)socket.LocalEndPoint;
                var requestLine = new RequestLine(
                    String.Format("http://{0}:{1}/", lep.Address, lep.Port),
                    fRequestLine.Result
                );

                var request = new Request(this, requestLine);
                IncomingRequests.Enqueue(request);
            }
        }
    }
}
