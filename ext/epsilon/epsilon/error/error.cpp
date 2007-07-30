
#include "epsilon/error/error.h"
#include <queue>
#include <cassert>
#include <algorithm>
#include <string.h>

namespace {
    /// Temp error queue until the thread stuff is in place.
    std::queue<eps_Error*> errors;
}

EPS_EXPORT(void) eps_error_postError(eps_Error* err) {
    assert(err != 0);
    errors.push(err);
}

EPS_EXPORT(void) eps_error_postErrorString(eps_uint code, const eps_char* msg) {
    eps_error_postError(
        eps_error_createError(code, msg)
    );
}

EPS_EXPORT(eps_Error*) eps_error_createError(eps_uint code, const eps_char* msg) {
    eps_Error* err = new eps_Error;

    err->code = code;
    err->message = strdup(msg ? msg : "");

    return err;
}

EPS_EXPORT(eps_uint) eps_error_getErrorCount(void) {
    return static_cast<eps_uint>(errors.size());
}

EPS_EXPORT(eps_Error*) eps_error_peekError(void) {
    return errors.front();
}

EPS_EXPORT(eps_Error*) eps_error_getError(void) {
    if (!errors.empty()) {
        eps_Error* err = errors.front();
        errors.pop();
        return err;
    } else {
        return 0;
    }
}

EPS_EXPORT(void) eps_error_destroyError(eps_Error* err) {
    free(err->message);
    err->message = "Error message was unallocated!"; // just in case some goober looks at the error after it's been destroyed
    delete err;
}
