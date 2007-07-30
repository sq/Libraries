#ifndef _FR_WM
#define _FR_WM

#include "libs.hpp"

#include "..\script\script.hpp"

namespace wm {

  class Window;
  
  class Window : public enable_shared_from_this<Window> {
    eps_Window * m_handle;
    
    unsigned m_width, m_height;
    bool m_closed;
    
    void postConstruct(lua_State * L);
    
  public:
    luabind::object m_onClose;

    Window(unsigned width = 0, unsigned height = 0, unsigned flags = 0);
    virtual ~Window();
    
    eps_Window * getHandle() const;
    
    void setCaption(const char * text);
    std::string getCaption() const;
    
    unsigned getWidth() const;
    unsigned getHeight() const;
    bool getClosed() const;
    
    std::string toString() const;
    
    bool getVisible() const;
    void setVisible(bool visible);
    
    void getSize(unsigned & width, unsigned & height);
    void setSize(unsigned width, unsigned height);
    
    void poll();
    void close();
    
    unsigned getEventCount() const;
    eps::Event getEvent();
    
    void dispatch(eps::Event & event);
    
    void onClose();
    
    void onMouseMove(int x, int y, unsigned buttons);
    void onMouseDown(int x, int y, unsigned buttons);
    void onMouseUp(int x, int y, unsigned buttons);
    void onMouseWheel(int x, int y, unsigned buttons, unsigned wheel);
    
    void onKeyDown(unsigned key);
    void onKeyUp(unsigned key);
    
  };
  
  luabind::object poll(lua_State * L);
  
  void registerNamespace(shared_ptr<script::Context> context);
  
}

using namespace wm;
using namespace luabind;

_CLASS_WRAP(Window, shared_ptr<Window>)
  .def(constructor<>())
  .def(constructor<unsigned, unsigned>())
  .def(constructor<unsigned, unsigned, unsigned>())
  
  .def("__tostring", &Window::toString)  
  .def("poll", &Window::poll)
  .def("close", &Window::onClose)
  .def("getSize", &Window::getSize, pure_out_value(_2) + pure_out_value(_3))
  .def("setSize", &Window::setSize)
  
  .def_readwrite("onClose", &Window::m_onClose)

  _PROPERTY_R(width, getWidth)
  _PROPERTY_R(height, getHeight)
  _PROPERTY_R(closed, getClosed)
  _PROPERTY_RW(caption, getCaption, setCaption)
  _PROPERTY_RW(visible, getVisible, setVisible)
_END_CLASS  
  
#endif