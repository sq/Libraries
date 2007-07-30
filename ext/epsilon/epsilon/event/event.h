
#ifndef EPS_EVENT_EVENT_H
#define EPS_EVENT_EVENT_H

#include "../../epsilon.h"

// Platform specific.
#if defined(EPS_WIN32)
#   include "win32/event.h"
#elif defined(EPS_X11)
#   include "x11/event.h"
#elif defined(EPS_MACOSX)
#   include "macosx/event.h"
#else
#   error Unrecognized platform!
#endif

/** Enumeration for describing the type of a given event.
 *
 * @note Event types are nouns, not verbs.
 * @note _MARKER event types are not actual event types.
 * They're just spacers so that we can insert values in the enum
 * without breaking binary compatibility.
 */
typedef enum _eps_EventType {
    // Lowest 0x1000 events are reserved for user things.
    // (mainly paranoia)
    EPS_EVENT_USER = 0x0000,

    EPS_EVENT_WM_MARKER = 0x1000,
    EPS_EVENT_CLOSE,

    EPS_EVENT_MOUSE_MARKER = 0x1100,
    EPS_EVENT_MOUSE_MOTION,
    EPS_EVENT_MOUSE_BUTTON_DOWN,
    EPS_EVENT_MOUSE_BUTTON_UP,
    EPS_EVENT_MOUSE_WHEEL,

    EPS_EVENT_KEY_MARKER = 0x1200,
    EPS_EVENT_KEY,

    EPS_EVENT_INVALID = 0xF000,
} eps_EventType;

// event types

struct _eps_BaseEvent {
    eps_EventType type;     ///< Event type.
};

struct _eps_CloseEvent {
    eps_EventType type;     ///< Event type.  Should always be EPS_EVENT_CLOSE
};

struct _eps_KeyEvent {
    eps_EventType type;     ///< Event type.  Should always be EPS_EVENT_KEY_*
    eps_bool pressed;       ///< True if a key was pressed, else false.
    eps_uint keyCode;       ///< Key code... thing.  Currently platform specific and not very useful. FIXME
};

struct _eps_MouseEvent {
    eps_EventType type;     ///< Event type.  Should always be EPS_EVENT_MOUSE_*
    eps_int x;              ///< X position of the mouse, relative to the window.
    eps_int y;              ///< Y position of the mouse. (also relative to the window)
    eps_uint buttonState;   ///< Mask describing the state of each button.  Bit N is 1 if mouse button N is pressed.
    eps_uint wheelState;    ///< ..... wheelmouse... thing.  The sign indicates the direction.  The actual magnitude is currently of no importance.

    eps_uint buttonIndex;   ///< Button to which the event pertains if applicable
};

// main event union

typedef union _eps_Event {
    eps_EventType type;

    struct _eps_BaseEvent  base;
    struct _eps_CloseEvent close;
    struct _eps_KeyEvent   key;
    struct _eps_MouseEvent mouse;
} eps_Event;


#include "../../epsilon.h"
#include "../../epsilon/wm/wm.h" // yikes!  Fixme.

/** Peeks an event from the queue, if there is one.
 * Does not remove the message from the queue.
 *
 * @param window The window to check events for.
 * @param event Pointer to an eps_Event to recieve the event.
 * @return true if an event was retrieved.
 */
EPS_EXPORT(eps_bool) eps_event_peekEvent(eps_Window* window, eps_Event* event);

/** Gets an event from the queue, if there is one.
 * @param window The window to check events for.
 * @param event Pointer to an eps_Event to recieve the event.
 * @return true if an event was retrieved.
 */
EPS_EXPORT(eps_bool) eps_event_getEvent(eps_Window* window, eps_Event* event);

/** Waits for an event on the window, and returns it.
 * @param window The window to wait on.
 * @param event Pointer to an eps_Event to recieve the event.
 * @return true if an event was retrieved. (hypothetically, this will always be true)
 */
EPS_EXPORT(eps_bool) eps_event_waitEvent(eps_Window* window, eps_Event* event);

/** Sends an event to the window's event queue.
 * @param window The window to recieve the event.
 * @param event The event to send.
 */
EPS_EXPORT(void) eps_event_sendEvent(eps_Window* window, eps_Event* event);

/** Returns the number of events waiting in the window's event queue. Does not pump messages.
 * @param window The window to check events for.
 * @return number of events in the queue (or 0 if the queue is empty)
 */
EPS_EXPORT(eps_uint) eps_event_getEventCount(eps_Window* window);

#endif
