using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using System.IO;
using System.Threading;

namespace Squared.Task {
    public class IOTests {
        public MemoryStream Stream;

        [SetUp]
        public virtual void SetUp () {
            Stream = new MemoryStream();
        }

        public void RewindStream () {
            Stream.Seek(0, SeekOrigin.Begin);
        }

        public void WriteTestData (byte[] data) {
            Stream.Write(data, 0, data.Length);
        }

        public void WriteTestData (string data) {
            WriteTestData(Encoding.ASCII.GetBytes(data));
        }
    }

    [TestFixture]
    public class AsyncStreamReaderTests : IOTests {
        AsyncStreamReader Reader;

        [SetUp]
        public override void SetUp () {
            base.SetUp();
            Reader = new AsyncStreamReader(this.Stream);
        }

        [Test]
        public void ReadLineTest () {
            WriteTestData("abcd\r\nefgh\nijkl");
            RewindStream();

            Future f = Reader.ReadLine();
            f.GetCompletionEvent().WaitOne();
            Assert.AreEqual("abcd", f.Result);

            f = Reader.ReadLine();
            f.GetCompletionEvent().WaitOne();
            Assert.AreEqual("efgh", f.Result);

            f = Reader.ReadLine();
            f.GetCompletionEvent().WaitOne();
            Assert.AreEqual("ijkl", f.Result);

            f = Reader.ReadLine();
            f.GetCompletionEvent().WaitOne();
            Assert.AreEqual(null, f.Result);
        }

        [Test]
        public void ReadToEndTest () {
            WriteTestData("abcd\r\nefgh\0ijkl");
            RewindStream();

            Future f = Reader.ReadToEnd();
            f.GetCompletionEvent().WaitOne();
            Assert.AreEqual("abcd\r\nefgh\0ijkl", f.Result);

            f = Reader.ReadToEnd();
            f.GetCompletionEvent().WaitOne();
            Assert.AreEqual(null, f.Result);
        }

        [Test]
        public void MultiblockReadTest () {
            string testData = new string('a', AsyncStreamReader.DefaultBufferSize * 4);

            WriteTestData(testData);
            RewindStream();

            Future f = Reader.ReadToEnd();
            f.GetCompletionEvent().WaitOne();
            Assert.AreEqual(testData, f.Result);
        }

        [Test]
        public void ReadBlockTest () {
            string testData = new string('a', 320);

            WriteTestData(testData);
            RewindStream();

            char[] buffer = new char[256];
            Future f = Reader.Read(buffer, 0, 256);
            f.GetCompletionEvent().WaitOne();
            Assert.AreEqual(256, f.Result);
            Assert.AreEqual(testData.Substring(0, 256), new string(buffer, 0, 256));

            f = Reader.Read(buffer, 0, 256);
            f.GetCompletionEvent().WaitOne();
            Assert.AreEqual(64, f.Result);
            Assert.AreEqual(testData.Substring(256, 64), new string(buffer, 0, 64));

            f = Reader.Read(buffer, 0, 256);
            f.GetCompletionEvent().WaitOne();
            Assert.AreEqual(0, f.Result);
        }

        [Test]
        public void ReadTest () {
            string testData = new string('a', 2);

            WriteTestData(testData);
            RewindStream();

            Future f = Reader.Read();
            f.GetCompletionEvent().WaitOne();
            Assert.AreEqual('a', f.Result);

            f = Reader.Read();
            f.GetCompletionEvent().WaitOne();
            Assert.AreEqual('a', f.Result);

            f = Reader.Read();
            f.GetCompletionEvent().WaitOne();
            Assert.AreEqual(null, f.Result);
        }
    }
}
