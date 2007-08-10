#include <core\core.hpp>
#include <script\script.hpp>
#include <wm\wm.hpp>
#include <gl\gl.hpp>

#include <windows.h>

using namespace script;
using namespace luabind;
using namespace wm;

_CLASS_WRAP(Window, shared_ptr<Window>)
  .def(constructor<>())
  .def(constructor<unsigned, unsigned>())
  
  .def("__tostring", &Window::toString)  
  .def("poll", &Window::poll)
  .def("close", &Window::onClose)
  .def("getSize", &Window::getSize, pure_out_value(_2) + pure_out_value(_3))
  .def("setSize", &Window::setSize)
  .def("getMouseState", &Window::getMouseState, pure_out_value(_2) + pure_out_value(_3) + pure_out_value(_4))
  .def("getKeyState", &Window::getKeyState)
  
  .def_readwrite("onClose", &Window::m_onClose)
  .def_readwrite("onMouseMove", &Window::m_onMouseMove)
  .def_readwrite("onMouseDown", &Window::m_onMouseDown)
  .def_readwrite("onMouseUp", &Window::m_onMouseUp)
  .def_readwrite("onMouseWheel", &Window::m_onMouseWheel)
  .def_readwrite("onKeyDown", &Window::m_onKeyDown)
  .def_readwrite("onKeyUp", &Window::m_onKeyUp)
  .def_readwrite("onTick", &Window::m_onTick)
    
  _PROPERTY_R(width, getWidth)
  _PROPERTY_R(height, getHeight)
  _PROPERTY_R(closed, getClosed)
  _PROPERTY_R(glContext, getGLContext)
  _PROPERTY_RW(tickRate, getTickRate, setTickRate)
  _PROPERTY_RW(caption, getCaption, setCaption)
  _PROPERTY_RW(visible, getVisible, setVisible)
_END_CLASS  

namespace wm {

int g_refCount = 0;
unsigned g_pollingTimeout = 1;
volatile unsigned g_lastTick = 0;

void initialize() {
  if (g_refCount == 0)
    eps_wm_initialize();

  g_refCount += 1;
}

void uninitialize() {
  assert(g_refCount);
  
  g_refCount -= 1;
  
  if (g_refCount == 0)
    eps_wm_shutDown();
}

unsigned getPollingTimeout() {
  return g_pollingTimeout;
}

void setPollingTimeout(unsigned timeout) {
  g_pollingTimeout = timeout;
}

bool poll(bool wait) {
  eps_wm_pollMessages(0, wait ? g_pollingTimeout : 0);

  int result = 0;
  Context * context = script::getActiveContext();
  if (!context)
    return false;
  Object wm = context->getGlobals()["wm"];
  if (type(wm) != LUA_TTABLE)
    return false;
  Object windows = wm["windows"];
  if (type(windows) != LUA_TTABLE)
    return false;
      
  for (iterator i(windows), end; i != end; ++i) {
    shared_ptr<Window> window = object_cast<shared_ptr<Window>>(*i);
    
    if (window->poll(false))
      result += 1;
  }
  
  return result > 0;
}

script::Object getKeyName(int keyCode) {
  char buffer[256];
  memset(buffer, 0, sizeof(buffer));
  int scanCode = MapVirtualKey(keyCode, 0) << 16;
  if (GetKeyNameTextA(scanCode, buffer, sizeof(buffer))) {
    return script::Object(script::getActiveContext()->getContext(), std::string(buffer));
  } else {
    return script::Object();
  }
}

void registerNamespace(shared_ptr<script::Context> context) {
  module(context->getContext(), "wm") [
    def("getKeyName", &getKeyName),
    def("poll", &poll),
    def("getPollingTimeout", &getPollingTimeout),
    def("setPollingTimeout", &setPollingTimeout)
  ];

  context->registerClass<Window>();
  context->registerHolder<Window, weak_ptr<Window>>();
}

}