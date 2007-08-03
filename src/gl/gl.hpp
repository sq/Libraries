#ifndef _FR_GL
#define _FR_GL

#include "libs.hpp"

namespace gl {
  
  class GLContext : public enable_shared_from_this<GLContext> {
    wm::Window * m_parent;
    eps_OpenGLContext * m_handle;
    
    void postConstruct(script::Context * context);
    
  public:
    GLContext(wm::Window * parent, eps_OpenGLContext * handle);
    virtual ~GLContext();
    
    void makeCurrent() const;
    void clear();
    void flip();
    
    void getPixel(int x, int y, float & red, float & green, float & blue, float & alpha) const;
    
    void getClearColor(float & red, float & green, float & blue, float & alpha) const;
    void setClearColor(float red, float green, float blue, float alpha);
    bool getVSync() const;
    void setVSync(bool vsync);
    
    void draw(int drawMode, script::Object vertices);
    void drawImage(shared_ptr<image::Image> image, int x, int y);
    
    std::string toString() const;
    
    eps_OpenGLContext * getHandle() const;

  };
  
  void initialize();
  void uninitialize();
  
  void registerNamespace(shared_ptr<script::Context> context);
  
}
 
#endif