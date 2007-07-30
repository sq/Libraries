
#ifndef EPS_OPENGL_X11_OPENGL_H
#define EPS_OPENGL_X11_OPENGL_H

#if defined(EPS_WINDOWS)
    typedef int (__stdcall *eps_opengl_Proc)();
#else
    typedef int (*eps_opengl_Proc)();
#endif

#endif
