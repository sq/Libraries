#include <core\core.hpp>
#include <script\script.hpp>
#include <image\image.hpp>
#include <gl\gl.hpp>

using namespace gl;
using namespace image;
using namespace wm;

_CLASS_WRAP(GLContext, shared_ptr<GLContext>)
  .def("clear", &GLContext::clear)
  .def("flip", &GLContext::flip)
  .def("makeCurrent", &GLContext::makeCurrent)
  .def("__tostring", &GLContext::toString)
  
  .def("getClearColor", &GLContext::getClearColor, 
    pure_out_value(_2) + pure_out_value(_3) + pure_out_value(_4) + pure_out_value(_5)
  )
  .def("setClearColor", &GLContext::setClearColor)

  .def("getPixel", &GLContext::getPixel, 
    pure_out_value(_4) + pure_out_value(_5) + pure_out_value(_6) + pure_out_value(_7)
  )
  
  .def("draw", &GLContext::draw)
  .def("drawImage", &GLContext::drawImage)
  
  _PROPERTY_RW("vsync", getVSync, setVSync)
_END_CLASS  

namespace gl {

int g_refCount = 0;

void initialize() {
  if (g_refCount == 0)
    glewInit();

  g_refCount += 1;
}

void uninitialize() {
  assert(g_refCount);
  
  g_refCount -= 1;
}

void flush() {
  glFlush();
}

void registerNamespace(shared_ptr<script::Context> context) {
  using luabind::module;
  using luabind::def;

  module(context->getContext(), "gl") [
    def("flush", flush)
  ];
  
  context->registerClass<GLContext>();
  context->registerHolder<GLContext, weak_ptr<GLContext>>();
  
  #define _C(name) \
    context->setGlobal("gl." #name, GL_ ## name)
  
  _C(ZERO);
  _C(ONE);
  _C(TRUE);
  _C(FALSE);
  _C(POINTS);
  _C(LINES);
  _C(LINE_STRIP);
  _C(LINE_LOOP);
  _C(TRIANGLES);
  _C(TRIANGLE_STRIP);
  _C(TRIANGLE_FAN);
  _C(QUADS);
  _C(QUAD_STRIP);
  _C(POLYGON);
}

}