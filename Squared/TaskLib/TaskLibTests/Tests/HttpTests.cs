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
        static readonly IPEndPoint TestPort1 = new IPEndPoint(IPAddress.Any, 12345);
        static readonly IPEndPoint TestPort2 = new IPEndPoint(IPAddress.Any, 12346);

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
                if (se.ErrorCode == 10048)
                    return true;

                throw;
            }
        }

        [Test]
        public void ServerBindsPortsWhileListeningAndUnbindsWhenStopped () {
            Assert.IsFalse(Server.IsListening);

            Server.EndPoints.Add(TestPort1);
            Server.EndPoints.Add(TestPort2);
            Scheduler.WaitFor(Server.StartListening());

            Assert.IsTrue(Server.IsListening);

            Assert.IsTrue(IsEndPointBound(TestPort1));
            Assert.IsTrue(IsEndPointBound(TestPort2));

            Server.StopListening();

            Assert.IsFalse(IsEndPointBound(TestPort1));
            Assert.IsFalse(IsEndPointBound(TestPort2));
        }

        [Test]
        public void ThrowsIfYouAddOrRemoveEndPointsWhileListening () {
            Scheduler.WaitFor(Server.StartListening());

            Assert.Throws<InvalidOperationException>(
                () => Server.EndPoints.Add(TestPort1)
            );
            Assert.Throws<InvalidOperationException>(
                () => Server.EndPoints.Remove(TestPort1)
            );
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
