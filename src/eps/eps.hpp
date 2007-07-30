#include "libs.hpp"

namespace eps {

  class ErrorHolder;
  class ErrorHandler;
  
  class Event;
  
  class ErrorHolder {
    eps_Error * m_error;
    
  public:
    inline ErrorHolder(eps_Error * error) :
      m_error(error)
    {
    }
    
    inline ~ErrorHolder() {
      if (m_error) {
        eps_error_destroyError(m_error);
        m_error = 0;
      }
    }

    inline eps_Error * operator -> () const {
      return m_error;
    }
  };
  
  class ErrorHandler {
    unsigned m_initialErrorCount;
    
  public:
    inline ErrorHandler() {
      m_initialErrorCount = eps_error_getErrorCount();
    }
    
    inline ~ErrorHandler() {
      check();
    }
    
    inline void check() const {
      unsigned count = eps_error_getErrorCount();
      
      if (count > m_initialErrorCount) {
        ErrorHolder error(eps_error_getError());
        throw std::exception(error->message);
      }
    }
  };
  
  class Event {
    eps_Event m_event;
    
  public:
    inline Event() {
      memset(&m_event, 0, sizeof(m_event));
    }
    
    inline Event(eps_Event evt) {
      memcpy(&m_event, &evt, sizeof(evt));
    }
  
    inline eps_EventType getType() const {
      return m_event.type;
    }
    
    const eps_Event & getRef() const {
      return m_event;
    }
    eps_Event & getRef() {
      return m_event;
    }
    
    template <class T>
    inline T & as() {
      throw std::exception("Unknown event type");
    }
    
    template <>
    inline _eps_BaseEvent & as() {
      return m_event.base;
    }
    
    template <>
    inline _eps_CloseEvent & as() {
      return m_event.close;
    }
    
    template <>
    inline _eps_KeyEvent & as() {
      return m_event.key;
    }
    
    template <>
    inline _eps_MouseEvent & as() {
      return m_event.mouse;
    }
    
  };
  
}
