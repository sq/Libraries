#include <core\core.hpp>
#include <script\script.hpp>
#include <gl\gl.hpp>
#include <image\image.hpp>

#include <windows.h>

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
  _PROPERTY_RW(filename, getFilename, setFilename)
_END_CLASS

_CLASS_WRAP(ImageList, shared_ptr<ImageList>)
  .def(constructor<>())
  .def(constructor<script::Object>())
  
  .def("__tostring", &ImageList::toString)
  .def("add", &ImageList::add)
  .def("insert", &ImageList::insert)
  .def("remove", &ImageList::remove)
  .def("clear", &ImageList::clear)
  .def("getImage", &ImageList::getImage)
  .def("__call", &ImageList::getImage)

  _PROPERTY_R(count, getCount)
_END_CLASS

namespace image {

shared_ptr<Image> g_noneImage(new Image(0, 0));

shared_ptr<Image> getNone() {
  return g_noneImage;
}

void getFont(HDC & dc, HFONT & font, const char * fontName, double fontSize) {
  HDC desktopDc;
  HWND desktopWindow = GetDesktopWindow();
  desktopDc = GetDC(desktopWindow);
  dc = CreateCompatibleDC(desktopDc);
  ReleaseDC(desktopWindow, desktopDc);
  int fontHeight = (int)-ceil((fontSize * GetDeviceCaps(dc, LOGPIXELSY)) / 72.0f);
  font = CreateFontA(fontHeight, 0, 0, 0, 0, 0, 0, 0, DEFAULT_CHARSET, 0, 0, 0, 0, fontName);
  SelectObject(dc, font);
}

script::Object font_getMetrics(const char * fontName, double fontSize) {
  script::Object result = luabind::newtable(script::getActiveContext()->getContext());
  HDC dc;
  HFONT font;
  getFont(dc, font, fontName, fontSize);
  TEXTMETRIC textmetrics;
  memset(&textmetrics, 0, sizeof(textmetrics));
  GetTextMetrics(dc, &textmetrics);
  DeleteObject(font);
  DeleteDC(dc);
  
  result["ascent"] = textmetrics.tmAscent;
  result["averageCharWidth"] = textmetrics.tmAveCharWidth;
  result["breakChar"] = (int)textmetrics.tmBreakChar;
  result["charSet"] = (int)textmetrics.tmCharSet;
  result["defaultChar"] = (int)textmetrics.tmDefaultChar;
  result["descent"] = textmetrics.tmDescent;
  result["digitizedAspectX"] = textmetrics.tmDigitizedAspectX;
  result["digitizedAspectY"] = textmetrics.tmDigitizedAspectY;
  result["externalLeading"] = textmetrics.tmExternalLeading;
  result["firstChar"] = (int)textmetrics.tmFirstChar;
  result["height"] = textmetrics.tmHeight;
  result["internalLeading"] = textmetrics.tmInternalLeading;
  result["italic"] = (bool)textmetrics.tmItalic;
  result["lastChar"] = (int)textmetrics.tmLastChar;
  result["maxCharWidth"] = textmetrics.tmMaxCharWidth;
  result["overhang"] = textmetrics.tmOverhang;
  result["struckOut"] = (bool)textmetrics.tmStruckOut;
  result["underlined"] = (bool)textmetrics.tmUnderlined;
  result["weight"] = textmetrics.tmWeight;
  
  return result;
}

script::Object font_getCharacter(int character, const char * fontName, double fontSize) {
  script::Object result;
  HDC dc;
  HFONT font;
  getFont(dc, font, fontName, fontSize);
  
  GLYPHMETRICS metrics;
  MAT2 transform;
  memset(&metrics, 0, sizeof(metrics));
  memset(&transform, 0, sizeof(transform));
  transform.eM11.value = 1;
  transform.eM22.value = 1;
  // fetch metrics
  GetGlyphOutline(dc, character, GGO_METRICS, &metrics, sizeof(metrics), 0, &transform);
  // fetch size of glyph bitmap
  unsigned size = GetGlyphOutline(dc, character, GGO_GRAY8_BITMAP, &metrics, 0, 0, &transform);
  if (size) {
    unsigned char * data = new unsigned char[size];
    memset(data, 0, sizeof(data));
    GetGlyphOutline(dc, character, GGO_GRAY8_BITMAP, &metrics, size, data, &transform);
    unsigned width = ((metrics.gmBlackBoxX + 3) / 4) * 4;
    unsigned height = (size / width);
    shared_ptr<Image> img(new Image(width, height));
    unsigned char * out = (unsigned char *)img->getData();
    unsigned char * in = data;
    for (unsigned y = 0; y < height; y++) {
      for (unsigned x = 0; x < width; x++) {
        out[0] = out[1] = out[2] = 255;
        out[3] = (*in) * 255 / 65;
        in += 1;
        out += 4;
      }
    }
    delete[] data;
    result = script::Object(script::getActiveContext()->getContext(), img);
  } else {
    shared_ptr<Image> img(new Image(0, 0));
    result = script::Object(script::getActiveContext()->getContext(), img);
  }
  script::Object _metrics = luabind::newtable(script::getActiveContext()->getContext());
  _metrics["blackBoxX"] = metrics.gmBlackBoxX;
  _metrics["blackBoxY"] = metrics.gmBlackBoxY;
  _metrics["cellIncX"] = metrics.gmCellIncX;
  _metrics["cellIncY"] = metrics.gmCellIncY;
  _metrics["glyphOriginX"] = metrics.gmptGlyphOrigin.x;
  _metrics["glyphOriginY"] = metrics.gmptGlyphOrigin.y;
  result["character"] = character;
  result["metrics"] = _metrics;
  
  DeleteObject(font);
  DeleteDC(dc);
  return result;
}

void registerNamespace(shared_ptr<script::Context> context) {
  luabind::module(context->getContext(), "font") [
    luabind::def("getCharacter", font_getCharacter),
    luabind::def("getMetrics", font_getMetrics)
  ];

  context->registerClass<Image>();
  context->registerHolder<Image, weak_ptr<Image>>();
  
  context->registerClass<ImageList>();
  context->registerHolder<ImageList, weak_ptr<ImageList>>();
  
  if (script::getObjectType(context->getGlobal("image")) != LUA_TTABLE)
    context->setGlobal("image", luabind::newtable(context->getContext()));
  context->setGlobal("image.none", g_noneImage);
}

}

namespace image {

Image::Image(const char * filename) :
  m_filename(filename)
{
  m_image = corona::OpenImage(filename, corona::PF_R8G8B8A8);
  if (!m_image) {
    char buffer[512];
    strcpy(buffer, "Unable to load \"");
    strcat(buffer, filename);
    strcat(buffer, "\".");
    throw std::exception(buffer);
  }
  m_width = m_image->getWidth();
  m_height = m_image->getHeight();
}

Image::Image(int width, int height) {
  m_image = corona::CreateImage(width, height, corona::PF_R8G8B8A8);
  if (!m_image)
    throw std::exception("Unable to create image with specified parameters");
  m_width = m_image->getWidth();
  m_height = m_image->getHeight();
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
  return m_width;
}

int Image::getHeight() const {
  return m_height;
}

const std::string & Image::getFilename() const {
  return m_filename;
}

void Image::setFilename(const char * newFilename) {
  m_filename = std::string(newFilename);
}

void * Image::getData() const {
  return m_image->getPixels();
}

std::string Image::toString() const {
  std::stringstream buf;
  if ((m_width == 0) && (m_height == 0)) {
    buf << "<Image:none>";
  } else {
    buf << "<Image:" << core::ptrToString(this) << ">";
  }
  return buf.str();
}

}