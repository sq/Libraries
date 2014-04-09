using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Squared.Task.Http {
    public class HttpServer : IDisposable {
        public class EndPointList : IEnumerable<EndPoint> {
            public readonly HttpServer Owner;
            private readonly HashSet<EndPoint> EndPoints = new HashSet<EndPoint>();  

            internal EndPointList (HttpServer owner) {
                Owner = owner;
            }

            private void CheckInvariant () {
                if ((Owner == null) || Owner.IsDisposed)
                    throw new ObjectDisposedException("Owner");

                if (Owner.IsListening)
                    throw new InvalidOperationException("Endpoint list may not be modified while server is listening");
            }

            public void Add (EndPoint endPoint) {
                CheckInvariant();

                EndPoints.Add(endPoint);
            }

            public void Add (params EndPoint[] endPoints) {
                CheckInvariant();

                foreach (var ep in endPoints)
                    EndPoints.Add(ep);
            }

            public bool Remove (EndPoint endPoint) {
                CheckInvariant();

                return EndPoints.Remove(endPoint);
            }

            public EndPoint[] ToArray () {
                return EndPoints.ToArray();
            }

            public int Count {
                get {
                    return EndPoints.Count;
                }
            }

            public IEnumerator<EndPoint> GetEnumerator () {
                return EndPoints.GetEnumerator();
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator () {
                return EndPoints.GetEnumerator();
            }
        }

        private class ListenerContext : IDisposable {
            public readonly EndPoint[] EndPoints;
            public readonly Socket[] Sockets;
            public readonly SignalFuture Started = new SignalFuture();

            public ListenerContext (IEnumerable<EndPoint> endPoints) {
                EndPoints = endPoints.ToArray();
                Sockets = new Socket[EndPoints.Length];
            }

            public void BindAll () {
                for (var i = 0; i < EndPoints.Length; i++) {
                    var endPoint = EndPoints[i];
                    var socket = Sockets[i] = new Socket(endPoint.AddressFamily, SocketType.Stream, ProtocolType.IP);
                    socket.Bind(endPoint);
                }
            }

            public void Dispose () {
                for (var i = 0; i < Sockets.Length; i++) {
                    if (Sockets[i] != null)
                        Sockets[i].Dispose();

                    Sockets[i] = null;
                }

                Started.Dispose();
            }
        }

        public readonly TaskScheduler Scheduler;
        public readonly EndPointList EndPoints;

        public BackgroundTaskErrorHandler ListenerErrorHandler;

        private IFuture _ActiveListener = null;

        public HttpServer (TaskScheduler scheduler) {
            EndPoints = new EndPointList(this);

            Scheduler = scheduler;
        }

        public bool IsDisposed {
            get;
            private set;
        }

        public bool IsListening {
            get {
                return (_ActiveListener != null) && !_ActiveListener.Completed;
            }
        }

        public SignalFuture StartListening () {
            if (IsListening)
                throw new InvalidOperationException("Already listening");

            var context = new ListenerContext(EndPoints.ToArray());
            _ActiveListener = Scheduler.Start(ListenerTask(context));

            return context.Started;
        }

        private IEnumerator<object> ListenerTask (ListenerContext context) {
            using (context) {
                yield return Future.RunInThread(context.BindAll);

                context.Started.Complete();

                var wfns = new WaitForNextStep();

                while (true) {
                    yield return wfns;
                }
            }
        }

        public void StopListening () {
            if (_ActiveListener != null) {
                _ActiveListener.Dispose();
                _ActiveListener = null;
            }
        }

        public void Dispose () {
            if (IsDisposed)
                return;

            IsDisposed = true;

            if (IsListening)
                StopListening();
        }
    }
}
