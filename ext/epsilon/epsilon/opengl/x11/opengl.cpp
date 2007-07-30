
#include "epsilon/opengl/opengl.h"
#include "epsilon/wm/x11/internal.h"
#include "epsilon/error/error.h"
#include <GL/glx.h>

struct _eps_OpenGLContext {
    eps_Window* window;
    GLXContext context;
    XSetWindowAttributes attributes;
	bool ownsWindow;

	_eps_OpenGLContext(eps_Window* window=0, GLXContext ctx=0, XSetWindowAttributes attrs=XSetWindowAttributes(), bool ownsWindow=false)
		: window(window)
		, context(ctx)
		, attributes(attrs)
		, ownsWindow(ownsWindow)
	{}
};

// implementation details
namespace {
    const int attribList_32bpp[] = {
        GLX_RGBA,
        GLX_DOUBLEBUFFER,
        GLX_RED_SIZE,    8,
        GLX_BLUE_SIZE,   8,
        GLX_GREEN_SIZE,  8,
        GLX_DEPTH_SIZE, 16,
        None
    };

    const int attribList_16bpp[] = {
        GLX_RGBA,
        GLX_DOUBLEBUFFER,
        GLX_RED_SIZE,    4,
        GLX_BLUE_SIZE,   4,
        GLX_GREEN_SIZE,  4,
        GLX_DEPTH_SIZE, 16,
        None
    };

    const int* getAttribs(eps_opengl_PixelFormat pf) {
        switch (pf) {
            case EPS_OPENGL_PF_16BPP:
            case EPS_OPENGL_PF_X4R4G4B4:
            case EPS_OPENGL_PF_R5G6B5:
            case EPS_OPENGL_PF_X1R5G5B5:
                return attribList_16bpp;

            case EPS_OPENGL_PF_24BPP:
            case EPS_OPENGL_PF_R8G8B8:
            case EPS_OPENGL_PF_32BPP:
            case EPS_OPENGL_PF_X8R8G8B8:
                return attribList_32bpp;

            default:
                return 0;
        }
    }
}

EPS_EXPORT(eps_OpenGLContext*) eps_opengl_createOpenGLWindow(
    eps_uint width, eps_uint height, 
    eps_uint* options, eps_opengl_PixelFormat pf
) {
    /*
     * GLX doesn't seem all that receptive to the notion of
     * decoupling window creation with the GL context creation.
     * (not the way I want to do it anyway.  gah)
     */
    int q = 0;
    eps_OpenGLContext* context = new eps_OpenGLContext;
    try {
        const int* attribList = getAttribs(pf);
        if (!attribList) throw 0;

        context->window = new eps_Window;
        context->window->display = XOpenDisplay(0);
        if (!context->window->display) throw "Unable to open display";

        int dummy = 0;
        if (!glXQueryExtension(context->window->display, &dummy, &dummy)) {
            throw "X server does not support GLX";
        }

        int screen = DefaultScreen(context->window->display);

        XVisualInfo* visualInfo = glXChooseVisual(
            context->window->display, 
            screen, 
            const_cast<int*>(attribList)
        );
        if (!visualInfo) {
            throw "No RGB visual with depth buffer";
        }

        if (visualInfo->c_class != TrueColor) {
            throw "TrueColour visual unavailable";
        }        

        context->window = eps_wm_createWindow(width, height, 0);
        if (!context->window) throw 1;

        if (!visualInfo) throw 2;

        // TODO: sharing context namespaces. (textures, display lists etc)
        context->context = glXCreateContext(context->window->display, visualInfo, 0, GL_TRUE);

        return context;

    } catch (int i) {
        const char* messages[] = {
            "Invalid pixel format.  15, 16, 24, and 32 bpp modes only for now!",
            "Unable to create window.",
            "Unable to find a suitable video mode.",
        };
        eps_error_postErrorString(EPS_ERROR_GENERAL, messages[i]);

        return 0;

    } catch (const char* msg) {
        eps_error_postErrorString(EPS_ERROR_GENERAL, msg);
        return 0;
    }
}

EPS_EXPORT(void) eps_opengl_destroyOpenGLWindow(eps_OpenGLContext* context) {
	glXDestroyContext(context->window->display, context->context);
	if (context->window) {
		eps_wm_destroyWindow(context->window);
	}
	delete context;
}

EPS_EXPORT(void*) eps_opengl_getNativeOpenGLContext(eps_OpenGLContext* context) {
	eps_error_postErrorString(EPS_ERROR_INTERNAL, "eps_opengl_getNativeOpenGLContext is not yet implemented!");
	return 0;
}

EPS_EXPORT(eps_Window*) eps_opengl_getContextWindow(eps_OpenGLContext* context) {
	return context->window;
}

EPS_EXPORT(eps_OpenGLContext*) eps_opengl_createFreeOpenGLContext(void* nativeContext) {
	eps_error_postErrorString(EPS_ERROR_INTERNAL, "eps_opengl_createFreeOpenGLContext is not yet implemented!");
	return 0;
}

EPS_EXPORT(void) eps_opengl_destroyFreeOpenGLContext(eps_OpenGLContext* context) {
	if (!context) {
		eps_error_postErrorString(EPS_ERROR_INVALID_ARGUMENT, "Null context passed to eps_opengl_destroyFreeOpenGLContext");
	}

	glXDestroyContext(context->window->display, context->context);
}

EPS_EXPORT(eps_bool) eps_opengl_setResolution(
    eps_OpenGLContext* context, 
    eps_uint xres, eps_uint yres, 
    eps_uint* options, eps_opengl_PixelFormat pf
) {
}

EPS_EXPORT(eps_opengl_Proc) eps_opengl_getProcAddress(const eps_char* procName) {
	const GLubyte* ptr = reinterpret_cast<const GLubyte*>(procName);
	return reinterpret_cast<eps_opengl_Proc>(glXGetProcAddress(ptr));
}

EPS_EXPORT(void) eps_opengl_swapBuffers(eps_OpenGLContext* context) {
    glXSwapBuffers(context->window->display, context->window->window);

    if (int i = glGetError()) {
        char blah[256];
        sprintf(blah, "GL error %d", i);
        eps_error_postErrorString(EPS_ERROR_INTERNAL, blah);
    }

}

EPS_EXPORT(void) eps_opengl_setCurrent(eps_OpenGLContext* context) {
}
