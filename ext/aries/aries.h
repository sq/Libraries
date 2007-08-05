/**
 * Aries - The simplest markup language ever made.
 * Coded by Andy Friesen.
 *
 * Pseudo-BNF because I don't recall how BNF works exactly:
 *
 *      Node :== string | (nodeName OneOrMoreNodes)
 *
 * Look like lisp?  Guess what inspired it.
 * Extremely easy to parse, extremely easy to read.
 * Much less verbose than XML as well, though the lack of an analogue to
 * attributes makes automated conversion more or less impossible.
 *
 * Text is brought into the markup literally, with only leading and trailing
 * whitespace removed.  (of course, you could always cut it up into tokens
 * yourself)  Thus, if exact whitespace is important to you, just enclose the
 * datum in quotes. (or anything that's not parenthesis, really)
 *
 * Gay little example from an alternate dimension where HTML has been turned
 * into an Aries-based markup:
 *
 * (html
 *   (head
 *     (title tSBUPoNiP)
 *   )
 *   (body
 *     (font (size 3)(bold (underline
 *       News
 *       (-- Note that this markup has no comment system.  Thus, we agree to
 *           ignore all nodes that are named "--".  Voila.  Comments. ^_^
 *
 *           Note that comments can nest, due to the fact that they're not
 *           really comments at all.  I do have misgivings about forcing
 *           comments to balance parenths,  though.  Maybe comments should be
 *           added to the spec.
 *       )
 *     )))
 *     (p
 *       (bold (underline October 20))
 *       (br) Minor update.
 *       (ul
 *         (li v2 8 bit fonts added.  v2.7 will (italics always) assume they
 *             use v2's default palette; if they do not, you will have to
 *             convert them to something else first. )
 *         (li major optimization concerning WinMapEd's image import feature.
 *             The speed increase should be considerable. )
 *         (li v2.7 now uses (a (href http://www.python.org) Python 2.2).  This
 *             has a number of exceptional new language features.  I highly
 *             reccomend that you check the official site to see what's new. )
 *       )
 *   )
 * )
 */

/*
 * Legal garbage:
 *
 * Copyright (c) 2003 Andy Friesen
 *
 * This software is provided 'as-is', without any express or implied warranty.
 * In no event will the authors be held liable for any damages arising from
 * the use of this software.
 *
 * Permission is granted to anyone to use this software for any purpose,
 * including commercial applications, and to alter it and redistribute it
 * freely, subject to the following restrictions:
 *
 *     1. The origin of this software must not be misrepresented; you must not
 *        claim that you wrote the original software. If you use this software
 *        in a product, an acknowledgment in the product documentation would be
 *        appreciated but is not required.
 *
 *     2. Altered source versions must be plainly marked as such, and must not
 *        be misrepresented as being the original software.
 *
 *     3. This notice may not be removed or altered from any source distribution.
 *
 */

#pragma once

#include <string>
#include <vector>

namespace aries {
    typedef std::vector<struct Node*> NodeList;
    typedef std::vector<struct DataNode*> DataNodeList;

    /**
     * Base class for all document nodes.
     * (there are only two anyway)
     */
    struct Node {
        virtual ~Node(){}

        /**
         * Basically required so that RTTI doesn't need to be enabled.  Returns true if the node
         * is a StringNode, false if it is a DataNode
         */
        virtual bool isString() const = 0;

        /**
         * Returns a string representation of the node.
         */
        virtual std::string toString() const = 0;

        /**
         * Creates a clone of the node, and returns it.  The caller assumes ownership of the new node.
         */
        virtual Node* clone() const = 0;

        /**
         * Writes the node to the stream.
         * The overloaded << operator calls this.
         */
        virtual std::ostream& write(std::ostream& stream) const = 0;

        /**
         * Reads a document from the stream, and returns a node containing the document.
         * Note that, if you have a single root in your document, a la XML, that it will
         * be the only child of the node returned.
         *
         * operator >> calls this.
         */
        static struct DataNode* readDocument(std::istream& stream);
    };

    struct StringNode : Node {
        StringNode(const std::string& str);
        virtual bool isString() const;
        virtual std::string toString() const;
        virtual StringNode* clone() const;

        virtual std::ostream& write(std::ostream& stream) const;

    private:
        std::string _str;
    };

    struct DataNode : Node {
        DataNode(const std::string& name);
        virtual ~DataNode();
        virtual bool isString() const;
        virtual std::string toString() const;
        virtual DataNode* clone() const;
        std::string getString() const;                          ///< Returns the string data of the first string node, or "" if there isn't one.
        NodeList& getChildren();                                ///< Returns a list of the children
        const NodeList& getChildren() const;                    ///< ditto

        DataNodeList getChildren(const std::string& name) const; ///< Returns all data nodes with the specified node name
        DataNode* getChild(const std::string& name) const;      ///< Returns the first data node with the specified name.  Throws a std::runtime_error if the child does not exist.
        DataNode* getChild(const std::string& name, DataNode* defaultValue) const;
                                                                ///< Returns the first data node with the specified name, or defaultValue if the node cannot be found.

        bool hasChild(const std::string& name) const;           ///< Returns true if the node has a child by this name.

        std::string getName() const;                            ///< Returns the name of the node

        DataNode* addChild(const std::string& str);             ///< Creates a StringNode and adds it as a new child
        DataNode* addChild(int i);
        //DataNode* addChild(double d);
        DataNode* addChild(const Node& n);                      ///< Adds a copy of the node as a child
        DataNode* addChild(Node* n);                            ///< Adds the node as a child.  The node assumes ownership of the pointer. (so don't delete it yourself)

        virtual std::ostream& write(std::ostream& stream) const;

    private:
        void write(std::ostream& stream, unsigned int indentLevel = 0) const;

        NodeList _children;
        std::string _name;
    };

    /**
     * since VC7 does not consider new DataNode(...)->addChild(...) to be valid C++,
     * You'll have to either put parenths around it, or use this.
     */
    DataNode* newNode(const std::string& str);
}

std::ostream& operator << (std::ostream& stream, aries::Node* node);   /// Converts the node (and its children) to a human readable format, and dumps it to the stream.
std::istream& operator >> (std::istream& stream, aries::DataNode*& node);  /// Reads the document in from the stream, and returns the root node.  The caller assumes ownership of the pointer.
