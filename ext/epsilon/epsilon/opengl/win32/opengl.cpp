
#include "epsilon.h"
#include "epsilon/error/error.h"
#include "epsilon/opengl/opengl.h"
#include "epsilon/wm/win32/internal.h"

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <gl/gl.h>
#include <sstream>

struct _eps_OpenGLContext {
    HGLRC glrc;
    HDC hdc;
    eps_Window* window;

    _eps_OpenGLContext()
        : glrc(0)
        , hdc(0)
        , window(0)
    {}
};

// implementation details
namespace {
    /// Parsed video options
    struct DisplayOptions {
        eps_uint depthBits;
        eps_uint stencilBits;
        eps_uint refreshRate;
        eps_bool fullScreen;

        DisplayOptions()
            : depthBits(0)
            , stencilBits(0)
            , refreshRate(0)
            , fullScreen(0)
        {}
    };

    eps_bool parseOptions(eps_uint* in, DisplayOptions* out) {
        eps_uint i = 0;
        while (true) {
            eps_opengl_VideoOption opt = static_cast<eps_opengl_VideoOption>(in[i]);

            if (opt == EPS_OPENGL_OPT_END) {
                break;
            }

            eps_uint value = in[i + 1];
            i += 2;
            switch (opt) {
                case EPS_OPENGL_OPT_DEPTH_BITS:     out->depthBits = value;     break;
                case EPS_OPENGL_OPT_STENCIL_BITS:   out->stencilBits = value;   break;
                case EPS_OPENGL_OPT_REFRESH_RATE:   out->refreshRate = value;   break;
                case EPS_OPENGL_OPT_FULL_SCREEN:    out->fullScreen = (value != 0);     break;
                default:
                    std::stringstream ss;
                    ss << "Invalid OpenGL option " << opt;
                    eps_error_postErrorString(EPS_ERROR_INVALID_ARGUMENT, ss.str().c_str());
                    return false;
            }
        }

        return true;
    }

    void setPFD(PIXELFORMATDESCRIPTOR* pfd, int cb, int rb, int rs, int gb, int gs, int bb, int bs) {
        pfd->nSize = sizeof pfd;
        pfd->nVersion = 1;
        pfd->dwFlags = PFD_DRAW_TO_WINDOW | PFD_SUPPORT_OPENGL | PFD_DOUBLEBUFFER;
        pfd->iPixelType = PFD_TYPE_RGBA;
        pfd->cColorBits = cb;
        pfd->cRedBits   = rb;   pfd->cRedShift = rs;
        pfd->cGreenBits = gb;   pfd->cGreenShift = gs;
        pfd->cBlueBits  = bb;   pfd->cBlueShift = bs;
        pfd->cDepthBits = 16;   // FIXME: way to set different depth buffer sizes!
    }

    void convertPixelFormat(PIXELFORMATDESCRIPTOR* pfd, eps_opengl_PixelFormat pf) {
        memset(pfd, 0, sizeof pfd);

        // I hate this.  Greatly.
        switch (pf) {
            case EPS_OPENGL_PF_8I:
                pfd->iPixelType = PFD_TYPE_COLORINDEX;
            case EPS_OPENGL_PF_8BPP:
                pfd->cColorBits = 8;
                break;

            case EPS_OPENGL_PF_X1R5G5B5:    setPFD(pfd, 15, 5, 10, 5, 5, 5, 0);     break;
            case EPS_OPENGL_PF_X4R4G4B4:    setPFD(pfd, 12, 4,  8, 4, 4, 4, 0);     break;
            case EPS_OPENGL_PF_16BPP:
            case EPS_OPENGL_PF_R5G6B5:      setPFD(pfd, 16, 5, 11, 6, 5, 5, 0);     break;

            case EPS_OPENGL_PF_24BPP:
            case EPS_OPENGL_PF_R8G8B8:      setPFD(pfd, 24, 8, 16, 8, 8, 8, 0);     break;

            case EPS_OPENGL_PF_32BPP:       
            case EPS_OPENGL_PF_X8R8G8B8:    
                setPFD(pfd, 24, 8, 16, 8, 8, 8, 0);
                // probably not used, but what the hell.
                pfd->cAlphaBits = 8;
                pfd->cAlphaShift = 24;
                break;

            default:
                std::stringstream ss;
                ss << "Invalid pixel format " << static_cast<int>(pf);
                eps_error_postErrorString(EPS_ERROR_INVALID_ARGUMENT, ss.str().c_str());
                break;
        }
    }
}

EPS_EXPORT(eps_OpenGLContext*) eps_opengl_createOpenGLWindow(eps_uint width, eps_uint height, eps_uint* options, eps_opengl_PixelFormat pf) {
    PIXELFORMATDESCRIPTOR pfd;
    eps_OpenGLContext* context = new eps_OpenGLContext;

    /*DisplayOptions displayOptions;
    if (options) {
        bool result = parseOptions(options, &displayOptions);
        if (!result) {
            return 0;
        }
    }*/

    // TODO: fullscreen :P

    try {
        convertPixelFormat(&pfd, pf);
        // size is 0 only when pf is invalid.
        if (pfd.nSize == 0)         throw "Invalid pixel format";

        context->window = eps_wm_createWindow(width, height, 0);
        if (context->window == 0)   throw "Unable to create window";

        context->hdc = GetDC(context->window->handle);
        if (!context->hdc)          throw "Unable to create DC";

        eps_uint pixelFormat = ChoosePixelFormat(context->hdc, &pfd);
        if (!pixelFormat)           throw "Unable to find suitable pixel format";

        int result = SetPixelFormat(context->hdc, pixelFormat, &pfd);
        if (!result)                throw "Unable to set pixel format";

        context->glrc = wglCreateContext(context->hdc);
        if (!context->glrc)         throw "Unable to create context";

        wglMakeCurrent(context->hdc, context->glrc);
        SetForegroundWindow(context->window->handle);
        SetFocus(context->window->handle);

    } catch (const char* msg) {
        /*
         * Sloppy error checking! :D
         *
         * TODO: report this correctly.
         * 0 - invalid pixel format
         * 1 - unable to create window
         * 2 - unable to create DC
         * 3 - unable to find suitable pixel format
         * 4 - unable to set pixel format
         * 5 - unable to create context
         */

        // cleanup
        if (context != 0) {
            if (context->glrc)          wglDeleteContext(context->glrc);
            if (context->hdc)           ReleaseDC(context->window->handle, context->hdc);
            if (context->window != 0)   eps_wm_destroyWindow(context->window);
            delete context;
            context = 0;
        }

        eps_error_postErrorString(EPS_ERROR_GENERAL, msg);
    }

    return context;
}

EPS_EXPORT(void) eps_opengl_destroyOpenGLWindow(eps_OpenGLContext* context) {
    if (context->glrc)  wglDeleteContext(context->glrc);
    if (context->hdc) {
        if (context->window) {
            ReleaseDC(context->window->handle, context->hdc);
        } else {
            //eps_error_PostError("eps_OpenGLContext has HDC but no window handle!  Can't free it!");
        }
    }
    if (context->window) {
        eps_wm_destroyWindow(context->window);
    }
    delete context;
}

EPS_EXPORT(void*) eps_opengl_getNativeOpenGLContext(eps_OpenGLContext* context) {
    if (!context) return 0;
    return context->glrc;
}

EPS_EXPORT(eps_Window*) eps_opengl_getContextWindow(eps_OpenGLContext* context) {
    if (!context) return 0;
    return context->window;
}

EPS_EXPORT(eps_OpenGLContext*) eps_opengl_createFreeOpenGLContext(void* nativeContext) {
    eps_OpenGLContext* context = new eps_OpenGLContext;
    context->glrc = reinterpret_cast<HGLRC>(nativeContext);
    return context;
}

EPS_EXPORT(void) eps_opengl_destroyFreeOpenGLContext(eps_OpenGLContext* context) {
    if (context == 0) return;
    delete context;
}

EPS_EXPORT(eps_bool) eps_opengl_setResolution(
    eps_OpenGLContext* context, 
    eps_uint xres, eps_uint yres, 
    eps_uint* options, eps_opengl_PixelFormat pf
) {
    if (context == 0 || context->window == 0) return false;
    eps_wm_moveWindow(context->window, 0, 0, xres, yres);
    return true;
}

EPS_EXPORT(eps_opengl_Proc) eps_opengl_getProcAddress(const eps_char* procName) {
#ifndef EPS_UNICODE
    return reinterpret_cast<eps_opengl_Proc>(wglGetProcAddress(procName));
#else
#   error "Not ready for EPS_UNICODE on win32 yet!  eps_opengl_getProcAddress not properly implemented!"
#endif
}

EPS_EXPORT(void) eps_opengl_swapBuffers(eps_OpenGLContext* context) {
    if (context == 0 || context->hdc == 0) {
        eps_error_postErrorString(EPS_ERROR_INTERNAL, "Invalid OpenGL context!");
        return; // !!!
    }

    SwapBuffers(context->hdc);
}

EPS_EXPORT(void) eps_opengl_setCurrent(eps_OpenGLContext* context) {
    if (context == 0 || context->hdc == 0) {
        eps_error_postErrorString(EPS_ERROR_INTERNAL, "Invalid OpenGL context!");
        return; // !!!
    }
    wglMakeCurrent(context->hdc, context->glrc);
}
