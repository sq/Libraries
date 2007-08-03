#ifndef _FR_IMAGE
#define _FR_IMAGE

#include "libs.hpp"

namespace image {

  class Image {
    corona::Image * m_image;
    
  public:
    Image(const char * filename);
    Image(int width, int height);
    
    ~Image();
    
    std::string toString() const;

    int getWidth() const;
    int getHeight() const;
    void * getData() const;
  };
  
  void registerNamespace(shared_ptr<script::Context> context);
  
}

#endif