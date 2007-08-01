#ifndef _FR_CORE
#define _FR_CORE

#define _SCL_SECURE_NO_DEPRECATE

#include "libs.hpp"
#include "classes.hpp"

namespace core {

  template <class T>
  inline string ptrToString(T * ptr) {
    unsigned value = 0;
    unsigned size = sizeof(ptr) > sizeof(value) ? sizeof(value) : sizeof(ptr);
    memcpy(&value, &ptr, size);
    std::stringstream buffer;
    buffer << value;
    return buffer.str();
  }

}

#endif