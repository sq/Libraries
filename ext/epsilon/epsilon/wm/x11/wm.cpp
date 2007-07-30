
#include "epsilon.h"
#include "epsilon/wm/x11/wm.h"
#include "epsilon/wm/x11/internal.h"
#include "epsilon/error/error.h"

#include <X11/Xlib.h>
#include <X11/Xutil.h>
#include <cassert>

#ifndef EPS_X11
#   error "How did you get here?  This is X11 code."
#endif

namespace {
    const int NOTIFY_MASK = 0
        | KeyPressMask
        | KeyReleaseMask
        | ButtonPressMask
        | ButtonReleaseMask
        | PointerMotionMask
        | Button1MotionMask
        | Button2MotionMask
        | Button3MotionMask
        | Button4MotionMask
        | Button5MotionMask
        | ButtonMotionMask

        | StructureNotifyMask
        | ExposureMask
    ;

    Atom WM_DELETE_WINDOW = 0;

    void doMouseButtonEvent(eps_Window* window, eps_uint button, eps_bool pressed) {
        if (pressed) {
            window->mouseCaptureCount++;
            if (window->mouseCaptureCount == 1) {
                // capture mouse
            }
            window->mouseState.buttonState |= (1 << button);
        } else {
            assert(window->mouseCaptureCount > 0);
            window->mouseCaptureCount--;
            if (window->mouseCaptureCount == 0) {
                // uncapture
            }
            window->mouseState.buttonState &= ~(1 << button);
        }

        eps_Event evt;
        evt.mouse = window->mouseState;
        evt.mouse.buttonIndex = button;
        evt.type = pressed ? EPS_EVENT_MOUSE_BUTTON_DOWN : EPS_EVENT_MOUSE_BUTTON_UP;
        window->events.push_back(evt);
    }

    void processEvent(eps_Window* window, XEvent* xevt) {
        eps_Event evt;

        switch (xevt->type) {
            case ClientMessage: {
                if (xevt->xclient.format == 32 && xevt->xclient.data.l[0] == WM_DELETE_WINDOW) {
                    evt.type = EPS_EVENT_CLOSE;
                    break;
                } else {
                    // Dunno what this is.  Ignore it.
                    return;
                }
            }

            case DestroyNotify: {
                evt.type = EPS_EVENT_CLOSE;
                break;
            }

            case ButtonPress:
            case ButtonRelease: {
                // http://tronche.com/gui/x/xlib/events/keyboard-pointer/keyboard-pointer.html#XButtonEvent
                int buttonIndex = xevt->xbutton.button - 1; // 1-based to 0-based
                bool pressed = xevt->type == ButtonPress;
                doMouseButtonEvent(window, buttonIndex, pressed);
                return;
            }

            case KeyPress:
            case KeyRelease: {
                int keyIndex = xevt->xkey.keycode;
                bool pressed = xevt->type == KeyPress;
                evt.type = EPS_EVENT_KEY;
                evt.key.keyCode = keyIndex;
                evt.key.pressed = pressed;
                break;
            }

            case MotionNotify: {
                window->mouseState.x = xevt->xmotion.x;
                window->mouseState.y = xevt->xmotion.y;

                evt.mouse = window->mouseState;
                evt.type = EPS_EVENT_MOUSE_MOTION;
                break;
            }

            case Expose: {
                // bleh
                //XFlush(window->display);
                XSync(window->display, False);
                return;
            }

            default: {
                // unknown event.  Swallow.
                return;
            }
        }

        window->events.push_back(evt);
    }
}

EPS_EXPORT(eps_bool) eps_wm_initialize(void) {
    // no-op
}

EPS_EXPORT(void) eps_wm_shutDown(void) {
    // no-op
}

EPS_EXPORT(eps_Window*) eps_wm_createWindow(eps_uint width, eps_uint height, eps_uint options) {
    eps_Window* window = new eps_Window;

    try {
        window->display = XOpenDisplay(0);
        if (!window->display) throw "Unable to open display";

        int screen = DefaultScreen(window->display);
        int black = BlackPixel(window->display, screen);
        int white = WhitePixel(window->display, screen);

        window->window = XCreateSimpleWindow(
            window->display,
            DefaultRootWindow(window->display),
            0, 0, width, height, 0,
            white, black
        );

        XSelectInput(window->display, window->window, NOTIFY_MASK);
        XMapWindow(window->display, window->window);
        window->context = XCreateGC(window->display, window->window, 0, 0);
        XSetForeground(window->display, window->context, white);

        // Set funny extension things so we can catch close events.
        WM_DELETE_WINDOW = XInternAtom(window->display, "WM_DELETE_WINDOW", false);
        XSetWMProtocols(window->display, window->window, &WM_DELETE_WINDOW, 1);

        return window;
    } catch (const char* msg) {
        eps_error_postErrorString(EPS_ERROR_GENERAL, msg);

        if (window->display) {
            XCloseDisplay(window->display);
        }

        delete window;
        return 0;
    }
}

EPS_EXPORT(void) eps_wm_destroyWindow(eps_Window* window) {
    XDestroyWindow(window->display, window->window);
    XCloseDisplay(window->display);
    // free window, context?
    delete window;
}

EPS_EXPORT(void) eps_wm_moveWindow(eps_Window* window, eps_int x, eps_int y, eps_uint width, eps_uint height) {
}

EPS_EXPORT(eps_hWnd) eps_wm_getHWnd(eps_Window* window) {
    return reinterpret_cast<void*>(window->window);
}

EPS_EXPORT(void) eps_wm_setCaption(eps_Window* window, const eps_char* caption) {
    XTextProperty textprop;

    char** char_ptr = reinterpret_cast<char**>(const_cast<eps_char**>(&caption));

#ifdef EPS_UNICODE
#   error "wchar stuff is totally not in yet.  Sorry!"
#else
    // Try plain old ASCII if UTF8 fails
    int result = Xutf8TextListToTextProperty(window->display, char_ptr, 1, XUTF8StringStyle, &textprop);
    if (result != Success) {
        XStringListToTextProperty(char_ptr, 1, &textprop);
    }
#endif
    XSetWMName(window->display, window->window, &textprop);
    XFree(textprop.value);
}

EPS_EXPORT(void) eps_wm_pollMessages(eps_Window* window, eps_bool block) {
    XEvent event;
    if (block) {
        XNextEvent(window->display, &event);
        processEvent(window, &event);
    } else {
        int pending = XPending(window->display);
        if (pending) {
            XNextEvent(window->display, &event);
            processEvent(window, &event);
        }
    }
}

EPS_EXPORT(void) eps_wm_getMouseState(eps_Window* window, eps_int* x, eps_int* y, eps_uint* buttons) {
    if (window == 0) {
        eps_error_postErrorString(EPS_ERROR_INVALID_ARGUMENT, "eps_wm_getMouseState needs a non-null window pointer!");
        return;
    }
    if (x)          *x = window->mouseState.x;
    if (y)          *y = window->mouseState.y;
    if (buttons)    *buttons = window->mouseState.buttonState;
}
