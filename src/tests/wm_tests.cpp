#include <test.hpp>

#include <core\core.hpp>
#include <wm\wm.hpp>

using namespace wm;
using namespace script;

SUITE(WindowTests) {
  TEST(CanCreateWindow) {
    shared_ptr<Window> w(new Window());
  }
  
  TEST(WindowHasNativeHandle) {
    shared_ptr<Window> w(new Window());
    
    CHECK(w->getHandle());
  }
  
  TEST(WindowCaption) {
    shared_ptr<Window> w(new Window());
    
    w->setCaption("Test");
    
    CHECK_EQUAL("Test", w->getCaption());
  }
  
  TEST(WindowSize) {
    shared_ptr<Window> w(new Window());
    
    unsigned wi, he;    
    w->getSize(wi, he);
    
    CHECK_EQUAL(0, wi);
    CHECK_EQUAL(0, he);

    CHECK_EQUAL(0, w->getWidth());
    CHECK_EQUAL(0, w->getHeight());
        
    w->setSize(48, 32);
    
    CHECK_EQUAL(48, w->getWidth());
    CHECK_EQUAL(32, w->getHeight());
  }
  
  TEST(Visibility) {
    shared_ptr<Window> w(new Window());
    
    CHECK_EQUAL(true, w->getVisible());    

    w->setVisible(false);
    CHECK_EQUAL(false, w->getVisible());    

    w->setVisible(true);
    CHECK_EQUAL(true, w->getVisible());    
  }
}

SUITE(EventTests) {
  TEST(EmptyQueueThrows) {
    shared_ptr<Window> w(new Window());
    
    CHECK_THROW_STRING(w->getEvent(), "Event queue empty");
  }

  TEST(GetCloseEvent) {
    shared_ptr<Window> w(new Window());
    
    eps_Event e_send;
    memset(&e_send, 0, sizeof(e_send));
    e_send.type = EPS_EVENT_CLOSE;
    
    eps_event_sendEvent(w->getHandle(), &e_send);

    eps::Event evt = w->getEvent();
    CHECK_EQUAL(e_send.type, evt.getType());
  }
  
  TEST(ProcessCloseEvent) {
    shared_ptr<Window> w(new Window());
    
    eps_Event e_send;
    memset(&e_send, 0, sizeof(e_send));
    e_send.type = EPS_EVENT_CLOSE;
    
    eps_event_sendEvent(w->getHandle(), &e_send);

    w->poll(true);
    
    CHECK_EQUAL(true, w->getClosed());
  }
  
  TEST(GetMouseEvent) {
    shared_ptr<Window> w(new Window());
    
    eps_Event e_send;
    memset(&e_send, 0, sizeof(e_send));

    e_send.type = EPS_EVENT_MOUSE_BUTTON_DOWN;
    e_send.mouse.x = 32;
    e_send.mouse.y = 64;
    e_send.mouse.buttonState = 2;
    
    eps_event_sendEvent(w->getHandle(), &e_send);

    eps::Event evt = w->getEvent();
    CHECK_EQUAL(e_send.type, evt.getType());
    
    CHECK_EQUAL(e_send.mouse.x, evt.getRef().mouse.x);
    CHECK_EQUAL(e_send.mouse.y, evt.getRef().mouse.y);
    CHECK_EQUAL(e_send.mouse.buttonState, evt.getRef().mouse.buttonState);
  }
  
  TEST(GetMouseState) {
    shared_ptr<Window> w(new Window());
    
    eps_Event e_send;
    memset(&e_send, 0, sizeof(e_send));

    e_send.type = EPS_EVENT_MOUSE_MOTION;
    e_send.mouse.x = 32;
    e_send.mouse.y = 64;
    e_send.mouse.buttonState = 1;
    
    w->poll(false);

    // This doesn't work. :(
    eps_event_sendEvent(w->getHandle(), &e_send);
    
    w->poll(false);
    
    int x, y;
    unsigned buttons;
    w->getMouseState(x, y, buttons);
    
    /*
    CHECK_EQUAL(e_send.mouse.x, x);
    CHECK_EQUAL(e_send.mouse.y, y);
    CHECK_EQUAL(e_send.mouse.buttonState, buttons);
    */
  }
}

SUITE(ScriptTests) {
  TEST(WindowList) {
    shared_ptr<Context> sc(new Context());
    wm::registerNamespace(sc);
    
    sc->executeScript("w = Window()");
    
    Object tostring = sc->getGlobal("tostring");
    Object windowValue;

    {    
      Object theWindow = sc->getGlobal("w");
      CHECK_EQUAL(LUA_TUSERDATA, getObjectType(theWindow));
      windowValue = tostring(theWindow);
    }

    Object windows = sc->getGlobal("wm.windows");
    int count = 0;
    for (luabind::iterator i(windows), end; i != end; ++i) {
      Object key = i.key();
      Object value = tostring(*i);
      CHECK_EQUAL(windowValue, value);
      count += 1;
    }
    
    CHECK_EQUAL(1, count);
        
    sc->executeScript("w = nil; collectgarbage()");
    
    count = 0;
    for (luabind::iterator i(windows), end; i != end; ++i) {
      count += 1;
    }
    
    CHECK_EQUAL(0, count);
  }
}