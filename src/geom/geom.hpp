#ifndef _FR_GEOM
#define _FR_GEOM

#include "..\script\script.hpp"

namespace geom {

  struct Vertex {
    float x;
    float y;
    float z;
    
    Vertex(float _x, float _y, float _z = 0.0f) :
      x(_x), y(_y), z(_z)
    {
    }
  };
  
  class Polygon : public enable_shared_from_this<Polygon> {
    typedef vector<Vertex> TVertices;
  
    TVertices m_vertices;
    
  public:
    Polygon();
    Polygon(script::Object vertices);
    
    ~Polygon();
    TVertices::iterator at(int index);
    script::Object getVertex(int index);
      
    int getCount() const;
    string toString() const;    
  };
  
  void registerNamespace(shared_ptr<script::Context> context);
  
}
 
#endif