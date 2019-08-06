#include "CompilerWorkarounds.fxh"
#include "ViewTransformCommon.fxh"
#include "BitmapCommon.fxh"
#include "TargetInfo.fxh"
#include "DitherCommon.fxh"

#define LUT_BITMAP
#include "LUTCommon.fxh"

uniform float2 LightmapUVOffset;

float4 LightmappedPixelShaderCore(
    in float4 multiplyColor,
    in float4 addColor,
    in float2 texCoord1,
    in float4 texRgn1,
    in float2 texCoord2,
    in float4 texRgn2
) {
    float2 lightmapTexCoord = clamp2(texCoord2 + LightmapUVOffset, texRgn2.xy, texRgn2.zw);

    float4 lightmapColor = tex2D(TextureSampler2, lightmapTexCoord) * 2;
    lightmapColor.a = 1;

    addColor.rgb *= addColor.a;
    addColor.a = 0;

    multiplyColor = multiplyColor * lightmapColor;

    texCoord1 = clamp2(texCoord1, texRgn1.xy, texRgn1.zw);

    float4 result = multiplyColor * tex2D(TextureSampler, texCoord1);
    result += (addColor * result.a);
    return result;
}

void LightmappedPixelShader(
    in float4 multiplyColor : COLOR0, 
    in float4 addColor : COLOR1, 
    in float2 texCoord1 : TEXCOORD0,
    in float4 texRgn1 : TEXCOORD1,
    in float2 texCoord2 : TEXCOORD2,
    in float4 texRgn2 : TEXCOORD3,
    ACCEPTS_VPOS,
    out float4 result : COLOR0
) {
    result = LightmappedPixelShaderCore(multiplyColor, addColor, texCoord1, texRgn1, texCoord2, texRgn2);

    result.rgb = ApplyDither(result.rgb, GET_VPOS);

    const float discardThreshold = (1.0 / 255.0);
    clip(result.a - discardThreshold);
}

void sRGBLightmappedPixelShader(
    in float4 multiplyColor : COLOR0,
    in float4 addColor : COLOR1,
    in float2 texCoord1 : TEXCOORD0,
    in float4 texRgn1 : TEXCOORD1,
    in float2 texCoord2 : TEXCOORD2,
    in float4 texRgn2 : TEXCOORD3,
    ACCEPTS_VPOS,
    out float4 result : COLOR0
) {
    result = LightmappedPixelShaderCore(multiplyColor, addColor, texCoord1, texRgn1, texCoord2, texRgn2);

    result.rgb = LinearToSRGB(result.rgb);

    result.rgb = ApplyDither(result.rgb, GET_VPOS);

    const float discardThreshold = (1.0 / 255.0);
    clip(result.a - discardThreshold);
}

void LightmappedPixelShaderWithLUT(
    in float4 multiplyColor : COLOR0,
    in float4 addColor : COLOR1,
    in float2 texCoord1 : TEXCOORD0,
    in float4 texRgn1 : TEXCOORD1,
    in float2 texCoord2 : TEXCOORD2,
    in float4 texRgn2 : TEXCOORD3,
    ACCEPTS_VPOS,
    out float4 result : COLOR0
) {
    result = LightmappedPixelShaderCore(multiplyColor, addColor, texCoord1, texRgn1, texCoord2, texRgn2);

    result.rgb = ApplyLUT(result.rgb, LUT2Weight);
    result.rgb = ApplyDither(result.rgb, GET_VPOS);

    const float discardThreshold = (1.0 / 255.0);
    clip(result.a - discardThreshold);
}

technique ScreenSpaceLightmappedBitmap
{
    pass P0
    {
        vertexShader = compile vs_3_0 ScreenSpaceVertexShader();
        pixelShader = compile ps_3_0 LightmappedPixelShader();
    }
}

technique WorldSpaceLightmappedBitmap
{
    pass P0
    {
        vertexShader = compile vs_3_0 WorldSpaceVertexShader();
        pixelShader = compile ps_3_0 LightmappedPixelShader();
    }
}

technique ScreenSpaceLightmappedsRGBBitmap
{
    pass P0
    {
        vertexShader = compile vs_3_0 ScreenSpaceVertexShader();
        pixelShader = compile ps_3_0 sRGBLightmappedPixelShader();
    }
}

technique WorldSpaceLightmappedsRGBBitmap
{
    pass P0
    {
        vertexShader = compile vs_3_0 WorldSpaceVertexShader();
        pixelShader = compile ps_3_0 sRGBLightmappedPixelShader();
    }
}

technique ScreenSpaceLightmappedBitmapWithLUT
{
    pass P0
    {
        vertexShader = compile vs_3_0 ScreenSpaceVertexShader();
        pixelShader = compile ps_3_0 LightmappedPixelShaderWithLUT();
    }
}

technique WorldSpaceLightmappedBitmapWithLUT
{
    pass P0
    {
        vertexShader = compile vs_3_0 WorldSpaceVertexShader();
        pixelShader = compile ps_3_0 LightmappedPixelShaderWithLUT();
    }
}
