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
        internal static Future TelnetReadLine (this AsyncStreamReader reader) {
            Future innerFuture = reader.ReadLine();
            Future f = new Future();
            innerFuture.RegisterOnComplete((result, error) => {
                /*
                if ((result == null) && (error == null))
                    error = new DisconnectedException();
                 */
                f.SetResult(result, error);
            });
            return f;
        }
    }

    static class Program {
        static TaskScheduler Scheduler = new TaskScheduler(true);
        static bool Disconnected = false;

        static IEnumerator<object> SendTask (TcpClient client) {
            var stream = client.GetStream();
            var output = new AsyncStreamWriter(stream);
            string nextMessageText = String.Format("ChatBot{0}", Process.GetCurrentProcess().Id);
            int i = 0;
            yield return new Sleep(new Random(Process.GetCurrentProcess().Id).NextDouble());
            while (true) {
                Future f = output.WriteLine(nextMessageText);
                yield return f;
                if (f.CheckForFailure(typeof(DisconnectedException), typeof(IOException), typeof(SocketException))) {
                    object r;
                    Exception ex;
                    f.GetResult(out r, out ex);
                    File.AppendAllText(".\\disconnect.log", ex.ToString() + "\r\n");
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
                Future f = input.TelnetReadLine();
                yield return f;
                if (f.CheckForFailure(typeof(DisconnectedException), typeof(IOException), typeof(SocketException))) {
                    object r;
                    Exception ex;
                    f.GetResult(out r, out ex);
                    File.AppendAllText(".\\disconnect.log", ex.ToString() + "\r\n");
                    Disconnected = true;
                    yield break;
                }
                string message = f.Result as string;
                Console.WriteLine(message);
            }
        }

        static void Main (string[] args) {
            try {
                
                Console.WriteLine("Connecting to server...");
                Future f = Network.ConnectTo("localhost", 1234);
                f.GetCompletionEvent().WaitOne();
                Console.WriteLine("Connected.");
                TcpClient client = f.Result as TcpClient;
                client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                Scheduler.Start(ReceiveTask(client), TaskExecutionPolicy.RunAsBackgroundTask);
                Scheduler.Start(SendTask(client), TaskExecutionPolicy.RunAsBackgroundTask);

                while (!Disconnected) {
                    bool r = Scheduler.WaitForWorkItems(1.1);
                    if (!r) {
                        System.Windows.Forms.MessageBox.Show("WaitForWorkItems timed out!", Process.GetCurrentProcess().Id.ToString());
                    }
                    Scheduler.Step();
                }
                Console.WriteLine("Disconnected.");

            } catch (Exception ex) {
                File.AppendAllText(".\\error.log", ex.ToString() + "\r\n");
            }
        }
    }
}
