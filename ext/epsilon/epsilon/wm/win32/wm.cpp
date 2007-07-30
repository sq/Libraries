
#include "epsilon.h"
#include "epsilon/error/error.h"

#include <vector>
#include <stdexcept>
#include <cassert>

#define _WIN32_WINNT 0x0500
#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#include "epsilon/wm/win32/wm.h"
#include "epsilon/wm/win32/internal.h"
#include "epsilon/event/event.h"

// --- Implementation ---

namespace {
    const char* CLASS_NAME = "eps_wnd_class";
    const int WINDOW_STYLE = 0;
    bool  initialized = false;

    void doMouseButtonEvent(eps_Window* window, eps_uint button, bool pressed) {
        if (pressed) {
            window->mouseCaptureCount++;
            if (window->mouseCaptureCount == 1) {
              SetCapture(window->handle);
            }
            window->mouseState.buttonState |=  (1 << button);
        } else {
            assert(window->mouseCaptureCount > 0);
            window->mouseCaptureCount--;
            if (window->mouseCaptureCount == 0) {
              ReleaseCapture();
            }
            window->mouseState.buttonState &= ~(1 << button);
        }

        // copy the mouse state into an event, tweak to suit, queue it up.
        eps_Event evt;
        evt.mouse = window->mouseState;
        evt.mouse.buttonIndex = button;
        evt.type = pressed ? EPS_EVENT_MOUSE_BUTTON_DOWN : EPS_EVENT_MOUSE_BUTTON_UP;
        window->events.push_back(evt);
    }

    LRESULT CALLBACK WndProc(HWND hWnd, UINT msg, WPARAM wParam, LPARAM lParam) {
        _eps_Window* window = reinterpret_cast<_eps_Window*>(GetWindowLongPtr(hWnd, GWLP_USERDATA));
        if (!window && msg == WM_CREATE) {
            // attach the window instance to the window.
            LPCREATESTRUCT cs = reinterpret_cast<LPCREATESTRUCT>(lParam);
            window = reinterpret_cast<_eps_Window*>(cs->lpCreateParams);
            assert(window);

            window->handle = hWnd;
            SetWindowLongPtr(hWnd, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(cs->lpCreateParams));

        } else if (window) {
            // translate win32 message to eps_Event and post.
            switch (msg) {
                // EPS_EVENT_CLOSE
                case WM_CLOSE:  {
                    eps_Event evt;
                    evt.type = EPS_EVENT_CLOSE;
                    window->events.push_back(evt);
                    return 0;
                }

                // EPS_EVENT_KEY
                case WM_KEYDOWN: {
                    eps_Event evt;
                    evt.type = EPS_EVENT_KEY;
                    evt.key.pressed = true;
                    evt.key.keyCode = wParam; // FIXME
                    window->events.push_back(evt);
                    break;
                }
                case WM_KEYUP: {
                    eps_Event evt;
                    evt.type = EPS_EVENT_KEY;
                    evt.key.pressed = false;
                    evt.key.keyCode = wParam; // FIXME
                    window->events.push_back(evt);
                    break;
                }

                // EPS_EVENT_MOUSE
                case WM_MOUSEMOVE: {
                    // update the last known mouse state
                    window->mouseState.x = reinterpret_cast<eps_s16_x2&>(lParam).low;
                    window->mouseState.y = reinterpret_cast<eps_s16_x2&>(lParam).high;

                    // copy it into an event and set the type
                    eps_Event evt;
                    evt.mouse = window->mouseState;
                    evt.type = EPS_EVENT_MOUSE_MOTION;

                    // post
                    window->events.push_back(evt);
                    break;
                }
                case WM_LBUTTONDOWN:    doMouseButtonEvent(window, 0, true);     break;
                case WM_LBUTTONUP:      doMouseButtonEvent(window, 0, false);    break;
                case WM_RBUTTONDOWN:    doMouseButtonEvent(window, 1, true);     break;
                case WM_RBUTTONUP:      doMouseButtonEvent(window, 1, false);    break;
                case WM_MBUTTONDOWN:    doMouseButtonEvent(window, 2, true);     break;
                case WM_MBUTTONUP:      doMouseButtonEvent(window, 2, false);    break;
                case WM_MOUSEWHEEL: {
                    // wParam and lParam are treated as pairs of signed ints in WM_MOUSEWHEEL
                    eps_s16_x2& wp = reinterpret_cast<eps_s16_x2&>(wParam);
                    eps_s16_x2& lp = reinterpret_cast<eps_s16_x2&>(lParam);

                    signed short delta = wp.high;

                    window->mouseState.x = lp.low;
                    window->mouseState.y = lp.high;

                    eps_Event evt;
                    evt.mouse = window->mouseState;
                    evt.type = EPS_EVENT_MOUSE_WHEEL;
                    evt.mouse.wheelState = delta;
                    window->events.push_back(evt);
                    break;
                }
            }
        }

        // if nothing above returns a value, we defer to the defaults.
        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    bool registerClass() {
        if (initialized) {
            return true;
        }

        WNDCLASSEX wc;
        memset(&wc, 0, sizeof(wc));

        wc.cbSize = sizeof(wc);
        wc.hInstance = GetModuleHandle(0);
        wc.lpszClassName = CLASS_NAME;
        wc.lpfnWndProc = WndProc;
        wc.style = WINDOW_STYLE;

        wc.hIcon   = LoadIcon(0, IDI_APPLICATION);
        wc.hIconSm = LoadIcon(0, IDI_APPLICATION);
        wc.hCursor = LoadCursor(0, IDC_ARROW);
        wc.lpszMenuName = 0;
        wc.cbClsExtra = 0;
        wc.cbWndExtra = 0;

        wc.hbrBackground = (HBRUSH) GetStockObject(LTGRAY_BRUSH);

        int result = RegisterClassEx(&wc);
        if (!result) {
            return false;
        }

        initialized = true;
        return true;
    }
}

// --- Public interface follows ---

EPS_EXPORT(eps_bool) eps_wm_initialize(void) {
    return registerClass();
}

EPS_EXPORT(void) eps_wm_shutDown(void) {
    // no-op
}

/** Creates a new window.
 * @param width desired width of the window.
 * @param height desired height of the window.
 * @param options Flag things that I haven't quite thought through yet.
 * @return A handle to the window, or 0 on fail.
 */
EPS_EXPORT(eps_Window*) eps_wm_createWindow(eps_uint width, eps_uint height, eps_uint options) {
    eps_Window* window = new eps_Window;
    memset(&window->mouseState, 0, sizeof(window->mouseState));

    // added to width and height so that the window encloses a region of the requested size.
    const int BORDER_WIDTH = GetSystemMetrics(SM_CXFIXEDFRAME);
    const int BORDER_HEIGHT = GetSystemMetrics(SM_CYFIXEDFRAME);
    const int CAPTION_HEIGHT = GetSystemMetrics(SM_CYCAPTION);

    HWND handle = CreateWindowEx(
        0,                      // exStyle
        CLASS_NAME,
        "",                     // caption
        WS_CAPTION | WS_MINIMIZEBOX | WS_SYSMENU,    // style
        CW_USEDEFAULT,
        CW_USEDEFAULT,
        width + BORDER_WIDTH * 2, // accomodate the border
        height + BORDER_HEIGHT * 2 + CAPTION_HEIGHT, // accomodate the border and title
        HWND_DESKTOP,           // parent
        0,                      // hMenu
        GetModuleHandle(0),     // hInstance
        window                   // lParam
    );

    if (handle == 0) {
        eps_error_postErrorString(EPS_ERROR_GENERAL, "win32: CreateWindowEx failed");
        // eps_core_setError(...)
        return 0;
    } else {
        ShowWindow(handle, SW_SHOW);
        return window;
    }
}

/** Destroys an existing window.
 * @param window The window to destroy.
 */
EPS_EXPORT(void) eps_wm_destroyWindow(eps_Window* window) {
    DestroyWindow(window->handle);
    delete window;
}

/** Sets a window's position and size.
 * @param window The window to move.
 * @param x X position.  -1 to leave as-is.
 * @param y Y position. -1 to leave as-is.
 * @param width Desired width.  -1 to leave as-is.
 * @param height Desired height.  -1 to leave as-is.
 */
EPS_EXPORT(void) eps_wm_moveWindow(eps_Window* window, eps_int x, eps_int y, eps_uint width, eps_uint height) {
    const eps_uint UNSET = static_cast<eps_int>(-1);

    RECT r;
    GetWindowRect(window->handle, &r);

    if (x == UNSET) { x = r.left; }
    if (y == UNSET) { y = r.top;  }
    if (width == UNSET) { width = r.right - r.left; }
    if (height == UNSET) { height = r.bottom - r.top; }

    MoveWindow(window->handle, x, y, width, height, true);
}

/** Returns a platform specific window handle thing.
 * @param window The window
 * @return A scary handle thing.
 */
EPS_EXPORT(eps_hWnd) eps_wm_getHWnd(eps_Window* window) {
    return window->handle;
}

/** Sets the caption on an existing window.
 * @param window The window.
 * @param caption The desired caption.
 */
EPS_EXPORT(void) eps_wm_setCaption(eps_Window* window, const eps_char* caption) {
    SetWindowText(window->handle, caption);
}

/** Gets the caption of an existing window.
 * @param window The window.
 */
EPS_EXPORT(eps_bool) eps_wm_getCaption(eps_Window* window, eps_char* buffer, eps_uint bufSize) {
    return GetWindowText(window->handle, buffer, bufSize);
}

//void eps_wm_setIcon(eps_Window* window, void* icon);

EPS_EXPORT(void) eps_wm_pollMessages(eps_Window* window, eps_bool block) {
    MSG msg;
    if (block) {
        GetMessage(&msg, 0, 0, 0);
        TranslateMessage(&msg);
        DispatchMessage(&msg);
    } else {
        if (PeekMessage(&msg, 0, 0, 0, PM_REMOVE)) {
            TranslateMessage(&msg);
            DispatchMessage(&msg);
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

EPS_EXPORT(void) eps_wm_setVisible(eps_Window* window, eps_bool visible) {
  ShowWindow(window->handle, visible ? SW_SHOW : SW_HIDE);
}

EPS_EXPORT(eps_bool) eps_wm_getVisible(eps_Window* window) {
  return IsWindowVisible(window->handle) != 0;
}