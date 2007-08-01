#pragma comment(lib, "opengl32.lib")
#ifdef _DEBUG
  #pragma comment(lib, "glew-d.lib")
#else
  #pragma comment(lib, "glew.lib")
#endif

#include "..\wm\wm.hpp"

#define GLEW_STATIC
#include <glew\glew.h>
#include <glew\wglew.h>
