#include <core\core.hpp>
#include <script\script.hpp>
#include <gl\gl.hpp>
#include <image\image.hpp>

using namespace gl;
using namespace image;
using namespace wm;

namespace gl {

GLTexture::GLTexture(shared_ptr<GLContext> context, unsigned handle, int width, int height) :
  m_handle(handle),
  m_context(context),
  m_u0(0), m_v0(0), m_u1(0), m_v1(0),
  m_width(width), m_height(height)
{
  int w2 = core::powerOfTwo(width);
  int h2 = core::powerOfTwo(height);
  m_u0 = (0.0f) / (float)w2;
  m_v0 = (0.0f) / (float)h2;
  m_u1 = (width - 0.5f) / (float)w2;
  m_v1 = (height - 0.5f) / (float)h2;
  doTailCall();
}

GLTexture::GLTexture(shared_ptr<GLContext> context, shared_ptr<image::Image> image) :
  m_handle(0),
  m_image(image),
  m_context(context),
  m_u0(0), m_v0(0), m_u1(0), m_v1(0),
  m_width(0), m_height(0)
{
  glGenTextures(1, &m_handle);
  upload(image.get());
  doTailCall();
}

GLTexture::~GLTexture() {
  if (!m_context.expired()) {
    shared_ptr<GLContext> context = m_context.lock();
    context->removeTexture(this);
  }
  if (m_handle) {
    glDeleteTextures(1, &m_handle);
    m_handle = 0;
  }
}

void GLTexture::postConstruct(script::Context * context) {
  shared_ptr<GLContext> s_context = m_context.lock();
  s_context->addTexture(shared_from_this());  
  
  if (m_image)
    m_image->setTexture(shared_from_this());
}

void GLTexture::doTailCall() {
  struct PostConstructTailCall : public script::TailCall {
    GLTexture * m_this;
    
    PostConstructTailCall(GLTexture * _this) : m_this(_this) {}
    void invoke(script::Context * context) {
      m_this->postConstruct(context);
    }  
  };
  
  script::tailCall(new PostConstructTailCall(this));
}

unsigned GLTexture::getHandle() const {
  return m_handle;
}

int GLTexture::getWidth() const {
  return m_width;
}

int GLTexture::getHeight() const {
  return m_height;
}

float GLTexture::getU0() const {
  return m_u0;
}

float GLTexture::getV0() const {
  return m_v0;
}

float GLTexture::getU1() const {
  return m_u1;
}

float GLTexture::getV1() const {
  return m_v1;
}

float GLTexture::getU(float x) const {
  return (m_u0) + ((m_u1 - m_u0) * (x / m_width));
}

float GLTexture::getV(float y) const {
  return (m_v0) + ((m_v1 - m_v0) * (y / m_height));
}

std::string GLTexture::toString() const {
  std::stringstream buf;
  buf << "<GLTexture:" << core::ptrToString(this) << ">";
  return buf.str();
}

void GLTexture::upload(image::Image * image) {
  shared_ptr<GLContext> context = m_context.lock();
  context->makeCurrent();
  int w = image->getWidth();
  int h = image->getHeight();
  if ((w < 1) || (h < 1))
    return;
  int tw = core::powerOfTwo(w);
  int th = core::powerOfTwo(h);
  m_width = w;
  m_height = h;
  m_u0 = (0.0f) / (float)tw;
  m_v0 = (0.0f) / (float)th;
  m_u1 = (w) / (float)tw;
  m_v1 = (h) / (float)th;
  glEnable(GL_TEXTURE_2D);
  glBindTexture(GL_TEXTURE_2D, m_handle);

  glTexImage2D(
    GL_TEXTURE_2D, 0, GL_RGBA8, tw, th,
    0, GL_RGBA, GL_UNSIGNED_BYTE, 0
  );
  if (GLenum e = glGetError())
    throw std::exception("create failed");
    
  if (image->getPitch() == image->getWidth()) {
    glTexSubImage2D(
      GL_TEXTURE_2D, 0, 0, 0, w, h, 
      GL_RGBA, GL_UNSIGNED_BYTE, image->getPixelAddress(0, 0)
    );
    if (GLenum e = glGetError())
      throw std::exception("upload failed");
  } else {
    for (int y = 0; y < h; y++) {
      glTexSubImage2D(
        GL_TEXTURE_2D, 0, 0, y, w, 1, 
        GL_RGBA, GL_UNSIGNED_BYTE, image->getPixelAddress(0, y)
      );
      if (GLenum e = glGetError())
        throw std::exception("upload failed");
    }
  }
    
  glDisable(GL_TEXTURE_2D);
}

}