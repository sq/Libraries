
#ifndef EPS_EPSILON_ERROR_H
#define EPS_EPSILON_ERROR_H

#include "../../epsilon.h"

typedef enum _eps_ErrorCode {
    EPS_ERROR_USER = 0, ///< The first few thousand error messages are reserved for client code

    EPS_ERROR_RESERVED = 0x10000,
    EPS_ERROR_GENERAL,  ///< Catch-all error code
    EPS_ERROR_INTERNAL, ///< Signifies that it's our fault, not yours.
    EPS_ERROR_UNKNOWN,  ///< Even we have no idea what went wrong!

    EPS_ERROR_INVALID_ARGUMENT, ///< You gave us a funny value.
} eps_ErrorCode;

typedef struct _eps_Error {
    eps_uint code;
    eps_char* message;
} eps_Error;

/** Posts an error to the queue for the current thread.
 * @note Epsilon assumes ownership of the error object.
 *       Do not deallocate it.
 * @param err The error to post.
 */
EPS_EXPORT(void) eps_error_postError(eps_Error* err);

/** Creates and posts an error with the given code and
 * message to the current thread.
 * @param code Error code enum... thing.
 * @param msg Diagnostic message to attach to the error.
 */
EPS_EXPORT(void) eps_error_postErrorString(eps_uint code, const eps_char* msg);

/** Creates an eps_Error which can later be posted.
 * @note The string message is copied.  
 *       Epsilon will not deallocate the string.
 * @param code Error code.
 * @param msg Diagnostic error message to attach to the error.
 */
EPS_EXPORT(eps_Error*) eps_error_createError(eps_uint code, const eps_char* msg);

/** Returns the number of errors in the queue.
 * @return The count, silly.
 */
EPS_EXPORT(eps_uint) eps_error_getErrorCount(void);

/** Peek at the front of the error queue.
 * @return The next error in the queue, or 0 if it is empty.
 */
EPS_EXPORT(eps_Error*) eps_error_peekError(void);

/** Returns the next error in the currently running thread.
 * @note Don't forget to free the error with 
 *       eps_error_destroyError() when you're through!
 *       Alternatively, re-posting via eps_error_postError()
 *       is safe.
 * @return The error object, or 0 if no errors have occurred.
 */
EPS_EXPORT(eps_Error*) eps_error_getError(void);

/** Deallocates an error.
 * @param err The error to deallocate.  Can be 0.
 */
EPS_EXPORT(void) eps_error_destroyError(eps_Error* err);

#endif
