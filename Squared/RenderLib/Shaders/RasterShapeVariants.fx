// O3 produces literally 1/3 the instructions of OD or O0 so let's just be kind to the driver
#pragma fxcparams(/O3 /Zi)

#pragma fxcvariant(EVALUATE_TYPE=TYPE_Ellipse,INCLUDE_ELLIPSE)
#pragma fxcvariant(EVALUATE_TYPE=TYPE_LineSegment,INCLUDE_LINE)
#pragma fxcvariant(EVALUATE_TYPE=TYPE_Rectangle,INCLUDE_RECTANGLE,OPTIMIZE_RECTANGLE_INTERIOR)
#pragma fxcvariant(EVALUATE_TYPE=TYPE_Triangle,INCLUDE_TRIANGLE)
#pragma fxcvariant(EVALUATE_TYPE=TYPE_Polygon,INCLUDE_POLYGON)
// ubershader
#pragma fxcvariant(EVALUATE_TYPE=type,INCLUDE_ELLIPSE,INCLUDE_LINE,INCLUDE_BEZIER,INCLUDE_RECTANGLE,INCLUDE_TRIANGLE,INCLUDE_ARC,INCLUDE_STAR,INCLUDE_COMPOSITES)

#pragma fxcflagset(VARIANT_NORMAL,VARIANT_SHADOWED,VARIANT_SIMPLE,VARIANT_SIMPLE_SHADOWED,VARIANT_TEXTURED,VARIANT_TEXTURED_SHADOWED,VARIANT_RAMP,VARIANT_RAMP_SHADOWED)


#if VARIANT_SIMPLE_SHADOWED
#define VARIANT_SIMPLE 1
#define VARIANT_SHADOWED 1
#endif

#if VARIANT_TEXTURED_SHADOWED
#define VARIANT_TEXTURED 1
#define VARIANT_SHADOWED 1
#endif

#if VARIANT_RAMP_SHADOWED
#define VARIANT_RAMP 1
#define VARIANT_SHADOWED 1
#endif


#include "RasterShapeConstants.fxh"
#if INCLUDE_POLYGON
#include "PolygonCommon.fxh"
#include "RasterPolygonImpl.fxh"
#endif
#include "RasterShapeSkeleton.fxh"


#if VARIANT_RAMP

uniform const float2 RampUVOffset;

Texture2D RampTexture;
sampler   RampTextureSampler {
    Texture = (RampTexture);
    AddressU = CLAMP;
    AddressV = WRAP;
    MipFilter = POINT;
    MinFilter = LINEAR;
    MagFilter = LINEAR;
};

// FIXME: In FNA ramp textures + sphere lights creates very jagged shadows but in XNA it does not.
float4 SampleFromRamp(float x) {
    return tex2Dlod(RampTextureSampler, float4(x, 0, 0, 0));
}

float4 SampleFromRamp2(float2 xy) {
    return tex2Dlod(RampTextureSampler, float4(xy, 0, 0));
}

#endif


void __VARIANT_FS_NAME (
    RASTERSHAPE_FS_ARGS
) {
    float2 tl, br, gradientWeight;
    float  fillAlpha, outlineAlpha, shadowAlpha;

    rasterShapeCommon(
        worldPositionTypeAndWorldSpace, VARIANT_SHADOWED != 0, VARIANT_SIMPLE != 0,
        ab, cd, params, params2, params3,
        GET_VPOS, tl, br,
        gradientWeight, fillAlpha, outlineAlpha, shadowAlpha
    );

    float4 fill = lerp(centerColor, edgeColor, gradientWeight.x);
#if VARIANT_RAMP
    fill *= SampleFromRamp2((1 - gradientWeight) + RampUVOffset);
#endif

#if VARIANT_TEXTURED
    result = texturedShapeCommon(
        worldPositionTypeAndWorldSpace.xy, texRgn,
        ab, cd,
        fill, outlineColor,
        fillAlpha, outlineAlpha, shadowAlpha,
        params, params2, tl, br, VARIANT_SHADOWED != 0, GET_VPOS
    );
#else
    result = composite(fill, outlineColor, fillAlpha, outlineAlpha, shadowAlpha, VARIANT_SIMPLE != 0, VARIANT_SHADOWED != 0, GET_VPOS);
#endif

    if (result.a <= 0.5 / 255) {
        discard;
        return;
    }
}


technique __VARIANT_TECHNIQUE_NAME
{
    pass P0
    {
        vertexShader = compile vs_3_0 RasterShapeVertexShader();
        pixelShader = compile ps_3_0 __VARIANT_FS_NAME();
    }
}