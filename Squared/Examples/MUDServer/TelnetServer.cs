using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;
using Squared.Task;
using Squared.Task.IO;
using System.IO;
using System.Net;

namespace MUDServer {
    public class TelnetClient : IDisposable {
        public TelnetServer Server;
        public SocketDataAdapter Data;
        public AsyncTextReader Input;
        public AsyncTextWriter Output;
        private BlockingQueue<string> _OutboundText = new BlockingQueue<string>();
        private IFuture _SendFuture;

        internal TelnetClient (TelnetServer server, TcpClient client) {
            Server = server;
            client.Client.NoDelay = true;
            client.Client.Blocking = false;
            Data = new SocketDataAdapter(client.Client, true);
            Encoding encoding = Encoding.GetEncoding(1252);
            Input = new AsyncTextReader(Data, encoding);
            Output = new AsyncTextWriter(Data, encoding);
            Output.AutoFlush = true;
            _SendFuture = server._Scheduler.Start(SendMessagesTask(), TaskExecutionPolicy.RunWhileFutureLives);
        }

        public IFuture ReadLineText () {
            var f = new Future();
            var inner = Input.ReadLine();
            inner.RegisterOnComplete((_) => {
                var e = _.Error;
                if ((e is SocketDisconnectedException) || (e is IOException) || (e is SocketException)) {
                    f.Complete();
                    Dispose();
                    return;
                } else if (e != null) {
                    f.Fail(e);
                    return;
                }
                string text = _.Result as string;
                int count = 0;
                int toSkip = 0;
                char[] buf = new char[text.Length];
                for (int i = 0; i < text.Length; i++) {
                    toSkip -= 1;
                    char ch = text[i];
                    if (ch == 8) {
                        if (count > 0)
                            count--;
                    } else if (ch == 0xFF) {
                        toSkip = 3;
                    } else if ((ch >= 32) && (ch <= 127)) {
                        if (toSkip <= 0) {
                            buf[count] = ch;
                            count++;
                        }
                    }
                }
                text = new string(buf, 0, count);
                f.Complete(text);
            });
            return f;
        }

        private IEnumerator<object> SendMessagesTask () {
            IFuture f;
            string text = null;
            while (true) {
                f = _OutboundText.Dequeue();
                yield return f;
                text = f.Result as string;

                while (true) {
                    try {
                        f = Output.Write(text);
                        break;
                    } catch (SocketBufferFullException) {
                    }
                    yield return new Yield();
                }
                yield return f;

                if (f.CheckForFailure(typeof(SocketDisconnectedException))) {
                    Dispose();
                    yield break;
                }
            }
        }

        public void RegisterOnDispose (OnDispose handler) {
            _SendFuture.RegisterOnDispose(handler);
        }

        public void ClearScreen () {
            _OutboundText.Enqueue("\x1b[2J");
        }

        public void SendText (string text) {
            _OutboundText.Enqueue(text);
        }

        public void Dispose () {
            if (Data != null) {
                Data.Dispose();
                Data = null;
            }

            if (_SendFuture != null) {
                _SendFuture.Dispose();
                _SendFuture = null;
            }
        }
    }

    public class TelnetServer : IDisposable {
        internal TaskScheduler _Scheduler;
        private TcpListener _Listener;
        private IFuture _ListenerTask;
        private List<TelnetClient> _Clients = new List<TelnetClient>();
        private BlockingQueue<TelnetClient> _NewClients = new BlockingQueue<TelnetClient>();

        public TelnetServer (TaskScheduler scheduler, IPAddress address, int port) {
            _Scheduler = scheduler;
            _Listener = new TcpListener(address, port);
            _Listener.Start();
            _ListenerTask = scheduler.Start(this.ListenTask(), TaskExecutionPolicy.RunWhileFutureLives);
        }

        private IEnumerator<object> ListenTask () {
            while (true) {
                var f = _Listener.AcceptIncomingConnection();
                yield return f;
                TcpClient tcpClient = f.Result as TcpClient;
                TelnetClient client = new TelnetClient(this, tcpClient);
                _Clients.Add(client);
                _NewClients.Enqueue(client);
            }
        }

        public IFuture AcceptNewClient () {
            return _NewClients.Dequeue();
        }

        public void Dispose () {
            foreach (var tc in _Clients) {
                tc.Dispose();
            }

            if (_ListenerTask != null) {
                _ListenerTask.Dispose();
                _ListenerTask = null;
            }

            if (_Listener != null) {
                _Listener.Stop();
                _Listener = null;
            }
        }
    }
}
