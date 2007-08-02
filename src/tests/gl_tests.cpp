#include <test.hpp>

#include <core\core.hpp>
#include <wm\wm.hpp>
#include <gl\gl.hpp>

using namespace wm;
using namespace gl;

SUITE(ContextTests) {
  TEST(CanGetContext) {
    shared_ptr<Window> w(new Window(32, 32));
    shared_ptr<GLContext> gl = w->getGLContext();
    
    CHECK(gl.get());
    if (gl)
      CHECK(gl->getHandle());
  }
}