#include <iostream>
#include <fstream>
#include <sstream>
#include <stack>
#include <string>
#include <cassert>
#include <stdexcept>

#include "aries.h"

// New as of 0.60: quoted strings behave like C string literals.
// (except using single quotes instead of double)
// Backslash is interpreted like in C.

namespace aries {
    // Helper functions for the parser

	// These are defined as constants because they look too similar if
	// used in the code directly.  It confuses [andy's] feeble mind.
	const char _singleQuote = '\'';
	const char _backSlash = '\\';

    bool isWhiteSpace(char c) {
        return c == ' ' || c == '\t' || c == '\n' || c == '\r';
    }

    std::string readIdentifier(std::istream& stream) {
        std::stringstream ss;

        for (;;) {
            char c = stream.peek();
			if (stream.fail()) {
				throw std::runtime_error("Error reading document.");
			}

            if (isWhiteSpace(c)) {
                break;
            }

            stream.get();
            ss << c;
        }

        return ss.str();
    }

    void eatWhiteSpace(std::istream& stream) {
        for (;;) {
            char c = stream.peek();
            if (isWhiteSpace(c) || !stream.good()) {
                break;
            }
            stream.get();
        }
    }

    // Removes all leading and trailing whitespace from the string.
    std::string stripString(const std::string& str) {
        unsigned int start = 0;
        unsigned int end = str.length();

        while (start < str.length() && isWhiteSpace(str[start]))
			start++;
        while (end > 0 && isWhiteSpace(str[end - 1]))
			end--;
        if (start >= end) {
            return "";
        } else {
            return str.substr(start, end - start);
        }
    }

    DataNode* Node::readDocument(std::istream& stream) {
        // I don't want to recurse for some reason, so I keep the context in an
		// explicit stack.
        std::stack<DataNode*> docStack;
        DataNode* rootNode = new DataNode("root");
        docStack.push(rootNode);

        /*
         * Read a character.
         *   If it's not an opening parenth, then grab characters until we get
		 *   to a parenth, and pack it into a single StringNode.
         *   If it is, then grab characters up until a space, then create a new
		 *   DataNode, and parse its children.
         *   If it is a closing parenth, then the node is complete.  We resume
		 *   parsing its parent.
         */

        for (;;) {
			assert(docStack.size() >= 1);
            char c = stream.get();
			if (stream.eof()) {
				break;
			} else if (stream.fail()) {
				throw std::runtime_error("Error reading document.");
			} else if (isWhiteSpace(c)) {
                continue;
            } else if (c == '(') {
                std::string nodeName = stripString(readIdentifier(stream));
                DataNode* newNode = new DataNode(nodeName);
                docStack.top()->addChild(newNode);
                docStack.push(newNode);

            } else if (c == ')') {
                // the root node is 1, and you may not actually terminate that
				// node, as it is implicit, and not part of the document
				// itself.
                if (docStack.size() < 2) {
                    throw std::runtime_error("Too many closing parentheses encountered.");
                }

                docStack.pop();

            } else if (c == _singleQuote) {
                std::stringstream ss;
                for (;;) {
                    c = stream.get();
                    if (stream.fail()) {
                        throw std::runtime_error("Unterminated string literal data.");
                    }
                    if (c == _singleQuote) {
                        break;
                    } else if (c != _backSlash) {
                        ss << c;
                    } else {
                        c = stream.get();
                        if (stream.fail()) {
                            throw std::runtime_error("Unterminated string literal data (unterminated escape sequence too!)");
                        }
                        if (c == 'n') {
                            ss << '\n';
                        } else if (c == 't') {
                            ss << '\t';
                        } else {
                            ss << c;
                        }
                    }
                }

                docStack.top()->addChild(ss.str());

            } else {
                // The way string literals used to be encoded.
                std::stringstream ss;
                while (c != '(' && c != ')' && stream.good()) {
                    ss << c;
                    c = stream.get();
					if (stream.fail()) {
						throw std::runtime_error("Unterminated element.");
					}
                }
                stream.unget();

                std::string s = stripString(ss.str());

                if (s.length()) {
                    docStack.top()->addChild(s);
                }
            }
        }

        return rootNode;
    }

    //-------------------------------------------------------------------------

    StringNode::StringNode(const std::string& str)
        : _str(str)
    {}

    bool StringNode::isString() const {
        return true;
    }

    std::string StringNode::toString() const {
        return _str;
    }

    StringNode* StringNode::clone() const {
        return new StringNode(_str);
    }

    std::ostream& StringNode::write(std::ostream& stream) const {
        stream << _singleQuote;
        for (std::string::const_iterator c = _str.begin(); c != _str.end(); c++) {
            if (*c == _singleQuote) {
                stream << _backSlash << _singleQuote;
            } else if (*c == _backSlash) {
                stream << _backSlash << _backSlash;
            } else {
                stream << *c;
            }
        }
        stream << _singleQuote;
        return stream;
    }

    //-------------------------------------------------------------------------

    DataNode::DataNode(const std::string& name)
        : _name(name)
    {}

    DataNode::~DataNode() {
        for (unsigned int i = 0; i < _children.size(); i++) {
            delete _children[i];
        }
    }

    bool DataNode::isString() const {
        return false;
    }

    std::string DataNode::toString() const {
        std::stringstream ss;
        write(ss, 0);
        return ss.str();
    }

    DataNode* DataNode::clone() const {
        DataNode* newNode = new DataNode(_name);

        for (unsigned int i = 0; i < _children.size(); i++) {
            newNode->addChild(*_children[i]);
        }

        return newNode;
    }

    std::string DataNode::getString() const {
        for (unsigned int i = 0; i < _children.size(); i++) {
            if (_children[i]->isString()) {
                return _children[i]->toString();
            }
        }
        return "";
    }

    const NodeList& DataNode::getChildren() const {
        return _children;
    }

    NodeList& DataNode::getChildren() {
        return _children;
    }

    DataNodeList DataNode::getChildren(const std::string& name) const {
        DataNodeList list;

        for (NodeList::const_iterator i = _children.begin(); i != _children.end(); i++) {
            Node* n = *i;

            if (!n->isString()) {
                DataNode* dataNode = static_cast<DataNode*>(n);
                if (dataNode->getName() == name) {
                    list.push_back(dataNode);
                }
            }
        }

        return list;
    }

    DataNode* DataNode::getChild(const std::string& name) const {
        DataNode* n = getChild(name, 0);
        if (!n) {
            throw std::runtime_error(std::string("Unable to find node ") + name);
        } else {
            return n;
        }
    }

    DataNode* DataNode::getChild(const std::string& name, DataNode* defaultValue) const {
        for (NodeList::const_iterator i = _children.begin(); i != _children.end(); i++) {
            Node* n = *i;
            if (!n->isString()) {
                DataNode* dataNode = static_cast<DataNode*>(n);
                if (dataNode->getName() == name) {
                    return dataNode;
                }
            }
        }

        return defaultValue;
    }

    bool DataNode::hasChild(const std::string& name) const {
        return getChild(name, 0) != 0;
    }

    std::string DataNode::getName() const {
        return _name;
    }

    DataNode* DataNode::addChild(const std::string& str) {
        return addChild(new StringNode(str));
    }

    DataNode* DataNode::addChild(int n) {
        std::stringstream ss;
        ss << n;
        return addChild(ss.str());
    }

    DataNode* DataNode::addChild(Node* n) {
        _children.push_back(n);
        return this;
    }

    DataNode* DataNode::addChild(const Node& n) {
        _children.push_back(n.clone());
        return this;
    }

    std::ostream& DataNode::write(std::ostream& stream) const {
        write(stream, 0);
        return stream;
    }

    void DataNode::write(std::ostream& stream, unsigned int indentLevel) const {
        indentLevel += 1;

        stream << "(" << _name << " ";

        if (_children.empty()) {
            // empty nodes are one-liners
            stream << ")";

        } else if (_children.size() == 1 && _children[0]->isString()) {
            // So are nodes that contain exactly one string child.
            _children[0]->write(stream);
            stream << ")";

        } else {
            for (unsigned int i = 0; i < _children.size(); i++) {
                const Node* const child = _children[i];

                stream << "\n";
                stream << std::string(indentLevel, '\t');

                if (child->isString()) {
                    child->write(stream);
                } else {
                    static_cast<DataNode const*>(child)->write(stream, indentLevel);
                }
            }
            stream << "\n";
            stream << std::string(indentLevel - 1, '\t');
            stream << ")";
        }
    }

    DataNode* newNode(const std::string& str) {
        return new DataNode(str);
    }
}

std::ostream& operator << (std::ostream& stream, aries::Node* node) {
    node->write(stream);
    return stream;
}

std::istream& operator >> (std::istream& stream, aries::DataNode*& node) {
    node = aries::Node::readDocument(stream);
    return stream;
}
