using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using System.Xml;
using Squared.Game.Graph;
using System.Xml.Serialization;
using Squared.Game.Serialization;
using System.Reflection;

namespace Squared.Game.Graph {
    public class Node : INode {
        [XmlIgnore]
        public LinkedList<INode> Children = new LinkedList<INode>();
        public string Name;

        IEnumerable<INode> INode.GetChildren () {
            return Children;
        }

        public override string ToString () {
            return Name;
        }
    }

    public class GraphWriter : IGraphWriter {
        public List<string> Trace = new List<string>();

        public void BeginWrite (INode root) {
            Trace.Add(String.Format("begin({0})", (root as Node).Name));
        }

        public void WriteNodeHeader (INode node) {
            Trace.Add("+" + (node as Node).Name);
        }

        public void WriteNodeFooter (INode node) {
            Trace.Add("-" + (node as Node).Name);
        }

        public void EndWrite () {
            Trace.Add("end");
        }
    }

    [TestFixture]
    public class GraphTests {
        [Test]
        public void BasicTraversal () {
            var root = new Node { Name = "root" };
            var childA = new Node { Name = "a" };
            var childB = new Node { Name = "b" };
            var subchildAA = new Node { Name = "a.a" };
            var subchildAB = new Node { Name = "a.b" };
            var subchildAAA = new Node { Name = "a.a.a" };

            root.Children.AddLast(childA);
            root.Children.AddLast(childB);
            childA.Children.AddLast(subchildAA);
            childA.Children.AddLast(subchildAB);
            subchildAA.Children.AddLast(subchildAAA);

            var nodes = root.TraverseDepthFirst().ToArray();
            Assert.AreEqual(new NodeInfo[] { 
                new NodeInfo(root, null),
                new NodeInfo(childA, root),
                new NodeInfo(subchildAA, childA), 
                new NodeInfo(subchildAAA, subchildAA), 
                new NodeInfo(subchildAB, childA), 
                new NodeInfo(childB, root)
            }, nodes);

            nodes = root.TraverseBreadthFirst().ToArray();
            Assert.AreEqual(new NodeInfo[] { 
                new NodeInfo(root, null),
                new NodeInfo(childA, root),
                new NodeInfo(childB, root),
                new NodeInfo(subchildAA, childA), 
                new NodeInfo(subchildAB, childA), 
                new NodeInfo(subchildAAA, subchildAA)
            }, nodes);
        }

        [Test]
        public void GraphSerialization () {
            var root = new Node { Name = "root" };
            var childA = new Node { Name = "a" };
            var childB = new Node { Name = "b" };
            var subchildAA = new Node { Name = "a.a" };
            var subchildAB = new Node { Name = "a.b" };
            var subchildAAA = new Node { Name = "a.a.a" };

            root.Children.AddLast(childA);
            root.Children.AddLast(childB);
            childA.Children.AddLast(subchildAA);
            childA.Children.AddLast(subchildAB);
            subchildAA.Children.AddLast(subchildAAA);

            var writer = new GraphWriter();
            root.Serialize(writer);

            Assert.AreEqual(
                new string[] { "begin(root)", "+root", "+a", "+a.a", "+a.a.a", "-a.a.a", "-a.a", "+a.b", "-a.b", "-a", "+b", "-b", "-root", "end" },
                writer.Trace.ToArray()
            );
        }

        [Test]
        public void GraphXmlSerialization () {
            var root = new Node { Name = "root" };
            var childA = new Node { Name = "a" };
            var childB = new Node { Name = "b" };
            var subchildAA = new Node { Name = "a.a" };

            root.Children.AddLast(childA);
            root.Children.AddLast(childB);
            childA.Children.AddLast(subchildAA);

            var sb = new StringBuilder();
            using (var xwriter = XmlWriter.Create(sb, null)) {
                xwriter.WriteStartElement("test");
                var writer = new XmlGraphWriter(xwriter, new AssemblyTypeResolver(Assembly.GetExecutingAssembly()));
                root.Serialize(writer);
                xwriter.WriteEndElement();
            }

            var expected = 
                "<?xml version=\"1.0\" encoding=\"utf-16\"?>" +
                "<test><graph>" +
                "<node_instance key=\"1\"><node_instance key=\"2\"><node_instance key=\"3\" /></node_instance><node_instance key=\"4\" /></node_instance>" +
                "</graph><nodes>" +
                "<types>" +
                "<type id=\"0\" name=\"Squared.Game.Graph.Node\" />" +
                "</types>" +
                "<values>" +
                "<Node key=\"1\" typeId=\"0\"><Name>root</Name></Node>" +
                "<Node key=\"2\" typeId=\"0\"><Name>a</Name></Node>" +
                "<Node key=\"3\" typeId=\"0\"><Name>a.a</Name></Node>" +
                "<Node key=\"4\" typeId=\"0\"><Name>b</Name></Node>" +
                "</values>" +
                "</nodes></test>";

            Assert.AreEqual(
                expected,
                sb.ToString()
            );
        }
    }
}
