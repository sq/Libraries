using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace Squared.Util {
    [TestFixture]
    public class IOTests {
        public string DataPath;

        [SetUp]
        public void SetUp () {
            DataPath = System.IO.Path.GetFullPath(System.IO.Directory.GetCurrentDirectory() + @"\..\..\data\");
        }

        [Test]
        public void EnumDirectories () {
            DataPath += @"dirs\";

            IEnumerable<string> dirList = Squared.Util.IO.EnumDirectories(DataPath);
            string[] dirs = (from x in dirList where !x.Contains(".svn") select x).ToArray();
            string[] expected = new string[] {
                @"dirA\",
                @"dirB\",
                @"dirC\"
            };
            expected = (from x in expected select DataPath + x).ToArray();

            Array.Sort(expected);
            Array.Sort(dirs);

            Assert.AreEqual(
                expected,
                dirs
            );
        }

        [Test]
        public void RecursiveEnumDirectories () {
            DataPath += @"dirs\";

            IEnumerable<string> dirList = Squared.Util.IO.EnumDirectories(DataPath, "*", true);
            string[] dirs = (from x in dirList where !x.Contains(".svn") select x).ToArray();
            string[] expected = new string[] {
                @"dirA\",
                @"dirB\",
                @"dirC\",
                @"dirA\subdirA_A\",
                @"dirA\subdirA_B\",
                @"dirB\subdirB_A\"
            };
            expected = (from x in expected select DataPath + x).ToArray();

            Array.Sort(expected);
            Array.Sort(dirs);
            
            Assert.AreEqual(
                expected,
                dirs
            );
        }

        [Test]
        public void EnumFiles () {
            DataPath += @"dirs\";

            IEnumerable<string> fileList = Squared.Util.IO.EnumFiles(DataPath);
            string[] files = (from x in fileList where !x.Contains(".svn") select x).ToArray();
            string[] expected = new string[] {
                @"file"
            };
            expected = (from x in expected select DataPath + x).ToArray();

            Array.Sort(expected);
            Array.Sort(files);
            
            Assert.AreEqual(
                expected,
                files
            );
        }

        [Test]
        public void RecursiveEnumFiles () {
            DataPath += @"dirs\";

            IEnumerable<string> fileList = Squared.Util.IO.EnumFiles(DataPath, "*", true);
            string[] files = (from x in fileList where !x.Contains(".svn") select x).ToArray();
            string[] expected = new string[] {
                @"file",
                @"dirA\subdirA_A\fileA_A_A.txt",
                @"dirA\subdirA_B\fileA_B_A.txt",
                @"dirB\fileB_A.txt",
                @"dirC\fileC_A.txt",
                @"dirC\fileC_B.txt"
            };
            expected = (from x in expected select DataPath + x).ToArray();

            Array.Sort(expected);
            Array.Sort(files);

            Assert.AreEqual(
                expected,
                files
            );
        }

        [Test]
        public void RecursiveEnumFilesWithFilter () {
            DataPath += @"dirs\";

            IEnumerable<string> fileList = Squared.Util.IO.EnumFiles(DataPath, "*.txt", true);
            string[] files = (from x in fileList where !x.Contains(".svn") select x).ToArray();
            string[] expected = new string[] {
                @"dirA\subdirA_A\fileA_A_A.txt",
                @"dirA\subdirA_B\fileA_B_A.txt",
                @"dirB\fileB_A.txt",
                @"dirC\fileC_A.txt",
                @"dirC\fileC_B.txt"
            };
            expected = (from x in expected select DataPath + x).ToArray();

            Array.Sort(expected);
            Array.Sort(files);

            Assert.AreEqual(
                expected,
                files
            );
        }

        [Test]
        public void EnumFilesMultiplePatterns () {
            DataPath += @"files\";

            IEnumerable<string> fileList = Squared.Util.IO.EnumFiles(DataPath, "*.txt;*.png;*.jpg", false);
            string[] files = (from x in fileList where !x.Contains(".svn") select x).ToArray();
            string[] expected = new string[] {
                @"fileA.txt",
                @"fileB.txt",
                @"fileC.png",
                @"fileD.png",
                @"fileE.jpg",
                @"fileF.jpg"
            };
            expected = (from x in expected select DataPath + x).ToArray();

            Array.Sort(expected);
            Array.Sort(files);

            Assert.AreEqual(
                expected,
                files
            );
        }

        [Test]
        public void EnumDirectoryEntriesTimestamps () {
            DataPath += @"dirs\";

            var entries = Squared.Util.IO.EnumDirectoryEntries(DataPath, "*.*", true, (_) => true);
            foreach (var entry in entries) {
                Console.WriteLine("entry created={0}, written={1}, accessed={2}", 
                    entry.Created, entry.LastWritten, entry.LastAccessed
                );
                Console.WriteLine("file created={0}, written={1}, accessed={2}", 
                    System.IO.File.GetCreationTimeUtc(entry.Name).ToFileTimeUtc(), 
                    System.IO.File.GetLastWriteTimeUtc(entry.Name).ToFileTimeUtc(), 
                    System.IO.File.GetLastAccessTimeUtc(entry.Name).ToFileTimeUtc()
                );

                Assert.AreEqual(
                    DateTime.FromFileTimeUtc(entry.Created),
                    System.IO.File.GetCreationTimeUtc(entry.Name)
                );
                Assert.AreEqual(
                    DateTime.FromFileTimeUtc(entry.LastWritten),
                    System.IO.File.GetLastWriteTimeUtc(entry.Name)
                );
            }
        }

        [Test]
        public void TestGlobToRegex () {
            var g = Squared.Util.IO.GlobToRegex("*.txt");
            Assert.IsTrue(g.IsMatch("test.txt"));
            Assert.IsTrue(g.IsMatch("test.png.txt"));
            Assert.IsFalse(g.IsMatch("test.txt.png"));

            g = Squared.Util.IO.GlobToRegex("*.png.txt");
            Assert.IsTrue(g.IsMatch("test.png.txt"));
            Assert.IsFalse(g.IsMatch("test.txt.png"));

            g = Squared.Util.IO.GlobToRegex("*.txt.png");
            Assert.IsFalse(g.IsMatch("test.png.txt"));
            Assert.IsTrue(g.IsMatch("test.txt.png"));
        }
    }

    [TestFixture]
    public class CharacterBufferTests {
        [Test]
        public void TestBuildString () {
            var buffer = new CharacterBuffer();

            var s = "Some test string";

            for (int i = 0; i < s.Length; i++)
                buffer.Append(s[i]);

            Assert.AreEqual(buffer.DisposeAndGetContents(), s);
        }

        [Test]
        public void TestClear () {
            var buffer = new CharacterBuffer();

            buffer.Append('a');
            buffer.Append('b');

            buffer.Clear();

            buffer.Append('c');

            Assert.AreEqual(buffer.DisposeAndGetContents(), "c");
        }

        [Test]
        public void TestRemoveLastCharacter () {
            var buffer = new CharacterBuffer();

            int size = buffer.Capacity;

            for (int i = 0; i < size; i++)
                buffer.Append('a');

            for (int i = 0; i < size; i++)
                buffer.Remove(buffer.Length - 1, 1);

            buffer.Dispose();
        }
    }
}
