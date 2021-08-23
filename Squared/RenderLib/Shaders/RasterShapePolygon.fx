// O3 produces literally 1/3 the instructions of OD or O0 so let's just be kind to the driver
#pragma fxcparams(/O3 /Zi)

#include "RasterShapeConstants.fxh"

Texture2D VertexDataTexture : register(t7);

sampler VertexDataSampler : register(s7) {
    Texture = (VertexDataTexture);
    AddressU = CLAMP;
    AddressV = CLAMP;
    MipFilter = POINT;
    MinFilter = POINT;
    MagFilter = POINT;
};

#define SHAPE_TYPE_NAME RasterPolygonUntextured
#define SHAPE_TYPE_NAME_TEX RasterPolygonTextured
#define SHAPE_TYPE_TECHNIQUE_NAME RasterPolygonTechnique
#define SHAPE_TYPE_TECHNIQUE_NAME_TEX TexturedRasterPolygonTechnique
#define SHAPE_TYPE_NAME_SHADOWED ShadowedRasterPolygonUntextured
#define SHAPE_TYPE_NAME_TEX_SHADOWED ShadowedRasterPolygonTextured
#define SHAPE_TYPE_TECHNIQUE_NAME_SHADOWED ShadowedRasterPolygonTechnique
#define SHAPE_TYPE_TECHNIQUE_NAME_TEX_SHADOWED ShadowedTexturedRasterPolygonTechnique
#define SHAPE_TYPE_NAME_SIMPLE RasterPolygonSimple
#define SHAPE_TYPE_TECHNIQUE_NAME_SIMPLE RasterPolygonSimpleTechnique
#define SHAPE_TYPE_NAME_SIMPLE_SHADOWED ShadowedRasterPolygonSimple
#define SHAPE_TYPE_TECHNIQUE_NAME_SIMPLE_SHADOWED ShadowedRasterPolygonSimpleTechnique
#define SHAPE_TYPE_NAME_RAMP RasterPolygonRamp
#define SHAPE_TYPE_TECHNIQUE_NAME_RAMP RasterPolygonRampTechnique
#define SHAPE_TYPE_NAME_RAMP_SHADOWED ShadowedRasterPolygonRamp
#define SHAPE_TYPE_TECHNIQUE_NAME_RAMP_SHADOWED ShadowedRasterPolygonRampTechnique

#define INCLUDE_POLYGON
#define EVALUATE_TYPE TYPE_Polygon

// Separate header to avoid recompiling all the other shaders when it changes
#include "RasterPolygonImpl.fxh"
#include "RasterShapeSkeleton.fxh"
#include "RasterShapeImpl.fxh"