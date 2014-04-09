using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using System.IO;
using System.Threading;
using System.Net.Sockets;
using System.Net;
using System.Linq;

namespace Squared.Task.Http {
    [TestFixture]
    public class HttpTests {
        IPEndPoint ListenPort;
        string ServerUri;

        public TaskScheduler Scheduler;
        public HttpServer Server;

        const int BasePort = 12300;
        const int NumPorts = 99;
        static int PortIndex = 0;

        const double WebClientTimeout = 3;

        [SetUp]
        public void SetUp () {
            Scheduler = new TaskScheduler();
            Server = new HttpServer(Scheduler);

            var port = BasePort + PortIndex;
            PortIndex += 1;
            if (PortIndex > NumPorts)
                PortIndex = 0;

            ListenPort = new IPEndPoint(IPAddress.Any, port);
            ServerUri = String.Format("http://localhost:{0}/", port);
        }

        private static bool IsEndPointBound (EndPoint endPoint) {
            try {
                using (var socket = new Socket(endPoint.AddressFamily, SocketType.Stream, ProtocolType.IP))
                    socket.Bind(endPoint);

                return false;
            } catch (SocketException se) {
                const int endPointAlreadyBound = 10048;

                if (se.ErrorCode == endPointAlreadyBound)
                    return true;

                throw;
            }
        }
        
        // FIXME: Move this into Task.IO or Task.Sockets, maybe?
        private static Future<Socket> ConnectAsync (EndPoint endPoint) {
            var fResult = new Future<Socket>();
            var socket = new Socket(endPoint.AddressFamily, SocketType.Stream, ProtocolType.IP);
            socket.BeginConnect(endPoint, (ar) => {
                try {
                    socket.EndConnect(ar);
                    fResult.SetResult(socket, null);
                } catch (Exception exc) {
                    fResult.SetResult(null, exc);
                    socket.Dispose();
                }
            }, null);
            return fResult;
        }

        [Test]
        public void ServerBindsPortsWhileListeningAndUnbindsWhenStopped () {
            Assert.IsFalse(Server.IsListening);

            Server.EndPoints.Add(ListenPort);
            Scheduler.WaitFor(Server.StartListening());

            Assert.IsTrue(Server.IsListening);

            Assert.IsTrue(IsEndPointBound(ListenPort));

            Server.StopListening();

            Assert.IsFalse(IsEndPointBound(ListenPort));
        }

        [Test]
        public void ThrowsIfYouAddOrRemoveEndPointsWhileListening () {
            Scheduler.WaitFor(Server.StartListening());

            Assert.Throws<InvalidOperationException>(
                () => Server.EndPoints.Add(ListenPort)
            );
            Assert.Throws<InvalidOperationException>(
                () => Server.EndPoints.Remove(ListenPort)
            );
        }

        [Test]
        public void AcceptRequestReturnsFutureThatCompletesWhenRequestIsAvailable () {
            Server.EndPoints.Add(ListenPort);
            Scheduler.WaitFor(Server.StartListening());

            var fRequest = Server.AcceptRequest();

            Assert.IsFalse(fRequest.Completed);

            using (var wc = new WebClient()) {
                var fGet = Future.RunInThread(
                    () => {
                        var result = wc.DownloadData(ServerUri + "test");
                        Console.WriteLine("Download complete");
                        return result;
                    }
                );

                var request = Scheduler.WaitFor(fRequest, 2);
                Assert.IsNotNull(request);

                Console.WriteLine(request);

                request.Dispose();

                Scheduler.WaitFor(fGet, WebClientTimeout);
            }
        }

        [Test]
        public void AcceptedRequestContainsParsedHeaders () {
            Server.EndPoints.Add(ListenPort);
            Scheduler.WaitFor(Server.StartListening());

            using (var wc = new WebClient()) {
                var fGet = Future.RunInThread(
                    () => {
                        var result = wc.DownloadData(ServerUri + "subdir/test?a=b&c=d");
                        Console.WriteLine("Download complete");
                        return result;
                    }
                );

                var request = Scheduler.WaitFor(Server.AcceptRequest(), 3);
                Console.WriteLine(request);

                Assert.AreEqual("GET", request.Line.Method);
                Assert.AreEqual("localhost", request.Line.Uri.Host);
                Assert.AreEqual("/subdir/test", request.Line.Uri.AbsolutePath);
                Assert.AreEqual("?a=b&c=d", request.Line.Uri.Query);

                request.Dispose();

                Scheduler.WaitFor(fGet, WebClientTimeout);
            }
        }

        [Test]
        public void AcceptedRequestOffersBody () {
            Server.EndPoints.Add(ListenPort);
            Scheduler.WaitFor(Server.StartListening());

            var dataToUpload = new byte[1024 * 1024];
            for (var i = 0; i < dataToUpload.Length; i++)
                dataToUpload[i] = (byte)(i % 256);

            using (var wc = new WebClient()) {
                var fPost = Future.RunInThread(
                    () => {
                        var result = wc.UploadData(ServerUri, dataToUpload);
                        Console.WriteLine("Upload complete");
                        return result;
                    }
                );

                var request = Scheduler.WaitFor(Server.AcceptRequest(), 3);
                Console.WriteLine(request);

                Assert.AreEqual("POST", request.Line.Method);
                Assert.AreEqual("localhost", request.Line.Uri.Host);
                Assert.AreEqual("/", request.Line.Uri.AbsolutePath);

                var requestBody = Scheduler.WaitFor(request.Body.Bytes);
                Assert.AreEqual(dataToUpload.Length, requestBody.Length);
                Assert.AreEqual(dataToUpload, requestBody);

                request.Dispose();

                Scheduler.WaitFor(fPost, WebClientTimeout);
            }
        }

        [TearDown]
        public void TearDown () {
            if (Server != null)
                Server.Dispose();

            if (Scheduler != null)
                Scheduler.Dispose();

            Server = null;
            Scheduler = null;
        }
    }
}
