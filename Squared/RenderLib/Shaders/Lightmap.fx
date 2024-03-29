#include "CompilerWorkarounds.fxh"
#include "ViewTransformCommon.fxh"
#include "FormatCommon.fxh"
#include "BitmapCommon.fxh"
#include "TargetInfo.fxh"
#include "DitherCommon.fxh"
#include "sRGBCommon.fxh"

#define LUT_BITMAP
#include "LUTCommon.fxh"

uniform const float2 LightmapUVOffset;

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

    float4 texColor = tex2D(TextureSampler, texCoord1);
    texColor = ExtractRgba(texColor, BitmapTraits);
    float4 result = multiplyColor * texColor;
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

    result = pLinearToPSRGB(result);

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

technique LightmappedBitmap
{
    pass P0
    {
        vertexShader = compile vs_3_0 GenericVertexShader();
        pixelShader = compile ps_3_0 LightmappedPixelShader();
    }
}

technique LightmappedsRGBBitmap
{
    pass P0
    {
        vertexShader = compile vs_3_0 GenericVertexShader();
        pixelShader = compile ps_3_0 sRGBLightmappedPixelShader();
    }
}

technique LightmappedBitmapWithLUT
{
    pass P0
    {
        vertexShader = compile vs_3_0 GenericVertexShader();
        pixelShader = compile ps_3_0 LightmappedPixelShaderWithLUT();
    }
}