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

            private readonly SocketDataAdapter Adapter;

            internal Request (
                HttpServer server, Socket socket, SocketDataAdapter adapter,
                RequestLine line, HeaderCollection headers, RequestBody body
            ) {
                WeakServer = new WeakReference(server);
                Adapter = adapter;

                LocalEndPoint = socket.LocalEndPoint;
                RemoteEndPoint = socket.RemoteEndPoint;

                Line = line;
                Headers = headers;
                Body = body;
                Response = new Response(this, Adapter);

                server.OnRequestCreated(this);
            }

            public HttpServer Server {
                get {
                    return (HttpServer)WeakServer.Target;
                }
            }

            public IAsyncDataWriter ResponseWriter {
                get {
                    return Adapter;
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
                if (!Response.HeadersSent && !Adapter.IsDisposed && !byServer) {
                    var fSend = Response.SendHeaders();
                    fSend.RegisterOnComplete((_) => Adapter.Dispose());
                } else {
                    Adapter.Dispose();
                }

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

        private IEnumerator<object> RequestTask (ListenerContext context, Socket socket) {
            SocketDataAdapter adapter = null;
            bool successful = false;

            try {
                const int headerBufferSize = 1024 * 32;
                const int bodyBufferSize = 1024 * 128;

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

                adapter = new SocketDataAdapter(socket, true);
                var reader = new AsyncTextReader(adapter, headerEncoding, headerBufferSize, false);
                try {
                    string requestLineText;

                    while (true) {
                        var fRequestLine = reader.ReadLine();
                        yield return fRequestLine;

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

                    string hostName;
                    if (headers.Contains("Host")) {
                        hostName = String.Format("http://{0}", headers["Host"].Value);
                    } else {
                        var lep = (IPEndPoint)socket.LocalEndPoint;
                        hostName = String.Format("http://{0}:{1}", lep.Address, lep.Port);
                    }

                    var remainingBytes = reader.DisposeAndGetRemainingBytes();
                    bodyBytesRead += remainingBytes.Count;

                    if (headers.Contains("Content-Length"))
                        expectedBodyLength = long.Parse(headers["Content-Length"].Value);

                    body = new RequestBody(remainingBytes, expectedBodyLength);

                    request = new Request(
                        this, socket, adapter,
                        new RequestLine(hostName, requestLineText),
                        headers, body
                    );

                    IncomingRequests.Enqueue(request);
                } finally {
                    if (!reader.IsDisposed)
                        reader.Dispose();
                }

                using (var bodyBuffer = BufferPool<byte>.Allocate(bodyBufferSize))
                while (!expectedBodyLength.HasValue || (bodyBytesRead < expectedBodyLength.Value)) {
                    long bytesToRead = bodyBufferSize;
                    if (expectedBodyLength.HasValue)
                        bytesToRead = Math.Min(expectedBodyLength.Value - bodyBytesRead, bodyBufferSize);

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

                body.Finish();
                successful = true;
            } finally {
                if (!successful)
                    adapter.Dispose();
            }
        }
    }
}
