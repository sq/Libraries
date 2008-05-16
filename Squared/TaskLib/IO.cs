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
    }
}
