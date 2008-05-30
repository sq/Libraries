using System;
using System.Collections.Generic;
using System.Collections;

namespace MUDServer {

    public class AlphaTrie<T> where T : class {
        public const int NodeCount = 26;

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
            KeyValueReference<string, T> KVRTemp = FindByKeyStart(key);
            if (KVRTemp == null || KVRTemp.Key != key.ToLower())
                return null;
            else
                return KVRTemp;

        }

        // Searches the trie for a node starting with the given key. If multiple are found, null will be returned.
        public KeyValueReference<string, T> FindByKeyStart (string _key) {
            string key = _key.ToLower();
            AlphaTrieNode currentNode = rootNode;

            int level = -1;
            while (level < key.Length - 1) {
                level++;

                // Found the key exactly here.
                if (currentNode.Value != null && currentNode.Value.Key == key)
                    return currentNode.Value;

                int ix;
                try {
                    ix = AlphaTrieNode.CharToTrieNodeIndex(key[level]);
                } catch (InvalidOperationException) {
                    return null;
                }

                // Ran into a dead end.
                if (currentNode.Nodes == null || currentNode.Nodes[ix] == null) {
                    if (currentNode.Value == null)
                        return null;
                    if (currentNode.Value.Key.StartsWith(key))
                        return currentNode.Value;
                    return null;
                }

                currentNode = currentNode.Nodes[ix];
            }

            while (true) {
                if (currentNode.Branches > 1)
                    return null;

                if (currentNode.Value != null && currentNode.Branches == 0)
                    return currentNode.Value;

                // Indeterminate. There's one value here, but there's another longer value later on.
                if (currentNode.Value != null && currentNode.Branches == 1)
                    return null;

                // Guaranteed 1 branch.
                for (int i = 0; i < NodeCount; i++) {
                    if (currentNode.Nodes[i] != null) {
                        currentNode = currentNode.Nodes[i];
                        break;
                    }
                }
            }

        }

        // Inserts a new key-value pair into the trie.
        public void Insert (string key, T value) {
            if (key.Length == 0)
                throw new InvalidOperationException("Attempted to insert a blank key into the trie.");

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

        // Removes a key-value pair from the trie by its key.
        public void Remove (string key) {
            throw new NotImplementedException();
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
