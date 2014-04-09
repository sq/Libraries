using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Squared.Task.IO;
using Squared.Util;

namespace Squared.Task.Http {
    public partial class HttpServer {
        public class Response {
            private static readonly byte[] ContinueMessageBytes;

            public static Encoding DefaultEncoding = Encoding.UTF8;

            public readonly HeaderCollection Headers = new HeaderCollection();

            private readonly Request Request;
            private readonly IAsyncDataWriter Adapter;

            private AsyncDataAdapterShim _Shim = null;

            public int StatusCode = 200;
            public string StatusText = "OK";

            private static readonly byte[] ResponseEpilogue = Encoding.ASCII.GetBytes("\r\n");

            static Response () {
            }

            internal Response (Request request, IAsyncDataWriter adapter, bool keepAlive) {
                Request = request;
                Adapter = adapter;
                KeepAlive = keepAlive;
                ContentLength = 0;
            }

            public bool HeadersSent {
                get;
                private set;
            }

            public bool ResponseSent {
                get;
                private set;
            }

            public bool EpilogueSent {
                get;
                private set;
            }

            public bool KeepAlive {
                get {
                    var state = (Headers.GetValue("Connection") ?? "").ToLowerInvariant();
                    return (state == "keep-alive");
                }
                set {
                    Headers.SetValue("Connection", value ? "keep-alive" : "close");
                }
            }

            public long ContentLength {
                get {
                    var lengthHeader = Headers.GetValue("Content-Length");
                    return
                        (lengthHeader == null)
                            ? 0
                            : long.Parse(lengthHeader);
                }
                set {
                    Headers.SetValue(
                        "Content-Length", 
                        value.ToString()
                    );
                }
            }

            public string ContentType {
                get {
                    return Headers.GetValue("Content-Type");
                }
                set {
                    Headers.SetValue("Content-Type", value);
                }
            }

            private TaskScheduler Scheduler {
                get {
                    return Request.Server.Scheduler;
                }
            }

            public IFuture SendHeaders () {
                if (HeadersSent)
                    throw new InvalidOperationException("Response headers already sent");
                HeadersSent = true;

                return Scheduler.Start(SendHeadersTask());
            }

            private IEnumerator<object> SendHeadersTask () {
                const int writeBufferSize = 1024;

                using (var atw = new AsyncTextWriter(Adapter, Encoding.ASCII, writeBufferSize, false)) {
                    var prologue = String.Format(
                        "HTTP/1.1 {0} {1}",
                        StatusCode, StatusText ?? (StatusCode == 200 ? "OK" : "Unknown")
                    );

                    yield return atw.WriteLine(prologue);

                    foreach (var header in Headers)
                        yield return atw.WriteLine(header.ToString());

                    yield return atw.WriteLine("");

                    yield return atw.Flush();
                }
            }

            private IAsyncDataWriter GetShim () {
                if (_Shim == null)
                    _Shim = new AsyncDataAdapterShim(null, Adapter);

                return _Shim;
            }

            public IAsyncDataWriter GetResponseWriter () {
                if (!HeadersSent)
                    throw new InvalidOperationException("Headers must be sent first");

                return GetShim();
            }

            public IFuture SendResponse (
                string responseText, Encoding responseEncoding = null
            ) {
                if (ResponseSent)
                    throw new InvalidOperationException("Response already sent");
                ResponseSent = true;

                if (responseEncoding == null)
                    responseEncoding = DefaultEncoding;

                return Scheduler.Start(SendResponseTask(responseText, responseEncoding));
            }

            public IFuture SendResponse (ArraySegment<byte> response) {
                if (ResponseSent)
                    throw new InvalidOperationException("Response already sent");
                ResponseSent = true;

                return Scheduler.Start(SendResponseTask(response));
            }

            public IFuture SendResponse (Stream source, int? count = null) {
                if (ResponseSent)
                    throw new InvalidOperationException("Response already sent");
                ResponseSent = true;

                return Scheduler.Start(SendResponseTask(source, count));
            }

            private IEnumerator<object> SendResponseTask (string text, Encoding encoding) {
                var fEncodedBytes = Future.RunInThread(
                    () => encoding.GetBytes(text)
                );
                yield return fEncodedBytes;

                if (ContentType == null)
                    ContentType = "text/plain";

                yield return SendResponseTask(
                    new ArraySegment<byte>(fEncodedBytes.Result, 0, fEncodedBytes.Result.Length)
                );
            }

            private IEnumerator<object> SendResponseTask (ArraySegment<byte> payload) {
                ContentLength = payload.Count;

                if (!HeadersSent) {
                    HeadersSent = true;
                    yield return SendHeadersTask();
                }

                ResponseSent = true;
                yield return Adapter.Write(payload.Array, payload.Offset, payload.Count);
            }

            private IEnumerator<object> SendResponseTask (Stream source, int? count) {
                var length = Math.Min(
                    (int)source.Length, 
                    count.GetValueOrDefault(int.MaxValue)
                );
                ContentLength = length;

                if (!HeadersSent) {
                    HeadersSent = true;
                    yield return SendHeadersTask();
                }

                ResponseSent = true;

                const int blockSize = 1024 * 128;
                var bytesLeft = length;

                using (var sda = new StreamDataAdapter(source, false))
                using (var buffer = BufferPool<byte>.Allocate(blockSize))
                while (bytesLeft > 0) {
                    var readSize = Math.Min(blockSize, bytesLeft);
                    var fBlock = sda.Read(buffer.Data, 0, blockSize);
                    yield return fBlock;

                    bytesLeft -= fBlock.Result;

                    yield return Adapter.Write(buffer.Data, 0, fBlock.Result);
                }
            }

            internal void SendAndDispose () {
                if (!HeadersSent)
                    SendHeaders();
            }
        }
    }
}
