#include <core\core.hpp>
#include <script\script.hpp>
#include <gl\gl.hpp>
#include <image\image.hpp>

using namespace image;

namespace image {

ImageList::ImageList() {
}

ImageList::ImageList(script::Object images) {
  using namespace script;
  using namespace luabind;

  if (!isTable(images))
    throw std::exception("images must be a table");
    
  for (iterator iter(images), end; iter != end; ++iter) {
    Object item = *iter;
    try {
      shared_ptr<Image> image = castObject<shared_ptr<Image>>(item);
      add(item);
    } catch (...) {
      try {
        std::string filename = castObject<std::string>(item);
        shared_ptr<Image> image(new Image(filename.c_str()));
        script::Object new_item(images.interpreter(), image);
        add(new_item);
      } catch (...) {
      }
    }
  }
}

ImageList::~ImageList() {
}

int ImageList::add(ImageList::TItem value) {
  try {
    script::castObject<TRawItem>(value);
  } catch (luabind::cast_failed ex) {
    throw std::exception("ImageLists can only contain Images");
  }
  int pos = (int)m_images.size();
  m_images.push_back(value);
  return pos;
}

void ImageList::insert(int index, ImageList::TItem value) {
  try {
    script::castObject<TRawItem>(value);
  } catch (luabind::cast_failed ex) {
    throw std::exception("ImageLists can only contain Images");
  }
  if (index > (int)m_images.size()) {
    m_images.resize(index - 1);
    m_images.push_back(value);
  } else {
    m_images.insert(at(index), value);
  }
}

void ImageList::remove(int index) {
  m_images.erase(at(index));
}

void ImageList::clear() {
  m_images.clear();
}

ImageList::TImages::iterator ImageList::at(int index) {
  if ((index < 1) || (index > (int)m_images.size()))
    throw std::exception("1 >= index < count");
  return m_images.begin() + (index - 1);
}

ImageList::TItem ImageList::getImage(int index) {
  TImages::iterator iter = at(index);
  TItem item = *iter;
  return item;
}

int ImageList::getCount() const {
  return (int)m_images.size();
}

std::string ImageList::toString() const {
  std::stringstream buf;
  buf << "<ImageList:" << core::ptrToString(this) << ">";
  return buf.str();
}

}