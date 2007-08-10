#include <core\core.hpp>
#include <script\script.hpp>
#include <base64\base64.h>

namespace script {

int table_map(lua_State * L) {
  int argc = lua_gettop(L);  

  if (argc < 2) {
    lua_pushstring(L, "table.map expected (fn, table1, ...)");
    lua_error(L);
    return 0;
  }
    
  Object fn(luabind::from_stack(L, 1));
  argc -= 1;
  Object nil;
  luabind::iterator end;
  luabind::iterator * iterators = new luabind::iterator[argc];
  Object result = luabind::newtable(L);

  for (int i = 0; i < argc; i++) {
    Object tbl(luabind::from_stack(L, i + 2));
    iterators[i] = luabind::iterator(tbl);
  }
  
  lua_pop(L, argc + 1);
  
  int j = 1;
  bool stop = false;
  while (!stop) {
    stop = true;
    fn.push(L);
    for (int i = 0; i < argc; i++) {
      if (iterators[i] != end) {
        Object item = *(iterators[i]);
        item.push(L);
        stop = false;
      } else {
        nil.push(L);
      }
    }
    
    if (stop)
      break;
    
    if (lua_pcall(L, argc, 1, 0) != 0) {
      delete[] iterators;
      lua_error(L);
      return 0;
    } else {
      Object item(luabind::from_stack(L, -1));
      result[j] = item;
    }
    for (int i = 0; i < argc; i++) {
      if (iterators[i] != end)
        ++iterators[i];
    }
    j++;
  }
  
  delete[] iterators;
  
  result.push(L);
  return 1;
}

int table_reduce(lua_State * L) {
  if (lua_gettop(L) != 2) {
    lua_pushstring(L, "table.reduce expected (fn, table)");
    lua_error(L);
    return 0;
  }
    
  Object fn(luabind::from_stack(L, 1));
  Object table(luabind::from_stack(L, 2));
  lua_pop(L, 2);

  luabind::iterator iter(table), end;
  Object result(*iter);
  ++iter;
  
  while (iter != end) {
    Object item(*iter);
    fn.push(L);
    result.push(L);
    item.push(L);
    if (lua_pcall(L, 2, 1, 0) != 0) {
      lua_error(L);
      return 0;
    } else {
      result = Object(luabind::from_stack(L, -1));
    }
    ++iter;
  }
  
  result.push(L);
  return 1;
}

int table_filter(lua_State * L) {
  if (lua_gettop(L) != 2) {
    lua_pushstring(L, "table.filter expected (fn, table)");
    lua_error(L);
    return 0;
  }
    
  Object fn(luabind::from_stack(L, 1));
  Object table(luabind::from_stack(L, 2));
  lua_pop(L, 2);

  luabind::iterator iter(table), end;
  Object result = luabind::newtable(L);
  int i = 1;
  
  while (iter != end) {
    Object item(*iter);
    Object call_result(fn(item));
    if (call_result && castObject<bool>(call_result)) {
      result[i] = item;
      i++;
    }
    ++iter;
  }
  
  result.push(L);
  return 1;
}

int table_apply(lua_State * L) {
  if (lua_gettop(L) != 2) {
    lua_pushstring(L, "table.apply expected (fn, table)");
    lua_error(L);
    return 0;
  }
    
  Object fn(luabind::from_stack(L, 1));
  Object table(luabind::from_stack(L, 2));
  lua_pop(L, 2);

  luabind::iterator iter(table), end;
  int i = 0;
  
  fn.push(L);
  while (iter != end) {
    Object item(*iter);
    item.push(L);
    i++;
    ++iter;
  }
  
  if (lua_pcall(L, i, 1, 0) != 0) {
    lua_error(L);
    return 0;
  }
  
  return 1;
}

void registerTableExtensions(shared_ptr<Context> context) {
  context->registerFunction("table.map", table_map);
  context->registerFunction("table.reduce", table_reduce);
  context->registerFunction("table.filter", table_filter);
  context->registerFunction("table.apply", table_apply);
}

}