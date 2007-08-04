#include <core\core.hpp>
#include <script\script.hpp>
#include <gl\gl.hpp>
#include <image\image.hpp>

using namespace image;

_CLASS_WRAP(Image, shared_ptr<Image>)
  .def(constructor<const char *>())
  .def(constructor<int, int>())

  .def("__tostring", &Image::toString)
  .def("getTexture", (shared_ptr<gl::GLTexture>(Image::*)())&Image::getTexture)
  .def("getTexture", (shared_ptr<gl::GLTexture>(Image::*)(shared_ptr<gl::GLContext>))&Image::getTexture)
  .def("getPixel", &Image::getPixel, 
    pure_out_value(_4) + pure_out_value(_5) + pure_out_value(_6) + pure_out_value(_7)
  )
  .def("setPixel", (void(Image::*)(int, int, int, int, int, int))&Image::setPixel)
  .def("setPixel", (void(Image::*)(int, int, script::Object))&Image::setPixel)
  .def("save", &Image::save)

  _PROPERTY_R(width, getWidth)
  _PROPERTY_R(height, getHeight)
_END_CLASS

_CLASS_WRAP(ImageList, shared_ptr<ImageList>)
  .def(constructor<>())
  
  .def("__tostring", &ImageList::toString)
  .def("add", &ImageList::add)
  .def("remove", &ImageList::remove)
  .def("getImage", &ImageList::getImage)
  .def("__call", &ImageList::getImage)

  .def_readwrite("images", &ImageList::images, return_stl_iterator)
  _PROPERTY_R(count, getCount)
_END_CLASS

namespace image {

void registerNamespace(shared_ptr<script::Context> context) {
  /*
  module(context->getContext(), "image") [
  ];
  */

  context->registerClass<Image>();
  context->registerHolder<Image, weak_ptr<Image>>();
  
  context->registerClass<ImageList>();
  context->registerHolder<ImageList, weak_ptr<ImageList>>();
}

}

namespace image {

Image::Image(const char * filename) {
  m_image = corona::OpenImage(filename, corona::PF_R8G8B8A8);
  if (!m_image) {
    char buffer[512];
    strcpy(buffer, "Unable to load \"");
    strcat(buffer, filename);
    strcat(buffer, "\".");
    throw std::exception(buffer);
  }
}

Image::Image(int width, int height) {
  m_image = corona::CreateImage(width, height, corona::PF_R8G8B8A8);
  if (!m_image)
    throw std::exception("Unable to create image with specified parameters");
}

Image::~Image() {
  if (m_image) {
    delete m_image;
    m_image = 0;
  }
}

bool Image::save(const char * filename) {
  return corona::SaveImage(filename, corona::FF_AUTODETECT, m_image);
}

void Image::getPixel(int x, int y, int & red, int & green, int & blue, int & alpha) const {
  if ((x < 0) || (y < 0) || (x >= m_image->getWidth()) || (y >= m_image->getHeight()))
    throw std::exception("(0 >= x < width) && (0 >= y < height)");

  void * pixels = m_image->getPixels();
  unsigned char * pixel = 
    reinterpret_cast<unsigned char *>(pixels) + 
    (((y * m_image->getWidth()) + x) * 4);
  
  red = pixel[0];
  green = pixel[1];
  blue = pixel[2];
  alpha = pixel[3];
}

void Image::setPixel(int x, int y, script::Object color) {
  int c[4] = {0, 0, 0, 0};
  unsigned n = script::unpackTable(color, c, 4);
  
  if (n != 4)
    throw std::exception("Expected {r, g, b, a}");
    
  setPixel(x, y, c[0], c[1], c[2], c[3]);
}

void Image::setPixel(int x, int y, int red, int green, int blue, int alpha) {
  if ((x < 0) || (y < 0) || (x >= m_image->getWidth()) || (y >= m_image->getHeight()))
    throw std::exception("(0 >= x < width) && (0 >= y < height)");

  void * pixels = m_image->getPixels();
  unsigned char * pixel = 
    reinterpret_cast<unsigned char *>(pixels) + 
    (((y * m_image->getWidth()) + x) * 4);
    
  pixel[0] = red & 0xFF;
  pixel[1] = green & 0xFF;
  pixel[2] = blue & 0xFF;
  pixel[3] = alpha & 0xFF;
}

shared_ptr<gl::GLTexture> Image::getTexture(shared_ptr<gl::GLContext> context) {
  if (m_ownedTexture)
    return m_ownedTexture;
  else if (!m_texture.expired())
    return m_texture.lock();
  else {
    m_ownedTexture = shared_ptr<gl::GLTexture>(new gl::GLTexture(context, shared_from_this()));
    return m_ownedTexture;
  }
}

shared_ptr<gl::GLTexture> Image::getTexture() {
  if (m_ownedTexture)
    return m_ownedTexture;
  else if (!m_texture.expired())
    return m_texture.lock();
  else
    return shared_ptr<gl::GLTexture>();
}

void Image::setTexture(shared_ptr<gl::GLTexture> texture) {
  m_texture = weak_ptr<gl::GLTexture>(texture);
}

int Image::getWidth() const {
  return m_image->getWidth();
}

int Image::getHeight() const {
  return m_image->getHeight();
}

void * Image::getData() const {
  return m_image->getPixels();
}

std::string Image::toString() const {
  std::stringstream buf;
  buf << "<Image:" << core::ptrToString(this) << ">";
  return buf.str();
}

}