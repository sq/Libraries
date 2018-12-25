#define STBIDEF  extern "C" __declspec(dllexport)
#define STBIWDEF extern "C" __declspec(dllexport)

#define STBI_NO_STDIO
#define STBI_WRITE_NO_STDIO

#define STBI_WRITE_NO_HDR
#define STBI_BUFFER_SIZE 40960

#define STBI_NO_BMP
#define STBI_NO_GIF
#define STBI_NO_HDR
#define STBI_NO_PIC
#define STBI_NO_PNM

#include "stb_image.h"
#include "stb_image_write.h"

#define STB_IMAGE_IMPLEMENTATION
#define STB_IMAGE_WRITE_IMPLEMENTATION

#include "stb_image.h"
#include "stb_image_write.h"
