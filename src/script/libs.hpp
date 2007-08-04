#ifdef _DEBUG
  #pragma comment(lib, "lua-d.lib")
#else
  #pragma comment(lib, "lua.lib")
#endif

extern "C" {
  #include <lua\lua.h>
  #include <lua\lualib.h>
  #include <lua\lauxlib.h>
}

#ifdef _DEBUG
  #pragma comment(lib, "luabind-d.lib")
#else
  #pragma comment(lib, "luabind.lib")
#endif

#pragma warning(disable: 4996)

#include <luabind\luabind.hpp>
#include <luabind\operator.hpp>
#include <luabind\object.hpp>
#include <luabind\out_value_policy.hpp>
#include <luabind\iterator_policy.hpp>
#include <luabind\return_reference_to_policy.hpp>
#include <luabind\adopt_policy.hpp>