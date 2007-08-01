#include <core\core.hpp>
#include <script\script.hpp>
#include <gl\gl.hpp>

using namespace gl;
using namespace wm;

namespace gl {

eps_OpenGLContext * g_currentContext = 0;

GLContext::GLContext(wm::Window * parent, eps_OpenGLContext * handle) :
  m_parent(parent),
  m_handle(handle)
{
  gl::initialize();
}

GLContext::~GLContext() {
  if (g_currentContext == m_handle)
    g_currentContext = 0;

  gl::uninitialize();
}

eps_OpenGLContext * GLContext::getHandle() const {
  return m_handle;
}

std::string GLContext::toString() const {
  std::stringstream buf;
  buf << "<GLContext:" << core::ptrToString(this) << ">";
  return buf.str();
}

void GLContext::makeCurrent() const {
  if (g_currentContext != m_handle) {
    eps_opengl_setCurrent(m_handle);
    g_currentContext = m_handle;
  }
}

void GLContext::clear() {
  makeCurrent();
  
  glClear(
    GL_COLOR_BUFFER_BIT	| GL_DEPTH_BUFFER_BIT | GL_ACCUM_BUFFER_BIT | GL_STENCIL_BUFFER_BIT
  );
}

void GLContext::flip() {
  makeCurrent();
  
  glFlush();
  eps_opengl_swapBuffers(m_handle);
}

void GLContext::getClearColor(float & red, float & green, float & blue, float & alpha) const {
  makeCurrent();
  
  float buf[4];
  glGetFloatv(GL_COLOR_CLEAR_VALUE, buf);
  
  red = buf[0];
  green = buf[1];
  blue = buf[2];
  alpha = buf[3];
}

void GLContext::setClearColor(float red, float green, float blue, float alpha) {
  makeCurrent();
  
  glClearColor(red, green, blue, alpha);
}

bool GLContext::getVSync() const {
  makeCurrent();
  
  if (WGLEW_EXT_swap_control) {
    int interval = wglGetSwapIntervalEXT();
    return (interval == 1);
  } else {
    return false;
  }
}

void GLContext::setVSync(bool vsync) {
  makeCurrent();
  
  if (WGLEW_EXT_swap_control) {
    wglSwapIntervalEXT(vsync ? 1 : 0);
  }
}

}