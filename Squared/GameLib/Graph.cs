using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using Squared.Game.Serialization;

namespace Squared.Game.Graph {
    public interface INode {
        IEnumerable<INode> GetChildren ();
        void AddChild (INode child);
    }

    public interface IGraphWriter {
        void BeginWrite (INode root);
        void WriteNodeHeader (INode node);
        void WriteNodeFooter (INode node);
        void EndWrite ();
    }

    public struct NodeInfo {
        public INode Node;
        public INode Parent;

        public NodeInfo (INode node, INode parent) {
            Node = node;
            Parent = parent;
        }

        public NodeInfo (INode node) {
            Node = node;
            Parent = null;
        }

        public override string ToString () {
            return String.Format("NodeInfo({0}, {1})", Node, Parent);
        }
    }

    public class XmlGraphReader {
        struct StackEntry {
            public XmlNode Child;
            public INode Parent;
        }

        private XmlReader _Reader;
        private ITypeResolver _TypeResolver;

        public XmlGraphReader (XmlReader reader)
            : this(reader, SerializationExtensions.DefaultTypeResolver) {
        }

        public XmlGraphReader (XmlReader reader, ITypeResolver typeResolver) {
            _Reader = reader;
            _TypeResolver = typeResolver;
        }

        private XmlDocument ReadXmlFragment (string elementName) {
            if (_Reader.IsEmptyElement)
                return null;

            var result = new XmlDocument();
            if (!_Reader.Read())
                return null;

            while (_Reader.NodeType != XmlNodeType.EndElement || _Reader.Name != elementName) {
                var node = result.ReadNode(_Reader);

                if (node != null)
                    result.AppendChild(node);
                else
                    throw new ArgumentException("Invalid XML fragment");
            }

            return result;
        }

        private INode ResolveNode (XmlNode node, StringValueDictionary<INode> nodes) {
            return nodes[node.Attributes.GetNamedItem("key").Value];
        }

        private INode BuildGraph (XmlDocument graph, StringValueDictionary<INode> nodes) {
            INode root = null;
            var xmlStack = new LinkedList<StackEntry>();
            foreach (var child in graph.ChildNodes.Cast<XmlNode>()) {
                if (child.NodeType == XmlNodeType.Element) {
                    xmlStack.AddLast(new StackEntry { Child = child, Parent = null });
                    break;
                }
            }

            while (xmlStack.Count > 0) {
                var current = xmlStack.First.Value;
                xmlStack.RemoveFirst();

                var node = ResolveNode(current.Child, nodes);
                if (current.Parent == null)
                    root = node;
                else
                    current.Parent.AddChild(node);

                foreach (var child in current.Child.ChildNodes.Cast<XmlNode>()) {
                    if (child.NodeType == XmlNodeType.Element)
                        xmlStack.AddLast(new StackEntry { Child = child, Parent = node });
                }
            }

            return root;
        }

        public INode Read () {
            _Reader.ReadToDescendant("graph");
            var graph = ReadXmlFragment("graph");

            _Reader.ReadToFollowing("nodes");
            var nodes = _Reader.ReadDictionary<INode>(_TypeResolver);

            return BuildGraph(graph, nodes);
        }
    }

    public class XmlGraphWriter : IGraphWriter {
        private XmlWriter _Writer;
        private ITypeResolver _TypeResolver;
        private int _NextNodeID;
        private Dictionary<INode, string> _NodeIDs;
        private StringValueDictionary<INode> _Nodes;

        public XmlGraphWriter (XmlWriter writer) 
            : this (writer, SerializationExtensions.DefaultTypeResolver) {
        }

        public XmlGraphWriter (XmlWriter writer, ITypeResolver typeResolver) {
            _Writer = writer;
            _TypeResolver = typeResolver;
        }

        public void BeginWrite (INode root) {
            _NextNodeID = 1;
            _Nodes = new StringValueDictionary<INode>();
            _NodeIDs = new Dictionary<INode, string>();
            _Writer.WriteStartElement("graph");
        }

        internal string GetNodeID (INode node) {
            string id = null;

            if (_NodeIDs.TryGetValue(node, out id))
                return id;

            if (node is INamedObject)
                id = (node as INamedObject).Name;

            if ((id != null) && _Nodes.ContainsKey(id)) {
                if (_Nodes[id] == node)
                    return id;
            } 

            if (id == null)
                id = _NextNodeID.ToString();

            _NextNodeID += 1;
            _NodeIDs[node] = id;
            _Nodes[id] = node;
            return id;
        }

        public void WriteNodeHeader (INode node) {
            string id = GetNodeID(node);

            _Writer.WriteStartElement("node");
            _Writer.WriteAttributeString("key", id.ToString());
        }

        public void WriteNodeFooter (INode node) {
            _Writer.WriteEndElement();
        }

        public void EndWrite () {
            _Writer.WriteEndElement();
            _Writer.WriteStartElement("nodes");
            _Writer.WriteDictionary(_Nodes, _TypeResolver);
            _Writer.WriteEndElement();
            _Writer.Flush();
        }
    }

    public static class GraphExtensionMethods {
        public static IEnumerable<NodeInfo> TraverseDepthFirst (this INode root) {
            var visitedNodes = new Dictionary<INode, bool>();
            var list = new LinkedList<NodeInfo>();
            var result = new NodeInfo { Node = root, Parent = null };
            list.AddLast(result);

            while (list.Count > 0) {
                var current = list.First;
                result.Parent = current.Value.Node;
                if (!visitedNodes.ContainsKey(result.Parent)) {
                    visitedNodes.Add(result.Parent, true);
                    foreach (var leaf in current.Value.Node.GetChildren()) {
                        result.Node = leaf;
                        list.AddBefore(current, result);
                    }
                }

                list.Remove(current);
                yield return current.Value;
            }
        }

        public static IEnumerable<NodeInfo> TraverseBreadthFirst (this INode root) {
            var visitedNodes = new Dictionary<INode, bool>();
            var list = new LinkedList<NodeInfo>();
            var result = new NodeInfo { Node = root, Parent = null };
            list.AddLast(result);

            while (list.Count > 0) {
                var current = list.First;
                result.Parent = current.Value.Node;
                if (!visitedNodes.ContainsKey(result.Parent)) {
                    visitedNodes.Add(result.Parent, true);
                    foreach (var leaf in current.Value.Node.GetChildren()) {
                        result.Node = leaf;
                        list.AddLast(result);
                    }
                }

                list.Remove(current);
                yield return current.Value;
            }
        }

        public static void Serialize (this INode root, IGraphWriter writer) {
            var stack = new Stack<INode>();

            writer.BeginWrite(root);

            foreach (var info in root.TraverseDepthFirst()) {
                while ((stack.Count > 0) && (stack.Peek() != info.Parent)) {
                    writer.WriteNodeFooter(stack.Pop());
                }

                writer.WriteNodeHeader(info.Node);
                stack.Push(info.Node);
            }

            while (stack.Count > 0)
                writer.WriteNodeFooter(stack.Pop());

            writer.EndWrite();
        }

        public static INode ReadGraph (this XmlReader reader) {
            return ReadGraph(reader, SerializationExtensions.DefaultTypeResolver);
        }

        public static INode ReadGraph (this XmlReader reader, ITypeResolver typeResolver) {
            var greader = new XmlGraphReader(reader, typeResolver);
            return greader.Read();
        }
    }
}