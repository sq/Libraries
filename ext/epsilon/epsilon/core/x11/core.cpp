
#include "epsilon.h"
#include <string>

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

/** Returns the version number.
 */
EPS_EXPORT(eps_uint) eps_core_getVersion(void) {
    return EPS_VERSION;
}

/** Queries the existence of a system.
 * @param name Name of the system to query.
 * @returns True if the system is present.
 */
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
