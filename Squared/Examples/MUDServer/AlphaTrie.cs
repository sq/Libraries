using System;
using System.Collections.Generic;
using System.Collections;

namespace MUDServer {

    public class AlphaTrie<T> where T : class {
        public const int NodeCount = ('z' - 'a') + 1;

        AlphaTrieNode rootNode;

        private class AlphaTrieNode {
            public AlphaTrieNode[] Nodes = null;
            public KeyValueReference<string, T> Value = null;
            public int Branches = 0;

            public AlphaTrieNode () {
            }

            public AlphaTrieNode (string _key, T _value) {
                Value = new KeyValueReference<string, T>(_key, _value);
            }

            public AlphaTrieNode (KeyValueReference<string, T> KVR) {
                Value = KVR;
            }

            public static int CharToTrieNodeIndex (char C) {
                if (C < 'a' || C > 'z')
                    throw new InvalidOperationException("Attempted to convert an invalid character to a trie index.");
                else
                    return (C - 'a');
            }
        }

        public AlphaTrie () {
            rootNode = new AlphaTrieNode();
        }

        // Searches the trie for a node with the exact key provided and returns the value of that node.
        public KeyValueReference<string, T> FindByKeyExact (string key) {
            foreach (var KV in FindByKeyStart(key))
                if (KV.Key == key.ToLower())
                    return KV;
            return null;
        }

        // Searches the trie for a node starting with the given key. If multiple are found, null will be returned.
        public IEnumerable<KeyValueReference<string, T>> FindByKeyStart (string _key) {
            string key = _key.ToLower();
            AlphaTrieNode currentNode = rootNode;

            int level = -1;
            while (level < key.Length - 1) {
                level++;

                // Found the key exactly here, so return it and all of its underlings.
                if (currentNode.Value != null && currentNode.Value.Key == key)
                    foreach (var retKV in Traverse(currentNode))
                        yield return retKV;

                int ix;
                try {
                    ix = AlphaTrieNode.CharToTrieNodeIndex(key[level]);
                } catch (InvalidOperationException) {
                    yield break;
                }

                // Ran into a dead end.
                if (currentNode.Nodes == null || currentNode.Nodes[ix] == null) {
                    if (currentNode.Value == null)
                        yield break;
                    if (currentNode.Value.Key.StartsWith(key))
                        yield return currentNode.Value;
                    yield break;
                }

                currentNode = currentNode.Nodes[ix];
            }

            foreach (var retKV in Traverse(currentNode))
                yield return retKV;
        }

        // Inserts a new key-value pair into the trie.
        public void Insert (string key, T value) {
            if (key.Length == 0)
                throw new InvalidOperationException("Attempted to insert a blank key into the trie.");
            if (value == null)
                throw new InvalidOperationException("Attempted to insert a blank value into the trie.");

            Insert(rootNode, new KeyValueReference<string, T>(key.ToLower(), value), 0);
        }

        private void Insert (AlphaTrieNode node, KeyValueReference<string, T> insKV, int level) {

            if (node.Value != null && node.Value.Key == insKV.Key)
                throw new InvalidOperationException("Attempted to insert duplicate key into the trie.");

            if (node.Nodes == null && node.Value == null) {
                node.Value = insKV;
                return;
            }

            if (node.Nodes == null && node.Value != null) {
                node.Nodes = new AlphaTrieNode[NodeCount];
                // Only move the current Value if it isn't exactly there.
                if (node.Value.Key.Length > level) {
                    KeyValueReference<string, T> moveKV = node.Value;
                    node.Value = null;
                    int im = AlphaTrieNode.CharToTrieNodeIndex(moveKV.Key[level]);
                    node.Nodes[im] = new AlphaTrieNode(moveKV);
                    node.Branches++;
                }
                // If ours fits perfectly, we know we had to move the old one.
                if (insKV.Key.Length == level) {
                    node.Value = insKV;
                    return;
                }

                int ii = AlphaTrieNode.CharToTrieNodeIndex(insKV.Key[level]);
                if (node.Nodes[ii] == null) {
                    node.Nodes[ii] = new AlphaTrieNode(insKV);
                    node.Branches++;
                }
                else
                    Insert(node.Nodes[ii], insKV, level + 1);
                return;
            }

            if (node.Nodes != null) {
                // If we go exactly here, stick it in the Value.
                if (insKV.Key.Length == level) {
                    node.Value = insKV;
                    return;
                }
                // Otherwise, make new path or follow the old path.
                int ii = AlphaTrieNode.CharToTrieNodeIndex(insKV.Key[level]);
                if (node.Nodes[ii] == null) {
                    node.Nodes[ii] = new AlphaTrieNode(insKV);
                    node.Branches++;
                }
                else
                    Insert(node.Nodes[ii], insKV, level + 1);
            }

        }
        
        public bool Remove (string key) {
            return false;
            /*
            AlphaTrieNode parentNode, foundNode;
            KeyValueReference<string, T> kvr = FindByKeyExact(key, out parentNode, out foundNode);
            if (kvr == null)
                return false;

            for (int i = 0; i < parentNode.Nodes.Length; i++) {
                if (parentNode.Nodes[i] == foundNode) {
                    parentNode.Nodes[i] = null;
                    parentNode.Branches -= 1;
                    return true;
                }
            }

            return false;
            */
        }

        // Traverse the trie with an enumerator.
        public IEnumerable<KeyValueReference<string, T>> Traverse () {
            return Traverse(rootNode);
        }

        private IEnumerable<KeyValueReference<string, T>> Traverse (AlphaTrieNode startingNode) {
            if (startingNode.Value != null)
                yield return startingNode.Value;

            if (startingNode.Nodes != null) {
                for (int i = 0; i < NodeCount; i++) {
                    AlphaTrieNode nextNode = startingNode.Nodes[i];
                    if (nextNode != null)
                        foreach (var newNode in this.Traverse(nextNode))
                            yield return newNode;
                }
            }
        }

    }

    public class KeyValueReference<K, V> {
        K _Key;
        V _Value;

        public K Key { get { return _Key; } }
        public V Value { get { return _Value; } }

        public KeyValueReference (K __Key, V __Value) {
            _Key = __Key;
            _Value = __Value;
        }
    }
}
