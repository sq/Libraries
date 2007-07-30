
#ifndef EPS_CORE_WIN32_CORE_H
#define EPS_CORE_WIN32_CORE_H

#include "epsilon/core/win32/types.h"

/** Convenience method for setting options.
 * @note This may or may not become part of the core API.  
 *       I dunno yet.
 * @param buffer Buffer to recieve the options.
 * @param size Size of the buffer.
 * @return True if the buffer was big enough, else false.
 */
EPS_EXPORT(eps_bool) eps_core_fillOptions(eps_uint* buffer, eps_uint size, ...);

#endif
