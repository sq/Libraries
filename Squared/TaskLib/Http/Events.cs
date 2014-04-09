using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Squared.Task.Http {
    public class ConnectionEventArgs : EventArgs {
        public readonly EndPoint LocalEndPoint, RemoteEndPoint;

        public ConnectionEventArgs (EndPoint localEndPoint, EndPoint remoteEndPoint) {
            LocalEndPoint = localEndPoint;
            RemoteEndPoint = remoteEndPoint;
        }
    }
}
