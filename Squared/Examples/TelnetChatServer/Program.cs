using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;
using Squared.Task;
using Squared.Task.IO;
using System.IO;
using System.Threading;
using System.Diagnostics;

namespace TelnetChatServer {
    public class DisconnectedException : Exception {
    }

    internal static class VT100 {
        public static string ESC = "\x1b";
        public static string CursorUp = "\x1b[1A";
        public static string EraseToStartOfLine = "\x1b[1K";
        public static string EraseScreen = "\x1b[2J";
    }

    class Peer {
        public int PeerId;
        public string Name;
        public bool Connected = false;
        public int CurrentId = -1;
        public AsyncTextWriter Output = null;
        public AsyncTextReader Input = null;

        public override string ToString () {
            if (Name != null)
                return Name;
            else
                return String.Format("Peer{0}", PeerId);
        }
    }

    struct Message {
        public Peer From;
        public string Text;
        public string DisplayText;
    }

    static class Program {
        static TaskScheduler Scheduler = new TaskScheduler();
        static List<Message> Messages = new List<Message>();
        static List<Peer> Peers = new List<Peer>();
        static BlockingQueue<Message> NewMessages = new BlockingQueue<Message>();
        static Future WaitingForMessages = null;
        const int MaxMessagesToDispatch = 100;
        const int MaxMessagesToStore = 1024;
        static int MessageIdBase = 0;
        static IEnumerator<object> _Dispatcher;
        static StringBuilder _MessageBuilder = new StringBuilder();

        static void DispatchNewMessage (Peer from, string message) {
            _MessageBuilder.Remove(0, _MessageBuilder.Length);
            if (from != null) {
                _MessageBuilder.Append("<");
                _MessageBuilder.Append(from);
                _MessageBuilder.Append("> ");
                _MessageBuilder.Append(message);
            } else {
                _MessageBuilder.Append("*** ");
                _MessageBuilder.Append(message);
            }

            Messages.Add(new Message { From = from, Text = message, DisplayText = _MessageBuilder.ToString() });

            if (Messages.Count > MaxMessagesToStore) {
                int numToRemove = MaxMessagesToStore / 2;
                Messages.RemoveRange(0, numToRemove);
                MessageIdBase += numToRemove;
            }

            if (WaitingForMessages != null) {
                WaitingForMessages.Complete();
                WaitingForMessages = null;
            }
        }

        static IEnumerator<object> MessageDispatcher () {
            while (true) {
                var waitList = new List<IFuture>();
                var waitingPeers = new List<Peer>();

                bool moreWork;
                do {
                    moreWork = false;
                    int newestId = (Messages.Count - 1) + MessageIdBase;
                    foreach (Peer peer in Peers.ToArray()) {
                        if (!peer.Connected)
                            continue;

                        if (peer.CurrentId != newestId) {
                            if ((newestId - peer.CurrentId) > MaxMessagesToDispatch)
                                peer.CurrentId = newestId - MaxMessagesToDispatch;

                            string text = null;
                            Message message = Messages[peer.CurrentId - MessageIdBase + 1];

                            if (message.From == peer) {
                                peer.CurrentId += 1;
                                continue;
                            } else {
                                text = message.DisplayText;
                            }

                            Future f = null;
                            f = peer.Output.PendingOperation as Future;
                            if (f == null) {
                                try {
                                    f = peer.Output.WriteLine(text);
                                } catch (SocketBufferFullException) {
                                    Console.WriteLine("Send buffer for peer {0} full", peer);
                                    continue;
                                }

                                f.RegisterOnComplete((_, r, e) => {
                                    if ((e is DisconnectedException) || (e is IOException) || (e is SocketException) || (e is FutureDisposedException)) {
                                        Scheduler.QueueWorkItem(() => {
                                            PeerDisconnected(peer);
                                        });
                                    }
                                });
                                peer.CurrentId += 1;
                            } else {
                                waitList.Add(f);
                                waitingPeers.Add(peer);
                                continue;
                            }
                        }

                        if (peer.CurrentId != newestId)
                            moreWork = true;
                    }
                } while (moreWork);

                Future waitForNewMessage = new Future();
                WaitingForMessages = waitForNewMessage;
                waitList.Add(waitForNewMessage);
                yield return Future.WaitForFirst(waitList);
            }
        }

        static void PeerConnected (Peer peer) {
            if (peer.Connected)
                return;
            peer.Connected = true;
            Console.WriteLine("User {0} has connected", peer);
            DispatchNewMessage(null, String.Format("{0} has joined the chat", peer));
            Peers.Add(peer);
        }

        static void PeerDisconnected (Peer peer) {
            if (!peer.Connected)
                return;
            Peers.Remove(peer);
            peer.Connected = false;
            Console.WriteLine("User {0} has disconnected", peer);
            DispatchNewMessage(null, String.Format("{0} has left the chat", peer));
        }

        static IEnumerator<object> PeerTask (TcpClient client, Peer peer) {
            var adapter = new SocketDataAdapter(client.Client, true);
            var input = new AsyncTextReader(adapter, Encoding.ASCII);
            var output = new AsyncTextWriter(adapter, Encoding.ASCII);
            peer.Input = input;
            peer.Output = output;

            Type[] disconnectExceptions = new Type[] { typeof(DisconnectedException), typeof(IOException), typeof(SocketException) };

            output.AutoFlush = true;
            yield return output.WriteLine("Welcome! Please enter your name.");
            output.AutoFlush = false;

            Future f = input.ReadLine();
            yield return f;
            if (f.CheckForFailure(disconnectExceptions)) {
                PeerDisconnected(peer);
                yield break;
            }
            peer.Name = (f.Result as string);

            PeerConnected(peer);

            yield return output.Write(VT100.EraseScreen);
            
            while (peer.Connected) {
                f = input.ReadLine();
                yield return f;
                string nextLineText = null;
                if (f.CheckForFailure(disconnectExceptions)) {
                    PeerDisconnected(peer);
                    yield break;
                }
                nextLineText = f.Result as string;

                if ((nextLineText != null) && (nextLineText.Length > 0)) {
                    DispatchNewMessage(peer, nextLineText);
                }
            }
        }

        static IEnumerator<object> AcceptConnectionsTask (TcpListener server) {
            server.Start();
            try {
                int nextId = 0;
                while (true) {
                    Future connection = server.AcceptIncomingConnection();
                    yield return connection;

                    Peer peer = new Peer { PeerId = nextId++ };
                    TcpClient client = connection.Result as TcpClient;
                    client.Client.Blocking = false;
                    client.Client.NoDelay = true;
                    var peerTask = PeerTask(client, peer);
                    Scheduler.Start(peerTask, TaskExecutionPolicy.RunAsBackgroundTask);
                }
            } finally {
                server.Stop();
            }
        }
        
        static void Main (string[] args) {
            Thread.CurrentThread.Name = "MainThread";
            ThreadPool.SetMinThreads(1, 4);

            TcpListener server = new TcpListener(System.Net.IPAddress.Any, 1234);
            Scheduler.Start(AcceptConnectionsTask(server), TaskExecutionPolicy.RunAsBackgroundTask);
            _Dispatcher = MessageDispatcher();
            Scheduler.Start(_Dispatcher, TaskExecutionPolicy.RunAsBackgroundTask);

            Console.WriteLine("Ready for connections.");

            try {
                while (true) {
                    Scheduler.Step();
                    Scheduler.WaitForWorkItems();
                }
            } catch (Exception ex) {
                Console.WriteLine("Unhandled exception: {0}", ex);
                Console.ReadLine();
            }
        }
    }
}
