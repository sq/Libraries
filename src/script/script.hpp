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

  template <class name> struct classRegistrar {
    static luabind::scope doRegisterClass();
  };
  
  #define _CLASS(name) \
    namespace script { \
      template <> struct classRegistrar<name> { \
        static luabind::scope doRegisterClass() { \
          using namespace luabind; \
          typedef name thistype; \
          class_<name> _class(#name);

  #define _CLASS_BASE(name, wrapper) \
    namespace script { \
      template <> struct classRegistrar<name> { \
        static luabind::scope doRegisterClass() { \
          using namespace luabind; \
          typedef name thistype; \
          typedef wrapper thiswrapper; \
          class_<name, wrapper> _class(#name);
        
  #define _DEF(x) \
      _class.def(x);
        
  #define _METHOD(name) \
      _class.def(#name, &thistype::name);
        
  #define _DERIVED_METHOD(name) \
      _class.def(#name, &thistype::name, &thiswrapper::default_ ## name);

  #define _FIELD_R(name) \
      _class.def_readonly(#name, &thistype::name);

  #define _FIELD_RW(name) \
      _class.def_readwrite(#name, &thistype::name);
      
  #define _END_CLASS \
          return _class; \
        } \
      }; \
    }
  
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
    
    template <class T>
    void registerClass(const char * moduleName = 0) {
      luabind::module(getContext(), moduleName) [
        ::classRegistrar<T>::doRegisterClass()
      ];
    }
    
    void executeScript(const char * source);
    
    shared_ptr<CompiledScript> compileScript(const char * source, const char * name = 0);
    
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