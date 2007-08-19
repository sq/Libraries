#ifndef _FR_GL
#define _FR_GL

#include "libs.hpp"

namespace gl {

  struct GLState {
    GLTexture * textures[8];
    GLenum drawMode;
    
    GLState() {
      memset(textures, 0, sizeof(textures));
      drawMode = 0;
    }
  };
  
  class GLContext : public enable_shared_from_this<GLContext> {
    friend class gl::GLTexture;
  
    wm::Window * m_parent;
    eps_OpenGLContext * m_handle;
    GLState m_state;
    
    std::vector<weak_ptr<GLTexture>> m_textures;
    
    void postConstruct(script::Context * context);
    
  protected:
    void addTexture(shared_ptr<GLTexture> texture);
    void removeTexture(GLTexture * texture);
    
    void bindTexture(int stage, GLTexture * texture);
    void unpackColor(script::Object color);
    
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
    void draw(int drawMode, script::Object vertices, script::Object textures);
    void drawPixel(float x, float y, script::Object color);
    void drawLine(float x1, float y1, float x2, float y2, script::Object color);
    void drawRect(float x1, float y1, float x2, float y2, bool filled, script::Object color);
    void drawImage(image::Image & image, float x, float y, float opacity);
    void drawImage(image::Image & image, float x, float y);
    
    std::string toString() const;
    
    eps_OpenGLContext * getHandle() const;

  };
  
  class GLTexture : public enable_shared_from_this<GLTexture> {
    friend class GLContext;
  
    unsigned m_handle;
    shared_ptr<image::Image> m_image;
    weak_ptr<GLContext> m_context;
    int m_width, m_height;
    
    void postConstruct(script::Context * context);
    void doTailCall();
    
  protected:
    float m_u0, m_v0, m_u1, m_v1;
    
  public:
    GLTexture(shared_ptr<GLContext> context, unsigned handle, int width, int height);
    GLTexture(shared_ptr<GLContext> context, shared_ptr<image::Image> image);
    virtual ~GLTexture();
    
    void upload(image::Image * image);
    
    std::string toString() const;
    
    unsigned getHandle() const;
    int getWidth() const;
    int getHeight() const;
    float getU0() const;
    float getV0() const;
    float getU1() const;
    float getV1() const;
    
    float getU(float x) const;
    float getV(float y) const;
    
  };
  
  void initialize();
  void uninitialize();
  
  void registerNamespace(shared_ptr<script::Context> context);
  
}
 
#endif