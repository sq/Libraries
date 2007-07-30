
/*
 * Epsilon - Core
 *
 * Type definitions for the X11 platform.
 *
 * 07 June 2004
 * Coded by Andy Friesen
 * See license.txt for redistribution terms.
 */

#ifndef EPS_CORE_TYPES_WIN32_H
#define EPS_CORE_TYPES_WIN32_H

#if !defined(EPS_X11)
#   error How did you get here?  This is X11 code.
#endif

// config.h?

#ifdef __cplusplus
#   define EPS_EXTERN_C extern "C"
#else
#   define EPS_EXTERN_C
#endif

// combinatorics are a real bitch
#if defined(EPS_WINDOWS)
#   if defined(_USRDLL)
#       if defined(EPS_EXPORTING)
#           define EPS_EXPORT(type) EPS_EXTERN_C __declspec(dllexport) type __stdcall
#       else
#           define EPS_EXPORT(type) EPS_EXTERN_C __declspec(dllimport) type __stdcall
#       endif
#   else
#      define EPS_EXPORT(type) EPS_EXTERN_C type __stdcall
#   endif
#else
#   define EPS_EXPORT(type) EPS_EXTERN_C type
#endif

#if defined (__GNUC__)
    // GCC

    #ifdef EPS_UNICODE
        typedef        wchar_t eps_char;
    #else
        typedef           char eps_char;
    #endif

    typedef unsigned      char eps_u8;
    typedef   signed      char eps_s8;
    typedef unsigned short int eps_u16;
    typedef   signed short int eps_s16;
    typedef unsigned       int eps_u32;
    typedef   signed       int eps_s32;

    typedef unsigned long long eps_u64;
    typedef          long long eps_s64;

    typedef unsigned       int eps_uint;
    typedef   signed       int eps_int;

    typedef eps_uint           eps_bool;

    typedef struct 
    { eps_u16 low, high; }     eps_u16_x2;
    typedef struct 
    { eps_s16 low, high; }     eps_s16_x2;

#else
#   error Unsupported compiler!  Fixme!
#endif

#endif
