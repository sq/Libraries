using System;
using System.Collections.Generic;
using System.Threading;
using System.Net.Sockets;
using System.IO;
using System.Text;
using System.Diagnostics;
using Squared.Util;

namespace Squared.Task.IO {
    public class SocketDisconnectedException : IOException {
        public SocketDisconnectedException ()
            : base("The operation failed because the socket has disconnected.") {
        }
    }

    public class SocketBufferFullException : IOException {
        public SocketBufferFullException ()
            : base("The operation failed because the socket's buffer is full.") {
        }
    }

    public class OperationPendingException : InvalidOperationException {
        public OperationPendingException ()
            : base("A previous operation on this object is still pending.") {
        }
    }    

    public static class IOExtensionMethods {
        public static Future AsyncRead (this Stream stream, byte[] buffer, int offset, int count) {
            var f = new Future();
            try {
                stream.BeginRead(buffer, offset, count, (ar) => {
                    try {
                        int bytesRead;
                        lock (stream)
                            bytesRead = stream.EndRead(ar);
                        f.Complete(bytesRead);
                    } catch (FutureHandlerException) {
                        throw;
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
                    try {
                        lock (stream)
                            stream.EndWrite(ar);
                        f.Complete();
                    } catch (FutureHandlerException) {
                        throw;
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

    public interface IAsyncDataSource : IDisposable {
        Future Read (byte[] buffer, int offset, int count);

        bool EndOfStream { get; }
    }

    public interface IAsyncDataWriter : IDisposable {
        Future Write (byte[] buffer, int offset, int count);
    }

    public class SocketDataAdapter : IAsyncDataSource, IAsyncDataWriter {
        Socket _Socket;
        bool _OwnsSocket;
        AsyncCallback _ReadCallback, _WriteCallback;

        public SocketDataAdapter (Socket socket)
            : this(socket, true) {
        }

        public SocketDataAdapter (Socket socket, bool ownsSocket) {
            _Socket = socket;
            _OwnsSocket = ownsSocket;
            _ReadCallback = ReadCallback;
            _WriteCallback = WriteCallback;
        }

        public void Dispose () {
            if (_OwnsSocket)
                _Socket.Close();
        }

        private void ReadCallback (IAsyncResult ar) {
            Future f = (Future)ar.AsyncState;

            if (!_Socket.Connected) {
                f.Fail(new SocketDisconnectedException());
                return;
            }

            try {
                int bytesRead = _Socket.EndReceive(ar);
                if (bytesRead == 0) {
                    f.Fail(new SocketDisconnectedException());
                } else {
                    f.Complete(bytesRead);
                }
            } catch (FutureHandlerException) {
                throw;
            } catch (Exception ex) {
                f.Fail(ex);
            }
        }

        public Future Read (byte[] buffer, int offset, int count) {
            Future f = new Future();
            if (!_Socket.Connected) {
                f.Fail(new SocketDisconnectedException());
            } else {
                SocketError errorCode;
                if (_Socket.Available >= count) {
                    try {
                        int bytesRead = _Socket.Receive(buffer, offset, count, SocketFlags.None, out errorCode);
                        if (bytesRead == 0) {
                            f.Fail(new SocketDisconnectedException());
                        } else {
                            f.Complete(bytesRead);
                        }
                    } catch (Exception ex) {
                        f.Fail(ex);
                    }
                } else {
                    _Socket.BeginReceive(buffer, offset, count, SocketFlags.None, out errorCode, _ReadCallback, f);
                }
            }
            return f;
        }

        private bool IsSendBufferFull () {
            if (_Socket.Blocking)
                return false;

            return !_Socket.Poll(0, SelectMode.SelectWrite);
        }

        private void WriteCallback (IAsyncResult ar) {
            Future f = (Future)ar.AsyncState;

            if (!_Socket.Connected) {
                f.Fail(new SocketDisconnectedException());
                return;
            }
            
            try {
                int bytesSent = _Socket.EndSend(ar);
                f.Complete();
            } catch (FutureHandlerException) {
                throw;
            } catch (Exception ex) {
                f.Fail(ex);
            }
        }
        
        public Future Write (byte[] buffer, int offset, int count) {
            Future f = new Future();
            if (!_Socket.Connected) {
                f.Fail(new SocketDisconnectedException());
            } else {
                if (IsSendBufferFull())
                    throw new SocketBufferFullException();
                SocketError errorCode;
                _Socket.BeginSend(buffer, offset, count, SocketFlags.None, out errorCode, _WriteCallback, f);
            }
            return f;
        }

        public bool EndOfStream {
            get {
                return !_Socket.Connected;
            }
        }
    }

    public class StreamDataAdapter : IAsyncDataSource, IAsyncDataWriter {
        Stream _Stream;
        bool _OwnsStream;
        bool _EOF = false;
        AsyncCallback _ReadCallback, _WriteCallback;

        public StreamDataAdapter (Stream stream)
            : this(stream, true) {
        }

        public StreamDataAdapter (Stream stream, bool ownsStream) {
            _Stream = stream;
            _OwnsStream = ownsStream;
            _ReadCallback = ReadCallback;
            _WriteCallback = WriteCallback;
        }

        public void Dispose () {
            if (_OwnsStream)
                _Stream.Dispose();
        }

        private void ReadCallback (IAsyncResult ar) {
            Future f = (Future)ar.AsyncState;
            try {
                int bytesRead = _Stream.EndRead(ar);

                if (bytesRead == 0)
                    _EOF = true;

                f.Complete(bytesRead);
            } catch (FutureHandlerException) {
                throw;
            } catch (Exception ex) {
                f.Fail(ex);
            }
        }

        public Future Read (byte[] buffer, int offset, int count) {
            Future f = new Future();
            _Stream.BeginRead(buffer, offset, count, _ReadCallback, f);
            return f;
        }

        private void WriteCallback (IAsyncResult ar) {
            Future f = (Future)ar.AsyncState;
            try {
                _Stream.EndWrite(ar);
                f.Complete();
            } catch (FutureHandlerException) {
                throw;
            } catch (Exception ex) {
                f.Fail(ex);
            }
        }
        
        public Future Write (byte[] buffer, int offset, int count) {
            Future f = new Future();
            _Stream.BeginWrite(buffer, offset, count, _WriteCallback, f);
            return f;
        }

        public Future Flush () {
            var f = new Future();
            WaitCallback wc = (state) => {
                var stream = (Stream)state;
                try {
                    stream.Flush();
                    f.Complete();
                } catch (Exception ex) {
                    f.Fail(ex);
                }
            };
            ThreadPool.QueueUserWorkItem(wc, _Stream);
            return f;
        }

        public bool EndOfStream {
            get {
                try {
                    if (_Stream.CanSeek)
                        return (_Stream.Position >= _Stream.Length);
                    else
                        return _EOF;
                } catch (NotSupportedException) {
                    return _EOF;
                }
            }
        }
    }

    public class AsyncTextReader : PendingOperationManager, IDisposable {
        public static Encoding DefaultEncoding = Encoding.UTF8;
        public static int DefaultBufferSize = 2048;

        IAsyncDataSource _DataSource;
        Encoding _Encoding;
        Decoder _Decoder;
        int _BufferSize;

        bool _ExtraLine = false;

        byte[] _InputBuffer;
        char[] _DecodedBuffer;
        int _DecodedCharacterCount = 0;
        int _DecodedCharacterOffset = 0;

        public IAsyncDataSource DataSource {
            get {
                return _DataSource;
            }
        }

        public AsyncTextReader (IAsyncDataSource dataSource)
            : this(dataSource, DefaultEncoding) {
        }

        public AsyncTextReader (IAsyncDataSource dataSource, Encoding encoding) {
            _DataSource = dataSource;
            _Encoding = encoding;
            _Decoder = _Encoding.GetDecoder();
            _BufferSize = DefaultBufferSize;
            AllocateBuffer();
        }

        public void Dispose () {
            if (_DataSource != null) {
                _DataSource.Dispose();
                _DataSource = null;
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
            return _DataSource.Read(_InputBuffer, 0, _BufferSize);
        }

        private Future DecodeMoreData () {
            Future f = new Future();
            Future readData = ReadMoreData();
            readData.RegisterOnComplete((_, result, error) => {
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
                } catch (FutureHandlerException) {
                    throw;
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

        public bool EndOfStream {
            get {
                return (_DecodedCharacterOffset >= _DecodedCharacterCount) && (_DataSource.EndOfStream);
            }
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
                decodeMoreChars.RegisterOnComplete((_a, _b, error) => {
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

            if (EndOfStream) {
                f.Complete(0);
                return f;
            }

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

            OnComplete onDecodeComplete = (_f, result, error) => {
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
            CharacterBuffer buffer = new CharacterBuffer();
            OnComplete[] oc = new OnComplete[1];

            if (EndOfStream) {
                f.Complete(_ExtraLine ? "" : null);
                _ExtraLine = false;
                return f;
            }

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

                            if (EndOfStream)
                                _ExtraLine = true;

                            done = true;
                            break;
                        default:
                            buffer.Append(value);
                            break;
                    }

                    if (done) {
                        ClearPendingOperation(f);
                        f.Complete(buffer.DisposeAndGetContents());
                        return;
                    }
                }

                Future decodeMoreChars = DecodeMoreData();
                decodeMoreChars.RegisterOnComplete(oc[0]);
            };

            OnComplete onDecodeComplete = (_f, result, error) => {
                if (error != null) {
                    ClearPendingOperation(f);
                    f.Fail(error);
                    buffer.Dispose();
                } else {
                    int numChars = (int)result;

                    if (numChars > 0) {
                        try {
                            processDecodedChars();
                        } catch (Exception ex) {
                            f.Fail(ex);
                            buffer.Dispose();
                        }
                    } else {
                        ClearPendingOperation(f);

                        string resultString = buffer.DisposeAndGetContents();
                        if (resultString.Length == 0)
                            resultString = null;

                        f.Complete(resultString);
                    }
                }
            };

            oc[0] = onDecodeComplete;
            processDecodedChars();
            return f;
        }

        public Future ReadToEnd () {
            Future f = new Future();
            CharacterBuffer buffer = new CharacterBuffer();
            OnComplete[] oc = new OnComplete[1];

            if (EndOfStream) {
                f.Complete(null);
                return f;
            }

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

            OnComplete onDecodeComplete = (_f, result, error) => {
                if (error != null) {
                    ClearPendingOperation(f);
                    buffer.Dispose();
                    f.Fail(error);
                } else {
                    int numChars = (int)result;

                    if (numChars > 0) {
                        processDecodedChars();
                    } else {
                        ClearPendingOperation(f);

                        string resultString = buffer.DisposeAndGetContents();
                        if (resultString.Length == 0)
                            resultString = null;

                        f.Complete(resultString);
                    }
                }
            };

            oc[0] = onDecodeComplete;
            processDecodedChars();
            return f;
        }
    }

    public class AsyncTextWriter : PendingOperationManager, IDisposable {
        public static Encoding DefaultEncoding = Encoding.UTF8;
        public static char[] DefaultNewLine = new char[] { '\r', '\n' };
        public static int DefaultBufferSize = 512;

        char[] _NewLine;
        byte[] _NewLineBytes;
        IAsyncDataWriter _DataWriter;
        Encoding _Encoding;
        Encoder _Encoder;
        OnComplete _WriteOnComplete;

        byte[] _WriteBuffer;

        public IAsyncDataWriter DataWriter {
            get {
                return _DataWriter;
            }
        }

        public AsyncTextWriter (IAsyncDataWriter dataWriter)
            : this(dataWriter, DefaultEncoding) {
        }

        public AsyncTextWriter (IAsyncDataWriter dataWriter, Encoding encoding) {
            _DataWriter = dataWriter;
            _Encoding = encoding;
            _Encoder = encoding.GetEncoder();
            _NewLine = DefaultNewLine;
            _NewLineBytes = _Encoding.GetBytes(_NewLine);
            _WriteBuffer = new byte[DefaultBufferSize];
            _WriteOnComplete = WriteOnComplete;
        }

        public void Dispose () {
            if (_DataWriter != null) {
                _DataWriter.Dispose();
                _DataWriter = null;
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
            return Write(bytes, bytes.Length);
        }

        private void WriteOnComplete (Future f, object r, Exception e) {
            ClearPendingOperation(f);
        }

        public Future Write (byte[] bytes, int count) {
            SetPendingOperation(null);
            var f = _DataWriter.Write(bytes, 0, count);
            SetPendingOperation(f);
            f.RegisterOnComplete(_WriteOnComplete);
            return f;
        }

        private byte[] GetStringBuffer (int numBytes) {
            if (_WriteBuffer.Length < numBytes)
                _WriteBuffer = new byte[numBytes];

            return _WriteBuffer;
        }

        public Future Write (string text) {
            int numBytes = _Encoding.GetByteCount(text);
            byte[] buf = GetStringBuffer(numBytes);
            _Encoding.GetBytes(text, 0, text.Length, buf, 0);
            return Write(buf, numBytes);
        }

        public Future WriteLine (string text) {
            int numBytes = _Encoding.GetByteCount(text) + _NewLineBytes.Length;
            byte[] buf = GetStringBuffer(numBytes);
            _Encoding.GetBytes(text, 0, text.Length, buf, 0);
            Array.Copy(_NewLineBytes, 0, buf, numBytes - _NewLineBytes.Length, _NewLineBytes.Length);

            return Write(buf, numBytes);
        }
    }
}
