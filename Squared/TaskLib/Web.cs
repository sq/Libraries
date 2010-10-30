using System;
using System.Collections.Generic;
using System.Threading;
using System.Net;
using Squared.Task.IO;
using System.Text;
using System.Web;

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
    }
}
