using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using System.Xml;
using System.IO;
using System.Reflection;

namespace Squared.Game.Serialization {
    public class EmptyType {
    }

    public class NamedType : INamedObject {
        public string Name { get; set; }
    }

    [TestFixture]
    public class SerializationTests {
        public const string DictionaryXML =
            "<?xml version=\"1.0\" encoding=\"utf-16\"?>" +
            "<dict>" +
            "<types>" +
            "<type id=\"0\" name=\"System.Int32\" />" +
            "<type id=\"1\" name=\"System.String\" />" +
            "</types>" +
            "<values>" +
            "<int key=\"a\" typeId=\"0\">1</int>" +
            "<string key=\"b\" typeId=\"1\">asdf</string>" +
            "</values>" +
            "</dict>";

        [Test]
        public void WriteDictionary () {
            var dict = new Dictionary<string, object>();
            dict.Add("a", 1);
            dict.Add("b", "asdf");

            var sb = new StringBuilder();
            using (var writer = XmlWriter.Create(sb, null)) {
                writer.WriteStartElement("dict");
                writer.WriteDictionary(dict, new AssemblyTypeResolver(typeof(int).Assembly));
                writer.WriteEndElement();
            }

            Assert.AreEqual(
                DictionaryXML, sb.ToString()
            );
        }

        [Test]
        public void ReadDictionary () {
            using (var reader = XmlReader.Create(new StringReader(DictionaryXML))) {
                reader.ReadToDescendant("dict");
                var dict = reader.ReadDictionary<object>(new AssemblyTypeResolver(typeof(int).Assembly));

                Assert.AreEqual(2, dict.Count);
                Assert.AreEqual(1, dict["a"]);
                Assert.AreEqual("asdf", dict["b"]);
            }
        }

        [Test]
        public void SerializeEmptyType () {
            var et1 = new EmptyType();
            var et2 = new EmptyType();

            var dict = new Dictionary<string, object>();
            dict.Add("a", et1);
            dict.Add("b", et2);

            var sb = new StringBuilder();
            using (var writer = XmlWriter.Create(sb, null)) {
                writer.WriteStartElement("dict");
                writer.WriteDictionary(dict);
                writer.WriteEndElement();
            }

            using (var reader = XmlReader.Create(new StringReader(sb.ToString()), null)) {
                reader.ReadToDescendant("dict");
                var newDict = reader.ReadDictionary<object>();

                Assert.AreEqual(2, newDict.Count);
                Assert.AreNotEqual(newDict["a"], newDict["b"]);
            }
        }

        [Test]
        public void SerializeNamedObjects () {
            const string expectedXML =
                "<?xml version=\"1.0\" encoding=\"utf-16\"?>" +
                "<dict><types><type id=\"0\" name=\"Squared.Game.Serialization.NamedType\" /></types>" +
                "<values><NamedType typeId=\"0\"><Name>2</Name></NamedType>" +
                "<NamedType typeId=\"0\"><Name>1</Name></NamedType>" +
                "<NamedType key=\"3\" typeId=\"0\"><Name>1</Name></NamedType></values></dict>";

            var nt1 = new NamedType { Name = "2" };
            var nt2 = new NamedType { Name = "1" };
            var nt3 = new NamedType { Name = "1" };

            var dict = new Dictionary<string, object>();
            dict.Add("2", nt1);
            dict.Add("1", nt2);
            dict.Add("3", nt3);

            var sb = new StringBuilder();
            using (var writer = XmlWriter.Create(sb, null)) {
                writer.WriteStartElement("dict");
                writer.WriteDictionary(dict, new AssemblyTypeResolver(Assembly.GetExecutingAssembly()));
                writer.WriteEndElement();
            }

            Assert.AreEqual(expectedXML, sb.ToString());
        }
    }
}
