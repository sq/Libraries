#include <core\core.hpp>
#include <script\script.hpp>
#include <wm\wm.hpp>

#include <windows.h>

using namespace script;
using namespace luabind;
using namespace wm;

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
  _PROPERTY_RW(tickRate, getTickRate, setTickRate)
  _PROPERTY_RW(caption, getCaption, setCaption)
  _PROPERTY_RW(visible, getVisible, setVisible)
_END_CLASS  

namespace wm {

int g_refCount = 0;
unsigned g_pollingTimeout = 2;
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

unsigned now() {
  LARGE_INTEGER freq, time;
  QueryPerformanceFrequency(&freq);
  QueryPerformanceCounter(&time);
  long long result = (time.QuadPart * 1000 / freq.QuadPart) % ((unsigned)0xFFFFFFFF);
  return (unsigned)result;
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

void registerNamespace(shared_ptr<script::Context> context) {
  module(context->getContext(), "wm") [
    def("poll", &poll),
    def("getPollingTimeout", &getPollingTimeout),
    def("setPollingTimeout", &setPollingTimeout)
  ];

  context->registerClass<Window>();
  context->registerHolder<Window, weak_ptr<Window>>();
}

}