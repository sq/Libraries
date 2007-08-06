#ifndef _FR_IMAGE
#define _FR_IMAGE

#include "libs.hpp"

namespace image {

  class Image : public enable_shared_from_this<Image> {
    friend class gl::GLTexture;
  
    int m_width, m_height;
    std::string m_filename;
    corona::Image * m_image;
    weak_ptr<gl::GLTexture> m_texture;
    shared_ptr<gl::GLTexture> m_ownedTexture;
    
  protected:
    void setTexture(shared_ptr<gl::GLTexture> texture);
    
  public:
    Image(const char * filename);
    Image(int width, int height);
    
    ~Image();
    
    bool save(const char * filename);
    void getPixel(int x, int y, int & red, int & green, int & blue, int & alpha) const;
    void setPixel(int x, int y, script::Object color);
    void setPixel(int x, int y, int red, int green, int blue, int alpha);
    shared_ptr<gl::GLTexture> getTexture(shared_ptr<gl::GLContext> context);
    shared_ptr<gl::GLTexture> getTexture();
    string toString() const;

    int getWidth() const;
    int getHeight() const;
    const std::string & getFilename() const;
    void setFilename(const char * newFilename);
    void * getData() const;
  };
  
  class ImageList : public enable_shared_from_this<ImageList> {
    typedef script::Object TItem;
    typedef shared_ptr<Image> TRawItem;
    typedef vector<TItem> TImages;
  
    TImages m_images;
    
  public:
    ImageList();
    ImageList(script::Object images);
    
    ~ImageList();
    
    int add(TItem value);
    void insert(int position, TItem value);
    void remove(int index);
    void clear();
    TImages::iterator at(int index);
    TItem getImage(int index);
    
    int getCount() const;    
    string toString() const;    
  };
  
  shared_ptr<Image> getNone();
  void registerNamespace(shared_ptr<script::Context> context);
  
}

#endif