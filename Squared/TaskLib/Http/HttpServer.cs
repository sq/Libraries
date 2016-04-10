using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Squared.Task.IO;
using Squared.Threading;

namespace Squared.Task.Http {
    public partial class HttpServer : IDisposable {
        public readonly TaskScheduler Scheduler;
        public readonly EndPointList EndPoints;

        /// <summary>
        /// Handles any errors not processed by the listener/request error handlers.
        /// </summary>
        public BackgroundTaskErrorHandler ErrorHandler;
        /// <summary>
        /// Handles errors that occur during socket listening.
        /// </summary>
        public BackgroundTaskErrorHandler ListenerErrorHandler;
        /// <summary>
        /// Handles errors that occur during request processing.
        /// </summary>
        public BackgroundTaskErrorHandler RequestErrorHandler;

        public EventHandler<ConnectionEventArgs> SocketOpened;
        public EventHandler<ConnectionEventArgs> SocketClosed;
        public Action<string>                    Trace;

        private IFuture ActiveListener = null;
        private readonly BlockingQueue<Request> IncomingRequests = new BlockingQueue<Request>();
        private readonly HashSet<Request> InFlightRequests = new HashSet<Request>(); 

        private readonly OnComplete RequestOnComplete;

        public HttpServer (TaskScheduler scheduler) {
            EndPoints = new EndPointList(this);

            Scheduler = scheduler;

            RequestOnComplete = _RequestOnComplete;
        }

        public bool IsDisposed {
            get;
            private set;
        }

        public bool IsListening {
            get {
                return !IsDisposed && (ActiveListener != null) && !ActiveListener.Completed;
            }
        }

        public SignalFuture StartListening () {
            if (IsListening)
                throw new InvalidOperationException("Already listening");

            var context = new ListenerContext(this);
            ActiveListener = Scheduler.Start(ListenerTask(context));
            ActiveListener.RegisterOnComplete((_) => {
                if (_.Failed)
                    OnListenerError(_.Error);
            });

            return context.Started;
        }

        internal void OnError (Exception exc) {
            if (
                (ErrorHandler == null) ||
                !ErrorHandler(exc)
            )
                Scheduler.OnTaskError(exc);
        }

        internal void OnListenerError (Exception exc) {
            if (
                (ListenerErrorHandler == null) || 
                !ListenerErrorHandler(exc)
            )
                OnError(new Exception("Error while listening for http requests", exc));
        }

        internal void OnRequestError (Exception exc) {
            if (
                (RequestErrorHandler == null) ||
                !RequestErrorHandler(exc)
            )
                OnError(new Exception("Error while handling request", exc));
        }

        private void OnRequestCreated (Request request) {
            lock (InFlightRequests)
                InFlightRequests.Add(request);
        }

        private void OnRequestDisposed (Request request) {
            lock (InFlightRequests)
                InFlightRequests.Remove(request);
        }

        private void _RequestOnComplete (IFuture future) {
            if (future.Failed)
                OnRequestError(future.Error);
        }

        public Future<Request> AcceptRequest () {
            if (!IsListening)
                throw new InvalidOperationException("Server is not listening");

            return IncomingRequests.Dequeue();
        }

        private IEnumerator<object> ListenerTask (ListenerContext context) {
            using (context) {
                yield return Future.RunInThread(context.Start);

                var wfns = new WaitForNextStep();
                var acceptedConnections = new List<IncomingConnection>();
                const int connectionsToAcceptPerStep = 4;
                Future<IncomingConnection> acceptedConnection = null;

                while (true) {
                    if (acceptedConnection != null) {
                        if (acceptedConnection.Failed)
                            OnListenerError(acceptedConnection.Error);
                        else
                            acceptedConnections.Add(acceptedConnection.Result);
                    }

                    context.IncomingConnections.DequeueMultiple(
                        acceptedConnections, connectionsToAcceptPerStep
                    );

                    foreach (var ac in acceptedConnections) {
                        var fKeepAlive = Scheduler.Start(KeepAliveTask(context, ac));
                        fKeepAlive.RegisterOnComplete(RequestOnComplete);
                    }

                    acceptedConnections.Clear();
                    acceptedConnection = context.IncomingConnections.Dequeue();

                    yield return acceptedConnection;
                }
            }
        }

        private IEnumerator<object> KeepAliveTask (ListenerContext context, IncomingConnection incomingConnection) {
            var socket = incomingConnection.Socket;
            EndPoint localEp = socket.LocalEndPoint, remoteEp = socket.RemoteEndPoint;
            var evtArgs = new ConnectionEventArgs(localEp, remoteEp);

            var keepAliveStarted = DateTime.UtcNow;

            if (SocketOpened != null)
                SocketOpened(this, evtArgs);

            int requestCount = 0;

            try {
                using (var adapter = new SocketDataAdapter(socket, true)) {
                    while (!adapter.IsDisposed && adapter.Socket.Connected) {
                        var fTask = Scheduler.Start(RequestTask(context, adapter));
                        yield return fTask;

                        requestCount += 1;

                        if (fTask.Failed) {
                            adapter.Dispose();
                            yield break;
                        }
                    }
                }
            } finally {
                var keepAliveEnded = DateTime.UtcNow;

                if (SocketClosed != null)
                    SocketClosed(this, evtArgs);

                if (Trace != null)
                    Trace(
                        String.Format(
                            "KA_START {0:0000.0}ms  KA_LENGTH {1:00000.0}ms  {2} REQ(S)",
                            (keepAliveStarted - incomingConnection.AcceptedWhenUTC).TotalMilliseconds,
                            (keepAliveEnded - keepAliveStarted).TotalMilliseconds,
                            requestCount
                        )
                    );
            }
        }

        public void StopListening () {
            if (ActiveListener != null) {
                ActiveListener.Dispose();
                ActiveListener = null;
            }
        }

        public void Dispose () {
            if (IsDisposed)
                return;

            if (IsListening)
                StopListening();

            Request[] inFlightRequests;
            lock (InFlightRequests) {
                inFlightRequests = InFlightRequests.ToArray();
                InFlightRequests.Clear();
            }

            foreach (var ifr in inFlightRequests)
                ifr.Dispose();

            IsDisposed = true;
        }
    }
}
