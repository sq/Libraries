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
            string[] dirs = dirList.ToArray();
            string[] expected = new string[] {
                @"dirA",
                @"dirB",
                @"dirC"
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
            string[] dirs = dirList.ToArray();
            string[] expected = new string[] {
                @"dirA",
                @"dirB",
                @"dirC",
                @"dirA\subdirA_A",
                @"dirA\subdirA_B",
                @"dirB\subdirB_A"
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
            string[] files = fileList.ToArray();
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
            string[] files = fileList.ToArray();
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
            string[] files = fileList.ToArray();
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
            string[] files = fileList.ToArray();
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
    }
}
