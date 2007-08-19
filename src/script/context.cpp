#include <core\core.hpp>
#include <script\script.hpp>
#include <wm\wm.hpp>
#include <gl\gl.hpp>
#include <image\image.hpp>

namespace script {

extern lua_State * g_activeContext;
extern std::map<lua_State *, Context *> g_contextMap;
void LuaContextHook(lua_State * L, lua_Debug * ar);

LuaContext::LuaContext() :
  m_state(0)
{
  m_state = lua_open();
  luaL_openlibs(m_state);
  luabind::open(m_state);
  g_activeContext = m_state;
  lua_sethook(m_state, LuaContextHook, 0, 0);
}

LuaContext::~LuaContext() {
  lua_sethook(m_state, LuaContextHook, 0, 0);
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

void LuaContext::collectGarbage() {
  lua_gc(m_state, LUA_GCCOLLECT, 0);
}

Object LuaContext::getGlobals() const {
  return luabind::globals(m_state);
}

Object LuaContext::getGlobal(const char * path) const {
  Object current = getGlobals();
  char current_key[256];
  
  const char * pos = path;
  const char * end = path + strlen(path);
  const char * nextpos = 0;
  while (pos) {
    nextpos = strchr(pos, '.');
    if (nextpos == 0)
      nextpos = end;
    memset(current_key, 0, 256);
    memcpy(current_key, pos, nextpos - pos);

    if ((current) && (getObjectType(current) == LUA_TTABLE)) {
      current = current[current_key];
    } else {
      Object nil;
      return nil;
    }
   
    if (nextpos >= end)
      break;
    pos = nextpos + 1;
  }
  
  return current;
}
    
Object LuaContext::createTable() {
  return luabind::newtable(m_state);
}
    
Context::Context() {
  g_contextMap[getContext()] = this;
  getContext().emptyStack();
}

Context::~Context() {
  g_contextMap.erase(getContext());
}

const LuaContext & Context::getContext() const {
  return (const LuaContext &)(*this);
}

LuaContext & Context::getContext() {
  return (LuaContext &)(*this);
}

void Context::registerFunction(const char * name, lua_CFunction function) {
  lua_pushcfunction(getContext(), function);
  script::Object fn(luabind::from_stack(getContext(), -1));
  setGlobal(name, fn);
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
  std::stringstream namebuf;
  if (name) {
    namebuf << "=";
    namebuf << name;
  }
  handleError(
    luaL_loadbuffer(getContext(), source, strlen(source), name ? namebuf.str().c_str() : source)
  );
  return shared_ptr<CompiledScript>(
    new CompiledScript(shared_from_this(), name)
  );
}

std::string Context::getIncludePath() const {
  std::stringstream buf;
  buf << (getGlobal("package.path"));
  return buf.str();
}

void Context::setIncludePath(std::string & path) {
  getGlobal("package")["path"] = path;
}

CompiledScript::CompiledScript(shared_ptr<Context> parent, const char * name) :
  m_parent(parent),
  m_id(0),
  m_name()
{
  m_id = luaL_ref(m_parent->getContext(), LUA_REGISTRYINDEX);
  if (name)
    m_name = std::string(name);
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

const std::string & CompiledScript::getName() const {
  return m_name;
}

}