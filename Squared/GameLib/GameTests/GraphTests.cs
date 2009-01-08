using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using System.Xml;
using Squared.Game.Graph;

namespace Squared.Game.Graph {
    public class Node : INode {
        public LinkedList<INode> Children = new LinkedList<INode>();
        public string Name;

        IEnumerable<INode> INode.GetChildren () {
            return Children;
        }

        public override string ToString () {
            return Name;
        }
    }

    public class NodeWriter : INodeWriter {
        public List<string> Trace = new List<string>();

        public void WriteNodeHeader (INode node) {
            Trace.Add("+" + (node as Node).Name);
        }

        public void WriteNodeFooter (INode node) {
            Trace.Add("-" + (node as Node).Name);
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

            var writer = new NodeWriter();
            root.Serialize(writer);

            Assert.AreEqual(
                new string[] { "+root", "+a", "+a.a", "+a.a.a", "-a.a.a", "-a.a", "+a.b", "-a.b", "-a", "+b", "-b", "-root" },
                writer.Trace.ToArray()
            );
        }
    }
}
