using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;
using Squared.Task;
using System.IO;
using System.Diagnostics;
using System.Threading;

namespace TelnetChatBot {
    public class DisconnectedException : Exception {
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

    static class Program {
        static TaskScheduler Scheduler = new TaskScheduler(true);
        static bool Disconnected = false;
        static int LastRecieveLine = -1;

        static IEnumerator<object> SendTask (TcpClient client) {
            var stream = client.GetStream();
            var output = new StreamWriter(stream);
            string nextMessageText = String.Format("ChatBot{0}", Process.GetCurrentProcess().Id);
            int i = 0;
            yield return new Sleep(new Random(Process.GetCurrentProcess().Id).NextDouble());
            while (true) {
                Future f = output.TelnetWriteLine(nextMessageText);
                yield return f;
                if (f.CheckForFailure(typeof(DisconnectedException))) {
                    Disconnected = true;
                    yield break;
                }
                i += 1;
                nextMessageText = String.Format("Message {0}", i);
                yield return new Sleep(1.0);
            }
        }
        
        static IEnumerator<object> ReceiveTask (TcpClient client) {
            var stream = client.GetStream();
            var input = new AsyncStreamReader(stream);
            while (true) {
                LastRecieveLine = 0;
                Future f = input.TelnetReadLine();
                LastRecieveLine = 1;
                yield return f;
                LastRecieveLine = 2;
                if (f.CheckForFailure(typeof(DisconnectedException))) {
                    LastRecieveLine = -2;
                    Disconnected = true;
                    yield break;
                }
                LastRecieveLine = 3;
                string message = f.Result as string;
                LastRecieveLine = 4;
                Console.WriteLine(message);
                LastRecieveLine = 5;
            }
        }

        static void Main (string[] args) {
            Console.WriteLine("Connecting to server...");
            Future f = Network.ConnectTo("localhost", 1234);
            f.GetCompletionEvent().WaitOne();
            Console.WriteLine("Connected.");
            TcpClient client = f.Result as TcpClient;
            client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            Scheduler.Start(ReceiveTask(client), TaskExecutionPolicy.RunAsBackgroundTask);
            Scheduler.Start(SendTask(client), TaskExecutionPolicy.RunAsBackgroundTask);

            try {
                while (!Disconnected) {
                    Scheduler.WaitForWorkItems();
                    Scheduler.Step();
                }
            } catch (Exception ex) {
                Console.WriteLine("Unhandled exception: {0}", ex);
            }

            Console.WriteLine("Disconnected.");
        }
    }
}
