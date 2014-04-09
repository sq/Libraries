using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Squared.Task.IO;

namespace Squared.Task.Http {
    public class AsyncDataAdapterShim : IAsyncDataSource, IAsyncDataWriter, IDisposable {
        public event EventHandler Disposed;

        public readonly IAsyncDataSource Source;
        public readonly IAsyncDataWriter Writer;

        public bool IsDisposed {
            get;
            private set;
        }

        public AsyncDataAdapterShim (IAsyncDataSource source, IAsyncDataWriter writer) {
            Source = source;
            Writer = writer;
        }

        public Future<int> Read (byte[] buffer, int offset, int count) {
            return Source.Read(buffer, offset, count);
        }

        public bool EndOfStream {
            get { return Source.EndOfStream; }
        }

        public SignalFuture Write (byte[] buffer, int offset, int count) {
            return Writer.Write(buffer, offset, count);
        }

        void IDisposable.Dispose () {
            if (IsDisposed)
                return;

            IsDisposed = true;
            if (Disposed != null)
                Disposed(this, EventArgs.Empty);
        }
    }
}
