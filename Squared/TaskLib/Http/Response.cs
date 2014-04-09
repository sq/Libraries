using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Squared.Task.IO;

namespace Squared.Task.Http {
    public partial class HttpServer {
        public class Response {
            public static Encoding DefaultEncoding = Encoding.UTF8;

            public readonly HeaderCollection Headers = new HeaderCollection();

            private readonly Request Request;
            private readonly SocketDataAdapter Adapter;

            public int StatusCode = 200;
            public string StatusText = "OK";

            internal Response (Request request, SocketDataAdapter adapter) {
                Request = request;
                Adapter = adapter;
            }

            public bool HeadersSent {
                get;
                private set;
            }

            public bool ResponseSent {
                get;
                private set;
            }

            private string GetHeader (string key) {
                if (Headers.Contains(key))
                    return Headers[key].Value;
                else
                    return null;
            }

            private void SetHeader (string key, string value) {
                if (Headers.Contains(key))
                    Headers.Remove(key);

                if (value != null)
                    Headers.Add(new Header(key, value));
            }

            public long? ContentLength {
                get {
                    var lengthHeader = GetHeader("Content-Length");
                    return
                        (lengthHeader == null)
                            ? null
                            : (long?)long.Parse(lengthHeader);
                }
                set {
                    SetHeader(
                        "Content-Length", 
                        value.HasValue 
                            ? value.Value.ToString()
                            : null
                    );
                }
            }

            public string ContentType {
                get {
                    return GetHeader("Content-Type");
                }
                set {
                    SetHeader("Content-Type", value);
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

            public IAsyncDataWriter GetResponseWriter () {
                if (!HeadersSent)
                    throw new InvalidOperationException("Headers must be sent first");

                return Adapter;
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

            private IEnumerator<object> SendResponseTask (string text, Encoding encoding) {
                var fEncodedBytes = Future.RunInThread(
                    () => encoding.GetBytes(text)
                );
                yield return fEncodedBytes;

                if (ContentType == null)
                    ContentType = "text/plain";
                if (ContentLength == null)
                    ContentLength = fEncodedBytes.Result.Length;

                if (!HeadersSent) {
                    HeadersSent = true;
                    yield return SendHeadersTask();
                }

                yield return Adapter.Write(fEncodedBytes.Result, 0, fEncodedBytes.Result.Length);

                Dispose();
            }

            private void Dispose () {
                Adapter.Dispose();
                Request.Dispose();
            }
        }
    }
}
