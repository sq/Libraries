using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;
using Squared.Task;
using Squared.Task.IO;
using System.IO;
using System.Diagnostics;
using System.Threading;

namespace TelnetChatBot {
    public class DisconnectedException : Exception {
    }

    static class Program {
        static AsyncTextReader Reader;
        static AsyncTextWriter Writer;
        static TaskScheduler Scheduler = new TaskScheduler();
        static bool Disconnected = false;
        static float SendRate;

        static IEnumerator<object> SendTask (SocketDataAdapter adapter) {
            var output = new AsyncTextWriter(adapter, Encoding.ASCII);
            output.AutoFlush = true;
            Writer = output;
            string nextMessageText = String.Format("ChatBot{0:00000}", Process.GetCurrentProcess().Id);
            Console.Title = nextMessageText;
            int i = 0;
            yield return new Sleep(new Random(Process.GetCurrentProcess().Id).NextDouble());
            while (true) {
                var f = output.WriteLine(nextMessageText);
                yield return f;

                if (f.Failed) {
                    Disconnected = true;
                    throw new DisconnectedException();
                }

                i += 1;

                if ((i % 1000) == 0)
                    Console.WriteLine("Sent: {0}", i);

                nextMessageText = String.Format("Message {0}", i);
                yield return new Sleep(SendRate);
            }
        }

        static IEnumerator<object> ReceiveTask (SocketDataAdapter adapter) {
            var input = new AsyncTextReader(adapter, Encoding.ASCII);
            int i = 0;
            string message = null;
            Reader = input;
            while (true) {                
                var f = input.ReadLine();
                yield return f;

                if (!f.GetResult(out message))
                    throw new DisconnectedException();

                if (message == null)
                    throw new DisconnectedException();
                else
                    i += 1;

                if ((i % 1000) == 0)
                    Console.WriteLine("Recieved: {0}", i);
            }
        }

        static void Main (string[] args) {
            if ((args.Length < 1) || !float.TryParse(args[0], out SendRate))
                SendRate = 1.0f;

            Thread.CurrentThread.Name = "MainThread";
            ThreadPool.SetMinThreads(1, 1);

            try {
                Console.WriteLine("Connecting to server...");
                var f = Network.ConnectTo("localhost", 1234);
                f.GetCompletionEvent().Wait();
                Console.WriteLine("Connected.");
                TcpClient client = f.Result as TcpClient;
                client.Client.Blocking = false;
                client.Client.NoDelay = true;
                SocketDataAdapter adapter = new SocketDataAdapter(client.Client);
                adapter.ThrowOnDisconnect = false;
                adapter.ThrowOnFullSendBuffer = false;

                Scheduler.Start(ReceiveTask(adapter), TaskExecutionPolicy.RunAsBackgroundTask);
                Scheduler.Start(SendTask(adapter), TaskExecutionPolicy.RunAsBackgroundTask);

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
