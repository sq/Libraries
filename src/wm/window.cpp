#include <core\core.hpp>
#include <script\script.hpp>
#include <wm\wm.hpp>

namespace wm {

int g_refCount = 0;

static void initialize() {
  g_refCount += 1;
  
  eps_wm_initialize();
}

static void uninitialize() {
  assert(g_refCount);
  
  g_refCount -= 1;
  
  eps_wm_shutDown();
}

luabind::object poll(lua_State * L) {
  luabind::object result;
  
  return result;
}

void registerNamespace(shared_ptr<script::Context> context) {
  module(context->getContext(), "wm") [
    def("poll", &poll, raw(_1))
  ];

  context->registerClass<Window>();
  context->registerHolder<Window, weak_ptr<Window>>();
}

Window::Window(unsigned width, unsigned height, unsigned flags) :
  m_handle(0),
  m_width(width),
  m_height(height),
  m_closed(false)
{
  wm::initialize();

  eps::ErrorHandler e;
  m_handle = eps_wm_createWindow(width, height, flags);
  
  lua_State * L = script::getActiveContext();
  
  struct PostConstructTailCall : public script::TailCall {
    Window * m_this;
    
    PostConstructTailCall(Window * _this) : m_this(_this) {}
    void invoke(lua_State * L) {
      m_this->postConstruct(L);
    }  
  };
  
  script::tailCall(new PostConstructTailCall(this));
}

Window::~Window() {
  if (m_handle) {
    eps_wm_destroyWindow(m_handle);
    m_handle = 0;
  }

  lua_State * L = script::getActiveContext();
  if (L) {
    object wm = globals(L)["wm"];
    if (type(wm) != LUA_TTABLE)
      return;
    object windows = wm["windows"];
    if (type(windows) != LUA_TTABLE)
      return;
    windows[core::ptrToString(this)] = luabind::object();
  }

  wm::uninitialize();
}

void Window::postConstruct(lua_State * L) {
  if (L) {
    object wm = globals(L)["wm"];
    if (type(wm) != LUA_TTABLE)
      return;
    object windows = wm["windows"];
    if (type(windows) != LUA_TTABLE) {
      windows = newtable(L);
      wm["windows"] = windows;
    }
    windows[core::ptrToString(this)] = weak_ptr<Window>(shared_from_this());
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

void Window::poll() {
  eps::ErrorHandler e;
  eps_wm_pollMessages(m_handle, false);
  
  while (getEventCount()) {
    eps::Event event = getEvent();
    dispatch(event);
  }
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
    luabind::object result = m_onClose();
    cancel = object_cast<bool>(result);
  }
  
  if (!cancel) {
    close();
  }
}

void Window::onMouseMove(int x, int y, unsigned buttons) {
}
void Window::onMouseDown(int x, int y, unsigned buttons) {
}
void Window::onMouseUp(int x, int y, unsigned buttons) {
}
void Window::onMouseWheel(int x, int y, unsigned buttons, unsigned wheel) {
}

void Window::onKeyDown(unsigned key) {
}
void Window::onKeyUp(unsigned key) {
}

}