using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Squared.Task;
using Squared.Task.Http;

namespace HttpServerTest {
    public static class Program {
        public static readonly TaskScheduler Scheduler = new TaskScheduler();
        public static readonly HttpServer Server;

        static Program () {
            Server = new HttpServer(Scheduler) {
                EndPoints = {
                    new IPEndPoint(IPAddress.Any, 10337)
                }
            };

            Server.SocketOpened += OnSocketOpened;
            Server.SocketClosed += OnSocketClosed;
        }

        static void OnSocketOpened (object server, ConnectionEventArgs e) {
            Console.WriteLine("{0} <- {1} open", e.LocalEndPoint, e.RemoteEndPoint);
        }

        static void OnSocketClosed (object server, ConnectionEventArgs e) {
            Console.WriteLine("{0} <- {1} close", e.LocalEndPoint, e.RemoteEndPoint);
        }

        static void Main (string[] args) {
            Scheduler.Start(HttpTask());

            while (true) {
                Scheduler.Step();
                Scheduler.WaitForWorkItems();
            }
        }

        static IEnumerator<object> HttpTask () {
            yield return Server.StartListening();
            Console.WriteLine("Ready for requests at {0}", Server.EndPoints.First());

            while (true) {
                var fRequest = Server.AcceptRequest();
                yield return fRequest;

                Scheduler.Start(RequestTask(fRequest.Result));
            }
        }

        static IEnumerator<object> RequestTask (HttpServer.Request request) {
            Console.WriteLine("{0} <- {1} {2}", request.LocalEndPoint, request.RemoteEndPoint, request.Line.Uri);

            using (request) {
                if (request.Line.Uri.PathAndQuery == "/favicon.ico") {
                    request.Response.StatusCode = 404;
                    request.Response.StatusText = "File not found";
                    yield return request.Response.SendHeaders();
                } else {
                    yield return request.Response.SendResponse("Hello world!");
                }
            }
        }
    }
}
