using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Remoting.Messaging;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Squared.Task.IO;
using Squared.Util;

namespace Squared.Task.Http {
    public partial class HttpServer {
        public class Request : IDisposable {
            private readonly WeakReference WeakServer;

            public readonly EndPoint LocalEndPoint;
            public readonly EndPoint RemoteEndPoint;

            public readonly RequestLine Line;
            public readonly HeaderCollection Headers;
            public readonly RequestBody Body;
            public readonly Response Response;

            private QueryStringCollection _QueryString = null;

            internal Request (
                HttpServer server, SocketDataAdapter adapter, bool shouldKeepAlive,
                RequestLine line, HeaderCollection headers, RequestBody body
            ) {
                WeakServer = new WeakReference(server);

                LocalEndPoint = adapter.Socket.LocalEndPoint;
                RemoteEndPoint = adapter.Socket.RemoteEndPoint;

                Line = line;
                Headers = headers;
                Body = body;
                Response = new Response(this, adapter, shouldKeepAlive);

                server.OnRequestCreated(this);
            }

            public bool IsDisposed {
                get;
                private set;
            }

            public QueryStringCollection QueryString {
                get {
                    if (_QueryString == null)
                        _QueryString = new QueryStringCollection(Line.Uri.Query, true);

                    return _QueryString;
                }
            }

            public HttpServer Server {
                get {
                    return (HttpServer)WeakServer.Target;
                }
            }

            public override string ToString() {
                var sb = new StringBuilder();

                sb.AppendLine(Line.ToString());

                foreach (var header in Headers)
                    sb.AppendLine(header.ToString());

                return sb.ToString();
            }

            internal void Dispose (bool byServer) {
                if (IsDisposed)
                    return;

                IsDisposed = true;

                if (!byServer)
                    Response.SendAndDispose();

                if (!byServer) {
                    var server = Server;
                    if (server != null)
                        server.OnRequestDisposed(this);
                }
            }

            public void Dispose () {
                Dispose(false);
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

        public class RequestBody {
            private readonly GrowableBuffer<byte> Buffer;
            public readonly long? ExpectedLength;
            public readonly Future<byte[]> Bytes;

            public RequestBody (ArraySegment<byte> prefillBytes, long? expectedLength) {
                const int minBufferSize = 1024 * 16;
                int bufferSize = Math.Max(prefillBytes.Count, minBufferSize);

                Buffer = new GrowableBuffer<byte>(bufferSize);
                Buffer.Append(prefillBytes.Array, prefillBytes.Offset, prefillBytes.Count);

                ExpectedLength = expectedLength;
                Bytes = new Future<byte[]>();
            }

            internal void Append (byte[] buffer, int offset, int count) {
                Buffer.Append(buffer, offset, count);
            }

            internal void Failed (Exception error) {
                Buffer.Dispose();
                Bytes.SetResult(null, error);
            }

            internal void Finish () {
                var contents = new byte[Buffer.Length];
                Buffer.DisposeAndGetContents(contents, 0);
                Bytes.SetResult(contents, null);
            }
        }

        static readonly byte[] Continue100 = Encoding.ASCII.GetBytes("HTTP/1.1 100 Continue\r\n\r\n");

        private IEnumerator<object> RequestTask (ListenerContext context, SocketDataAdapter adapter) {
            bool successful = false;

            try {
                const int headerBufferSize = 1024 * 32;
                const int bodyBufferSize = 1024 * 128;
                const double requestLineTimeout = 5;

                // RFC2616:
                // Words of *TEXT MAY contain characters from character sets other than ISO-8859-1 [22]
                //  only when encoded according to the rules of RFC 2047 [14].
                Encoding headerEncoding;
                try {
                    headerEncoding = Encoding.GetEncoding("ISO-8859-1");
                } catch {
                    headerEncoding = Encoding.ASCII;
                }

                Request request;
                RequestBody body;
                HeaderCollection headers;
                long bodyBytesRead = 0;
                long? expectedBodyLength = null;

                var reader = new AsyncTextReader(adapter, headerEncoding, headerBufferSize, false);
                string requestLineText;

                while (true) {
                    var fRequestLine = reader.ReadLine();
                    var fRequestOrTimeout = Scheduler.Start(new WaitWithTimeout(fRequestLine, requestLineTimeout));

                    yield return fRequestOrTimeout;

                    if (fRequestOrTimeout.Failed) {
                        if (!(fRequestOrTimeout.Error is TimeoutException))
                            OnRequestError(fRequestOrTimeout.Error);

                        yield break;
                    }

                    if (fRequestLine.Failed) {
                        if (!(fRequestLine.Error is SocketDisconnectedException))
                            OnRequestError(fRequestLine.Error);

                        yield break;
                    }

                    requestLineText = fRequestLine.Result;

                    // RFC2616: 
                    // In the interest of robustness, servers SHOULD ignore any empty line(s) received where a 
                    //  Request-Line is expected. In other words, if the server is reading the protocol stream 
                    //   at the beginning of a message and receives a CRLF first, it should ignore the CRLF. 
                    if ((requestLineText != null) && (requestLineText.Trim().Length == 0))
                        continue;

                    break;
                }

                headers = new HeaderCollection();
                while (true) {
                    var fHeaderLine = reader.ReadLine();
                    yield return fHeaderLine;

                    if (String.IsNullOrWhiteSpace(fHeaderLine.Result))
                        break;

                    headers.Add(new Header(fHeaderLine.Result));
                }

                var expectHeader = (headers.GetValue("Expect") ?? "").ToLowerInvariant();
                var expectsContinue = expectHeader.Contains("100-continue");

                string hostName;
                if (headers.Contains("Host")) {
                    hostName = String.Format("http://{0}", headers["Host"].Value);
                } else {
                    var lep = (IPEndPoint)adapter.Socket.LocalEndPoint;
                    hostName = String.Format("http://{0}:{1}", lep.Address, lep.Port);
                }

                var requestLine = new RequestLine(hostName, requestLineText);

                var remainingBytes = reader.DisposeAndGetRemainingBytes();
                bodyBytesRead += remainingBytes.Count;

                var connectionHeader = (headers.GetValue("Connection") ?? "").ToLowerInvariant();
                var shouldKeepAlive = 
                    ((requestLine.Version == "1.1") || connectionHeader.Contains("keep-alive")) &&
                    !connectionHeader.Contains("close");

                if (headers.Contains("Content-Length"))
                    expectedBodyLength = long.Parse(headers["Content-Length"].Value);

                body = new RequestBody(remainingBytes, expectedBodyLength);

                if (expectsContinue)
                    yield return adapter.Write(Continue100, 0, Continue100.Length);

                request = new Request(
                    this, adapter, shouldKeepAlive,
                    requestLine, headers, body
                );

                IncomingRequests.Enqueue(request);

                // FIXME: I think it's technically accepted to send a body without a content-length, but
                //  it seems to be impossible to make that work right.
                if (expectedBodyLength.HasValue) {
                    using (var bodyBuffer = BufferPool<byte>.Allocate(bodyBufferSize))
                    while (bodyBytesRead < expectedBodyLength.Value) {
                        long bytesToRead = Math.Min(expectedBodyLength.Value - bodyBytesRead, bodyBufferSize);

                        if (bytesToRead <= 0)
                            break;

                        var fBytesRead = adapter.Read(bodyBuffer.Data, 0, (int)bytesToRead);
                        yield return fBytesRead;

                        if (fBytesRead.Failed) {
                            if (fBytesRead.Error is SocketDisconnectedException)
                                break;

                            body.Failed(fBytesRead.Error);
                            OnRequestError(fBytesRead.Error);
                            yield break;
                        }

                        var bytesRead = fBytesRead.Result;

                        bodyBytesRead += bytesRead;
                        body.Append(bodyBuffer.Data, 0, bytesRead);
                    }
                }

                body.Finish();
                successful = true;
            } finally {
                if (!successful)
                    adapter.Dispose();
            }
        }
    }
}
