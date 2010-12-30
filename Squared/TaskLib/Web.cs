using System;
using System.Collections.Generic;
using System.Threading;
using System.Net;
using Squared.Task.IO;
using System.Text;
using System.Web;
using System.Collections.Specialized;
using System.Reflection;
using System.IO;

namespace Squared.Task {
    public static class Web {
        public class RequestFailedException : Exception {
            public RequestFailedException (Exception reason) 
                : base ("The request failed.", reason) {
            }
        }

        public class Response {
            public string Body;
            public string ContentType;
            public HttpStatusCode StatusCode;
            public string StatusDescription;
            public Cookie[] Cookies;

            public override string ToString () {
                return Body;
            }
        }

        public static IEnumerator<object> IssueRequest (HttpWebRequest request) {
            var fResponse = new Future<HttpWebResponse>();
            ThreadPool.QueueUserWorkItem(
                (__) => {
                    try {
                        request.BeginGetResponse(
                            (ar) => {
                                try {
                                    var _ = (HttpWebResponse)request.EndGetResponse(ar);
                                    fResponse.SetResult(_, null);
                                } catch (Exception ex) {
                                    fResponse.SetResult(null, ex);
                                }
                            }, null
                        );
                    } catch (Exception ex_) {
                        fResponse.SetResult(null, ex_);
                    }
                }
            );

            yield return fResponse;
            if (fResponse.Failed)
                throw new RequestFailedException(fResponse.Error);

            string responseText = null;

            using (var response = fResponse.Result) {
                var fResponseStream = Future.RunInThread(
                    () => response.GetResponseStream()
                );
                yield return fResponseStream;

                Encoding encoding = AsyncTextReader.DefaultEncoding;
                if ((response.CharacterSet != null) && (response.CharacterSet.Length > 0))
                    encoding = Encoding.GetEncoding(response.CharacterSet);

                using (var stream = fResponseStream.Result)
                using (var adapter = new AsyncTextReader(new StreamDataAdapter(stream, false), encoding)) {
                    var fText = adapter.ReadToEnd();

                    yield return fText;

                    responseText = fText.Result;
                }

                var cookies = new Cookie[response.Cookies.Count];
                response.Cookies.CopyTo(cookies, 0);

                yield return new Result(new Response {
                    Body = responseText,
                    ContentType = response.ContentType,
                    StatusCode = response.StatusCode,
                    StatusDescription = response.StatusDescription,
                    Cookies = cookies
                });
            }
        }

        public static string FormEncode (string text) {
            return HttpUtility.UrlEncode(text, Encoding.UTF8);
        }

        public static string BuildPostText (params string[] pairs) {
            var sb = new StringBuilder();
            for (int i = 0; i < pairs.Length; i += 2) {
                if (i != 0)
                    sb.Append("&");

                sb.AppendFormat(
                    "{0}={1}",
                    FormEncode(pairs[i]),
                    FormEncode(pairs[i + 1])
                );
            }
            return sb.ToString();
        }
    }

    public static class WebExtensionMethods {
        public static Future<HttpListenerContext> GetContextAsync (this HttpListener listener) {
            var f = new Future<HttpListenerContext>();
            listener.BeginGetContext((ar) => {
                try {
                    var result = listener.EndGetContext(ar);
                    f.Complete(result);
                } catch (FutureHandlerException) {
                    throw;
                } catch (Exception ex) {
                    f.Fail(ex);
                }
            }, null);
            return f;
        }

        public static NameValueCollection ParseRequestBody (this HttpListenerContext context) {
            NameValueCollection result = null;

            byte[] bytes;
            using (var ms = new MemoryStream())
            using (var stream = context.Request.InputStream) {
                byte[] buf = new byte[4096];
                while (true) {
                    int count = stream.Read(buf, 0, buf.Length);
                    if (count == 0)
                        break;

                    ms.Write(buf, 0, count);
                }

                bytes = ms.ToArray();
            }

            var encoding = context.Request.ContentEncoding ?? Encoding.UTF8;

            var asm = Assembly.GetAssembly(typeof(HttpUtility));
            var type = asm.GetType("System.Web.HttpValueCollection", true, false);
            var constructor = type.GetConstructor(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null,
                new Type[0], null
            );

            result = (NameValueCollection)constructor.Invoke(new object[0]);
            type.InvokeMember(
                "FillFromEncodedBytes",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.InvokeMethod,
                null, result, new object[] { bytes, encoding }
            );
                
            return result;
        }

        public static AsyncTextReader GetRequestReader (this HttpListenerContext context) {
            var encoding = context.Request.ContentEncoding ?? Encoding.UTF8;
            var adapter = new StreamDataAdapter(context.Request.InputStream, true);
            var result = new AsyncTextReader(adapter, encoding);
            return result;
        }

        public static AsyncTextWriter GetResponseWriter (this HttpListenerContext context, Encoding encoding) {
            var adapter = new StreamDataAdapter(context.Response.OutputStream, true);
            var result = new AsyncTextWriter(adapter, encoding);
            result.AutoFlush = true;
            return result;
        }
    }
}
