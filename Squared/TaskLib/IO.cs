using System;
using System.Collections.Generic;
using System.Threading;
using System.Net.Sockets;
using System.IO;

namespace Squared.Task {
    public static class IOExtensionMethods {
        public static Future AsyncRead (this Stream stream, byte[] buffer, int offset, int count) {
            var f = new Future();
            stream.BeginRead(buffer, offset, count, (ar) => {
                int bytesRead = stream.EndRead(ar);
                f.Complete(bytesRead);
            }, null);
            return f;
        }

        public static Future AsyncWrite (this Stream stream, byte[] buffer, int offset, int count) {
            var f = new Future();
            stream.BeginWrite(buffer, offset, count, (ar) => {
                stream.EndWrite(ar);
                f.Complete();
            }, null);
            return f;
        }

        public static Future AsyncWriteLine (this TextWriter writer, string value) {
            var f = new Future();
            WaitCallback fn = (state) => {
                try {
                    writer.WriteLine(value);
                    f.Complete();
                } catch (Exception e) {
                    f.Fail(e);
                }
            };
            ThreadPool.QueueUserWorkItem(fn);
            return f;
        }

        public static Future AsyncReadLine (this TextReader reader) {
            var f = new Future();
            WaitCallback fn = (state) => {
                try {
                    string result = reader.ReadLine();
                    f.Complete(result);
                } catch (Exception e) {
                    f.Fail(e);
                }
            };
            ThreadPool.QueueUserWorkItem(fn);
            return f;
        }
    }
}
