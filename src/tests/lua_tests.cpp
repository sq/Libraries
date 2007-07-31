#include <test.hpp>

#include <core\core.hpp>
#include <script\script.hpp>

#include <windows.h>

using namespace script;

SUITE(Lua) {
  TEST(Tests) {
    shared_ptr<Context> sc(new Context());
    
    char buf[512];
    GetCurrentDirectoryA(sizeof(buf), buf);
    
    strcat(buf, "\\..\\src\\tests\\lua\\?.lua");
    
    sc->setIncludePath(std::string(buf));
    
    sc->executeScript("require('luatest\\\\luatest')");
    
    WIN32_FIND_DATAA findData;    
    HANDLE hFind = FindFirstFileA(
      "..\\src\\tests\\lua\\*.lua",
      &findData
    );
    
    while (hFind) {
      char script[512] = "require(\"";
      strcat(script, findData.cFileName);
      script[strlen(script) - 4] = 0;
      strcat(script, "\")");
      
      sc->executeScript(script);
      
      if (FindNextFileA(hFind, &findData) == 0)
        hFind = 0;
    }
    
    FindClose(hFind);
    
    Object runtests = sc->getGlobals()["luatest"]["runtests"];
    
    CHECK(runtests);
    CHECK_EQUAL(LUA_TFUNCTION, getObjectType(runtests));
    
    int result = 0;
    try {
      result = castObject<int>(Object(runtests()));
    } catch (...) {
    }
    
    CHECK_EQUAL(1, result);
 }
}