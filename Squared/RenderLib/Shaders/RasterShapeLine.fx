// O3 produces literally 1/3 the instructions of OD or O0 so let's just be kind to the driver
#pragma fxcparams(/O3 /Zi)

#define SHAPE_TYPE_NAME RasterLineUntextured
#define SHAPE_TYPE_NAME_TEX RasterLineTextured
#define SHAPE_TYPE_TECHNIQUE_NAME RasterLineTechnique
#define SHAPE_TYPE_TECHNIQUE_NAME_TEX TexturedRasterLineTechnique
#define SHAPE_TYPE_NAME_SHADOWED ShadowedRasterLineUntextured
#define SHAPE_TYPE_NAME_TEX_SHADOWED ShadowedRasterLineTextured
#define SHAPE_TYPE_TECHNIQUE_NAME_SHADOWED ShadowedRasterLineTechnique
#define SHAPE_TYPE_TECHNIQUE_NAME_TEX_SHADOWED ShadowedTexturedRasterLineTechnique

#define INCLUDE_LINE

#include "RasterShapeSkeleton.fxh"
#include "RasterShapeImpl.fxh"