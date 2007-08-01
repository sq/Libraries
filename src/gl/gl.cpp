#include <core\core.hpp>
#include <script\script.hpp>
#include <gl\gl.hpp>

using namespace gl;
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

void registerNamespace(shared_ptr<script::Context> context) {
  /*
  module(context->getContext(), "gl") [
  ];
  */

  context->registerClass<GLContext>();
  context->registerHolder<GLContext, weak_ptr<GLContext>>();
}

}