// Copyright (c) 2003 Daniel Wallin and Arvid Norberg

// Permission is hereby granted, free of charge, to any person obtaining a
// copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following conditions:

// The above copyright notice and this permission notice shall be included
// in all copies or substantial portions of the Software.

// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF
// ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED
// TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A
// PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT
// SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR
// ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN
// ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE
// OR OTHER DEALINGS IN THE SOFTWARE.

#ifndef LUA_INCLUDE_HPP_INCLUDED
#define LUA_INCLUDE_HPP_INCLUDED

extern "C"
{
	#include "lua/lua.h"
/*
    // Cygon fix:
    // ref.cpp expects these constants from lauxlib.h to be defined.
    // if, however, lauxlib actually gets included, some functions will
    // cause strange errors, likely because these function names are
    // defines for something else in lauxlib.h
    #ifndef LUA_NOREF
    #define LUA_NOREF       (-2)
    #endif
    #ifndef LUA_REFNIL
    #define LUA_REFNIL      (-1)
    #endif
*/
}

#endif

