
#include "epsilon.h"
#include <string>
#include <stdarg.h>

namespace {
    const char* eps_systems[] = {
        // "Tier 0".  Always there.
        "core",
        "wm",
        "event",
        // TODO: correctly deal with not compiling OpenGL support?
#if 1 || defined(EPS_OPENGL)
        "opengl",
#endif

        0   // sentinel
    };
}

EPS_EXPORT(eps_uint) eps_core_getVersion(void) {
    return EPS_VERSION;
}

EPS_EXPORT(eps_bool) eps_core_hasSystem(const eps_char* name) {
    // NOTE: this sucks.  Need some way to automatically generate the list of installed subsystems.
    const std::string n(name);

    for (int i = 0; eps_systems[i] != 0; i++) {
        if (n == eps_systems[i]) {
            return true;
        }
    }

    return false;
}

EPS_EXPORT(eps_bool) eps_core_fillOptions(eps_uint* buffer, eps_uint size, ...) {
    eps_uint i = 0;
    va_list args;
    va_start(args, size);
    va_arg(args, int); // discard the first.  We know about size already.  It's the ones after it that concern us.
    while (i < size) {
        int j = va_arg(args, int);
        if (j == 0) {
            va_end(args);
            return true; // hit the end of the list before the end of the buffer
        } else {
            buffer[i++] = j;
        }
    }
    va_end(args);
    // Buffer not big enough
    return false;
}
