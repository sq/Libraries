using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;
using Squared.Task;
using System.IO;
using System.Threading;
using System.Diagnostics;

namespace TelnetChatServer {
    public class DisconnectedException : Exception {
    }

    internal static class VT100 {
        public static char ESC = '\x1b';

        public static char[] CursorUp { get {
            return new char[] { ESC, '[', '1', 'A' };
        }}

        public static char[] EraseToStartOfLine { get {
            return new char[] { ESC, '[', '1', 'K' };
        }}

        public static char[] EraseScreen { get {
            return new char[] { ESC, '[', '2', 'J' };
        }}
    }

    internal static class TelnetExtensionMethods {
        internal static Future TelnetWriteLine (this StreamWriter writer, string value) {
            var f = new Future();
            WaitCallback fn = (state) => {
                try {
                    writer.WriteLine(value);
                    writer.Flush();
                    f.Complete();
                } catch (Exception ex) {
                    f.Fail(ex);
                }
            };
            ThreadPool.QueueUserWorkItem(fn);
            return f;
        }

        internal static Future TelnetReadLine (this AsyncStreamReader reader) {
            Future innerFuture = reader.ReadLine();
            Future f = new Future();
            innerFuture.RegisterOnComplete((result, error) => {
                if ((result == null) && (error == null))
                    error = new DisconnectedException();
                f.SetResult(result, error);
            });
            return f;
        }
    }

    class Peer {
        public int Id;
        public string Name;
        public bool Connected;

        public override string ToString () {
            if (Name != null)
                return Name;
            else
                return String.Format("Peer{0}", Id);
        }
    }

    struct Message {
        public Peer From;
        public string Text;
    }

    static class Program {
        static TaskScheduler Scheduler = new TaskScheduler(true);
        static List<Message> Messages = new List<Message>();
        static Future NewMessageFuture = new Future();

        static void DispatchNewMessage (Peer from, string message) {
            int newId = Messages.Count;
            Messages.Add(new Message { From = from, Text = message });
            Future f = NewMessageFuture;
            NewMessageFuture = new Future();
            f.Complete(newId);
        }

        static IEnumerator<object> DispatchMessagesSinceId (Peer peer, StreamWriter output, int lastId) {
            int newestMessageId = Messages.Count - 1;

            if (newestMessageId <= lastId) {
                yield return lastId;
                yield break;
            }

            if ((newestMessageId - lastId) > 10) {
                Console.WriteLine("Limited message dispatch for {0} to {1} messages (would've been {2}.)", peer, 10, newestMessageId - lastId);
                lastId = newestMessageId - 10;
            }

            while (true) {
                lastId += 1;

                Message message = Messages[lastId];

                string text = null;
                if (message.From == peer) {
                } else if (message.From != null) {
                    text = String.Format("<{0}> {1}", message.From, message.Text);
                } else {
                    text = String.Format("*** {0}", message.Text);
                }

                if (text != null) {
                    Future f = output.TelnetWriteLine(text);
                    yield return f;
                    if (f.CheckForFailure(typeof(DisconnectedException), typeof(IOException), typeof(SocketException))) {
                        PeerDisconnected(peer);
                        yield break;
                    }
                }

                if (lastId == newestMessageId) {
                    yield return lastId;
                    yield break;
                }
            }
        }

        static void PeerConnected (Peer peer) {
            if (peer.Connected)
                return;
            peer.Connected = true;
            Console.WriteLine("User {0} has connected", peer);
            DispatchNewMessage(null, String.Format("{0} has joined the chat", peer));
        }

        static void PeerDisconnected (Peer peer) {
            if (!peer.Connected)
                return;
            peer.Connected = false;
            Console.WriteLine("User {0} has disconnected", peer);
            DispatchNewMessage(null, String.Format("{0} has left the chat", peer));
        }

        static IEnumerator<object> PeerSendTask (StreamWriter output, Peer peer) {
            int lastId = -1;
            Future f;
            Future newMessages = NewMessageFuture;
            while (true) {
                yield return newMessages;
                newMessages = NewMessageFuture;
                f = Scheduler.Start(DispatchMessagesSinceId(peer, output, lastId), TaskExecutionPolicy.RunUntilComplete);
                yield return f;
                try {
                    lastId = (int)f.Result;
                } catch (NullReferenceException) {
                    // Terminated
                } catch (Exception ex) {
                    Console.WriteLine("Error in PeerSendTask({0}): {1}", peer, ex);
                    yield break;
                }
            }
        }

        static IEnumerator<object> PeerTask (TcpClient client, Peer peer) {
            client.NoDelay = true;
            var stream = client.GetStream();
            var input = new AsyncStreamReader(stream);
            var output = new StreamWriter(stream);

            yield return output.TelnetWriteLine("Welcome! Please enter your name.");
            Future f = input.TelnetReadLine();
            yield return f;
            if (f.CheckForFailure(typeof(DisconnectedException), typeof(IOException), typeof(SocketException))) {
                PeerDisconnected(peer);
                yield break;
            }
            peer.Name = f.Result as string;

            PeerConnected(peer);

            output.Write(VT100.EraseScreen);
            output.Flush();

            Scheduler.Start(PeerSendTask(output, peer), TaskExecutionPolicy.RunAsBackgroundTask);

            while (true) {
                f = input.TelnetReadLine();
                yield return f;
                string nextLineText = null;
                if (f.CheckForFailure(typeof(DisconnectedException), typeof(IOException), typeof(SocketException))) {
                    PeerDisconnected(peer);
                    yield break;
                }
                nextLineText = (string)f.Result;

                if (nextLineText.Length > 0) {
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

                    Peer peer = new Peer { Id = nextId++ };
                    TcpClient client = connection.Result as TcpClient;
                    client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                    client.NoDelay = true;
                    var peerTask = PeerTask(client, peer);
                    Scheduler.Start(peerTask, TaskExecutionPolicy.RunAsBackgroundTask);
                }
            } finally {
                server.Stop();
            }
        }
        
        static void Main (string[] args) {
            TcpListener server = new TcpListener(System.Net.IPAddress.Any, 1234);
            Scheduler.Start(AcceptConnectionsTask(server), TaskExecutionPolicy.RunAsBackgroundTask);

            Console.WriteLine("Ready for connections.");

            try {
                while (true) {
                    Scheduler.WaitForWorkItems();
                    Scheduler.Step();
                }
            } catch (Exception ex) {
                Console.WriteLine("Unhandled exception: {0}", ex);
                Console.ReadLine();
            }
        }
    }
}
