using System;
using System.Collections.Generic;
using System.Threading;
using System.Net.Sockets;
using System.IO;
using System.Text;
using System.Diagnostics;

namespace Squared.Task {
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
                        int bytesRead = stream.EndRead(ar);
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

        public static Future AsyncWrite<T> (this TextWriter writer, T value) {
            var f = new Future();
            WaitCallback fn = (state) => {
                try {
                    writer.Write(value);
                    f.Complete();
                } catch (Exception ex) {
                    f.Fail(ex);
                }
            };
            ThreadPool.QueueUserWorkItem(fn);
            return f;
        }

        public static Future AsyncWriteLine (this TextWriter writer, string value) {
            var f = new Future();
            WaitCallback fn = (state) => {
                try {
                    writer.WriteLine(value);
                    f.Complete();
                } catch (Exception ex) {
                    f.Fail(ex);
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
                } catch (Exception ex) {
                    f.Fail(ex);
                }
            };
            ThreadPool.QueueUserWorkItem(fn);
            return f;
        }
    }

    public class AsyncStreamReader : IDisposable {
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

        public AsyncStreamReader (Stream stream)
            : this(stream, DefaultEncoding) {
        }

        public AsyncStreamReader (Stream stream, Encoding encoding) {
            _BaseStream = stream;
            _Encoding = encoding;
            _Decoder = _Encoding.GetDecoder();
            _BufferSize = DefaultBufferSize;
            AllocateBuffer();
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
                if (_BaseStream == null) {
                    f.Fail(new Exception("Stream disposed before read could be completed"));
                    return;
                }
                try {
                    int bytesRead = _BaseStream.EndRead(ar);
                    f.Complete(bytesRead);
                    // Debug.WriteLine("ReadMoreData:Future.Complete");
                } catch (Exception ex) {
                    f.Fail(ex);
                    // Debug.WriteLine(String.Format("ReadMoreData:Future.Fail({0})", ex));
                    return;
                }
            };
            // Debug.WriteLine("ReadMoreData");
            try {
                IAsyncResult _ar = _BaseStream.BeginRead(_InputBuffer, 0, _BufferSize, callback, this);
            } catch (Exception ex) {
                f.Fail(ex);
            }
            return f;
        }

        private Future DecodeMoreData () {
            Future f = new Future();
            // Debug.WriteLine("DecodeMoreData");
            Future readData = ReadMoreData();
            readData.RegisterOnComplete((result, error) => {
                if (error != null) {
                    f.Fail(error);
                    // Debug.WriteLine(String.Format("DecodeMoreData:Future.Fail({0})", error));
                    return;
                }

                int bytesRead = (int)result;
                try {
                    _DecodedCharacterOffset = 0;
                    _DecodedCharacterCount = 0;
                    _DecodedCharacterCount = _Decoder.GetChars(_InputBuffer, 0, bytesRead, _DecodedBuffer, 0);
                    f.Complete(_DecodedCharacterCount);
                    // Debug.WriteLine("DecodeMoreData:Future.Complete");
                } catch (Exception ex) {
                    f.Fail(ex);
                    // Debug.WriteLine(String.Format("DecodeMoreData:Future.Fail({0})", ex));
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

        private bool IsEOL (char value) {
            return (value == 10) || (value == 13);
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
            char result;
            if (!GetCurrentCharacter(out result)) {
                Future decodeMoreChars = DecodeMoreData();
                decodeMoreChars.RegisterOnComplete((_, error) => {
                    if (error != null) {
                        f.Fail(error);
                    } else {
                        char ch;
                        if (GetCurrentCharacter(out ch)) {
                            if (advance)
                                ReadNextCharacter();
                            f.Complete(ch);
                        } else {
                            f.Complete(null);
                        }
                    }
                });
            } else {
                if (advance)
                    ReadNextCharacter();
                f.Complete(result);
            }
            return f;
        }

        public Future Read (char[] buffer, int offset, int count) {
            Future f = new Future();
            int[] wp = new int[1];
            OnComplete[] oc = new OnComplete[1];

            Action processDecodedChars = () => {
                char value;
                while (GetCurrentCharacter(out value)) {
                    if (wp[0] >= (offset + count)) {
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
                    f.Fail(error);
                } else {
                    int numChars = (int)result;

                    if (numChars > 0)
                        processDecodedChars();
                    else {
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

            // Debug.WriteLine("ReadLine");

            Action processDecodedChars = () => {
                // Debug.WriteLine("ReadLine:processDecodedChars");
                char value;
                while (GetCurrentCharacter(out value)) {
                    ReadNextCharacter();

                    if (IsEOL(value)) {
                        char nextValue;
                        if (GetCurrentCharacter(out nextValue) && IsEOL(nextValue) && (nextValue != value)) {
                            ReadNextCharacter();
                        }
                        f.Complete(ReturnBufferValue(buffer));
                        // Debug.WriteLine("ReadLine:Future.Complete");
                        return;
                    }

                    buffer.Append(value);
                }

                // Debug.WriteLine("ReadLine:DecodeMoreData:Request");
                Future decodeMoreChars = DecodeMoreData();
                decodeMoreChars.RegisterOnComplete(oc[0]);
            };

            OnComplete onDecodeComplete = (result, error) => {
                // Debug.WriteLine("ReadLine:DecodeMoreData");
                if (error != null) {
                    // Debug.WriteLine(String.Format("ReadLine:Future.Fail({0})", error));
                    f.Fail(error);
                } else {
                    int numChars = (int)result;

                    if (numChars > 0) {
                        processDecodedChars();
                    } else {
                        f.Complete(ReturnBufferValue(buffer));
                        // Debug.WriteLine("ReadLine:Future.Complete");
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
                    f.Fail(error);
                } else {
                    int numChars = (int)result;

                    if (numChars > 0)
                        processDecodedChars();
                    else
                        f.Complete(ReturnBufferValue(buffer));
                }
            };

            oc[0] = onDecodeComplete;
            processDecodedChars();
            return f;
        }
    }
}
