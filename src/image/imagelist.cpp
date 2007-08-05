#include <core\core.hpp>
#include <script\script.hpp>
#include <gl\gl.hpp>
#include <image\image.hpp>

using namespace image;

namespace image {

ImageList::ImageList() {
  /*
  struct PostConstructTailCall : public script::TailCall {
    ImageList * m_this;
    
    PostConstructTailCall(ImageList * _this) : m_this(_this) {}
    void invoke(script::Context * context) {
      m_this->postConstruct(context);
    }  
  };
  
  script::tailCall(new PostConstructTailCall(this));
  */
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
      add(image);
    } catch (...) {
      try {
        std::string filename = castObject<std::string>(item);
        shared_ptr<Image> image(new Image(filename.c_str()));
        add(image);
      } catch (...) {
      }
    }
  }
}

ImageList::~ImageList() {
}

/*
script::Object ImageList::indexHandler(script::Object key) {
  if (script::getObjectType(key) == LUA_TNUMBER) {
    int i = script::castObject<int>(key);
    script::Object result = script::getObject(getImage(i));
    return result;
  } else {
    shared_ptr<ImageList> _this = shared_from_this();
    script::Object metatable = script::getObjectMetatable(_this);
    script::Object defaultHandler = metatable["__luabind_index"];
    return defaultHandler(_this, key);
  }
}

void ImageList::postConstruct(script::Context * context) {
  script::Object metatable = script::getObjectMetatable(shared_from_this());
  script::Object alreadyOverloaded = metatable["__luabind_index"];
  if (script::getObjectType(alreadyOverloaded) == LUA_TFUNCTION)
    return;
  script::Object old_handler = metatable["__index"];
  metatable["__luabind_index"] = old_handler;
  metatable["__index"] = script::getObjectMember(shared_from_this(), "indexHandler");
}
*/

int ImageList::add(shared_ptr<Image> value) {
  int pos = (int)m_images.size();
  m_images.push_back(value);
  return pos;
}

void ImageList::insert(int index, shared_ptr<Image> value) {
  m_images.insert(at(index), value);
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

shared_ptr<Image> ImageList::getImage(int index) {
  TImages::iterator iter = at(index);
  return *iter;
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