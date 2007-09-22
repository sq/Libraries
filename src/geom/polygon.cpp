#include <core\core.hpp>
#include <script\script.hpp>
#include <geom\geom.hpp>

using namespace script;
using namespace luabind;
using namespace geom;

namespace geom {

Polygon::Polygon() {
}

Polygon::Polygon(script::Object vertices) {
  using namespace script;
  using namespace luabind;

  if (!isTable(vertices))
    throw std::exception("vertices must be a table");
    
  for (iterator iter(vertices), end; iter != end; ++iter) {
    Object item = *iter;
    Vertex v(0.0f, 0.0f, 0.0f);
    if (script::unpackTable(item, reinterpret_cast<float *>(&v), 3) > 0)
      m_vertices.push_back(v);
  }
}

Polygon::~Polygon() {
}

Polygon::TVertices::iterator Polygon::at(int index) {
  if ((index < 1) || (index > (int)m_vertices.size()))
    throw std::exception("1 >= index < count");
  return m_vertices.begin() + (index - 1);
}

script::Object Polygon::getVertex(int index) {
  if ((index < 1) || (index > (int)m_vertices.size()))
    throw std::exception("1 >= index < count");
  Vertex v = m_vertices[index - 1];
  script::Object result = luabind::newtable(script::getActiveContext()->getContext());
  result[1] = v.x;
  result[2] = v.y;
  result[3] = v.z;
  return result;
}

int Polygon::getCount() const {
  return (int)m_vertices.size();
}

std::string Polygon::toString() const {
  std::stringstream buf;
  buf << "<Polygon:" << core::ptrToString(this) << ">";
  return buf.str();
}

}