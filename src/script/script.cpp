#include <core\core.hpp>
#include <script\script.hpp>
#include <wm\wm.hpp>
#include <gl\gl.hpp>
#include <image\image.hpp>
#include <float.h>

namespace script {

lua_State * g_activeContext = 0;

std::map<lua_State *, Context *> g_contextMap;

std::list<TailCall *> g_tailCalls;

void LuaContextHook(lua_State * L, lua_Debug * ar) {
  g_activeContext = L;
  switch (ar->event) {
    case LUA_HOOKRET:
      if (g_tailCalls.size()) {
        Context * context = g_contextMap[g_activeContext];
        while (g_tailCalls.size()) {
          TailCall * call = g_tailCalls.back();
          g_tailCalls.pop_back();
          call->invoke(context);
          delete call;
        }
      }
      lua_sethook(g_activeContext, LuaContextHook, 0, 0);
    break;
  }
}

Context * getActiveContext() {
  if (g_activeContext)
    return g_contextMap[g_activeContext];
  else
    return 0;
}

void tailCall(TailCall * call) {
  if (g_activeContext) {
    lua_sethook(g_activeContext, LuaContextHook, LUA_MASKRET, 0);
    g_tailCalls.push_back(call);
  }
}

void registerStringExtensions(shared_ptr<Context> context);
void registerTableExtensions(shared_ptr<Context> context);
void registerAriesExtensions(shared_ptr<Context> context);

LARGE_INTEGER g_timeStart;

long long _clock() {
  LARGE_INTEGER freq, time;
  QueryPerformanceFrequency(&freq);
  QueryPerformanceCounter(&time);
  time.QuadPart -= g_timeStart.QuadPart;
  long long result = (time.QuadPart * 100000 / freq.QuadPart) % ((unsigned)0xFFFFFFFF);
  return result;
}

int clock(lua_State * L) {
  long long result = _clock();
  lua_pushnumber(L, ((double)result / 100000.0));
  return 1;
}

int sleep(lua_State * L) {
  double duration = lua_tonumber(L, 1);
  long long start = _clock();
  long long end = start + ((long long)ceil(duration * 100000.0));
  while (_clock() < end)
    Sleep(1);
  return 0;
}

void registerNamespaces(shared_ptr<Context> context) {
  registerStringExtensions(context);
  registerTableExtensions(context);
  registerAriesExtensions(context);
  
  QueryPerformanceCounter(&g_timeStart);
  context->registerFunction("os.clock", clock);
  context->registerFunction("os.sleep", sleep);
  context->setGlobal("os.exit", Object());

  wm::registerNamespace(context);
  gl::registerNamespace(context);
  image::registerNamespace(context);
}

}