
#ifndef EPS_OPENGL_WIN32_OPENGL_H
#define EPS_OPENGL_WIN32_OPENGL_H

#if !defined(EPS_WINDOWS) || !defined(EPS_WIN32)
#   error "What are you doing here?!  This is Win32 code!!"
#endif

typedef int (__stdcall *eps_opengl_Proc)();

// FIXME: NO.  Windows.h stuffs WAY too much shit in the preproc.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#endif
