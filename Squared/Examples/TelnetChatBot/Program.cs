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

    static class Program {
        static AsyncStreamReader Reader;
        static AsyncStreamWriter Writer;
        static TaskScheduler Scheduler = new TaskScheduler(true);
        static bool Disconnected = false;

        static IEnumerator<object> SendTask (TcpClient client) {
            var output = new AsyncStreamWriter(new AwesomeStream(client.Client, false), Encoding.ASCII);
            Writer = output;
            string nextMessageText = String.Format("ChatBot{0}", Process.GetCurrentProcess().Id);
            Console.Title = nextMessageText;
            int i = 0;
            yield return new Sleep(new Random(Process.GetCurrentProcess().Id).NextDouble());
            while (true) {
                Future f = output.WriteLine(nextMessageText);
                yield return f;
                if (f.CheckForFailure(typeof(DisconnectedException), typeof(IOException), typeof(SocketException))) {
                    Disconnected = true;
                    throw new DisconnectedException();
                }
                i += 1;
                nextMessageText = String.Format("Message {0}", i);
                yield return new Sleep(0.5);
            }
        }
        
        static IEnumerator<object> ReceiveTask (TcpClient client) {
            var input = new AsyncStreamReader(new AwesomeStream(client.Client, false), Encoding.ASCII);
            Reader = input;
            while (true) {
                Future f = input.ReadLine();
                yield return f;
                if (f.CheckForFailure(typeof(DisconnectedException), typeof(IOException), typeof(SocketException))) {
                    Disconnected = true;
                    throw new DisconnectedException();
                }
                try {
                    string message = (string)f.Result;
                    Console.WriteLine(message);
                } catch (Exception ex) {
                    Console.WriteLine(ex.ToString());
                }
            }
        }

        static void Main (string[] args) {
            try {
                
                Console.WriteLine("Connecting to server...");
                Future f = Network.ConnectTo("localhost", 1234);
                f.GetCompletionEvent().WaitOne();
                Console.WriteLine("Connected.");
                TcpClient client = f.Result as TcpClient;
                client.Client.Blocking = false;
                client.Client.NoDelay = true;
                Scheduler.Start(ReceiveTask(client), TaskExecutionPolicy.RunAsBackgroundTask);
                Scheduler.Start(SendTask(client), TaskExecutionPolicy.RunAsBackgroundTask);

                while (!Disconnected) {
                    Scheduler.Step();
                    Scheduler.WaitForWorkItems();
                }
                Console.WriteLine("Disconnected.");

            } catch (Exception ex) {
                if (ex is TaskException && ex.InnerException is DisconnectedException) {
                } else {
                    Console.WriteLine(ex.ToString());
                }
            }
        }
    }
}
