
#ifndef EPS_OPENGL_OPENGL_H
#define EPS_OPENGL_OPENGL_H

#include "epsilon.h"
#include "epsilon/wm/wm.h"

// Platform specific

#if defined(EPS_WIN32)
#   include "epsilon/opengl/win32/opengl.h"
#elif defined(EPS_X11)
#   include "epsilon/opengl/x11/opengl.h"
#elif defined(EPS_MACOSX)
#   include "epsilon/opengl/macosx/opengl.h"
#else
#   error Unrecognized platform!
#endif

// FIXME? Move this to eps_video?  Someplace else?
typedef enum _eps_opengl_PixelFormat {
    EPS_OPENGL_PF_INVALID,
    EPS_OPENGL_PF_DONTCARE,

    EPS_OPENGL_PF_8BPP = 0x100,
    EPS_OPENGL_PF_8I,

    EPS_OPENGL_PF_16BPP = 0x200,
    EPS_OPENGL_PF_X1R5G5B5,
    EPS_OPENGL_PF_R5G6B5,
    EPS_OPENGL_PF_X4R4G4B4,

    EPS_OPENGL_PF_24BPP = 0x300,
    EPS_OPENGL_PF_R8G8B8,

    EPS_OPENGL_PF_32BPP = 0x400,
    EPS_OPENGL_PF_X8R8G8B8,

    EPS_OPENGL_PF_64BPP = 0x500,

    EPS_OPENGL_PF_128BPP = 0x600,

    // alpha, accumulator, stencil, shadow, etc buffer things
} eps_opengl_PixelFormat;

/** Option... things.
 * Options are passed as an int array.  Each value, except the sentinel (EPS_OPENGL_OPT_END)
 * requires exactly one argument after it.  For some options, this value may be ignored.
 * These 'ignored' values are not.  They must be zero.  In the future, if they are given
 * meaning, zero will be taken to imply the current behaviour.
 *
 * If duplicates are present in the options list, the last one found will be taken as canon.
 *
 * It is an error to use a value not listed here.  The call that accepted the array will post
 * an EPS_ERROR_INVALID_ARGUMENT error in such an event.
 */
typedef enum _eps_opengl_VideoOption {
    EPS_OPENGL_OPT_END = 0,         ///< Sentinel value.  Indicates the end of the list of options.
    EPS_OPENGL_OPT_DEPTH_BITS,      ///< followed by the desired depth buffer... depth.
    EPS_OPENGL_OPT_STENCIL_BITS,    ///< followed by the desired stencil depth.
    EPS_OPENGL_OPT_REFRESH_RATE,    ///< followed by the desired refresh rate.  Default: whatever the underlying system thinks is convenient.
    EPS_OPENGL_OPT_FULL_SCREEN,     ///< followed by 1 or 0 (true or false) for fullscreen-ness.  Default is windowed.
    // alpha
    // accumulation buffer
    //EPS_OPENGL_OPT_DISPLAY,         ///< followed by the index of the display to start on
    // others?
} eps_opengl_VideoOption;

typedef struct _eps_OpenGLContext eps_OpenGLContext;

/** Create a window and an OpenGL context.
 * @param width Desired client width of the window, in pixels.
 * @param height Desired client height of the window, in pixels.
 * @param options Pointer to a zero-terminated array of eps_opengl_VideoOptions : int pairs.
 *                If this pointer is 0 (NULL), suitable defaults will be used.
 * @param pf The desired pixel format.
 */
EPS_EXPORT(eps_OpenGLContext*) eps_opengl_createOpenGLWindow(
    eps_uint width, eps_uint height, 
    eps_uint* options, eps_opengl_PixelFormat pf);

/** Destroy an OpenGL context and its enclosing window.
 * @param context The context to destroy.  If the context was created with eps_opengl_createOpenGLWindow,
 *                the window is destroyed as well.
 */
EPS_EXPORT(void) eps_opengl_destroyOpenGLWindow(eps_OpenGLContext* context);

/** Return a native OpenGL context handle from an epsilon context.
 * @param context The epsilon context.
 * @return A platform specific GL context.
 */
EPS_EXPORT(void*) eps_opengl_getNativeOpenGLContext(eps_OpenGLContext* context);

/** Return the window to which the OpenGL context is attached.
 * Free contexts return 0.
 *
 * @param context The epsilon context.
 * @return The containing window, or 0 if none.
 */
EPS_EXPORT(eps_Window*) eps_opengl_getContextWindow(eps_OpenGLContext* context);

/** Create a 'free' epsilon OpenGL context from a native one.
 * It is important to know that the client code assumes ownership of this pointer.
 * Forgetting to destroy it constitutes a leak!
 *
 * This is useful for using an epsilon OpenGL client in a more complex
 * GUI.
 *
 * @param nativeContext The context to wrap.
 * @return The epsilon context.
 */
EPS_EXPORT(eps_OpenGLContext*) eps_opengl_createFreeOpenGLContext(void* nativeContext);

/** Deallocates an epsilon OpenGL context created with eps_opengl_createFreeOpenGLContext
 * This does not destroy the platform specific context handle.
 * Do not use this on contexts created in other ways.
 *
 * @param context The context to deallocate.
 */
EPS_EXPORT(void) eps_opengl_destroyFreeOpenGLContext(eps_OpenGLContext* context);

/** Resizes the window, changing video modes if fullscreen.
 * @param context The context.
 * @param xres Desired horizontal width, in pixels.
 * @param yres Desired vertical height, in pixels.
 * @param options Misc options, of the same format that eps_wm_createWindow accepts.
 * @param pf Desired pixel format.  EPS_OPENGL_PF_DONTCARE preserves the current pixel format.
 * @return True if it worked.  False if there was an error.
 */
EPS_EXPORT(eps_bool) eps_opengl_setResolution(
    eps_OpenGLContext* context, 
    eps_uint xres, eps_uint yres, 
    eps_uint* options, eps_opengl_PixelFormat pf);

/** Retrieves the address of an extension procedure, given its name.
 * @param procName Name of the procedure.
 * @return A pointer to the procedure, or zero if it could not be retrieved. 
 *         (this usually means that it doesn't exist)
 */
EPS_EXPORT(eps_opengl_Proc) eps_opengl_getProcAddress(const eps_char* procName);

/** Swaps backbuffers.
 * @param context The context to swap.
 */
EPS_EXPORT(void) eps_opengl_swapBuffers(eps_OpenGLContext* context);

/** Set the currently active context.  Only one context can be active per thread.
 * @param context The context to activate.
 */
EPS_EXPORT(void) eps_opengl_setCurrent(eps_OpenGLContext* context);

#endif
