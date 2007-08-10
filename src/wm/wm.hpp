#ifndef _FR_WM
#define _FR_WM

#include "libs.hpp"

#include "..\script\script.hpp"

namespace wm {
  
  class Window : public enable_shared_from_this<Window> {
    eps_Window * m_handle;
    shared_ptr<gl::GLContext> m_glContext;
    
    bool m_keyState[256];
    unsigned m_tickRate;
    unsigned m_width, m_height;
    bool m_closed;
    
    void postConstruct(script::Context * context);
    
  public:
    script::Object m_onClose;

    script::Object m_onMouseMove;
    script::Object m_onMouseDown;
    script::Object m_onMouseUp;
    script::Object m_onMouseWheel;

    script::Object m_onKeyDown;
    script::Object m_onKeyUp;

    script::Object m_onTick;

    Window(unsigned width = 0, unsigned height = 0);
    virtual ~Window();
    
    eps_Window * getHandle() const;
    
    void setCaption(const char * text);
    std::string getCaption() const;
    
    unsigned getWidth() const;
    unsigned getHeight() const;
    bool getClosed() const;
    
    std::string toString() const;
    
    bool getVisible() const;
    void setVisible(bool visible);
    
    unsigned getTickRate() const;
    void setTickRate(unsigned tickRate);
    
    void getSize(unsigned & width, unsigned & height);
    void setSize(unsigned width, unsigned height);
    
    void getMouseState(int & x, int & y, unsigned & buttons);
    bool getKeyState(int key);
    
    bool poll(bool wait);
    void close();
    
    unsigned getEventCount() const;
    eps::Event getEvent();
    
    void dispatch(eps::Event & event);
    
    void onClose();
    
    void onMouseMove(int x, int y, unsigned buttons);
    void onMouseDown(int x, int y, unsigned buttons);
    void onMouseUp(int x, int y, unsigned buttons);
    void onMouseWheel(int x, int y, unsigned buttons, unsigned wheel);
    
    void onKeyDown(unsigned key);
    void onKeyUp(unsigned key);
    
    void onTick(unsigned absoluteTick, unsigned elapsedTicks);
    
    shared_ptr<gl::GLContext> getGLContext() const;

  };
  
  void initialize();
  void uninitialize();
  
  unsigned getPollingTimeout();
  void setPollingTimeout(unsigned timeout);
  
  bool poll(bool wait);
  
  script::Object getKeyName(int keyCode);
  
  void registerNamespace(shared_ptr<script::Context> context);
  
}
 
#endif