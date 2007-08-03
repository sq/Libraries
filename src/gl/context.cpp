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
  glOrtho(0, parent->getWidth(), parent->getHeight() - 1, 0, -1, 1);
  glMatrixMode(GL_MODELVIEW);
  glLoadIdentity();
  glViewport(0, 0, parent->getWidth(), parent->getHeight());
  glScissor(0, 0, parent->getWidth(), parent->getHeight());  
  glEnable(GL_BLEND);
  if (GLEW_EXT_blend_minmax)
    glBlendEquationEXT(GL_FUNC_ADD_EXT);
  glBlendFunc(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);
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
  glReadPixels(x, m_parent->getHeight() - y - 1, 1, 1, GL_RGBA, GL_FLOAT, &buf);
  
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
  script::Object nil;
  draw(drawMode, vertices, nil);
}

void GLContext::addTexture(shared_ptr<GLTexture> texture) {
  m_textures.push_back(texture);
}

void GLContext::removeTexture(GLTexture * texture) {
  for (unsigned i = 0; i < m_textures.size(); i++) {
    weak_ptr<GLTexture> item = m_textures[i];
    if (!item.expired()) {
      shared_ptr<GLTexture> s_item = item.lock();
      if (s_item.get() == texture) {
        m_textures.erase(m_textures.begin() + i);
        break;
      }
    }
  }
}

void GLContext::draw(int drawMode, script::Object vertices, script::Object textures) {
  using namespace script;
  using namespace luabind;
  makeCurrent();
  
  script::Context * sc = getActiveContext();
  
  if (!isTable(vertices))
    throw std::exception("vertices must be a table");
    
  if (isTable(textures)) {
    textures.push(sc->getContext());
    size_t size = lua_objlen(sc->getContext(), -1);
    lua_pop(sc->getContext(), 1);
    
    for (unsigned i = 1; i <= size; i++) {
      glActiveTextureARB(GL_TEXTURE0_ARB + (i - 1));
      Object item = textures[i];
      try {
        shared_ptr<GLTexture> texture = castObject<shared_ptr<GLTexture>>(item);
        glEnable(GL_TEXTURE_2D);
        glBindTexture(GL_TEXTURE_2D, texture->getHandle());
        glTexEnvi(GL_TEXTURE_ENV, GL_TEXTURE_ENV_MODE, GL_MODULATE);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_NEAREST);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_NEAREST);
      } catch (...) {
        glDisable(GL_TEXTURE_2D);
      }
    }
  } else {
    glActiveTextureARB(GL_TEXTURE0_ARB);
    glDisable(GL_TEXTURE_2D);
  }
  
  glBegin(drawMode);
  
  for (iterator iter(vertices), end; iter != end; ++iter) {
    Object vertex = *iter;

    if (!isTable(vertex))
      throw std::exception("vertices must be a table containing tables");
      
    vertex.push(sc->getContext());
    size_t size = lua_objlen(sc->getContext(), -1);
    lua_pop(sc->getContext(), 1);
      
    Object coords = vertex[1];
    Object color = vertex[2];

    if ((size > 1) && isTable(color)) {
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
    
    if ((size > 2)) {
      for (unsigned i = 3; i <= size; i++) {
        Object item = vertex[i];
        if (isTable(item)) {
          float t[2] = {0, 0};
          if (2 == unpackTable(item, t, 2))
            glMultiTexCoord2fARB(GL_TEXTURE0_ARB + (i - 3), t[0], t[1]); 
        }
      }
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

  glActiveTextureARB(GL_TEXTURE0_ARB);
  glDisable(GL_TEXTURE_2D);
}

void GLContext::drawImage(shared_ptr<image::Image> image, int x, int y) {
  glPixelZoom(1.0f, -1.0f);
  glRasterPos2i(x, y);
  glDrawPixels(image->getWidth(), image->getHeight(), GL_RGBA, GL_UNSIGNED_BYTE, image->getData());
  glPixelZoom(1.0f, 1.0f);
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