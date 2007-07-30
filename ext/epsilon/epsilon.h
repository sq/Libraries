
#ifndef EPS_EPSILON_H
#define EPS_EPSILON_H

// Deduce platform, include core.
#if defined(_MSC_VER)
#   include "epsilon/config_msvc.h"
#else
#   include "epsilon/config.h"
#endif

#  if defined(EPS_WIN32)
#elif defined(EPS_X11)
#elif defined(EPS_MACOSX)
#elif defined(EPS_TRS80)
#else
#   error Unknown platform!  Did you forget a compiler switch?
#endif

// Pull in core
#include "epsilon/core/core.h"

#endif
