#include <core\core.hpp>
#include <script\script.hpp>
#include <wm\wm.hpp>
#include <gl\gl.hpp>

#pragma comment(lib, "..\\lib\\modules.lib")

using std::cout;
using std::cin;
using script::Context;

static bool g_quit = false;

int _quit(lua_State * L) {
  g_quit = true;
  return 0;
}

int main (int argc, const char * argv[]) {
  cout << ":: fracture ::\n";
  
  shared_ptr<Context> sc(new Context());
  
  sc->registerFunction("quit", _quit);
  script::registerNamespaces(sc);
  
  std::stringstream buffer;

  char linebuf[1024];
  
  while (!g_quit) {
    if (buffer.tellg())
      cout << "> ";
    else
      cout << ". ";

    cin.getline(linebuf, 1024);
       
    bool erase = true;
    
    try {
      buffer << linebuf;
      sc->executeScript(buffer.str().c_str());      
    } catch (script::SyntaxError ex) {
      const char * msg = ex.what();
      const char search[] = "near '<eof>'";
      if (strstr(msg, search) == msg + strlen(msg) - strlen(search)) {
        // Syntax error near '<eof>', most likely an incomplete fragment.
        erase = false;
        buffer << " \n";
      } else {
        cout << "! " << ex.what() << "\n";        
      }
    } catch (std::exception ex) {
      cout << "! " << ex.what() << "\n";
    }
    
    if (erase)
      buffer.str("");
  }
}