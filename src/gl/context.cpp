#include <core\core.hpp>
#include <script\script.hpp>
#include <gl\gl.hpp>
#include <image\image.hpp>

using namespace gl;
using namespace image;
using namespace wm;

namespace gl {

eps_OpenGLContext * g_currentContext = 0;

GLContext::GLContext(wm::Window * parent, eps_OpenGLContext * handle) :
  m_parent(parent),
  m_handle(handle)
{
  gl::initialize();
  
  glMatrixMode(GL_PROJECTION);
  glLoadIdentity();
  glOrtho(0, parent->getWidth(), 0, parent->getHeight(), -1, 1);
  glMatrixMode(GL_MODELVIEW);
  glLoadIdentity();
  glViewport(0, 0, parent->getWidth(), parent->getHeight());
  glScissor(0, 0, parent->getWidth(), parent->getHeight());  
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
  
  float buf[4] = {0, 0, 0, 0};
  glGetFloatv(GL_COLOR_CLEAR_VALUE, buf);
  
  red = floor(buf[0] * 255.0f);
  green = floor(buf[1] * 255.0f);
  blue = floor(buf[2] * 255.0f);
  alpha = floor(buf[3] * 255.0f);
}

void GLContext::getPixel(int x, int y, float & red, float & green, float & blue, float & alpha) const {
  makeCurrent();
  
  float buf[4] = {0, 0, 0, 0};
  glReadPixels(x, y, 1, 1, GL_RGBA, GL_FLOAT, &buf);
  
  red = floor(buf[0] * 255.0f);
  green = floor(buf[1] * 255.0f);
  blue = floor(buf[2] * 255.0f);
  alpha = floor(buf[3] * 255.0f);
}

void GLContext::setClearColor(float red, float green, float blue, float alpha) {
  makeCurrent();
  
  glClearColor(red / 255.0f, green / 255.0f, blue / 255.0f, alpha / 255.0f);
}

void GLContext::draw(int drawMode, script::Object vertices) {
  using namespace script;
  using namespace luabind;
  makeCurrent();
  
  if (!isTable(vertices))
    throw std::exception("vertices must be a table");
  
  glBegin(drawMode);
  
  for (iterator iter(vertices), end; iter != end; ++iter) {
    Object vertex = *iter;

    if (!isTable(vertex))
      throw std::exception("vertices must be a table containing tables");
      
    Object coords = vertex[1];
    Object color = vertex[2];
    
    if (isTable(color)) {
      float c[4] = {0, 0, 0, 0};
      unsigned n = unpackTable(color, c, 4);
      for (int i = 0; i < 4; i++)
        c[i] = c[i] / 255.0f;
        
      if (n == 4)
        glColor4fv(c);
      else if (n == 3)
        glColor3fv(c);
      else if (n == 2)
        glColor4f(c[0], c[0], c[0], c[1]);
      else if (n == 1)
        glColor3f(c[0], c[0], c[0]);
    }
    
    if (isTable(coords)) {
      float v[4] = {0, 0, 0, 0};
      unsigned n = unpackTable(coords, v, 4);
      if (n == 4)
        glVertex4fv(v);
      else if (n == 3)
        glVertex3fv(v);
      else if (n == 2)
        glVertex2fv(v);
    }
  }
  
  glEnd();
}

void GLContext::drawImage(shared_ptr<image::Image> image, int x, int y) {
  glPixelZoom(1.0f, -1.0f);
  glRasterPos2i(x, y + image->getHeight());
  glDrawPixels(image->getWidth(), image->getHeight(), GL_RGBA, GL_UNSIGNED_BYTE, image->getData());
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