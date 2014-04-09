using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Squared.Task.Http {
    public partial class HttpServer {
        private class ListenerContext : IDisposable {
            public const int QueueSize = 16;

            public readonly HttpServer Server;
            public readonly EndPoint[] EndPoints;
            public readonly Socket[] ListeningSockets;

            public readonly BlockingQueue<Socket> IncomingConnections = new BlockingQueue<Socket>(); 
            public readonly SignalFuture Started = new SignalFuture();

            private readonly AsyncCallback OnAcceptBegan;

            public ListenerContext (HttpServer server) {
                Server = server;
                EndPoints = server.EndPoints.ToArray();
                ListeningSockets = new Socket[EndPoints.Length];

                OnAcceptBegan = _OnAcceptBegan;
            }

            public void BindAll () {
                for (var i = 0; i < EndPoints.Length; i++) {
                    var endPoint = EndPoints[i];
                    var socket = ListeningSockets[i] = new Socket(endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                    socket.Bind(endPoint);
                }
            }

            public void ListenAll () {
                foreach (var socket in ListeningSockets) {
                    socket.Listen(QueueSize);
                    socket.BeginAccept(_OnAcceptBegan, socket);
                }
            }

            public void Start () {
                BindAll();
                ListenAll();
                Started.Complete();
            }

            private void _OnAcceptBegan (IAsyncResult ar) {
                var listeningSocket = (Socket)ar.AsyncState;

                try {
                    var socket = listeningSocket.EndAccept(ar);
                    IncomingConnections.Enqueue(socket);
                } catch (ObjectDisposedException) {
                    // This is fine. Don't try to accept again on this socket.
                    return;
                } catch (Exception exc) {
                    Server.OnListenerError(exc);
                }

                if (Server.IsListening)
                    listeningSocket.BeginAccept(OnAcceptBegan, listeningSocket);
            }

            public void Dispose () {
                for (var i = 0; i < ListeningSockets.Length; i++) {
                    if (ListeningSockets[i] != null)
                        ListeningSockets[i].Dispose();

                    ListeningSockets[i] = null;
                }

                Started.Dispose();
            }
        }
    }
}
