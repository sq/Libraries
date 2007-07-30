#include <core\core.hpp>
#include <script\script.hpp>

namespace script {

lua_State * g_activeContext = 0;

std::list<TailCall *> g_tailCalls;

lua_State * getActiveContext() {
  return g_activeContext;
}

void tailCall(TailCall * call) {
  g_tailCalls.push_back(call);
}

static void LuaContextHook(lua_State * L, lua_Debug * ar) {
  g_activeContext = L;
  
  switch (ar->event) {
    case LUA_HOOKCALL:
    break;
    case LUA_HOOKRET:
      while (g_tailCalls.size()) {
        TailCall * call = g_tailCalls.back();
        g_tailCalls.pop_back();
        call->invoke(L);
        delete call;
      }
    break;
  }
}

LuaContext::LuaContext() :
  m_state(0)
{
  m_state = lua_open();
  
  lua_sethook(m_state, LuaContextHook, LUA_MASKCALL | LUA_MASKRET, 0);
}

LuaContext::~LuaContext() {
  lua_sethook(m_state, 0, 0, 0);
  if (g_activeContext == m_state)
    g_activeContext = 0;

  lua_close(m_state);
  m_state = 0;
}

lua_State * LuaContext::getState() const {
  return m_state;
}

LuaContext::operator lua_State * () const {
  return m_state;
}

void LuaContext::handleError(int resultCode) const {
  if (resultCode) {
    const char * msg = lua_tostring(m_state, -1);
    switch (resultCode) {
      case LUA_ERRSYNTAX:
        throw SyntaxError(msg);
      case LUA_ERRRUN:
        throw RuntimeError(msg);
      default:
        throw std::exception(msg);
    }
  }
}

int LuaContext::getStackSize() const {
  return lua_gettop(m_state);
}

int LuaContext::getStackIndex(int i) const {
  int top = lua_gettop(m_state);
  if ((i < 0) || (i >= top))
    throw std::exception("stackIndex(0 <= i < stackSize)");
  else
    return i + 1;
}

Object LuaContext::getStackValue(int i) const {
  return Object(luabind::from_stack(m_state, i + 1)); 
}

void LuaContext::emptyStack() {
  lua_settop(m_state, 0);
}

Object LuaContext::getGlobals() const {
  return luabind::globals(m_state);
}
    
Object LuaContext::createTable() {
  return luabind::newtable(m_state);
}
    
Context::Context() {
  luaL_openlibs(getContext());
  
  luabind::open(getContext());

  getContext().emptyStack();
}

Context::~Context() {
}

const LuaContext & Context::getContext() const {
  return (const LuaContext &)(*this);
}

LuaContext & Context::getContext() {
  return (LuaContext &)(*this);
}

void Context::registerFunction(const char * name, lua_CFunction function) {
  lua_register(getContext(), name, function);
}

void Context::executeScript(const char * source) {
  handleError(
    luaL_loadbuffer(getContext(), source, strlen(source), source)
  );
  handleError(
    lua_pcall(getContext(), 0, LUA_MULTRET, 0)
  );
}

shared_ptr<CompiledScript> Context::compileScript(const char * source, const char * name) {
  handleError(
    luaL_loadbuffer(getContext(), source, strlen(source), name ? name : source)
  );
  return shared_ptr<CompiledScript>(
    new CompiledScript(shared_from_this())
  );
}

CompiledScript::CompiledScript(shared_ptr<Context> parent) :
  m_parent(parent),
  m_id(0)
{
  m_id = luaL_ref(m_parent->getContext(), LUA_REGISTRYINDEX);
}

CompiledScript::~CompiledScript() {
  if (m_id) {
    luaL_unref(m_parent->getContext(), LUA_REGISTRYINDEX, m_id);
    m_id = 0;
  }
}

void CompiledScript::execute() const {
  LuaContext & context = m_parent->getContext();
  
  lua_rawgeti(context, LUA_REGISTRYINDEX, m_id);  
  context.handleError(
    lua_pcall(context, 0, 0, 0)
  );
}

}