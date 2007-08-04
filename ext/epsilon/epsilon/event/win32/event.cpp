
#include "epsilon.h"
#include "epsilon/wm/win32/internal.h"

eps_bool getVirtualEvent(eps_Window* window, eps_Event* event) {
    if (!eps_wm_getVisible(window))
        return false;

    LONG currentTick = window->currentTick;
    LONG previousTick = InterlockedExchange(&window->previousTick, currentTick);
      
    if (currentTick > previousTick) {
        event->type = EPS_EVENT_TICK;
        event->tick.absoluteTick = currentTick;
        event->tick.elapsedTicks = currentTick - previousTick;
        return true;
    }
    
    return false;
}

EPS_EXPORT(eps_bool) eps_event_peekEvent(eps_Window* window, eps_Event* event) {
    eps_wm_pollMessages(window, false);
    if (window->events.empty()) {
        return getVirtualEvent(window, event);
    } else {
        *event = window->events.back();
        return true;
    }
}

EPS_EXPORT(eps_bool) eps_event_getEvent(eps_Window* window, eps_Event* event) {
    eps_wm_pollMessages(window, false);
    if (eps_event_peekEvent(window, event)) {
        if (window->events.size())
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

EPS_EXPORT(eps_uint) eps_event_getEventCount(eps_Window* window) {
    unsigned n = window->events.size();
    if ((n == 0) && (eps_wm_getVisible(window))) {
        LONG currentTick = window->currentTick;
        LONG previousTick = window->previousTick;
        if (currentTick > previousTick)
            n += 1;
    }
    return n;
}
