// @#*(%@#K% sprintf
// #include <stdio.h>

#define STBIDEF  extern "C" __declspec(dllexport)
#define STBIWDEF extern "C" __declspec(dllexport)
#define STBIRDEF extern "C" __declspec(dllexport)

#define STBI_NO_STDIO
// #define STBI_WRITE_NO_STDIO

#define STBI_BUFFER_SIZE 40960

#define STBI_NO_BMP
#define STBI_NO_GIF
#define STBI_NO_PIC
#define STBI_NO_PNM
#define STBI_NO_HDR

// #define STBI_WRITE_NO_HDR

#define STBIR_MAX_CHANNELS 4

#include <stdio.h>

#include "stb_image.h"
#include "stb_image_write.h"
#include "stb_image_resize.h"

#define STB_IMAGE_IMPLEMENTATION
#define STB_IMAGE_WRITE_IMPLEMENTATION
#define STB_IMAGE_RESIZE_IMPLEMENTATION

#include "stb_image.h"
#include "stb_image_write.h"
#include "stb_image_resize.h"

STBIWDEF int get_stbi_write_png_compression_level () {
    return stbi_write_png_compression_level;
}

STBIWDEF void set_stbi_write_png_compression_level (int level) {
    stbi_write_png_compression_level = level;
}