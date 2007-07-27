#include <test.hpp>

#include <core\core.hpp>
#include <script\script.hpp>

using namespace script;

SUITE(ScriptContextTests) {
  TEST(CanConstruct) {
    shared_ptr<Context> sc = Context::create();
  }
  
  TEST(CanConstructTwo) {
    shared_ptr<Context> a = Context::create();
    shared_ptr<Context> b = Context::create();
  }
  
  TEST(HasState) {
    shared_ptr<Context> sc = Context::create();
    CHECK(sc->getContext().getState());
  }
  
  TEST(DifferentContextsHaveDifferentStates) {
    shared_ptr<Context> a = Context::create();
    shared_ptr<Context> b = Context::create();
    CHECK(a->getContext().getState() != b->getContext().getState());
  }
  
  static int g_last_test_arg = 0;
  
  int test_function(lua_State *L) {
    int argc = lua_gettop(L);
    
    if (argc == 1) {
      g_last_test_arg = (int)lua_tointeger(L, 1);
    }
    
    lua_pushinteger(L, 42);    
    return 1;
  }
  
  TEST(CanRegisterFunctions) {
    shared_ptr<Context> sc = Context::create();
    sc->registerFunction("test_function", test_function);
  }
  
  TEST(CanRunBasicScripts) {
    shared_ptr<Context> sc = Context::create();
    sc->registerFunction("test_function", test_function);
    
    sc->executeScript(
      "test_function(12)"
    );
    CHECK_EQUAL(12, g_last_test_arg);
    
    sc->executeScript(
      "test_function(test_function())"
    );
    CHECK_EQUAL(42, g_last_test_arg);
  }
  
  TEST(ExecuteScriptThrowsOnError) {
    shared_ptr<Context> sc = Context::create();
    CHECK_THROW_STRING(sc->executeScript("test"), "[string \"test\"]:1: '=' expected near '<eof>'");
  }
  
  TEST(ExecuteScriptOperatesInSharedNamespace) {
    shared_ptr<Context> sc = Context::create();
    
    sc->executeScript("a=1");
    sc->executeScript("b=a+1");
    
    LuaContext & context = sc->getContext();

    lua_getglobal(context, "a");
    CHECK_EQUAL(1, (int)lua_tointeger(context, -1));

    lua_getglobal(context, "b");
    CHECK_EQUAL(2, (int)lua_tointeger(context, -1));
  }
  
  TEST(StackManipulation) {
    shared_ptr<Context> sc = Context::create();
    LuaContext & context = sc->getContext();    

    CHECK_EQUAL(0, sc->getStackSize());
    
    for (int i = 0; i < 5; i++) {
      lua_pushinteger(context, i);
    }
    CHECK_EQUAL(5, sc->getStackSize());
    
    for (int i = 0; i < 5; i++) {
      int v = (int)lua_tointeger(context, sc->getStackIndex(i));
      CHECK_EQUAL(v, i);
    }
    
    sc->emptyStack();
    CHECK_EQUAL(0, sc->getStackSize());
  }
  
  TEST(GetStackValue) {
    shared_ptr<Context> sc = Context::create();
    LuaContext & context = sc->getContext();    

    lua_pushinteger(context, 1);
    lua_pushinteger(context, 2);
    
    Object a = sc->getStackValue(0);
    Object b = sc->getStackValue(1);
    
    CHECK_EQUAL(LUA_TNUMBER, getObjectType(a));
    CHECK_EQUAL(LUA_TNUMBER, getObjectType(b));
    
    CHECK_EQUAL(1, castObject<int>(a));
    CHECK_EQUAL(2, castObject<int>(b));
  }
  
  TEST(GetGlobals) {
    shared_ptr<Context> sc = Context::create();

    sc->executeScript("a=1; b=\"test\"");
    
    Object a = sc->getGlobals()["a"];
    Object b = sc->getGlobals()["b"];
    
    CHECK_EQUAL(LUA_TNUMBER, getObjectType(a));
    CHECK_EQUAL(LUA_TSTRING, getObjectType(b));
    
    CHECK_EQUAL(1, castObject<int>(a));
    CHECK_EQUAL("test", castObject<std::string>(b));
  }
  
  TEST(ScriptGeneratedExceptionsAreHandledByExecuteScript) {
    shared_ptr<Context> sc = Context::create();

    CHECK_THROW_STRING(sc->executeScript("error(\"wtf\")"), "[string \"error(\"wtf\")\"]:1: wtf");
  }
}

SUITE(CompiledScriptTests) {
  TEST(CanLoadFromString) {
    shared_ptr<Context> sc = Context::create();
    shared_ptr<CompiledScript> s = sc->compileScript("a=1");
  }
  
  TEST(CanExecute) {
    shared_ptr<Context> sc = Context::create();
    shared_ptr<CompiledScript> s = sc->compileScript("a=1");

    LuaContext & context = sc->getContext();

    s->execute();

    lua_getglobal(context, "a");
    CHECK_EQUAL(1, (int)lua_tointeger(context, -1));
    lua_pop(context, 1);
  }
  
  TEST(CanExecuteTwice) {
    shared_ptr<Context> sc = Context::create();
    shared_ptr<CompiledScript> s = sc->compileScript("if (a) then a=a+1 else a=1 end");

    LuaContext & context = sc->getContext();

    s->execute();

    lua_getglobal(context, "a");
    CHECK_EQUAL(1, (int)lua_tointeger(context, -1));

    s->execute();

    lua_getglobal(context, "a");
    CHECK_EQUAL(2, (int)lua_tointeger(context, -1));
  }
}
