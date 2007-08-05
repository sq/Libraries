#include <core\core.hpp>
#include <script\script.hpp>
#include <aries\aries.h>

using script::Object;
using luabind::iterator;
using aries::DataNode;
using aries::StringNode;
using aries::Node;
using aries::NodeList;

namespace script {

static void unpack(DataNode * from, Object & to) {
  NodeList & nodes = from->getChildren();
  
  to.push(to.interpreter());
  int n = (int)lua_objlen(to.interpreter(), -1) + 1;
  lua_pop(to.interpreter(), 1);
  
  for (unsigned i = 0; i < nodes.size(); i++) {
    Node * node = nodes[i];
    
    if (node->isString()) {
      StringNode * snode = (StringNode *)node;
      
      to[n] = snode->toString();
      n += 1;
    } else {
      DataNode * dnode = (DataNode *)node;
      std::string name = dnode->getName();
      Object t = to[name];

      if (getObjectType(t) != LUA_TTABLE) {
        Object pv = t;
        t = luabind::newtable(to.interpreter());
        if (pv)
          t[1] = pv;
        to[name] = t;
      }

      unpack(dnode, t);

      t.push(to.interpreter());
      int tn = (int)lua_objlen(to.interpreter(), -1);
      lua_pop(to.interpreter(), 1);
      if (tn == 1) {
        Object v = t[1];
        to[name] = v;
      }
    }
  }
}

int aries_load(lua_State * L) {
  int argc = lua_gettop(L);  

  if (argc < 1) {
    lua_pushstring(L, "aries.load expected (string)");
    lua_error(L);
    return 0;
  }
  
  try {    
    const char * input = lua_tostring(L, 1);
    std::stringstream buffer(input);
    script::Object result = luabind::newtable(L);
    
    shared_ptr<DataNode> tree;
    DataNode * root;
    {
      DataNode * _tree = 0;
      buffer >> _tree;
      
      if (!_tree || _tree->getChildren().size() == 0 || _tree->getChildren()[0]->isString())
        throw std::exception((_tree) ? "no root node" : "load failed");
      
      tree = shared_ptr<DataNode>(_tree);
      root = (DataNode *)(tree->getChildren()[0]);
    }
    
    unpack(root, result);
    
    result.push(L);
    lua_pushstring(L, root->getName().c_str());
    
    return 2;
  } catch (std::runtime_error ex) {
    lua_pushstring(L, ex.what());
    lua_error(L);
    return 0;
  } catch (std::exception ex) {
    lua_pushstring(L, ex.what());
    lua_error(L);
    return 0;
  }
}

static void pack(Object & from, DataNode * to) {
  DataNode * writeTo = to;
  for (iterator iter(from), end; iter != end; ++iter) {
    Object item = *iter;
    Object key = iter.key();
    
    switch (getObjectType(key)) {
      case LUA_TSTRING: {
        key.push(from.interpreter());
        const char * keytext = lua_tostring(from.interpreter(), -1);
        std::string keystr(keytext);
        lua_pop(from.interpreter(), 1);
        writeTo = aries::newNode(keystr);
        to->addChild(writeTo);
      } break;
      
      case LUA_TNUMBER: {
        writeTo = to;
      } break;
      
      default: {
        throw std::exception("aries only supports strings as keys");
      } break;
    }
    
    switch (getObjectType(item)) {
      case LUA_TTABLE: {
        pack(item, writeTo);
      } break;
      
      case LUA_TBOOLEAN: {
        item.push(from.interpreter());
        std::string value(lua_toboolean(from.interpreter(), -1) ? "true" : "false");
        lua_pop(from.interpreter(), 1);
        writeTo->addChild(value);
      } break;
      
      case LUA_TNUMBER:
      case LUA_TSTRING: {
        item.push(from.interpreter());
        const char * text = lua_tostring(from.interpreter(), -1);
        std::string value(text);
        lua_pop(from.interpreter(), 1);
        writeTo->addChild(value);
      } break;
      
      default: {
        throw std::exception("aries only supports storing strings, tables, booleans, and numbers");
      } break;
    }
    
    writeTo = to;
  }
}

int aries_save(lua_State * L) {
  int argc = lua_gettop(L);  
  
  if (argc < 2) {
    lua_pushstring(L, "aries.save expected (table, name)");
    lua_error(L);
    return 0;
  }
  
  try {
    Object input(luabind::from_stack(L, 1));
    const char * name = lua_tostring(L, 2);
    std::stringstream buffer;
    
    DataNode root(name);
    pack(input, &root);
    
    buffer << &root;
    
    lua_pushstring(L, buffer.str().c_str());

    return 1;
  } catch (std::runtime_error ex) {
    lua_pushstring(L, ex.what());
    lua_error(L);
    return 0;
  } catch (std::exception ex) {
    lua_pushstring(L, ex.what());
    lua_error(L);
    return 0;
  }
}

void registerAriesExtensions(shared_ptr<Context> context) {
  context->setGlobal("aries", luabind::newtable(context->getContext()));
  context->registerFunction("aries.load", aries_load);
  context->registerFunction("aries.save", aries_save);
}

}