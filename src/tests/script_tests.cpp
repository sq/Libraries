#include <test.hpp>

#include <core\core.hpp>
#include <script\script.hpp>

#include <windows.h>

using namespace script;

SUITE(ScriptContextTests) {
  TEST(CanConstruct) {
    shared_ptr<Context> sc(new Context());
  }
  
  TEST(CanConstructTwo) {
    shared_ptr<Context> a(new Context());
    shared_ptr<Context> b(new Context());
  }
  
  TEST(HasState) {
    shared_ptr<Context> sc(new Context());
    CHECK(sc->getContext().getState());
  }
  
  TEST(DifferentContextsHaveDifferentStates) {
    shared_ptr<Context> a(new Context());
    shared_ptr<Context> b(new Context());
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
  
  int test_string_method(lua_State *L) {
    int argc = lua_gettop(L);
    
    if (argc == 2) {
      g_last_test_arg = (int)lua_tointeger(L, 2);
    }
    
    lua_pushinteger(L, 42);
    return 1;
  }
    
  TEST(CanRegisterFunctions) {
    shared_ptr<Context> sc(new Context());
    sc->registerFunction("test_function", test_function);
  }
  
  TEST(CanExtendBuiltInClasses) {
    shared_ptr<Context> sc(new Context());
    sc->registerFunction("string.test", test_string_method);
    
    sc->executeScript("a = \"test\"");
    sc->executeScript("a:test(12)");

    CHECK_EQUAL(12, g_last_test_arg);
  }
  
  TEST(CanRunBasicScripts) {
    shared_ptr<Context> sc(new Context());
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
    shared_ptr<Context> sc(new Context());
    CHECK_THROW_STRING(sc->executeScript("test"), "[string \"test\"]:1: '=' expected near '<eof>'");
  }
  
  TEST(ExecuteScriptOperatesInSharedNamespace) {
    shared_ptr<Context> sc(new Context());
    
    sc->executeScript("a=1");
    sc->executeScript("b=a+1");
    
    LuaContext & context = sc->getContext();

    lua_getglobal(context, "a");
    CHECK_EQUAL(1, (int)lua_tointeger(context, -1));

    lua_getglobal(context, "b");
    CHECK_EQUAL(2, (int)lua_tointeger(context, -1));
  }
  
  TEST(GetActiveContext) {
    shared_ptr<Context> a(new Context());
    shared_ptr<Context> b(new Context());
    
    a->executeScript("a = 1");

    CHECK_EQUAL(a.get(), getActiveContext());

    b->executeScript("b = 1");

    CHECK_EQUAL(b.get(), getActiveContext());

  }
    
  TEST(StackManipulation) {
    shared_ptr<Context> sc(new Context());
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
    shared_ptr<Context> sc(new Context());
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
    shared_ptr<Context> sc(new Context());

    sc->executeScript("a=1; b=\"test\"");
    
    Object a = sc->getGlobals()["a"];
    Object b = sc->getGlobals()["b"];
    
    CHECK_EQUAL(LUA_TNUMBER, getObjectType(a));
    CHECK_EQUAL(LUA_TSTRING, getObjectType(b));
    
    CHECK_EQUAL(1, castObject<int>(a));
    CHECK_EQUAL("test", castObject<std::string>(b));
  }
  
  TEST(GetGlobal) {
    shared_ptr<Context> sc(new Context());

    sc->executeScript("a={}; a.b={}; a.b.c = 1");
    
    Object c = sc->getGlobal("a.b.c");
    
    CHECK_EQUAL(LUA_TNUMBER, getObjectType(c));
    CHECK_EQUAL(1, castObject<int>(c));
  }
  
  TEST(SetGlobal) {
    shared_ptr<Context> sc(new Context());

    sc->executeScript("a={}; a.b={}; a.b.c = 1");
    
    sc->setGlobal("a.b.c", 2);
    sc->setGlobal("a.d", 5);
    sc->setGlobal("b", 7);
    
    Object c = sc->getGlobal("a.b.c");    
    
    CHECK_EQUAL(LUA_TNUMBER, getObjectType(c));
    CHECK_EQUAL(2, castObject<int>(c));
    
    Object d = sc->getGlobal("a.d");
    
    CHECK_EQUAL(LUA_TNUMBER, getObjectType(d));
    CHECK_EQUAL(5, castObject<int>(d));
    
    Object b = sc->getGlobal("b");
    
    CHECK_EQUAL(LUA_TNUMBER, getObjectType(b));
    CHECK_EQUAL(7, castObject<int>(b));
  }
  
  TEST(ScriptGeneratedExceptionsAreHandledByExecuteScript) {
    shared_ptr<Context> sc(new Context());

    CHECK_THROW_STRING(sc->executeScript("error(\"wtf\")"), "[string \"error(\"wtf\")\"]:1: wtf");
  }
}

SUITE(CompiledScriptTests) {
  TEST(CanLoadFromString) {
    shared_ptr<Context> sc(new Context());
    shared_ptr<CompiledScript> s = sc->compileScript("a=1");
  }
  
  TEST(CanExecute) {
    shared_ptr<Context> sc(new Context());
    shared_ptr<CompiledScript> s = sc->compileScript("a=1");

    LuaContext & context = sc->getContext();

    s->execute();

    lua_getglobal(context, "a");
    CHECK_EQUAL(1, (int)lua_tointeger(context, -1));
    lua_pop(context, 1);
  }
  
  TEST(CanExecuteTwice) {
    shared_ptr<Context> sc(new Context());
    shared_ptr<CompiledScript> s = sc->compileScript("if (a) then a=a+1 else a=1 end");

    LuaContext & context = sc->getContext();

    s->execute();

    lua_getglobal(context, "a");
    CHECK_EQUAL(1, (int)lua_tointeger(context, -1));

    s->execute();

    lua_getglobal(context, "a");
    CHECK_EQUAL(2, (int)lua_tointeger(context, -1));
  }
  
  TEST(IncludePath) {
    shared_ptr<Context> sc(new Context());
    
    char buf[512];
    GetCurrentDirectoryA(sizeof(buf), buf);
    
    strcat(buf, "\\..\\src\\tests\\?.lua");
    
    sc->setIncludePath(std::string(buf));
    
    CHECK_EQUAL(buf, sc->getIncludePath());
    
    sc->executeScript("require('test_include_path')");
    
    LuaContext & context = sc->getContext();

    lua_getglobal(context, "a");
    CHECK_EQUAL(1, (int)lua_tointeger(context, -1));    
  }
}

static int times_constructed = 0;
static int times_destructed = 0;
static int times_method_invoked = 0;

class testclass {
public:
  int x;

  testclass() :
    x(0)
  {
    times_constructed += 1;
  }
  
  ~testclass() {
    times_destructed += 1;
  }
  
  void test_method() {
    times_method_invoked += 1;
  }
};

_CLASS(testclass)
  .def(constructor<>())
  _METHOD(test_method)
  _FIELD_RW(x)
_END_CLASS

class baseclass {
public:
  int x;

  baseclass() :
    x(0)
  {
    times_constructed += 1;
  }
  
  ~baseclass() {
    times_destructed += 1;
  }
  
  virtual void test_method() {
    times_method_invoked += 1;
  }
};

class baseclass_wrapper : public baseclass, public luabind::wrap_base {
public:
    baseclass_wrapper()
        : baseclass() 
    {}

    virtual void test_method() { 
        call<void>("test_method");
    }

    static void default_test_method(baseclass * obj) {
        return obj->baseclass::test_method();
    }  
};
    
_CLASS_WRAP(baseclass, baseclass_wrapper)
  .def(constructor<>())
  _DERIVED_METHOD(test_method)
  _FIELD_RW(x)
_END_CLASS

class outvalue {
public:
  outvalue() {
  }

  void getTwoValues(int &a, int &b) {
    a = 1;
    b = 2;
  }
};

_CLASS(outvalue)
  .def(constructor<>())
  .def("getTwoValues", &outvalue::getTwoValues, pure_out_value(_2) + pure_out_value(_3))
_END_CLASS

SUITE(ClassTests) {
  TEST(CanRegisterClass) {
    shared_ptr<Context> sc(new Context());
    
    sc->registerClass<testclass>();
  }
  
  TEST(CanInstantiateClass) {
    shared_ptr<Context> sc(new Context());
    
    times_constructed = times_destructed = times_method_invoked = 0;
    
    sc->registerClass<testclass>();
    
    CHECK_EQUAL(0, times_constructed);
    CHECK_EQUAL(0, times_destructed);

    sc->executeScript("a = testclass()");
    
    CHECK_EQUAL(1, times_constructed);
    CHECK_EQUAL(0, times_destructed);

    sc->executeScript("a = nil; collectgarbage()");
    
    CHECK_EQUAL(1, times_constructed);
    CHECK_EQUAL(1, times_destructed);
  }
  
  TEST(CanInvokeMethod) {
    shared_ptr<Context> sc(new Context());
    
    times_constructed = times_destructed = times_method_invoked = 0;

    sc->registerClass<testclass>();
    
    CHECK_EQUAL(0, times_method_invoked);

    sc->executeScript("a = testclass(); a:test_method()");
    
    CHECK_EQUAL(1, times_method_invoked);
  }
  
  TEST(CanGetInstanceFromScriptVariable) {
    shared_ptr<Context> sc(new Context());
    
    sc->registerClass<testclass>();
    
    sc->executeScript("a = testclass()");
    
    Object a = sc->getGlobal("a");
    testclass * pa = castObject<testclass *>(a);
    
    CHECK(pa);
  }
  
  TEST(CanManipulateInstanceFields) {
    shared_ptr<Context> sc(new Context());
    
    sc->registerClass<testclass>();
    
    sc->executeScript("a = testclass()");
    
    Object a = sc->getGlobal("a");
    testclass * pa = castObject<testclass *>(a);
    
    CHECK_EQUAL(0, pa->x);
    
    sc->executeScript("a.x = 1");
    
    CHECK_EQUAL(1, pa->x);
  }
  
  TEST(CanDeriveFromNativeClass) {
    shared_ptr<Context> sc(new Context());
    
    sc->registerClass<baseclass>();
    
    times_constructed = 0;

    sc->executeScript(
      "class 'derived' (baseclass) \n"
      "function derived:__init() super() end \n"
      "a = derived()"
    );
    
    CHECK_EQUAL(1, times_constructed);
  }
  
  TEST(DerivedClassesAreNotGCed) {
    shared_ptr<Context> sc(new Context());
    
    sc->registerClass<baseclass>();
    
    times_constructed = times_destructed = 0;

    sc->executeScript(
      "class 'derived' (baseclass) \n"
      "function derived:__init() super() end \n"
      "a = derived()"
    );
    
    CHECK_EQUAL(1, times_constructed);
    
    sc->executeScript(
      "a = nil \n"
      "collectgarbage() \n"
      "collectgarbage()"
    );

    CHECK_EQUAL(0, times_destructed);
  }
  
  TEST(CanInvokeDerivedMethod) {
    shared_ptr<Context> sc(new Context());
    
    sc->registerClass<baseclass>();
    
    times_constructed = times_method_invoked = 0;

    sc->executeScript(
      "class 'derived' (baseclass) \n"
      "function derived:__init() super() end \n"
      "function derived:test_method() \n"
      "  baseclass.test_method(self) \n"
      "  self.x = 1 \n"
      "end \n"
      "a = derived()"
    );
    
    CHECK_EQUAL(1, times_constructed);
    CHECK_EQUAL(0, times_method_invoked);
    
    Object a = sc->getGlobals()["a"];
    baseclass * pa = castObject<baseclass *>(a);
    
    CHECK(pa);
    CHECK_EQUAL(0, pa->x);
    
    pa->test_method();
    
    CHECK_EQUAL(1, pa->x);

    CHECK_EQUAL(1, times_constructed);
    CHECK_EQUAL(1, times_method_invoked);
  }
  
  TEST(CanReturnTwoValues) {
    shared_ptr<Context> sc(new Context());
    
    sc->registerClass<outvalue>();
    
    sc->executeScript(
      "a = outvalue() \n"
      "x, y = a:getTwoValues()"
    );
  }
}

static int last_tail_call = 0;

struct myTailCall : public TailCall {
  int m_a;

  myTailCall(int a) : 
    m_a(a) {
  }
  
  void invoke (Context * context) {
    last_tail_call = m_a;
  }
};

class tailcall {
public:
  tailcall() {
  }

  void doTailCall(int a) {
    script::tailCall(new myTailCall(a));
  }
};

_CLASS(tailcall)
  .def(constructor<>())
  .def("doTailCall", &tailcall::doTailCall)
_END_CLASS

SUITE(TailCall) {
  TEST(CanPerformTailCall) {
    shared_ptr<Context> sc(new Context());
        
    sc->registerClass<tailcall>();

    last_tail_call = 0;
    
    sc->executeScript(
      "a = tailcall() \n"
      "a:doTailCall(5)"
    );
    
    CHECK_EQUAL(5, last_tail_call);
 }
}

SUITE(CharacterizeLua) {
  TEST(StringPointerUniqueness) {
    #pragma warning(disable : 4311)
    shared_ptr<Context> sc(new Context());
    
    sc->executeScript("a = 'test'; b = 'test'; c = 'foo'; d = 'foo'");
    sc->getGlobal("a").push(sc->getContext());
    const char * a = lua_tostring(sc->getContext(), -1);
    sc->getGlobal("b").push(sc->getContext());
    const char * b = lua_tostring(sc->getContext(), -1);
    sc->getGlobal("c").push(sc->getContext());
    const char * c = lua_tostring(sc->getContext(), -1);
    sc->getGlobal("d").push(sc->getContext());
    const char * d = lua_tostring(sc->getContext(), -1);
    
    CHECK_EQUAL((unsigned)a, (unsigned)b);
    CHECK_EQUAL((unsigned)c, (unsigned)d);
  }
}