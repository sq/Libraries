#include "ViewTransformCommon.fxh"
#include "TargetInfo.fxh"
#include "DitherCommon.fxh"

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
    float2 a = ab.xy, b = ab.zw, c = cd.xy, d = cd.zw; \
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
    float2 a = ab.xy, b = ab.zw, c = cd.xy, d = cd.zw; \
    float outlineSize = outlineSizeMiterAndType.x, miter = outlineSizeMiterAndType.y; \
    int type = outlineSizeMiterAndType.z;

uniform float HalfPixelOffset;

float4 TransformPosition(float4 position, bool halfPixelOffset) {
    // Transform to view space, then offset by half a pixel to align texels with screen pixels
    float4 modelViewPos = mul(position, Viewport.ModelView);
    if (halfPixelOffset && HalfPixelOffset)
        modelViewPos.xy -= 0.5;
    // Finally project after offsetting
    return mul(modelViewPos, Viewport.Projection);
}

void ScreenSpaceEllipseVertexShader (
    RASTERSHAPE_VS_ARGS
) {
    RASTERSHAPE_VS_PROLOGUE
    float2 totalRadius = b + outlineSize + 1;
    float2 tl = a - totalRadius, br = a + totalRadius;
    position.xy = lerp(tl, br, QuadCorners[cornerIndex.x]);
    result = TransformPosition(
        float4(position.xy, position.z, 1), true
    );
}

void WorldSpaceEllipseVertexShader(
    RASTERSHAPE_VS_ARGS
) {
    RASTERSHAPE_VS_PROLOGUE
    float2 totalRadius = b + outlineSize + 1;
    float2 tl = a - totalRadius, br = a + totalRadius;
    position.xy = lerp(tl, br, QuadCorners[cornerIndex.x]);
    result = TransformPosition(
        float4(position.xy * GetViewportScale().xy, position.z, 1), true
    );
}

void EllipsePixelShader(
    RASTERSHAPE_FS_ARGS
) {
    RASTERSHAPE_FS_PROLOGUE;

    float2 screenPosition = GET_VPOS;
    float2 radiusXy = b;
    float  radius = length(radiusXy);

    float2 distanceXy = screenPosition - a;
    float  distanceF = length(distanceXy / radiusXy);
    float  distance = distanceF * radius;
    float  outlineDistance = (distance - radius) / outlineSize;
    float4 gradient = lerp(centerColor, edgeColor, saturate(distanceF));
    float4 gradientToOutline = lerp(gradient, outlineColor, saturate(outlineDistance));
    float4 outlineToTransparent = lerp(gradientToOutline, 0, saturate(outlineDistance - 1));

    result = outlineToTransparent;
}

technique WorldSpaceEllipse
{
    pass P0
    {
        vertexShader = compile vs_3_0 WorldSpaceEllipseVertexShader();
        pixelShader = compile ps_3_0 EllipsePixelShader();
    }
}

technique ScreenSpaceEllipse
{
    pass P0
    {
        vertexShader = compile vs_3_0 ScreenSpaceEllipseVertexShader();
        pixelShader = compile ps_3_0 EllipsePixelShader();
    }
}