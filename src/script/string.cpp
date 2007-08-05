#include <core\core.hpp>
#include <script\script.hpp>
#include <base64\base64.h>

namespace script {

int string_toBase64(lua_State * L) {
  int argc = lua_gettop(L);  

  if (argc < 1) {
    lua_pushstring(L, "string:toBase64 expected (self)");
    lua_error(L);
    return 0;
  }
    
  const char * input = lua_tostring(L, 1);
  
  lua_pushstring(L, base64::encode(input).c_str());
  return 1;
}

int string_fromBase64(lua_State * L) {
  int argc = lua_gettop(L);  
  
  if (argc < 1) {
    lua_pushstring(L, "string:fromBase64 expected (self)");
    lua_error(L);
    return 0;
  }

  const char * input = lua_tostring(L, 1);

  lua_pushstring(L, base64::decode(input).c_str());
  return 1;
}

int string_split(lua_State * L) {
  int argc = lua_gettop(L);  
  
  if (argc < 2) {
    lua_pushstring(L, "string:split expected (self, delimiter, [returnEmptyItems])");
    lua_error(L);
    return 0;
  }

  const char * input = lua_tostring(L, 1);
  const char * delimiter = lua_tostring(L, 2);
  bool returnEmptyItems = (argc < 3) || (lua_toboolean(L, 3));

  script::Object result = luabind::newtable(L);
  int i = 1;
  const char * pos = input;
  const char * next_pos = strstr(pos, delimiter);
  const char * end = input + lua_strlen(L, 1);
  if (next_pos) {
    std::string item;
    while (next_pos) {
      if ((next_pos > pos) || (returnEmptyItems)) {
        item = std::string(pos, next_pos - pos);
        result[i] = item.c_str();
        i += 1;
      }
      
      pos = next_pos + 1;
      next_pos = strstr(pos, delimiter);
    }
    if ((end > pos) || (returnEmptyItems)) {
      item = std::string(pos, end - pos);
      result[i] = item.c_str();
    }
  } else {
    result[i] = input;
  }
  
  result.push(L);
  return 1;
}

int string_startsWith(lua_State * L) {
  int argc = lua_gettop(L);  
  
  if (argc < 2) {
    lua_pushstring(L, "string:startsWith expected (self, what)");
    lua_error(L);
    return 0;
  }

  const char * input = lua_tostring(L, 1);
  const char * what = lua_tostring(L, 2);
  size_t input_len = lua_strlen(L, 1);
  size_t what_len = lua_strlen(L, 2);
  
  if (input_len < what_len) {
    lua_pushboolean(L, 0);
  } else {
    lua_pushboolean(L, (memcmp(input, what, what_len) == 0));
  }
  
  return 1;
}

int string_endsWith(lua_State * L) {
  int argc = lua_gettop(L);  
  
  if (argc < 2) {
    lua_pushstring(L, "string:endsWith expected (self, what)");
    lua_error(L);
    return 0;
  }

  const char * input = lua_tostring(L, 1);
  const char * what = lua_tostring(L, 2);
  size_t input_len = lua_strlen(L, 1);
  size_t what_len = lua_strlen(L, 2);
  
  if (input_len < what_len) {
    lua_pushboolean(L, 0);
  } else {
    lua_pushboolean(L, (memcmp(input + (input_len - what_len), what, what_len) == 0));
  }
  
  return 1;
}

int string_compare(lua_State * L) {
  int argc = lua_gettop(L);  
  
  if (argc < 2) {
    lua_pushstring(L, "string:compare expected (self, what)");
    lua_error(L);
    return 0;
  }

  const char * input = lua_tostring(L, 1);
  const char * what = lua_tostring(L, 2);
  
  lua_pushinteger(L, strcmp(input, what));
  
  return 1;
}

void registerStringExtensions(shared_ptr<Context> context) {
  context->registerFunction("string.toBase64", string_toBase64);
  context->registerFunction("string.fromBase64", string_fromBase64);
  context->registerFunction("string.split", string_split);
  context->registerFunction("string.startsWith", string_startsWith);
  context->registerFunction("string.endsWith", string_endsWith);
  context->registerFunction("string.compare", string_compare);
}

}