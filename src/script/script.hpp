#include "libs.hpp"

namespace script {

  class LuaContext;
  class Context;
  class CompiledScript;
  
  class SyntaxError;
  
  typedef luabind::object Object;
  
  template <class T>
  inline T castObject(Object &obj) {
    return luabind::object_cast<T>(obj);
  }
  
  inline int getObjectType(Object &obj) {
    return luabind::type(obj);
  }
  
}

namespace script {

  class SyntaxError : public std::exception {
  public:
    SyntaxError(const char * what) :
      std::exception(what) {
    }
  };

  class LuaContext {
    lua_State * m_state;
    
  public:  
    LuaContext();
    ~LuaContext();
  
    lua_State * getState() const;
    
    void handleError(int resultCode) const;
    
    operator lua_State * () const;
    
    int getStackSize() const;
    int getStackIndex(int i) const;
    Object getStackValue(int i);
    
    void emptyStack();
    
    Object getGlobals();
    
    Object createTable();        
  };

  class Context : public enable_shared_from_this<Context>, public LuaContext {
    Context();

  public:
    ~Context();

    static shared_ptr<Context> create();
      
    const LuaContext & getContext() const;
    LuaContext & getContext();
    
    void registerFunction(const char * name, lua_CFunction function);
    
    void executeScript(const char * source);
    
    shared_ptr<CompiledScript> compileScript(const char * source);
    
  };
  
  class CompiledScript {
    shared_ptr<Context> m_parent;
    int m_id;
    
  public:
    CompiledScript(shared_ptr<Context> parent);
    ~CompiledScript();
    
    const Context & getParent() const;
    Context & getParent();
    
    void execute() const;
  };

}