using System;
using System.Collections.Generic;
using System.Threading;
using System.Net;
using Squared.Task.IO;
using System.Text;
using System.Web;

namespace Squared.Task {
    public static class Web {
        public class Response {
            public string Body;
            public string ContentType;
            public HttpStatusCode StatusCode;
            public string StatusDescription;

            public override string ToString () {
                return Body;
            }
        }

        public static IEnumerator<object> IssueRequest (HttpWebRequest request) {
            var fResponse = new Future<HttpWebResponse>();
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

            yield return fResponse;

            string responseText = null;

            using (var response = fResponse.Result) {
                using (var stream = response.GetResponseStream())
                using (var adapter = new AsyncTextReader(new StreamDataAdapter(stream, false))) {
                    var fText = adapter.ReadToEnd();

                    yield return fText;

                    responseText = fText.Result;
                }

                yield return new Result(new Response {
                    Body = responseText,
                    ContentType = response.ContentType,
                    StatusCode = response.StatusCode,
                    StatusDescription = response.StatusDescription
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
}
