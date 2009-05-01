using System;
using System.Collections.Generic;
using System.Threading;
using System.IO;
using System.Text;
using System.Diagnostics;
using Squared.Util;

namespace Squared.Task.IO {
    public class OperationPendingException : InvalidOperationException {
        public OperationPendingException ()
            : base("A previous operation on this object is still pending.") {
        }
    }    

    public static class IOExtensionMethods {
        public static Future<int> AsyncRead (this Stream stream, byte[] buffer, int offset, int count) {
            var f = new Future<int>();
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

        public static SignalFuture AsyncWrite (this Stream stream, byte[] buffer, int offset, int count) {
            var f = new SignalFuture();
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
        IFuture _PendingOperation;
        internal OnComplete OperationOnComplete;

        public IFuture PendingOperation {
            get {
                return _PendingOperation;
            }
        }

        internal void SetPendingOperation (IFuture f) {
            if (Interlocked.CompareExchange<IFuture>(ref _PendingOperation, f, null) != null)
                throw new OperationPendingException();
        }

        internal void ClearPendingOperation (IFuture f) {
            if (Interlocked.CompareExchange<IFuture>(ref _PendingOperation, null, f) != f)
                throw new InvalidDataException();
        }

        private void _OperationOnComplete (IFuture f) {
            ClearPendingOperation(f);
        }

        internal PendingOperationManager () {
            OperationOnComplete = _OperationOnComplete;
        }
    }

    public interface IAsyncDataSource : IDisposable {
        Future<int> Read (byte[] buffer, int offset, int count);

        bool EndOfStream { get; }
    }

    public interface IAsyncDataWriter : IDisposable {
        SignalFuture Write (byte[] buffer, int offset, int count);
    }

    public class FileDataAdapter : StreamDataAdapter {
        public const int DefaultBufferSize = 0x8000;

        public FileDataAdapter (string filename, FileMode mode, FileAccess access)
            : this (
                filename, mode, access,
                access == FileAccess.Write ? FileShare.Write : (
                    access == FileAccess.Read ? FileShare.Read : FileShare.ReadWrite
                )
            ) {
        }

        public FileDataAdapter (string filename, FileMode mode, FileAccess access, FileShare share) 
            : this (filename, mode, access, share, DefaultBufferSize) {
        }

        public FileDataAdapter (string filename, FileMode mode, FileAccess access, FileShare share, int bufferSize)
            : base (
                new FileStream(filename, mode, access, share, DefaultBufferSize, true), true
            ) {
        }

        new public FileStream BaseStream {
            get {
                return _Stream as FileStream;
            }
        }
    }

    public class StreamDataAdapter : IAsyncDataSource, IAsyncDataWriter {
        protected Stream _Stream;
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
            var f = (Future<int>)ar.AsyncState;
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

        public Future<int> Read (byte[] buffer, int offset, int count) {
            var f = new Future<int>();
            _Stream.BeginRead(buffer, offset, count, _ReadCallback, f);
            return f;
        }

        private void WriteCallback (IAsyncResult ar) {
            var f = (SignalFuture)ar.AsyncState;
            try {
                _Stream.EndWrite(ar);
                f.Complete();
            } catch (FutureHandlerException) {
                throw;
            } catch (Exception ex) {
                f.Fail(ex);
            }
        }
        
        public SignalFuture Write (byte[] buffer, int offset, int count) {
            var f = new SignalFuture();
            _Stream.BeginWrite(buffer, offset, count, _WriteCallback, f);
            return f;
        }

        public SignalFuture Flush () {
            var f = new SignalFuture();
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

        public Stream BaseStream {
            get {
                return _Stream;
            }
        }
    }

    public class AsyncTextReader : PendingOperationManager, IDisposable {
        public static Encoding DefaultEncoding = Encoding.UTF8;

        public const int MinimumBufferSize = 256;
        public const int DefaultBufferSize = 2048;

        private class ReadBlockThunk {
            public AsyncTextReader Parent;
            public Future<int> Result = new Future<int>();
            public int InitialPosition, Count;
            public int Position;
            public char[] Buffer;
            public OnComplete OnDecodeComplete;

            public ReadBlockThunk () {
                OnDecodeComplete = _OnDecodeComplete;
            }

            public IFuture Run () {
                Parent.SetPendingOperation(Result);
                Result.RegisterOnComplete(Parent.OperationOnComplete);

                ProcessDecodedChars();

                return Result;
            }

            public void ProcessDecodedChars () {
                char value;
                while (Parent.GetCurrentCharacter(out value)) {
                    if (Position >= (InitialPosition + Count)) {
                        Result.Complete(Count);
                        return;
                    }

                    Parent.ReadNextCharacter();

                    Buffer[Position] = value;
                    Position += 1;
                }

                var decodeMoreChars = Parent.DecodeMoreData();
                decodeMoreChars.RegisterOnComplete(OnDecodeComplete);
            }

            private void _OnDecodeComplete (IFuture f) {
                var e = f.Error;
                if (e != null) {
                    Result.Fail(e);
                } else {
                    int numChars = (int)f.Result;

                    if (numChars > 0)
                        ProcessDecodedChars();
                    else {
                        Result.Complete(Position - InitialPosition);
                    }
                }
            }
        }

        private class ReadLineThunk {
            public AsyncTextReader Parent;
            public Future<string> Result = new Future<string>();
            public CharacterBuffer Buffer;
            public OnComplete OnDecodeComplete;

            public ReadLineThunk () {
                Buffer = new CharacterBuffer();
                OnDecodeComplete = _OnDecodeComplete;
            }

            public IFuture Run () {
                Parent.SetPendingOperation(Result);
                Result.RegisterOnComplete(Parent.OperationOnComplete);

                ProcessDecodedChars();

                return Result;
            }

            public void ProcessDecodedChars () {
                if (Parent.ReadDecodedCharactersUntilSentinel(Buffer, '\n')) {
                    if ((Buffer.Length > 0) && (Buffer[Buffer.Length - 1] == '\r'))
                        Buffer.Remove(Buffer.Length - 1, 1);

                    if (Parent.EndOfStream)
                        Parent._ExtraLine = true;

                    Result.Complete(Buffer.DisposeAndGetContents());
                } else {
                    var decodeMoreChars = Parent.DecodeMoreData();
                    decodeMoreChars.RegisterOnComplete(OnDecodeComplete);
                }
            }

            private void _OnDecodeComplete (IFuture f) {
                var e = f.Error;
                if (e != null) {
                    Buffer.Dispose();
                    Result.Fail(e);
                } else {
                    int numChars = (int)f.Result;

                    if (numChars > 0) {
                        try {
                            ProcessDecodedChars();
                        } catch (Exception ex) {
                            Buffer.Dispose();
                            Result.Fail(ex);
                        }
                    } else {
                        string resultString = Buffer.DisposeAndGetContents();
                        if (resultString.Length == 0)
                            resultString = null;

                        Result.Complete(resultString);
                    }
                }
            }
        }

        private class ReadToEndThunk {
            public AsyncTextReader Parent;
            public Future<string> Result = new Future<string>();
            public CharacterBuffer Buffer;
            public OnComplete OnDecodeComplete;

            public ReadToEndThunk () {
                Buffer = new CharacterBuffer();
                OnDecodeComplete = _OnDecodeComplete;
            }

            public IFuture Run () {
                Parent.SetPendingOperation(Result);
                Result.RegisterOnComplete(Parent.OperationOnComplete);

                ProcessDecodedChars();

                return Result;
            }

            void ProcessDecodedChars () {
                char value;
                while (Parent.GetCurrentCharacterAndAdvance(out value)) {
                    Buffer.Append(value);
                }

                var decodeMoreChars = Parent.DecodeMoreData();
                decodeMoreChars.RegisterOnComplete(OnDecodeComplete);
            }

            void _OnDecodeComplete (IFuture f) {
                var e = f.Error;
                if (e != null) {
                    Buffer.Dispose();
                    Result.Fail(e);
                } else {
                    int numChars = (int)f.Result;

                    if (numChars > 0) {
                        ProcessDecodedChars();
                    } else {
                        string resultString = Buffer.DisposeAndGetContents();
                        if (resultString.Length == 0)
                            resultString = null;

                        Result.Complete(resultString);
                    }
                }
            }
        }

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

        public AsyncTextReader(IAsyncDataSource dataSource, Encoding encoding) 
            : this(dataSource, encoding, DefaultBufferSize) {
        }

        public AsyncTextReader (IAsyncDataSource dataSource, Encoding encoding, int bufferSize) 
            : base() {
            _DataSource = dataSource;
            _Encoding = encoding;
            _Decoder = _Encoding.GetDecoder();
            _BufferSize = Math.Max(MinimumBufferSize, bufferSize);
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
            _DecodedBuffer = new char[_BufferSize + 1];
        }

        private Future<int> ReadMoreData () {
            return _DataSource.Read(_InputBuffer, 0, _BufferSize);
        }

        private Future<int> DecodeMoreData () {
            var f = new Future<int>();
            IFuture readData = ReadMoreData();
            readData.RegisterOnComplete((_) => {
                var error = _.Error;
                if (error != null) {
                    f.Fail(error);
                    return;
                }

                int bytesRead = (int)(_.Result);

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

        private bool GetCurrentCharacterAndAdvance (out char value) {
            if (_DecodedCharacterOffset < _DecodedCharacterCount) {
                value = _DecodedBuffer[_DecodedCharacterOffset++];
                return true;
            } else {
                value = default(char);
                return false;
            }
        }

        private bool ReadDecodedCharactersUntilSentinel (CharacterBuffer buffer, char sentinel) {
            bool result = false;
            int startOffset = _DecodedCharacterOffset;
            while (_DecodedCharacterOffset < _DecodedCharacterCount) {
                if (_DecodedBuffer[_DecodedCharacterOffset++] == sentinel) {
                    result = true;
                    break;
                }
            }

            int charCount = _DecodedCharacterOffset - startOffset;
            if (result)
                charCount -= 1;
            buffer.Append(_DecodedBuffer, startOffset, charCount);

            return result;
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

        public IFuture Read () {
            return Read(true);
        }

        public IFuture Peek () {
            return Read(false);
        }

        public Future<char> Read (bool advance) {
            var f = new Future<char>();

            SetPendingOperation(f);
            
            char result;
            if (!GetCurrentCharacter(out result)) {
                var decodeMoreChars = DecodeMoreData();
                decodeMoreChars.RegisterOnComplete((_) => {
                    var error = _.Error;
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

        public IFuture Read (char[] buffer, int offset, int count) {
            if (EndOfStream)
                return new Future(0);

            var thunk = new ReadBlockThunk {
                Parent = this,
                InitialPosition = offset,
                Position = offset,
                Count = count,
                Buffer = buffer
            };

            return thunk.Run();
        }

        public IFuture ReadLine () {
            if (EndOfStream) {
                string r = _ExtraLine ? "" : null;
                _ExtraLine = false;
                return new Future(r);
            }

            var thunk = new ReadLineThunk {
                Parent = this,
            };

            return thunk.Run();
        }

        public IFuture ReadToEnd () {
            if (EndOfStream) {
                return new Future(null);
            }

            var thunk = new ReadToEndThunk {
                Parent = this,
            };

            return thunk.Run();
        }
    }

    public class AsyncTextWriter : PendingOperationManager, IDisposable {
        public static Encoding DefaultEncoding = Encoding.UTF8;
        public static char[] DefaultNewLine = new char[] { '\r', '\n' };

        public const int MinimumBufferSize = 256;
        public const int DefaultBufferSize = 1024;

        private class WriteThunk {
            public AsyncTextWriter Parent;
            private IFuture Result;
            public char[][] Strings;
            public int NumStrings;
            public bool FlushWhenDone;
            public int StringIndex = -1;
            public int StringPos = 0;
            private OnComplete FlushOnComplete;

            public WriteThunk () {
                Result = new Future();
                FlushOnComplete = _FlushOnComplete;
            }

            public IFuture Run () {
                Parent.SetPendingOperation(Result);
                Result.RegisterOnComplete(Parent.OperationOnComplete);

                StepStrings();

                return Result;
            }

            private void _FlushOnComplete (IFuture f) {
                var e = f.Error;
                if (e != null)
                    Result.Fail(e);
                else
                    StepString();
            }

            public void StepStrings () {
                try {
                    StringIndex += 1;
                    if (StringIndex >= NumStrings) {
                        if (FlushWhenDone)
                            Result.Bind(Parent.Flush(Parent._BufferCount));
                        else
                            Result.Complete();
                        return;
                    }

                    StringPos = 0;
                    StepString();
                } catch (Exception ex) {
                    Result.Fail(ex);
                }
            }

            public void StepString () {
                try {
                    var str = Strings[StringIndex];
                    int charsRemaining = str.Length - StringPos;
                    int bufferRemaining = Parent._WriteBuffer.Length - Parent._BufferCount;
                    int numChars = Math.Min(charsRemaining, bufferRemaining);

                    if (numChars <= 0) {
                        StepStrings();
                        return;
                    }

                    Array.Copy(str, StringPos, Parent._WriteBuffer, Parent._BufferCount, numChars);
                    StringPos += numChars;
                    Parent._BufferCount += numChars;

                    if (Parent._BufferCount >= Parent._WriteBuffer.Length) {
                        var f = Parent.Flush(Parent._BufferCount);
                        f.RegisterOnComplete(FlushOnComplete);
                    } else {
                        StepStrings();
                    }
                } catch (Exception ex) {
                    Result.Fail(ex);
                }
            }
        }

        public bool AutoFlush = false;

        char[] _NewLine;
        byte[] _NewLineBytes;
        IAsyncDataWriter _DataWriter;
        Encoding _Encoding;
        Encoder _Encoder;

        char[] _WriteBuffer;
        byte[] _SendBuffer;
        int _BufferCount;

        public IAsyncDataWriter DataWriter {
            get {
                return _DataWriter;
            }
        }

        public AsyncTextWriter (IAsyncDataWriter dataWriter)
            : this(dataWriter, DefaultEncoding) {
        }

        public AsyncTextWriter (IAsyncDataWriter dataWriter, Encoding encoding)
            : this(dataWriter, encoding, DefaultBufferSize) {
        }

        public AsyncTextWriter (IAsyncDataWriter dataWriter, Encoding encoding, int bufferSize)
            : base() {
            _DataWriter = dataWriter;
            _Encoding = encoding;
            _Encoder = encoding.GetEncoder();
            _NewLine = DefaultNewLine;
            _NewLineBytes = _Encoding.GetBytes(_NewLine);
            bufferSize = Math.Max(MinimumBufferSize, bufferSize);
            _SendBuffer = new byte[bufferSize];
            int decodeSize = encoding.GetMaxCharCount(bufferSize);
            _WriteBuffer = new char[decodeSize];
            _BufferCount = 0;
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

        private IFuture Flush (int numChars) {
            if (numChars > 0) {
                _BufferCount = 0;
                int numBytes = _Encoder.GetBytes(_WriteBuffer, 0, numChars, _SendBuffer, 0, true);
                return _DataWriter.Write(_SendBuffer, 0, numBytes);
            } else
                return new Future(null);
        }

        public IFuture Flush () {
            SetPendingOperation(null);
            var f = Flush(_BufferCount);
            SetPendingOperation(f);
            f.RegisterOnComplete(OperationOnComplete);
            return f;
        }

        public IFuture Write (params char[][] strings) {
            SetPendingOperation(null);

            var state = new WriteThunk {
                Parent = this,
                Strings = strings,
                NumStrings = strings.Length,
                FlushWhenDone = AutoFlush
            };

            return state.Run();
        }

        public IFuture Write (string text) {
            return Write(text.ToCharArray());
        }

        public IFuture WriteLine (string text) {
            return Write(text.ToCharArray(), NewLine);
        }
    }
}
