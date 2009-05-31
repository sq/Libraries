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
using System.IO;
using System.Collections;

namespace Squared.Game.Graph {
    public class Node : INode, INamedObject {
        [XmlIgnore]
        public LinkedList<INode> Children = new LinkedList<INode>();
        public string Name { get; set; }

        void INode.AddChild (INode child) {
            Children.AddLast(child);
        }

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
        public const string GraphXML = "<?xml version=\"1.0\" encoding=\"utf-16\"?>" +
            "<test><graph>" +
            "<node key=\"root\"><node key=\"a\"><node key=\"a.a\" /></node><node key=\"b\" /></node>" +
            "</graph><nodes>" +
            "<types>" +
            "<type id=\"0\" name=\"Squared.Game.Graph.Node\" />" +
            "</types>" +
            "<values>" +
            "<Node typeId=\"0\"><Name>root</Name></Node>" +
            "<Node typeId=\"0\"><Name>a</Name></Node>" +
            "<Node typeId=\"0\"><Name>a.a</Name></Node>" +
            "<Node typeId=\"0\"><Name>b</Name></Node>" +
            "</values>" +
            "</nodes></test>";

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
        public void CycleTests () {
            var root = new Node { Name = "root" };
            var childA = new Node { Name = "a" };
            var childB = new Node { Name = "b" };
            var childC = new Node { Name = "c" };
            var childD = new Node { Name = "d" };

            root.Children.AddLast(childA);
            root.Children.AddLast(childB);
            root.Children.AddLast(childC);
            root.Children.AddLast(childD);

            foreach (var i in root.Children)
                foreach (var j in root.Children)
                    if (j != i)
                        i.AddChild(j);

            var depth = root.TraverseDepthFirst().ToArray();
            var breadth = root.TraverseBreadthFirst().ToArray();
            Assert.AreEqual(depth.Length, breadth.Length);
            var depthHash = new HashSet<NodeInfo>(depth);
            var breadthHash = new HashSet<NodeInfo>(breadth);
            depthHash.UnionWith(breadthHash);
            Assert.AreEqual(depthHash.Count, depth.Length);

            var writer = new GraphWriter();
            root.Serialize(writer);

            Assert.AreEqual(
                new string[] { 
                    "begin(root)", "+root", 
                    "+a", "+b", "+a", "-a", 
                    "+c", "+a", "-a", "+b", 
                    "-b", "+d", "+a", "-a", 
                    "+b", "-b", "+c", "-c", 
                    "-d", "-c", "+d", "-d", 
                    "-b", "+c", "-c", "+d", 
                    "-d", "-a", "+b", "-b", 
                    "+c", "-c", "+d", "-d", 
                    "-root", "end" 
                },
                writer.Trace.ToArray()
            );
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

            Assert.AreEqual(
                GraphXML,
                sb.ToString()
            );
        }

        [Test]
        public void GraphXmlDeserialization () {
            INode root;

            using (var xreader = XmlReader.Create(new StringReader(GraphXML))) {
                xreader.ReadToDescendant("test");
                root = xreader.ReadGraph(new AssemblyTypeResolver(Assembly.GetExecutingAssembly()));
            }

            var nodes = (from ni in root.TraverseDepthFirst() select ni.Node).ToArray();
            var nodeNames = (from node in nodes select (node as Node).Name).ToArray();

            Assert.AreEqual(new string[] { "root", "a", "a.a", "b" }, nodeNames);
        }
    }
}
