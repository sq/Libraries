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
        static readonly IPEndPoint ListenPort1 = new IPEndPoint(IPAddress.Any, 12345);
        static readonly IPEndPoint ListenPort2 = new IPEndPoint(IPAddress.Any, 12346);

        const string Server1 = "http://localhost:12345/";
        const string Server2 = "http://localhost:12346/";

        public TaskScheduler Scheduler;
        public HttpServer Server;

        [SetUp]
        public void SetUp () {
            Scheduler = new TaskScheduler();
            Server = new HttpServer(Scheduler);
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

            Server.EndPoints.Add(ListenPort1);
            Server.EndPoints.Add(ListenPort2);
            Scheduler.WaitFor(Server.StartListening());

            Assert.IsTrue(Server.IsListening);

            Assert.IsTrue(IsEndPointBound(ListenPort1));
            Assert.IsTrue(IsEndPointBound(ListenPort2));

            Server.StopListening();

            Assert.IsFalse(IsEndPointBound(ListenPort1));
            Assert.IsFalse(IsEndPointBound(ListenPort2));
        }

        [Test]
        public void ThrowsIfYouAddOrRemoveEndPointsWhileListening () {
            Scheduler.WaitFor(Server.StartListening());

            Assert.Throws<InvalidOperationException>(
                () => Server.EndPoints.Add(ListenPort1)
            );
            Assert.Throws<InvalidOperationException>(
                () => Server.EndPoints.Remove(ListenPort1)
            );
        }

        [Test]
        public void AcceptRequestReturnsFutureThatCompletesWhenRequestIsAvailable () {
            Server.EndPoints.Add(ListenPort1);
            Scheduler.WaitFor(Server.StartListening());

            var fRequest = Server.AcceptRequest();

            Assert.IsFalse(fRequest.Completed);

            var wc = new WebClient();
            var fResponse = Future.RunInThread(
                () => wc.DownloadData(Server1 + "test")
            );

            var request = Scheduler.WaitFor(fRequest, 2);
            Assert.AreEqual("GET", request.Line.Method);
            Assert.AreEqual("/test", request.Line.Uri.AbsolutePath);
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
