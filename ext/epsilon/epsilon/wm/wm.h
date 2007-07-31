#ifndef EPS_WM_WM_H
#define EPS_WM_WM_H

#include "../../epsilon.h"

#if defined(EPS_WIN32)
#   include "win32/wm.h"
#elif defined(EPS_X11)
#   include "x11/wm.h"
#elif defined(EPS_MACOSX)
#   include "macosx/wm.h"
#else
#   error Unrecognized platform!
#endif

typedef struct _eps_Window eps_Window;
typedef void* eps_hWnd;

/** Initialize the WM system
 * @return false on failure, true on success.
 */
EPS_EXPORT(eps_bool) eps_wm_initialize(void);

/** Shutdown the WM system
 */
EPS_EXPORT(void) eps_wm_shutDown(void);

/** Creates a new window.
 * @param width desired width of the window.
 * @param height desired height of the window.
 * @param options Flag things that I haven't quite thought through yet.
 * @return A handle to the window, or 0 on fail.
 */
EPS_EXPORT(eps_Window*) eps_wm_createWindow(eps_uint width, eps_uint height, eps_uint options);

/** Destroys an existing window.
 * @param window The window to destroy.
 */
EPS_EXPORT(void) eps_wm_destroyWindow(eps_Window* window);

/** Sets a window's position and size.
 * @param window The window to move.
 * @param x X position.  -1 to leave as-is.
 * @param y Y position. -1 to leave as-is.
 * @param width Desired width.  -1 to leave as-is.
 * @param height Desired height.  -1 to leave as-is.
 */
EPS_EXPORT(void) eps_wm_moveWindow(eps_Window* window, eps_int x, eps_int y, eps_uint width, eps_uint height);

/** Returns a platform specific window handle thing.
 * @param window The window
 * @return A scary handle thing.
 */
EPS_EXPORT(eps_hWnd) eps_wm_getHWnd(eps_Window* window);

/** Sets the caption on an existing window.
 * @param window The window.
 * @param caption The desired caption.
 */
EPS_EXPORT(void) eps_wm_setCaption(eps_Window* window, const eps_char* caption);

//void eps_wm_setIcon(eps_Window* window, void* icon);

/** Gets the window caption
 * @param window The window.
 * @param buffer Buffer to recieve the window caption.
 * @param bufSize length of the buffer, in characters.
 * @return True if the whole title was grabbed, false if the buffer wasn't big enough.
 */
EPS_EXPORT(eps_bool) eps_wm_getCaption(eps_Window* window, eps_char* buffer, eps_uint bufSize);

/** Processes incoming messages pertaining to the window.
 * @note This isn't typically useful for user-code.  Use
 *       the eps_event functions instead.
 * @param waitDuration The maximum number of milliseconds to wait for a message to be recieved. 
 *        (Pass 0 to return immediately if no messages are in the queue)
 * @param window The window to query. Pass 0 to process messages for all windows.
 */
EPS_EXPORT(void) eps_wm_pollMessages(eps_Window* window, eps_uint waitDuration);

/** Gets the current mouse state.
 * @note Any of these arguments can be null, in which case the value will not be returned.
 * @param window The window to ask.
 * @param x Pointer to an int that will recieve the x-axis position of the mouse cursor.
 * @param y Pointer to an int blah blah y-axis position of the cursor.
 * @param buttons Pointer to an int to recieve the button state.  
 *                The button state is a bit-field--bit n of the field 
 *                represents the state of button n.
 */
EPS_EXPORT(void) eps_wm_getMouseState(eps_Window* window, eps_int* x, eps_int* y, eps_uint* buttons);

/** Shows or hides a window.
 * @param visible If true, show the window. Otherwise, hide it.
 * @param window The window.
 */
EPS_EXPORT(void) eps_wm_setVisible(eps_Window* window, eps_bool visible);

/** Determines whether a window is visible.
 * @param window The window.
 */
EPS_EXPORT(eps_bool) eps_wm_getVisible(eps_Window* window);

/** Starts or stops the periodic 'tick' timer for a window.
 * @param window The window.
 * @param tickRate The rate (in milliseconds) to tick at, or 0 to stop the tick timer.
 */
EPS_EXPORT(void) eps_wm_setTickRate(eps_Window* window, eps_uint tickRate);

#endif
