/*
 * X11 specific wm things.
 * This must only ever be included from source files, not headers.
 * We have to pull in X headers here, so think hard before you include this. ;)
 */

#ifndef EPS_WM_X11_INTERNAL_H
#define EPS_WM_X11_INTERNAL_H

#if !defined(__cplusplus)
#   error "__cplusplus not defined!  O_O"
#endif

#if !defined(__cplusplus) || !defined(EPS_X11)
#   error "Trying to compile X11 stuff when EPS_X11 is not defined x_x"
#endif

#include "epsilon.h"
#include "epsilon/event/event.h"
#include <vector>

#include <X11/Xlib.h>

struct _eps_Window {

    _eps_Window()
        : display(0)
        , window(0)
        , mouseCaptureCount(0)
    {
		mouseState.x = 0;
		mouseState.y = 0;
		mouseState.buttonState = 0;
		mouseState.wheelState = 0;
		mouseState.buttonIndex = 0;
    }

    Display* display;
    Window   window;

    /// Graphics context
    GC       context;

    /// Event queue
    std::vector<eps_Event> events;

    /// State of the mouse pointer.
    struct _eps_MouseEvent mouseState;

    /// Number of mouse captures initiated.
    int mouseCaptureCount;
};

#endif
