using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;

namespace Squared.Game.Graph {
    public interface INode {
        IEnumerable<INode> GetChildren ();
    }

    public interface INodeWriter {
        void WriteNodeHeader (INode node);
        void WriteNodeFooter (INode node);
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

    public static class GraphExtensionMethods {
        public static IEnumerable<NodeInfo> TraverseDepthFirst (this INode root) {
            var list = new LinkedList<NodeInfo>();
            var result = new NodeInfo { Node = root, Parent = null };
            list.AddLast(result);

            while (list.Count > 0) {
                var current = list.First;
                result.Parent = current.Value.Node;
                foreach (var leaf in current.Value.Node.GetChildren()) {
                    result.Node = leaf;
                    list.AddBefore(current, result);
                }

                list.Remove(current);
                yield return current.Value;
            }
        }

        public static IEnumerable<NodeInfo> TraverseBreadthFirst (this INode root) {
            var list = new LinkedList<NodeInfo>();
            var result = new NodeInfo { Node = root, Parent = null };
            list.AddLast(result);

            while (list.Count > 0) {
                var current = list.First;
                result.Parent = current.Value.Node;
                foreach (var leaf in current.Value.Node.GetChildren()) {
                    result.Node = leaf;
                    list.AddLast(result);
                }

                list.Remove(current);
                yield return current.Value;
            }
        }

        public static void Serialize (this INode root, INodeWriter writer) {
            var stack = new Stack<INode>();

            foreach (var info in root.TraverseDepthFirst()) {
                while ((stack.Count > 0) && (stack.Peek() != info.Parent)) {
                    writer.WriteNodeFooter(stack.Pop());
                }

                writer.WriteNodeHeader(info.Node);
                stack.Push(info.Node);
            }

            while (stack.Count > 0)
                writer.WriteNodeFooter(stack.Pop());
        }
    }
}