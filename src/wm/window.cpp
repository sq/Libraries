#include <core\core.hpp>
#include <script\script.hpp>
#include <wm\wm.hpp>
#include <gl\gl.hpp>

using namespace script;
using namespace luabind;
using namespace wm;

namespace wm {

Window::Window(unsigned width, unsigned height) :
  m_handle(0),
  m_width(width),
  m_height(height),
  m_closed(false),
  m_tickRate(0)
{
  wm::initialize();

  eps::ErrorHandler e;
  eps_OpenGLContext * context = eps_opengl_createOpenGLWindow(width, height, 0, EPS_OPENGL_PF_32BPP);
  e.check();
  m_handle = eps_opengl_getContextWindow(context);
  e.check();
  m_glContext = shared_ptr<gl::GLContext>(new gl::GLContext(this, context));
  
  struct PostConstructTailCall : public script::TailCall {
    Window * m_this;
    
    PostConstructTailCall(Window * _this) : m_this(_this) {}
    void invoke(Context * context) {
      m_this->postConstruct(context);
    }  
  };
  
  script::tailCall(new PostConstructTailCall(this));
}

Window::~Window() {
  if (m_handle) {
    if (m_glContext) {
      eps_opengl_destroyOpenGLWindow(m_glContext->getHandle());
    } else {
      eps_wm_destroyWindow(m_handle);
    }
    m_handle = 0;
  }

  Context * context = script::getActiveContext();
  if (context) {
    Object windows = context->getGlobal("wm.windows");
    if (windows && type(windows) == LUA_TTABLE) {
      std::string ptr = core::ptrToString(this);
      windows[ptr] = Object();
    }
  }

  wm::uninitialize();
}

void Window::postConstruct(Context * context) {
  if (context) {
    Object wm = context->getGlobal("wm");
    if (wm && type(wm) == LUA_TTABLE) {
      Object windows = wm["windows"];
      if (!windows || type(windows) != LUA_TTABLE) {
        windows = context->createTable();
        wm["windows"] = windows;
      }
      std::string ptr = core::ptrToString(this);
      windows[ptr] = weak_ptr<Window>(shared_from_this());
    }
  }
}

std::string Window::toString() const {
  std::stringstream buf;
  buf << "<Window:" << core::ptrToString(this) << ">";
  return buf.str();
}

eps_Window * Window::getHandle() const {
  return m_handle;
}

void Window::setCaption(const char * text) {
  eps::ErrorHandler e;
  eps_wm_setCaption(m_handle, text);
}

std::string Window::getCaption() const {
  eps::ErrorHandler e;
  char buffer[512];
  eps_wm_getCaption(m_handle, buffer, sizeof(buffer));
  return std::string(buffer);
}

shared_ptr<gl::GLContext> Window::getGLContext() const {
  return m_glContext;
}

unsigned Window::getWidth() const {
  return m_width;
}

unsigned Window::getHeight() const {
  return m_height;
}

bool Window::getClosed() const {
  return m_closed;
}

bool Window::getVisible() const {
  eps::ErrorHandler e;
  return eps_wm_getVisible(m_handle) != 0;
}

void Window::setVisible(bool visible) {
  eps::ErrorHandler e;
  eps_wm_setVisible(m_handle, visible);
}

unsigned Window::getTickRate() const {
  return m_tickRate;
}

void Window::setTickRate(unsigned tickRate) {
  eps::ErrorHandler e;
  m_tickRate = tickRate;
  eps_wm_setTickRate(m_handle, tickRate);
}

void Window::getSize(unsigned & width, unsigned & height) {
  width = m_width;
  height = m_height;
}

void Window::setSize(unsigned width, unsigned height) {
  eps::ErrorHandler e;

  eps_wm_moveWindow(m_handle, -1, -1, m_width, m_height);

  m_width = width;
  m_height = height;
  
  eps_wm_pollMessages(m_handle, false);
}

bool Window::poll(bool wait) {
  if (m_closed)
    return false;

  eps::ErrorHandler e;
  eps_wm_pollMessages(m_handle, wait ? getPollingTimeout() : 0);
  
  while (getEventCount()) {
    eps::Event event = getEvent();
    dispatch(event);
  }
  
  return (!m_closed);
}

void Window::close() {
  if (m_closed)
    return;
    
  m_closed = true;
  eps::ErrorHandler e;
  eps_wm_setVisible(m_handle, false);
}

void Window::dispatch(eps::Event & _event) {
  const eps_Event & event = _event.getRef();
  
  switch (event.type) {
    case EPS_EVENT_CLOSE:
      onClose();
    break;
    case EPS_EVENT_MOUSE_MOTION:
      onMouseMove(event.mouse.x, event.mouse.y, event.mouse.buttonState);
    break;
    case EPS_EVENT_MOUSE_BUTTON_DOWN:
      onMouseDown(event.mouse.x, event.mouse.y, event.mouse.buttonState);
    break;
    case EPS_EVENT_MOUSE_BUTTON_UP:
      onMouseUp(event.mouse.x, event.mouse.y, event.mouse.buttonState);
    break;
    case EPS_EVENT_MOUSE_WHEEL:
      onMouseWheel(event.mouse.x, event.mouse.y, event.mouse.buttonState, event.mouse.wheelState);
    break;
    case EPS_EVENT_KEY:
      if (event.key.pressed)
        onKeyDown(event.key.keyCode);
      else
        onKeyUp(event.key.keyCode); 
    break;
    case EPS_EVENT_TICK:
      onTick(event.tick.absoluteTick, event.tick.elapsedTicks);
    break;
  }
}

unsigned Window::getEventCount() const {
  eps::ErrorHandler e;
  return eps_event_getEventCount(m_handle);
}

eps::Event Window::getEvent() {
  eps::Event evt;
  
  if (eps_event_getEvent(m_handle, &(evt.getRef()))) {  
    return evt;
  } else {
    throw std::exception("Event queue empty");
  }
}

void Window::onClose() {
  bool cancel = false;
  
  if (m_onClose) {
    Object result = m_onClose();
    try {
      cancel = castObject<bool>(result);
    } catch (...) {
    }
  }
  
  if (!cancel) {
    close();
  }
}

void Window::onTick(unsigned absoluteTick, unsigned elapsedTicks) {
  if (m_onTick)
    m_onTick(absoluteTick, elapsedTicks);
}

void Window::onMouseMove(int x, int y, unsigned buttons) {
  if (m_onMouseMove)
    m_onMouseMove(x, y, buttons);
}
void Window::onMouseDown(int x, int y, unsigned buttons) {
  if (m_onMouseDown)
    m_onMouseDown(x, y, buttons);
}
void Window::onMouseUp(int x, int y, unsigned buttons) {
  if (m_onMouseUp)
    m_onMouseUp(x, y, buttons);
}
void Window::onMouseWheel(int x, int y, unsigned buttons, unsigned wheel) {
  if (m_onMouseWheel)
    m_onMouseWheel(x, y, buttons, wheel);
}

void Window::onKeyDown(unsigned key) {
  if (m_onKeyDown)
    m_onKeyDown(key);
}
void Window::onKeyUp(unsigned key) {
  if (m_onKeyUp)
    m_onKeyUp(key);
}

}