#include "ViewTransformCommon.fxh"
#include "TargetInfo.fxh"
#include "DitherCommon.fxh"

// Approximations from http://chilliant.blogspot.com/2012/08/srgb-approximations-for-hlsl.html

float3 SRGBToLinear(float3 srgb) {
    return srgb * (srgb * (srgb * 0.305306011 + 0.682171111) + 0.012522878);
}

float3 LinearToSRGB(float3 rgb) {
    float3 S1 = sqrt(rgb);
    float3 S2 = sqrt(S1);
    float3 S3 = sqrt(S2);
    return 0.662002687 * S1 + 0.684122060 * S2 - 0.323583601 * S3 - 0.0225411470 * rgb;
}

#define TYPE_Ellipse 0
#define TYPE_LineSegment 1

#define DEFINE_QuadCorners const float2 QuadCorners[] = { \
    {0, 0}, \
    {1, 0}, \
    {1, 1}, \
    {0, 1} \
};

#define RASTERSHAPE_VS_ARGS \
    in int2 cornerIndex : BLENDINDICES0, \
    in float4 ab_in : POSITION0, \
    in float4 cd_in : POSITION1, \
    inout float3 outlineSizeMiterAndType : TEXCOORD0, \
    inout float4 centerColor : COLOR0, \
    inout float4 edgeColor : COLOR1, \
    inout float4 outlineColor : COLOR2, \
    out float4 result : POSITION0, \
    out float4 ab : TEXCOORD1, \
    out float4 cd : TEXCOORD2

#define RASTERSHAPE_VS_PROLOGUE \
    ab = ab_in; cd = cd_in; \
    float4 position = float4(ab_in.x, ab_in.y, 0, 1); \
    float2 a = ab.xy, b = ab.zw, c = cd.xy, radius = cd.zw; \
    float outlineSize = outlineSizeMiterAndType.x, miter = outlineSizeMiterAndType.y; \
    int type = outlineSizeMiterAndType.z; \
    DEFINE_QuadCorners

#define RASTERSHAPE_FS_ARGS \
    in float4 ab : TEXCOORD1, \
    in float4 cd : TEXCOORD2, \
    in float3 outlineSizeMiterAndType : TEXCOORD0, \
    in float4 centerColor : COLOR0, \
    in float4 edgeColor : COLOR1, \
    in float4 outlineColor : COLOR2, \
    ACCEPTS_VPOS, \
    out float4 result : COLOR0 \

#define RASTERSHAPE_FS_PROLOGUE \
    float2 a = ab.xy, b = ab.zw, c = cd.xy, radius = cd.zw; \
    float outlineSize = outlineSizeMiterAndType.x, miter = outlineSizeMiterAndType.y; \
    int type = outlineSizeMiterAndType.z; \
    centerColor.rgb = SRGBToLinear(centerColor.rgb); \
    edgeColor.rgb = SRGBToLinear(edgeColor.rgb); \
    outlineColor.rgb = SRGBToLinear(outlineColor.rgb);

uniform float HalfPixelOffset;

float4 TransformPosition(float4 position, bool halfPixelOffset) {
    // Transform to view space, then offset by half a pixel to align texels with screen pixels
    float4 modelViewPos = mul(position, Viewport.ModelView);
    if (halfPixelOffset && HalfPixelOffset)
        modelViewPos.xy -= 0.5;
    // Finally project after offsetting
    return mul(modelViewPos, Viewport.Projection);
}

void computeTLBR (
    int type, float2 totalRadius, 
    float2 a, float2 b, float2 c,
    out float2 tl, out float2 br
) {
    if (type == TYPE_Ellipse) {
        tl = a - totalRadius;
        br = a + totalRadius;
    } else if (type == TYPE_LineSegment) {
        // FIXME: Edge-fit a hull better instead of using a massive quad
        tl = min(a, b) - totalRadius;
        br = max(a, b) + totalRadius;
    }
}

void ScreenSpaceRasterShapeVertexShader (
    RASTERSHAPE_VS_ARGS
) {
    RASTERSHAPE_VS_PROLOGUE
    float2 totalRadius = radius + outlineSize + 1;
    float2 tl, br;

    computeTLBR(type, totalRadius, a, b, c, tl, br);

    position.xy = lerp(tl, br, QuadCorners[cornerIndex.x]);
    result = TransformPosition(
        float4(position.xy, position.z, 1), true
    );
}

void WorldSpaceRasterShapeVertexShader(
    RASTERSHAPE_VS_ARGS
) {
    RASTERSHAPE_VS_PROLOGUE
    float2 totalRadius = radius + outlineSize + 1;
    float2 tl, br;

    computeTLBR(type, totalRadius, a, b, c, tl, br);

    position.xy = lerp(tl, br, QuadCorners[cornerIndex.x]);
    result = TransformPosition(
        float4(position.xy * GetViewportScale().xy, position.z, 1), true
    );
}

float2 closestPointOnLine2(float2 a, float2 b, float2 pt, out float t) {
    float2  ab = b - a;
    t = dot(pt - a, ab) / dot(ab, ab);
    return a + t * ab;
}

float2 closestPointOnLineSegment2(float2 a, float2 b, float2 pt, out float t) {
    float2  ab = b - a;
    t = saturate(dot(pt - a, ab) / dot(ab, ab));
    return a + t * ab;
}

void RasterShapePixelShader(
    RASTERSHAPE_FS_ARGS
) {
    RASTERSHAPE_FS_PROLOGUE;

    float2 screenPosition = GET_VPOS;
    float  radiusLength = length(radius);

    float2 distanceXy;

    if (type == TYPE_Ellipse) {
        distanceXy = screenPosition - a;
    } else if (type == TYPE_LineSegment) {
        float t;
        float2 closestPoint = closestPointOnLineSegment2(a, b, screenPosition, t);
        distanceXy = screenPosition - closestPoint;
    }

    float  distanceF = length(distanceXy / radius);
    float  distance = distanceF * radiusLength;
    float  outlineDistance = (distance - radiusLength) / outlineSize;
    float4 gradient = lerp(centerColor, edgeColor, saturate(distanceF));
    float4 gradientToOutline = lerp(gradient, outlineColor, saturate(outlineDistance));
    float4 outlineToTransparent = lerp(
        float4(LinearToSRGB(gradientToOutline.rgb), gradientToOutline.a), 
        0, saturate(outlineDistance - 1)
    );

    result = outlineToTransparent;
    result.rgb = ApplyDither(result.rgb, screenPosition);
}

technique WorldSpaceRasterShape
{
    pass P0
    {
        vertexShader = compile vs_3_0 WorldSpaceRasterShapeVertexShader();
        pixelShader = compile ps_3_0 RasterShapePixelShader();
    }
}

technique ScreenSpaceRasterShape
{
    pass P0
    {
        vertexShader = compile vs_3_0 ScreenSpaceRasterShapeVertexShader();
        pixelShader = compile ps_3_0 RasterShapePixelShader();
    }
}