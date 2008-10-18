using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Squared.Util.Dependency;
using System.Linq.Expressions;
using System.Reflection;

namespace Squared.Util {
    public struct TestStruct {
        [Dependency("hello.txt")]
        public string Text;
        [Dependency("test.dat")]
        public byte[] Bytes;
    }

    [TestFixture]
    public class DependencyTests {
        public DependencyContext dc;

        [SetUp]
        public void SetUp () {
            string path = System.IO.Directory.GetCurrentDirectory() + @"\..\..\data\";
            dc = new DependencyContext(path);
        }

        [Test]
        public void ResolveString () {
            TestStruct ts = new TestStruct();
            dc.ResolveDependencies(ref ts);
            Assert.AreEqual("Hello!", ts.Text);
        }

        [Test]
        public void ResolveByteArray () {
            TestStruct ts = new TestStruct();
            dc.ResolveDependencies(ref ts);
            Assert.AreEqual(new byte[] { 0, 1, 64, 65, 240, 241 }, ts.Bytes);
        }
    }
}