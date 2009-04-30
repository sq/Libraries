using System;
using System.Collections.Generic;
using System.Threading;
using System.Net.Sockets;
using System.IO;
using System.Text;
using System.Diagnostics;
using Squared.Util;

namespace Squared.Task.IO {
    public class SocketDisconnectedException : IOException {
        public SocketDisconnectedException ()
            : base("The operation failed because the socket has disconnected.") {
        }
    }

    public class SocketBufferFullException : IOException {
        public SocketBufferFullException ()
            : base("The operation failed because the socket's buffer is full.") {
        }
    }

    public class SocketDataAdapter : IAsyncDataSource, IAsyncDataWriter {
        Socket _Socket;
        bool _OwnsSocket;
        AsyncCallback _ReadCallback, _WriteCallback;

        public SocketDataAdapter (Socket socket)
            : this(socket, true) {
        }

        public SocketDataAdapter (Socket socket, bool ownsSocket) {
            _Socket = socket;
            _OwnsSocket = ownsSocket;
            _ReadCallback = ReadCallback;
            _WriteCallback = WriteCallback;
        }

        public void Dispose () {
            if (_OwnsSocket)
                _Socket.Close();
        }

        private void ReadCallback (IAsyncResult ar) {
            var f = (IFuture)ar.AsyncState;

            if (!_Socket.Connected) {
                f.Fail(new SocketDisconnectedException());
                return;
            }

            try {
                int bytesRead = _Socket.EndReceive(ar);
                if (bytesRead == 0) {
                    f.Fail(new SocketDisconnectedException());
                } else {
                    f.Complete(bytesRead);
                }
            } catch (FutureHandlerException) {
                throw;
            } catch (Exception ex) {
                f.Fail(ex);
            }
        }

        public IFuture Read (byte[] buffer, int offset, int count) {
            var f = new Future();
            if (!_Socket.Connected) {
                f.Fail(new SocketDisconnectedException());
            } else {
                SocketError errorCode;
                if (_Socket.Available >= count) {
                    try {
                        int bytesRead = _Socket.Receive(buffer, offset, count, SocketFlags.None, out errorCode);
                        if (bytesRead == 0) {
                            f.Fail(new SocketDisconnectedException());
                        } else {
                            f.Complete(bytesRead);
                        }
                    } catch (Exception ex) {
                        f.Fail(ex);
                    }
                } else {
                    _Socket.BeginReceive(buffer, offset, count, SocketFlags.None, out errorCode, _ReadCallback, f);
                }
            }
            return f;
        }

        private bool IsSendBufferFull () {
            if (_Socket.Blocking)
                return false;

            return !_Socket.Poll(0, SelectMode.SelectWrite);
        }

        private void WriteCallback (IAsyncResult ar) {
            var f = (IFuture)ar.AsyncState;

            if (!_Socket.Connected) {
                f.Fail(new SocketDisconnectedException());
                return;
            }

            try {
                int bytesSent = _Socket.EndSend(ar);
                f.Complete();
            } catch (FutureHandlerException) {
                throw;
            } catch (Exception ex) {
                f.Fail(ex);
            }
        }

        public IFuture Write (byte[] buffer, int offset, int count) {
            var f = new Future();
            if (!_Socket.Connected) {
                f.Fail(new SocketDisconnectedException());
            } else {
                if (IsSendBufferFull())
                    throw new SocketBufferFullException();
                SocketError errorCode;
                _Socket.BeginSend(buffer, offset, count, SocketFlags.None, out errorCode, _WriteCallback, f);
            }
            return f;
        }

        public bool EndOfStream {
            get {
                return !_Socket.Connected;
            }
        }
    }
}
