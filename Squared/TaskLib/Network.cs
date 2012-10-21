using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Net.Sockets;

namespace Squared.Task {
    public static class Network {
        public struct UdpPacket {
            public readonly byte[] Bytes;
            public readonly IPEndPoint EndPoint;

            public UdpPacket(byte[] bytes, IPEndPoint endPoint) {
                Bytes = bytes;
                EndPoint = endPoint;
            }
        }

        public static Future<TcpClient> ConnectTo (string host, int port) {
            var f = new Future<TcpClient>();
            TcpClient client = new TcpClient();
            client.BeginConnect(host, port, (ar) => {
                try {
                    client.EndConnect(ar);
                    f.Complete(client);
                } catch (FutureHandlerException) {
                    throw;
                } catch (Exception ex) {
                    f.Fail(ex);
                    client.Close();
                }
            }, null);
            return f;
        }

        public static Future<TcpClient> ConnectTo (IPAddress address, int port) {
            var f = new Future<TcpClient>();
            TcpClient client = new TcpClient();
            client.BeginConnect(address, port, (ar) => {
                try {
                    client.EndConnect(ar);
                    f.Complete(client);
                } catch (FutureHandlerException) {
                    throw;
                } catch (Exception ex) {
                    f.Fail(ex);
                    client.Close();
                }
            }, null);
            return f;
        }
    }

    public static class NetworkExtensionMethods {
        public static Future<TcpClient> AcceptIncomingConnection (this TcpListener listener) {
            var f = new Future<TcpClient>();
            listener.BeginAcceptTcpClient((ar) => {
                try {
                    TcpClient result = listener.EndAcceptTcpClient(ar);
                    f.Complete(result);
                } catch (FutureHandlerException) {
                    throw;
                } catch (Exception ex) {
                    f.Fail(ex);
                }
            }, null);
            return f;
        }

        public static Future<Network.UdpPacket> AsyncReceive (this UdpClient udpClient) {
            var f = new Future<Network.UdpPacket>();
            try {
                udpClient.BeginReceive((ar) => {
                    IPEndPoint endpoint = default(IPEndPoint);
                    try {                        
                        var bytes = udpClient.EndReceive(ar, ref endpoint);
                        f.Complete(new Network.UdpPacket(bytes, endpoint));
                    } catch (FutureHandlerException) {
                        throw;
                    } catch (Exception ex) {
                        f.Fail(ex);
                    }
                }, null);
            } catch (Exception ex) {
                f.Fail(ex);
            }
            return f;
        }

        public static Future<int> AsyncSend (this UdpClient udpClient, byte[] datagram, int bytes, IPEndPoint endPoint) {
            var f = new Future<int>();
            try {
                udpClient.BeginSend(
                    datagram, bytes, endPoint,
                    (ar) => {
                        try {
                            var bytesSent = udpClient.EndSend(ar);
                            f.Complete(bytesSent);
                        } catch (FutureHandlerException) {
                            throw;
                        } catch (Exception ex) {
                            f.Fail(ex);
                        }
                    },
                    null
                );
            } catch (Exception ex) {
                f.Fail(ex);
            }
            return f;
        }

        public static Future<int> AsyncSend (this UdpClient udpClient, byte[] datagram, int bytes, string hostname, int port) {
            var f = new Future<int>();
            try {
                udpClient.BeginSend(
                    datagram, bytes, hostname, port,
                    (ar) => {
                        try {
                            var bytesSent = udpClient.EndSend(ar);
                            f.Complete(bytesSent);
                        } catch (FutureHandlerException) {
                            throw;
                        } catch (Exception ex) {
                            f.Fail(ex);
                        }
                    }, 
                    null
                );
            } catch (Exception ex) {
                f.Fail(ex);
            }
            return f;
        }
    }
}
