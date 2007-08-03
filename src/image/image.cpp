#include <core\core.hpp>
#include <script\script.hpp>
#include <image\image.hpp>

using namespace image;

_CLASS_WRAP(Image, shared_ptr<Image>)
  .def(constructor<const char *>())
  .def(constructor<int, int>())

  .def("__tostring", &Image::toString)

  _PROPERTY_R(width, getWidth)
  _PROPERTY_R(height, getHeight)
_END_CLASS

namespace image {

void registerNamespace(shared_ptr<script::Context> context) {
  /*
  module(context->getContext(), "image") [
  ];
  */

  context->registerClass<Image>();
  context->registerHolder<Image, weak_ptr<Image>>();
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