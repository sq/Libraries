#include "ViewTransformCommon.fxh"
#include "DitherCommon.fxh"

float4 TransformPosition(float4 position, float offset) {
    // Transform to view space, then offset by half a pixel to align texels with screen pixels
#ifdef FNA
    // ... Except for OpenGL, who don't need no half pixels
    float4 modelViewPos = mul(position, Viewport.ModelView);
#else
    float4 modelViewPos = mul(position, Viewport.ModelView) - float4(offset, offset, 0, 0);
#endif
    // Finally project after offsetting
    return mul(modelViewPos, Viewport.Projection);
}

void ScreenSpaceVertexShader(
    in float3 position : POSITION0, // x, y
    inout float4 centerAndRadius : TEXCOORD0,
    inout float outlineSize : TEXCOORD1,
    inout float4 centerColor : COLOR0,
    inout float4 edgeColor : COLOR1,
    inout float4 outlineColor : COLOR2,
    out float2 screenPosition : TEXCOORD2,
    out float4 result : POSITION0
) {
    result = TransformPosition(float4(position.xy, position.z, 1), 0.5);
    screenPosition = position.xy;
}

void WorldSpaceVertexShader(
    in float3 position : POSITION0, // x, y
    inout float4 centerAndRadius : TEXCOORD0,
    inout float outlineSize : TEXCOORD1,
    inout float4 centerColor : COLOR0,
    inout float4 edgeColor : COLOR1,
    inout float4 outlineColor : COLOR2,
    out float2 screenPosition : TEXCOORD2,
    out float4 result : POSITION0
) {
    result = TransformPosition(float4(position.xy * Viewport.Scale.xy, position.z, 1), 0.5);
    screenPosition = position.xy;
}

void EllipsePixelShader(
    in float4 centerAndRadius : TEXCOORD0,
    in float outlineSize : TEXCOORD1,
    in float2 screenPosition : TEXCOORD2,
    in float4 centerColor : COLOR0, 
    in float4 edgeColor : COLOR1, 
    in float4 outlineColor : COLOR2,
    out float4 result : COLOR0
) {
    float2 radiusXy = centerAndRadius.zw;
    float  radius = length(radiusXy);

    float2 distanceXy = screenPosition - centerAndRadius.xy;
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
        vertexShader = compile vs_3_0 WorldSpaceVertexShader();
        pixelShader = compile ps_3_0 EllipsePixelShader();
    }
}

technique ScreenSpaceEllipse
{
    pass P0
    {
        vertexShader = compile vs_3_0 ScreenSpaceVertexShader();
        pixelShader = compile ps_3_0 EllipsePixelShader();
    }
}