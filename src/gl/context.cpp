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
  m_handle(handle),
  m_state()
{
  gl::initialize();
  
  int w = parent->getWidth();
  int h = parent->getHeight();
  glMatrixMode(GL_PROJECTION);
  glLoadIdentity();
  glMatrixMode(GL_MODELVIEW);
  glLoadIdentity();
  glScalef(2.0f / float(w), -2.0f / float(h), 1.0f);
  glTranslatef(-(float(w) / 2.0f), -(float(h) / 2.0f), 0.0f);
  glViewport(0, 0, w, h);
  glScissor(0, 0, w, h);
  glEnable(GL_BLEND);
  if (GLEW_EXT_blend_minmax)
    glBlendEquationEXT(GL_FUNC_ADD_EXT);
  glBlendFunc(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);
  glClearColor(0.0f, 0.0f, 0.0f, 1.0f);
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

void GLContext::bindTexture(int stage, GLTexture * texture) {
  glActiveTextureARB(GL_TEXTURE0_ARB + stage);
  if (m_state.textures[stage] == texture)
    return;
  if (texture) {
    glEnable(GL_TEXTURE_2D);
    glBindTexture(GL_TEXTURE_2D, texture->getHandle());
    glTexEnvi(GL_TEXTURE_ENV, GL_TEXTURE_ENV_MODE, GL_MODULATE);
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_NEAREST);
    m_state.textures[stage] = texture;
  } else {
    glDisable(GL_TEXTURE_2D);
    m_state.textures[stage] = 0;
  }
}

void GLContext::unpackColor(script::Object color) {
  if (!script::isTable(color))
    return;

  float c[4] = {0, 0, 0, 0};
  unsigned n = script::unpackTable(color, c, 4);
  for (unsigned i = 0; i < n; i++)
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
      Object item = textures[i];
      try {
        shared_ptr<GLTexture> texture = castObject<shared_ptr<GLTexture>>(item);
        bindTexture(i - 1, texture.get());
      } catch (...) {
        bindTexture(i - 1, 0);
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

    if (size > 1)
      unpackColor(color);
    
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

void GLContext::drawPixel(float x, float y, script::Object color) {
  makeCurrent();
  glBegin(GL_POINTS);
  unpackColor(color);
  glVertex2f(x + 0.375f, y + 0.375f);
  glEnd();
}

void GLContext::drawLine(float x1, float y1, float x2, float y2, script::Object color) {
  makeCurrent();
  glPushMatrix();
  glTranslatef(0.375f, 0.375f, 0);  glBegin(GL_LINES);
  glBegin(GL_LINES);
  unpackColor(color);
  glVertex2f(x1, y1);
  glVertex2f(x2, y2);
  glEnd();
  glPopMatrix();
}

void GLContext::drawRect(float x1, float y1, float x2, float y2, bool filled, script::Object color) {
  makeCurrent();
  glPushMatrix();
  glTranslatef(0.375f, 0.375f, 0);
  if (filled) {
    x2 += 1;
    y2 += 1;
    glBegin(GL_QUADS);
  } else {
    glBegin(GL_LINE_LOOP);
  }
  unpackColor(color);
  glVertex2f(x1, y1);
  glVertex2f(x2, y1);
  glVertex2f(x2, y2);
  glVertex2f(x1, y2);
  glEnd();
  glPopMatrix();
}

void GLContext::drawImage(shared_ptr<image::Image> image, int x, int y) { 
  int w = image->getWidth();
  int h = image->getHeight();
  shared_ptr<GLTexture> texture = image->getTexture(shared_from_this());
  bindTexture(0, texture.get());
  
  glBegin(GL_QUADS);
  
  glColor4f(1.0f, 1.0f, 1.0f, 1.0f);
  glTexCoord2f(texture->m_u0, texture->m_v0);
  glVertex2i(x, y);
  glTexCoord2f(texture->m_u1, texture->m_v0);
  glVertex2i(x + w, y);
  glTexCoord2f(texture->m_u1, texture->m_v1);
  glVertex2i(x + w, y + h);
  glTexCoord2f(texture->m_u0, texture->m_v1);
  glVertex2i(x, y + h);
  
  glEnd();
  
  bindTexture(0, 0);
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