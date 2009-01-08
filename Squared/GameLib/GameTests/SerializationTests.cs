using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using System.Xml;
using System.IO;

namespace Squared.Game.Serialization {
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
    }
}
