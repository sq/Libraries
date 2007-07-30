
#ifndef EPS_EPSILON_CONFIG_MSVC_H
#define EPS_EPSILON_CONFIG_MSVC_H

#if !defined(_MSC_VER)
#   error "How did this happen?  Non-MSVC compilers should not include this!"
#endif

// allow X11 target if it's explicitly asked for. (what the hell.  it could happen!)
#if !defined(EPS_X11)
#   define EPS_WIN32
#endif

#define EPS_WINDOWS

#if defined(_DEBUG)
#   define EPS_DEBUG
#   undef  EPS_RELEASE
#else
#   define EPS_RELEASE
#   undef  EPS_DEBUG
#endif

#define EPS_LITTLE_ENDIAN
#undef  EPS_BIG_ENDIAN

/*
 * Windows DLL always uses ASCII to ensure binary compatibility.
 * (note: this is gay.  UTF-8?)
 * Exception: static linkage.
 */
#if 0 || defined(UNICODE)
#   define EPS_UNICODE
#   undef  EPS_ASCII
#else
#   define EPS_ASCII
#   undef  EPS_UNICODE
#endif

#endif
