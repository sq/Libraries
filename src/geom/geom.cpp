#include <core\core.hpp>
#include <script\script.hpp>
#include <geom\geom.hpp>

using namespace script;
using namespace luabind;
using namespace geom;

_CLASS_WRAP(Polygon, shared_ptr<Polygon>)
  .def(constructor<>())
  .def(constructor<script::Object>())
  
  .def("__tostring", &Polygon::toString)
//  .def("add", &Polygon::add)
//  .def("insert", &Polygon::insert)
//  .def("remove", &Polygon::remove)
//  .def("clear", &Polygon::clear)
  .def("getVertex", &Polygon::getVertex)
  .def("__call", &Polygon::getVertex)

  _PROPERTY_R(count, getCount)
_END_CLASS  

namespace geom {

void registerNamespace(shared_ptr<script::Context> context) {
//  module(context->getContext(), "geom") [
//  ];

  context->registerClass<Polygon>();
  context->registerHolder<Polygon, weak_ptr<Polygon>>();
}

}