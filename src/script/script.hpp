#ifndef _FR_SCRIPT
#define _FR_SCRIPT

#include "libs.hpp"
  
#define _CLASS(name) \
  template <> struct classRegistrar<name> { \
    static luabind::scope doRegisterClass() { \
      using namespace luabind; \
      typedef name thistype; \
      class_<name> _class(#name); \
      _class
  
#define _CLASS_WRAP(name, wrapper) \
  template <> struct classRegistrar<name> { \
    static luabind::scope doRegisterClass() { \
      using namespace luabind; \
      typedef name thistype; \
      typedef wrapper thiswrapper; \
      class_<name, wrapper> _class(#name); \
      _class
            
#define _DEF(what) \
    _class.def(what);
      
#define _METHOD(name) \
    .def(#name, &thistype:: ## name)
      
#define _METHOD_P(name, policies) \
    .def(#name, &thistype:: ## name, policies)
      
#define _DERIVED_METHOD(name) \
    .def(#name, &thistype:: ## name, &thiswrapper::default_ ## name)
      
#define _DERIVED_METHOD_P(name, policies) \
    .def(#name, &thistype:: ## name, &thiswrapper::default_ ## name, policies)

#define _FIELD_R(name) \
    .def_readonly(#name, &thistype:: ## name)

#define _FIELD_RW(name) \
    .def_readwrite(#name, &thistype:: ## name)
    
#define _PROPERTY_R(name, getter) \
    .property(#name, &thistype:: ## getter)
    
#define _PROPERTY_RW(name, getter, setter) \
    .property(#name, &thistype:: ## getter, &thistype:: ## setter)
    
#define _PROPERTY_RW_P(name, getter, setter, getpolicies) \
    .property(#name, &thistype:: ## getter, &thistype:: ## setter, getpolicies)
            
#define _END_CLASS \
        ; \
        return _class; \
      } \
    };
  
namespace luabind {
  template<class T>
  T* get_pointer(boost::shared_ptr<T>& p) 
  {
    T* result = p.get();
    if (result)
      return result;
    else
      throw std::exception("null reference");
  }

  template<class A>
  boost::shared_ptr<const A>* 
  get_const_holder(boost::shared_ptr<A>*)
  {
    return 0;
  }

  template<class T>
  T* get_pointer(boost::weak_ptr<T>& p) 
  {
    T* result = p.lock().get();
    if (result)
      return result;
    else
      throw std::exception("null reference");
  }

  template<class A>
  boost::weak_ptr<const A>* 
  get_const_holder(boost::weak_ptr<A>*)
  {
    return 0;
  }
}
  
template <class name> struct classRegistrar {
  static luabind::scope doRegisterClass();
};
    
namespace script {
  
  typedef luabind::object Object;
    
  class SyntaxError : public std::exception {
  public:
    SyntaxError(const char * what) :
      std::exception(what) {
    }
  };
  
  class RuntimeError : public std::exception {
  public:
    RuntimeError(const char * what) :
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
    Object getStackValue(int i) const;
    
    void emptyStack();
    void collectGarbage();
    
    Object getGlobals() const;
    Object getGlobal(const char * path) const;
    
    template <class T>
    void setGlobal(const char * path, const T & value);
    
    Object createTable();
  };

  class Context : public enable_shared_from_this<Context>, public LuaContext {
  public:
    Context();
    ~Context();
      
    const LuaContext & getContext() const;
    LuaContext & getContext();
    
    void registerFunction(const char * name, lua_CFunction function);
    
    template <class T>
    void registerClass(const char * moduleName = 0) {
      luabind::module(getContext(), moduleName) [
        ::classRegistrar<T>::doRegisterClass()
      ];
    }
    
    template <class T, class H>
    void registerHolder() {
      luabind::detail::class_registry * r = luabind::detail::class_registry::get_registry(getContext());
      luabind::detail::class_rep * cr = r->find_class(LUABIND_TYPEID(T));
      r->add_class(LUABIND_TYPEID(H), cr);
    }
    
    std::string getIncludePath() const;
    void setIncludePath(std::string & path);
    
    void executeScript(const char * source);
    
    shared_ptr<CompiledScript> compileScript(const char * source, const char * name = 0);
    
  };
  
  class NameTable {
    typedef std::map<const char *, script::Object> TValues;
    TValues m_values;
    script::Context * m_context;
    
  public:
    inline NameTable(script::Context * context) :
      m_context(context)
    {
    }
  
    inline script::Object & operator [] (const char * _literal) {
      TValues::iterator iter = m_values.find(_literal);
      if (iter == m_values.end()) {
        m_values[_literal] = script::Object(m_context->getContext(), _literal);
        iter = m_values.find(_literal);
      }
      return iter->second;
    }
  };
  
  class CompiledScript {
    std::string m_name;
    shared_ptr<Context> m_parent;
    int m_id;
    
  public:
    CompiledScript(shared_ptr<Context> parent, const char * name = 0);
    ~CompiledScript();
    
    const std::string & getName() const;
    
    const Context & getParent() const;
    Context & getParent();
    
    void execute() const;
  };
  
  struct TailCall {
    virtual void invoke(Context * context) = 0;
  };
  
  void tailCall(TailCall * call);
  
  Context * getActiveContext();
  
  template <class T>
  inline T castObject(Object &obj) {
    return luabind::object_cast<T>(obj);
  }
  
  inline int getObjectType(Object &obj) {
    return luabind::type(obj);
  }
  
  inline bool isTable(Object &obj) {
    return (obj) && (luabind::type(obj) == LUA_TTABLE);
  }
  
  template <class T>
  inline unsigned unpackTable(Object &src, T * dest, unsigned max) {
    unsigned count = 0;
    for (luabind::iterator iter(src), end; iter != end; ++iter) {
      if (count < max) {
        Object item = *iter;
        dest[count] = luabind::object_cast<T>(item);
      } else
        return count;

      count++;
    }
    
    return count;
  }
  
  template <class T>
  inline Object getObject(shared_ptr<T> obj) {
    Context * context = getActiveContext();
    Object _obj(context->getContext(), obj);
    return _obj;
  }
  
  template <class T>
  inline Object getObjectMetatable(shared_ptr<T> obj) {
    Object result;
    {
      Context * context = getActiveContext();
      Object _obj(context->getContext(), obj);
      _obj.push(context->getContext());
      if (lua_getmetatable(context->getContext(), -1)) {
        result = Object(luabind::from_stack(context->getContext(), -1));
        lua_pop(context->getContext(), 1);
      }
      lua_pop(context->getContext(), 1);    
    }
    return result;
  }
  
  template <class T>
  inline Object getObjectMember(shared_ptr<T> obj, const char * name) {
    Object result;
    {
      Context * context = getActiveContext();
      Object _obj(context->getContext(), obj);
      result = _obj[name];
    }
    return result;
  }
  
  template <class T>
  void LuaContext::setGlobal(const char * path, const T & value) {
    char parentPath[256];
    const char * endpos = strrchr(path, '.');
    const char * thename;
    Object parent;
    if (endpos) {
      memset(parentPath, 0, 256);
      memcpy(parentPath, path, endpos - path);
      parent = getGlobal(parentPath);
      thename = endpos + 1;
    } else {
      parent = getGlobals();
      thename = path;
    }
    if (parent && getObjectType(parent) == LUA_TTABLE) {
      parent[thename] = value;
    } else {
      char buffer[512];
      strcpy(buffer, parentPath);
      strcat(buffer, " does not exist");
      throw std::exception(buffer);
    }
  }
  
  void registerNamespaces(shared_ptr<Context> context);
}

#endif