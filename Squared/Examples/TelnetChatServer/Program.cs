using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;
using Squared.Task;
using System.IO;

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

        public static char[] EraseLine { get {
            return new char[] { ESC, '[', '2', 'K' };
        }}

        public static char[] EraseScreen { get {
            return new char[] { ESC, '[', '2', 'J' };
        }}

        public static char[] SaveCursor { get {
            return new char[] { ESC, '7' };
        }}

        public static char[] RestoreCursor { get {
            return new char[] { ESC, '8' };
        }}

        public static char[] SetScrollingRegion (int startRow, int stopRow) {
            var buffer = new StringBuilder();
            buffer.Append(ESC);
            buffer.AppendFormat("[{0};{1}r", startRow, stopRow);
            return buffer.ToString().ToCharArray();
        }

        public static char[] SetCursorPosition (int row, int column) {
            var buffer = new StringBuilder();
            buffer.Append(ESC);
            buffer.AppendFormat("[{0};{1}f", row, column);
            return buffer.ToString().ToCharArray();
        }
    }

    internal static class TelnetExtensionMethods {
        internal static Future TelnetWriteLine (this StreamWriter writer, string value) {
            Future f = writer.AsyncWriteLine(value);
            f.RegisterOnComplete((result, error) => {
                writer.Flush();
            });
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

        static int DispatchMessagesSinceId (Peer peer, StreamWriter output, int lastId) {
            int newestMessageId = Messages.Count - 1;

            if (newestMessageId <= lastId)
                return lastId;

            while (true) {
                lastId += 1;

                try {
                    Message message = Messages[lastId];
                    if (message.From != null)
                        output.TelnetWriteLine(String.Format("<{0}> {1}", message.From, message.Text));
                    else
                        output.TelnetWriteLine(String.Format("*** {0}", message.Text));

                    if (lastId == newestMessageId)
                        return lastId;
                } catch (Exception) {
                    throw new DisconnectedException();
                }
            }
        }

        static void PeerConnected (Peer peer) {
            Console.WriteLine("User {0} has connected", peer);
            DispatchNewMessage(null, String.Format("{0} has joined the chat", peer));
        }

        static void PeerDisconnected (Peer peer) {
            Console.WriteLine("User {0} has disconnected", peer);
            DispatchNewMessage(null, String.Format("{0} has left the chat", peer));
        }

        static IEnumerator<object> PeerTask (TcpClient client, Peer peer) {
            client.NoDelay = true;
            var stream = client.GetStream();
            var input = new AsyncStreamReader(stream);
            var output = new StreamWriter(stream);
            int lastMessageId = -1;

            yield return output.TelnetWriteLine("Welcome! Please enter your name.");
            Future f = input.TelnetReadLine();
            yield return f;
            if (f.CheckForFailure(typeof(DisconnectedException))) {
                PeerDisconnected(peer);
                yield break;
            }
            peer.Name = f.Result as string;

            PeerConnected(peer);

            output.Write(VT100.EraseScreen);
            output.Flush();

            Future nextLine = input.TelnetReadLine();
            while (true) {
                try {
                    lastMessageId = DispatchMessagesSinceId(peer, output, lastMessageId);
                } catch (DisconnectedException) {
                    PeerDisconnected(peer);
                    yield break;
                }

                Future newMessage = NewMessageFuture;
                f = Scheduler.Start(new WaitForFirst(nextLine, newMessage));
                yield return f;

                if (f.Result == nextLine) {
                    string nextLineText = null;
                    if (nextLine.CheckForFailure(typeof(DisconnectedException))) {
                        PeerDisconnected(peer);
                        yield break;
                    }
                    nextLineText = (string)nextLine.Result;

                    output.Write(VT100.CursorUp);
                    output.Write(VT100.EraseToStartOfLine);
                    output.Flush();

                    if (nextLineText.Length > 0) {
                        Console.WriteLine("New message from {0}: {1}", peer, nextLineText);
                        DispatchNewMessage(peer, nextLine.Result as string);
                    }

                    nextLine = input.TelnetReadLine();
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
                    var peerTask = PeerTask(connection.Result as TcpClient, peer);
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

            while (true) {
                Scheduler.WaitForWorkItems();
                Scheduler.Step();
            }
        }
    }
}
