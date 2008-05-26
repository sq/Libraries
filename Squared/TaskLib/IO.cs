using System;
using System.Collections.Generic;
using System.Threading;
using System.Net.Sockets;
using System.IO;
using System.Text;
using System.Diagnostics;

namespace Squared.Task {
    public class BufferFullException : InvalidOperationException {
        public BufferFullException ()
            : base("The operation could not be begun due to insufficient buffer space") {
        }
    }

    public class OperationPendingException : InvalidOperationException {
        public OperationPendingException ()
            : base("A previous operation on this object is still pending") {
        }
    }    

    public static class IOExtensionMethods {
        public static Future AsyncRead (this Stream stream, byte[] buffer, int offset, int count) {
            var f = new Future();
            try {
                stream.BeginRead(buffer, offset, count, (ar) => {
                    if (stream == null) {
                        f.Fail(new Exception("Stream disposed before read could be completed"));
                        return;
                    }
                    try {
                        int bytesRead;
                        lock (stream)
                            bytesRead = stream.EndRead(ar);
                        f.Complete(bytesRead);
                    } catch (Exception ex) {
                        f.Fail(ex);
                    }
                }, stream);
            } catch (Exception ex) {
                f.Fail(ex);
            }
            return f;
        }

        public static Future AsyncWrite (this Stream stream, byte[] buffer, int offset, int count) {
            var f = new Future();
            try {
                stream.BeginWrite(buffer, offset, count, (ar) => {
                    if (stream == null) {
                        f.Fail(new Exception("Stream disposed before write could be completed"));
                        return;
                    }
                    try {
                        lock (stream)
                            stream.EndWrite(ar);
                        f.Complete();
                    } catch (Exception ex) {
                        f.Fail(ex);
                    }
                }, stream);
            } catch (Exception ex) {
                f.Fail(ex);
            }
            return f;
        }
    }

    public class PendingOperationManager {
        Future _PendingOperation;

        public Future PendingOperation {
            get {
                return _PendingOperation;
            }
        }

        protected void SetPendingOperation (Future f) {
            if (Interlocked.CompareExchange<Future>(ref _PendingOperation, f, null) != null)
                throw new OperationPendingException();
        }

        protected void ClearPendingOperation (Future f) {
            if (Interlocked.CompareExchange<Future>(ref _PendingOperation, null, f) != f)
                throw new InvalidDataException();
        }
    }

    public class AwesomeResult : IAsyncResult {
        internal volatile bool Completed;
        public Exception Error;
        public object Result;
        public Socket Socket;
        public string Type;

        public override string ToString () {
            return String.Format("<AwesomeRequest ({0}): r={1}, e={2}>", Type, Result, Error);
        }

        object IAsyncResult.AsyncState {
            get { throw new NotImplementedException(); }
        }

        WaitHandle IAsyncResult.AsyncWaitHandle {
            get { throw new NotImplementedException(); }
        }

        bool IAsyncResult.CompletedSynchronously {
            get { return false; }
        }

        bool IAsyncResult.IsCompleted {
            get { return Completed; }
        }
    }

    public class AwesomeStream : Stream {
        Socket _Socket;
        public bool UseThreadedOperations = false;

        public AwesomeStream (Socket socket, bool ownsSocket) {
            _Socket = socket;
        }

        public override bool CanRead {
            get { return true; }
        }

        public override bool CanSeek {
            get { return false; }
        }

        public override bool CanWrite {
            get { return true; }
        }

        public override void Flush () {
        }

        public override long Length {
            get { throw new NotImplementedException(); }
        }

        public override long Position {
            get {
                throw new NotImplementedException();
            }
            set {
                throw new NotImplementedException();
            }
        }

        public override int Read (byte[] buffer, int offset, int count) {
            throw new NotImplementedException();
        }

        public override long Seek (long offset, SeekOrigin origin) {
            throw new NotImplementedException();
        }

        public override void SetLength (long value) {
            throw new NotImplementedException();
        }

        public override void Write (byte[] buffer, int offset, int count) {
            throw new NotImplementedException();
        }

        public override IAsyncResult BeginRead (byte[] buffer, int offset, int count, AsyncCallback callback, object state) {
            if (!_Socket.Connected)
                throw new IOException("Not connected");

            if (UseThreadedOperations) {
                var ar = new AwesomeResult();
                ar.Socket = _Socket;
                ar.Type = "Read";
                ThreadPool.QueueUserWorkItem((_) => {
                    int result = 0;
                    Exception error = null;
                    try {
                        SocketError errorCode;
                        result = _Socket.Receive(buffer, offset, count, SocketFlags.None, out errorCode);
                        if (errorCode != SocketError.Success)
                            Console.WriteLine("Error code from Socket.Recieve: {0}", errorCode);
                    } catch (Exception ex) {
                        error = ex;
                        Console.WriteLine("Error in socket worker: {0}", ex);
                    }
                    ar.Error = error;
                    ar.Result = result;
                    ar.Completed = true;
                    try {
                        callback(ar);
                    } catch (Exception ex) {
                        Console.WriteLine("Error in socket callback: {0}", ex);
                    }
                });
                return ar;
            } else {
                SocketError errorCode;
                IAsyncResult ar = _Socket.BeginReceive(buffer, offset, count, SocketFlags.None, out errorCode, callback, state);
                if (errorCode != SocketError.Success)
                    Console.WriteLine("Error code from Socket.BeginReceive: {0}", errorCode);
                return ar;
            }
        }

        private bool IsSendBufferFull () {
            try {
                int result = _Socket.Send(new byte[0]);
            } catch (SocketException se) {
                if (se.SocketErrorCode == SocketError.WouldBlock)
                    return true;
                else
                    throw;
            }
            return false;
        }

        public override IAsyncResult BeginWrite (byte[] buffer, int offset, int count, AsyncCallback callback, object state) {
            if (!_Socket.Connected)
                throw new IOException("Not connected");

            if (UseThreadedOperations) {
                var ar = new AwesomeResult();
                ar.Socket = _Socket;
                ar.Type = "Write";
                ThreadPool.QueueUserWorkItem((_) => {
                    int result = 0;
                    Exception error = null;
                    try {
                        SocketError errorCode;
                        result = _Socket.Send(buffer, offset, count, SocketFlags.None, out errorCode);
                        if (errorCode != SocketError.Success)
                            Console.WriteLine("Error code from Socket.Send: {0}", errorCode);
                    } catch (Exception ex) {
                        error = ex;
                        Console.WriteLine("Error in socket worker: {0}", ex);
                    }
                    if (result != count) {
                        error = new Exception("Could not send data");
                    }
                    ar.Error = error;
                    ar.Result = result;
                    ar.Completed = true;
                    try {
                        callback(ar);
                    } catch (Exception ex) {
                        Console.WriteLine("Error in socket callback: {0}", ex);
                    }
                });
                return ar;
            } else {
                if (IsSendBufferFull())
                    throw new BufferFullException();
                SocketError errorCode;
                IAsyncResult ar = _Socket.BeginSend(buffer, offset, count, SocketFlags.None, out errorCode, callback, state);
                if (errorCode != SocketError.Success)
                    Console.WriteLine("Error code from Socket.BeginSend: {0}", errorCode);
                return ar;
            }
        }

        public override int EndRead (IAsyncResult asyncResult) {
            if (UseThreadedOperations) {
                AwesomeResult ar = (AwesomeResult)asyncResult;
                if (ar.Error != null)
                    throw ar.Error;
                else
                    return (int)ar.Result;
            } else {
                SocketError errorCode;
                int result = _Socket.EndReceive(asyncResult, out errorCode);
                if (errorCode != SocketError.Success)
                    Console.WriteLine("Error code from Socket.EndReceive: {0}", errorCode);
                return result;
            }
        }

        public override void EndWrite (IAsyncResult asyncResult) {
            if (UseThreadedOperations) {
                AwesomeResult ar = (AwesomeResult)asyncResult;
                if (ar.Error != null)
                    throw ar.Error;
            } else {
                SocketError errorCode;
                _Socket.EndSend(asyncResult, out errorCode);
                if (errorCode != SocketError.Success)
                    Console.WriteLine("Error code from Socket.EndSend: {0}", errorCode);
            }
        }
    }

    public class AsyncStreamReader : PendingOperationManager, IDisposable {
        public static List<AsyncStreamReader> Readers = new List<AsyncStreamReader>();

        public static Encoding DefaultEncoding = Encoding.UTF8;
        public static int DefaultBufferSize = 1024;

        Stream _BaseStream;
        Encoding _Encoding;
        Decoder _Decoder;
        int _BufferSize;

        byte[] _InputBuffer;
        char[] _DecodedBuffer;
        int _DecodedCharacterCount = 0;
        int _DecodedCharacterOffset = 0;

        public Stream BaseStream {
            get {
                return _BaseStream;
            }
        }

        public AsyncStreamReader (Stream stream)
            : this(stream, DefaultEncoding) {
        }

        public AsyncStreamReader (Stream stream, Encoding encoding) {
            if (!stream.CanRead)
                throw new InvalidOperationException("Stream is not readable");

            _BaseStream = stream;
            _Encoding = encoding;
            _Decoder = _Encoding.GetDecoder();
            _BufferSize = DefaultBufferSize;
            AllocateBuffer();

            Readers.Add(this);
        }

        public void Dispose () {
            if (_BaseStream != null) {
                _BaseStream.Dispose();
                _BaseStream = null;
            }
            _Encoding = null;
            _Decoder = null;
            _InputBuffer = null;
            _DecodedBuffer = null;
        }

        private void AllocateBuffer () {
            _InputBuffer = new byte[_BufferSize];
            _DecodedBuffer = new char[_BufferSize];
        }

        private Future ReadMoreData () {
            Future f = new Future();

            AsyncCallback callback = (ar) => {
                int bytesRead = 0;
                Exception _failure = null;
                try {
                    bytesRead = _BaseStream.EndRead(ar);
                } catch (ObjectDisposedException) {
                    bytesRead = 0;
                } catch (Exception ex) {
                    _failure = ex;
                }
                f.SetResult(bytesRead, _failure);
            };
            try {
                IAsyncResult ar = _BaseStream.BeginRead(_InputBuffer, 0, _BufferSize, callback, _BaseStream);
            } catch (Exception ex) {
                f.Fail(ex);
            }

            return f;
        }

        private Future DecodeMoreData () {
            Future f = new Future();
            Future readData = ReadMoreData();
            readData.RegisterOnComplete((result, error) => {
                if (error != null) {
                    f.Fail(error);
                    return;
                }

                int bytesRead = (int)result;
                try {
                    _DecodedCharacterOffset = 0;
                    _DecodedCharacterCount = 0;
                    _DecodedCharacterCount = _Decoder.GetChars(_InputBuffer, 0, bytesRead, _DecodedBuffer, 0);
                    f.Complete(_DecodedCharacterCount);
                } catch (Exception ex) {
                    f.Fail(ex);
                }
            });
            return f;
        }

        private bool GetCurrentCharacter (out char value) {
            if (_DecodedCharacterOffset < _DecodedCharacterCount) {
                value = _DecodedBuffer[_DecodedCharacterOffset];
                return true;
            } else {
                value = default(char);
                return false;
            }
        }

        private bool ReadNextCharacter () {
            _DecodedCharacterOffset += 1;
            return (_DecodedCharacterOffset < _DecodedCharacterCount);
        }

        private string ReturnBufferValue (StringBuilder buffer) {
            if (buffer.Length > 0)
                return buffer.ToString();
            else
                return null;
        }

        public Future Read () {
            return Read(true);
        }

        public Future Peek () {
            return Read(false);
        }

        public Future Read (bool advance) {
            Future f = new Future();

            SetPendingOperation(f);
            
            char result;
            if (!GetCurrentCharacter(out result)) {
                Future decodeMoreChars = DecodeMoreData();
                decodeMoreChars.RegisterOnComplete((_, error) => {
                    if (error != null) {
                        ClearPendingOperation(f);
                        f.Fail(error);
                    } else {
                        char ch;
                        if (GetCurrentCharacter(out ch)) {
                            if (advance)
                                ReadNextCharacter();
                            ClearPendingOperation(f);
                            f.Complete(ch);
                        } else {
                            ClearPendingOperation(f);
                            f.Complete(null);
                        }
                    }
                });
            } else {
                if (advance)
                    ReadNextCharacter();
                ClearPendingOperation(f);
                f.Complete(result);
            }
            return f;
        }

        public Future Read (char[] buffer, int offset, int count) {
            Future f = new Future();
            int[] wp = new int[1];
            OnComplete[] oc = new OnComplete[1];

            SetPendingOperation(f);

            Action processDecodedChars = () => {
                char value;
                while (GetCurrentCharacter(out value)) {
                    if (wp[0] >= (offset + count)) {
                        ClearPendingOperation(f);
                        f.Complete(count);
                        return;
                    }

                    ReadNextCharacter();

                    buffer[wp[0]] = value;
                    wp[0] += 1;
                }

                Future decodeMoreChars = DecodeMoreData();
                decodeMoreChars.RegisterOnComplete(oc[0]);
            };

            OnComplete onDecodeComplete = (result, error) => {
                if (error != null) {
                    ClearPendingOperation(f);
                    f.Fail(error);
                } else {
                    int numChars = (int)result;

                    if (numChars > 0)
                        processDecodedChars();
                    else {
                        ClearPendingOperation(f);
                        f.Complete(wp[0] - offset);
                    }
                }
            };

            oc[0] = onDecodeComplete;
            wp[0] = offset;
            processDecodedChars();
            return f;
        }

        public Future ReadLine () {
            Future f = new Future();
            StringBuilder buffer = new StringBuilder();
            OnComplete[] oc = new OnComplete[1];

            SetPendingOperation(f);

            Action processDecodedChars = () => {
                char value;
                while (GetCurrentCharacter(out value)) {
                    ReadNextCharacter();

                    bool done = false;

                    switch (value) {
                        case '\n':
                            if ((buffer.Length > 0) && (buffer[buffer.Length - 1] == '\r'))
                                buffer.Remove(buffer.Length - 1, 1);
                            done = true;
                            break;
                        default:
                            buffer.Append(value);
                            break;
                    }
                    if (done) {
                        ClearPendingOperation(f);
                        f.Complete(ReturnBufferValue(buffer));
                        return;
                    }
                }

                Future decodeMoreChars = DecodeMoreData();
                decodeMoreChars.RegisterOnComplete(oc[0]);
            };

            OnComplete onDecodeComplete = (result, error) => {
                if (error != null) {
                    ClearPendingOperation(f);
                    f.Fail(error);
                } else {
                    int numChars = (int)result;

                    if (numChars > 0) {
                        try {
                            processDecodedChars();
                        } catch (Exception ex) {
                            f.Fail(ex);
                        }
                    } else {
                        ClearPendingOperation(f);
                        f.Complete(ReturnBufferValue(buffer));
                    }
                }
            };

            oc[0] = onDecodeComplete;
            processDecodedChars();
            return f;
        }

        public Future ReadToEnd () {
            Future f = new Future();
            StringBuilder buffer = new StringBuilder();
            OnComplete[] oc = new OnComplete[1];

            SetPendingOperation(f);

            Action processDecodedChars = () => {
                char value;
                while (GetCurrentCharacter(out value)) {
                    ReadNextCharacter();
                    buffer.Append(value);
                }

                Future decodeMoreChars = DecodeMoreData();
                decodeMoreChars.RegisterOnComplete(oc[0]);
            };

            OnComplete onDecodeComplete = (result, error) => {
                if (error != null) {
                    ClearPendingOperation(f);
                    f.Fail(error);
                } else {
                    int numChars = (int)result;

                    if (numChars > 0) {
                        processDecodedChars();
                    } else {
                        ClearPendingOperation(f);
                        f.Complete(ReturnBufferValue(buffer));
                    }
                }
            };

            oc[0] = onDecodeComplete;
            processDecodedChars();
            return f;
        }
    }

    public class AsyncStreamWriter : PendingOperationManager, IDisposable {
        public static Encoding DefaultEncoding = Encoding.UTF8;
        public static char[] DefaultNewLine = new char[] { '\r', '\n' };

        char[] _NewLine;
        byte[] _NewLineBytes;
        Stream _BaseStream;
        Encoding _Encoding;
        Encoder _Encoder;

        public Stream BaseStream {
            get {
                return _BaseStream;
            }
        }

        public AsyncStreamWriter (Stream stream)
            : this(stream, DefaultEncoding) {
        }

        public AsyncStreamWriter (Stream stream, Encoding encoding) {
            if (!stream.CanWrite)
                throw new InvalidOperationException("Stream is not writable");

            _BaseStream = stream;
            _Encoding = encoding;
            _Encoder = encoding.GetEncoder();
            _NewLine = DefaultNewLine;
            _NewLineBytes = _Encoding.GetBytes(_NewLine);
        }

        public void Dispose () {
            if (_BaseStream != null) {
                _BaseStream.Dispose();
                _BaseStream = null;
            }
            _Encoding = null;
            _Encoder = null;
        }

        public char[] NewLine {
            get {
                return _NewLine;
            }
            set {
                _NewLine = value;
                _NewLineBytes = _Encoding.GetBytes(_NewLine);
            }
        }

        public Future Write (byte[] bytes) {
            Future f = new Future();

            SetPendingOperation(f);

            AsyncCallback callback = (ar) => {
                Exception _failure = null;
                try {
                    _BaseStream.EndWrite(ar);
                } catch (Exception ex) {
                    _failure = ex;
                }
                ClearPendingOperation(f);
                f.SetResult(null, _failure);
            };

            try {
                IAsyncResult ar = _BaseStream.BeginWrite(bytes, 0, bytes.Length, callback, _BaseStream);
            } catch (Exception ex) {
                ClearPendingOperation(f);
                f.Fail(ex);
            }

            return f;
        }

        public Future Write (string text) {
            byte[] buf = _Encoding.GetBytes(text);
            return Write(buf);
        }

        public Future WriteLine (string line) {
            int numBytes = _Encoding.GetByteCount(line);
            byte[] buf = new byte[numBytes + _NewLineBytes.Length];
            _Encoding.GetBytes(line, 0, line.Length, buf, 0);
            Array.Copy(_NewLineBytes, 0, buf, numBytes, _NewLineBytes.Length);

            return Write(buf);
        }

        public void Flush () {
            lock (_BaseStream)
                _BaseStream.Flush();
        }
    }
}
