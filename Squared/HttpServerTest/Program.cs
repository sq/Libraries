using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
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
            Console.WriteLine("Received request from {0} for {1}", request.RemoteEndPoint, request.Line.Uri);

            using (request)
                yield return request.Response.SendResponse("Hello world!");
        }
    }
}
