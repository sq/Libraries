#ifdef _DEBUG
  #pragma comment(lib, "lua-d.msvc8.lib")
#else
  #pragma comment(lib, "lua.msvc8.lib")
#endif

extern "C" {
  #include <lua\lua.h>
  #include <lua\lualib.h>
  #include <lua\lauxlib.h>
}

#ifdef _DEBUG
  #pragma comment(lib, "luabind-d.msvc8.lib")
#else
  #pragma comment(lib, "luabind.msvc8.lib")
#endif

#pragma warning(disable: 4996)

#include <luabind\luabind.hpp>
#include <luabind\operator.hpp>
#include <luabind\object.hpp>