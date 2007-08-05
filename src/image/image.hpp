#ifndef _FR_IMAGE
#define _FR_IMAGE

#include "libs.hpp"

namespace image {

  class Image : public enable_shared_from_this<Image> {
    friend class gl::GLTexture;
  
    int m_width, m_height;
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
    void * getData() const;
  };
  
  class ImageList : public enable_shared_from_this<ImageList> {
    typedef vector<shared_ptr<Image>> TImages;
    
    // void postConstruct(script::Context * context);
  
    TImages m_images;
    
  public:
    ImageList();
    ImageList(script::Object images);
    
    ~ImageList();
    
    int add(shared_ptr<Image> value);
    void insert(int position, shared_ptr<Image> value);
    void remove(int index);
    void clear();
    TImages::iterator at(int index);
    shared_ptr<Image> getImage(int index);
    
    // script::Object indexHandler(script::Object key);
    
    int getCount() const;    
    string toString() const;    
  };
  
  void registerNamespace(shared_ptr<script::Context> context);
  
}

#endif