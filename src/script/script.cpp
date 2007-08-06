#include <core\core.hpp>
#include <script\script.hpp>
#include <wm\wm.hpp>
#include <gl\gl.hpp>
#include <image\image.hpp>

namespace script {

lua_State * g_activeContext = 0;

std::map<lua_State *, Context *> g_contextMap;

std::list<TailCall *> g_tailCalls;

void LuaContextHook(lua_State * L, lua_Debug * ar) {
  g_activeContext = L;
  Context * context = g_contextMap[L];
  
  switch (ar->event) {
    case LUA_HOOKCALL:
    break;
    case LUA_HOOKRET:
      while (g_tailCalls.size()) {
        TailCall * call = g_tailCalls.back();
        g_tailCalls.pop_back();
        call->invoke(context);
        delete call;
      }
//      lua_sethook(g_activeContext, 0, 0, 0);
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
//    lua_sethook(g_activeContext, LuaContextHook, LUA_MASKCALL | LUA_MASKRET, 0);
    g_tailCalls.push_back(call);
  }
}

void registerStringExtensions(shared_ptr<Context> context);
void registerAriesExtensions(shared_ptr<Context> context);

void registerNamespaces(shared_ptr<Context> context) {
  registerStringExtensions(context);
  registerAriesExtensions(context);
  
  context->setGlobal("os.exit", Object());

  wm::registerNamespace(context);
  gl::registerNamespace(context);
  image::registerNamespace(context);
}

}