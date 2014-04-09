using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Squared.Task.IO;

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

        private IFuture ActiveListener = null;
        private readonly BlockingQueue<Request> IncomingRequests = new BlockingQueue<Request>();

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

        private void _RequestOnComplete (IFuture future) {
            if (future.Failed)
                OnRequestError(future.Error);
        }

        public Future<Request> AcceptRequest () {
            return IncomingRequests.Dequeue();
        }

        private IEnumerator<object> ListenerTask (ListenerContext context) {
            using (context) {
                yield return Future.RunInThread(context.Start);

                var wfns = new WaitForNextStep();
                var acceptedConnections = new List<Socket>();
                const int connectionsToAcceptPerStep = 4;
                Future<Socket> acceptedConnection = null;

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
                        var fRequest = Scheduler.Start(RequestTask(context, ac));
                        fRequest.RegisterOnComplete(RequestOnComplete);
                    }

                    acceptedConnections.Clear();
                    acceptedConnection = context.IncomingConnections.Dequeue();

                    yield return acceptedConnection;
                }
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

            IsDisposed = true;
        }
    }
}
