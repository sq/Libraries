
#include "epsilon.h"
#include "epsilon/wm/x11/internal.h"

EPS_EXPORT(eps_bool) eps_event_peekEvent(eps_Window* window, eps_Event* event) {
    eps_wm_pollMessages(window, false);
    if (window->events.empty()) {
        return false;
    } else {
        *event = window->events.back();
        return true;
    }
}

EPS_EXPORT(eps_bool) eps_event_getEvent(eps_Window* window, eps_Event* event) {
    eps_wm_pollMessages(window, false);
    if (eps_event_peekEvent(window, event)) {
        window->events.pop_back();
        return true;
    } else {
        return false;
    }
}

EPS_EXPORT(eps_bool) eps_event_waitEvent(eps_Window* window, eps_Event* event) {
    while (window->events.empty()) {
        eps_wm_pollMessages(window, true);
    }
    return eps_event_getEvent(window, event);
}

EPS_EXPORT(void) eps_event_sendEvent(eps_Window* window, eps_Event* event) {
    window->events.push_back(*event);
}
