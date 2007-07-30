
/*
 * Epsilon - Core
 *
 * Main header for the core module.
 *
 * Core is the base module that is responsible for most low-level
 * introspection concerning the host system and hardware.
 * The whole library (loosely) revolves around core.
 *
 * 07 June 2004
 * Coded by Andy Friesen
 * See license.txt for redistribution terms.
 */

#ifndef EPS_CORE_CORE_H
#define EPS_CORE_CORE_H

// include platform specific details
#if defined(EPS_WIN32)
#   include "epsilon/core/win32/core.h"
#elif defined(EPS_X11)
#   include "epsilon/core/x11/core.h"
#elif defined(EPS_MACOSX)
#   include "epsilon/core/macosx/core.h"
#else
#   error Unrecognized platform!
#endif

// FIXME: endian
const eps_uint EPS_VERSION = (
    (0 << 24) | // major
    (0 << 16) | // minor
    (1 <<  8) | // micro
    (0 <<  0)   // pico
);

typedef struct _eps_DisplayInfo {
    eps_char* name;
    eps_uint  handle;
} eps_DisplayInfo;

/** Returns the version number.
 */
EPS_EXPORT(eps_uint) eps_core_getVersion(void);

/** Queries the existence of a system.
 * @param name Name of the system to query.
 * @returns True if the system is present.
 */
EPS_EXPORT(eps_bool) eps_core_hasSystem(const eps_char* name);

#endif
