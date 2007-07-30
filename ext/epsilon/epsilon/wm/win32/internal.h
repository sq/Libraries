/*
 * win32 specific wm things.
 * This must only ever be included from source files, not headers.
 */

#ifndef EPS_WM_WIN32_INTERNAL_H
#define EPS_WM_WIN32_INTERNAL_H

#if !defined(__cplusplus) || !defined(EPS_WIN32)
#   error "You shouldn't be here!"
#endif

#include "epsilon.h"
#include "epsilon/event/event.h"
#include <vector>

// not do this?
#define WIN32_LEAN_AND_MEAN
#include <windows.h>

struct _eps_Window {

    _eps_Window() : 
      handle(0),
      mouseCaptureCount(0)
    {
    }

    /// Win32 window handle.
    HWND handle;

    /// State of the mouse pointer.
    struct _eps_MouseEvent mouseState;

    /// Number of mouse captures initiated.
    int mouseCaptureCount;

    std::vector<eps_Event> events;
};

#endif
