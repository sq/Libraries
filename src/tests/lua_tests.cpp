#include <test.hpp>

#include <core\core.hpp>
#include <script\script.hpp>

#include <windows.h>
#include <fstream>

using namespace script;
using std::cout;

UnitTest::TestDetails * _test_details = 0;
UnitTest::TestResults * _test_results = 0;

int luatest_failure(lua_State * L) {
  int num_args = lua_gettop(L);
  if (num_args < 1)
    return 0;

  int offset = 1;
  if (num_args >= 2)
    offset = (int)lua_tointeger(L, 2);

  lua_Debug d;
  memset(&d, 0, sizeof(d));
  lua_getstack(L, offset, &d);
  lua_getinfo(L, "Sl", &d);

  const char * what = lua_tostring(L, 1);
  
  char filename[512];
  strcpy(filename, "\\");
  strcat(filename, d.short_src);
  
  UnitTest::TestDetails details(_test_details->testName, _test_details->suiteName, filename, d.currentline);
  _test_results->OnTestFailure(details, what);
  
  return 0;
}

SUITE(Lua) {
  TEST(Tests) {
    char buf[512];
    GetCurrentDirectoryA(sizeof(buf), buf);
    strcat(buf, "\\..\\src\\tests\\lua\\?.lua");

    shared_ptr<Context> sc(new Context());    
    sc->registerFunction("failure", luatest_failure);  
    sc->setIncludePath(std::string(buf));
    script::registerNamespaces(sc);    
    sc->executeScript("require('luatest\\\\luatest')");
    
    WIN32_FIND_DATAA findData;    
    HANDLE hFind = FindFirstFileA(
      "..\\src\\tests\\lua\\*.lua",
      &findData
    );
    
    vector<shared_ptr<CompiledScript>> scripts;
    
    while (hFind) {
      char fn[512];
      strcpy(fn, "..\\src\\tests\\lua\\");
      strcat(fn, findData.cFileName);
      
      std::string script;
      
      try {
        std::ifstream file(fn);
        std::stringstream buffer;
        std::string line;

        while(getline(file, line))
          buffer << line << "\n";
          
        script = buffer.str();

        try {
          scripts.push_back(sc->compileScript(script.c_str(), findData.cFileName));
        } catch (std::exception ex) {
          cout << "Unhandled exception while compiling '" << findData.cFileName << "': " << ex.what() << "\n";
          CHECK(false);
        }
      } catch (std::exception ex) {
        cout << "Unhandled exception while opening '" << findData.cFileName << "': " << ex.what() << "\n";
        CHECK(false);
      }
      
      if (FindNextFileA(hFind, &findData) == 0)
        hFind = 0;
    }
    
    for (unsigned i = 0; i < scripts.size(); i++) {
      shared_ptr<CompiledScript> script = scripts[i];
      try {
        script->execute();
      } catch (std::exception ex) {
        cout << "Unhandled exception while loading '" << script->getName() << "': " << ex.what() << "\n";
        CHECK(false);
      }
    }
    
    FindClose(hFind);
    
    int luaTestFailures = 0;

    Object globals = sc->getGlobals();
    std::vector<std::string> keys;
    for (luabind::iterator iter(globals), end; iter != end; ++iter) {
      std::string key;
      {
        std::stringstream keybuf;
        keybuf << iter.key();
        key = keybuf.str();
      }
      Object item = *iter;
      if ((key.find("test_") == 0) && (getObjectType(item) == LUA_TFUNCTION)) {
        keys.push_back(key);
      }
    }
    for (unsigned i = 0; i < keys.size(); i++) {
      std::string key = keys[i];
      Object item = globals[key];
      item.push(sc->getContext());
      UnitTest::Timer timer;
      UnitTest::TestDetails details(key.c_str(), "LuaTests", __FILE__, __LINE__);
      testResults_.OnTestStart(details);
      _test_details = &details;
      _test_results = &testResults_;
      try {
        timer.Start();
        sc->handleError(
          lua_pcall(sc->getContext(), 0, LUA_MULTRET, 0)
        );
      } catch (std::exception ex) {
        testResults_.OnTestFailure(details, ex.what());
      }
      testResults_.OnTestFinish(details, timer.GetTimeInMs() / 1000.0f);
      sc->collectGarbage();
    }
 }
}