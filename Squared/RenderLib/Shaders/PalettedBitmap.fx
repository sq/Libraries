#include "CompilerWorkarounds.fxh"
#include "ViewTransformCommon.fxh"
#include "BitmapCommon.fxh"
#include "TargetInfo.fxh"
#include "DitherCommon.fxh"

Texture2D Palette : register(t4);

sampler PaletteSampler : register(s4) {
    Texture = (Palette);
    AddressU = CLAMP;
    AddressV = CLAMP;
    MipFilter = POINT;
    MinFilter = POINT;
    MagFilter = POINT;
};

void BasicPixelShader(
    in float4 multiplyColor : COLOR0, 
    in float4 addColor : COLOR1, 
    in float4 paletteSelector : COLOR2,
    in float2 texCoord : TEXCOORD0,
    in float4 texRgn : TEXCOORD1,
    out float4 result : COLOR0
) {
    float paletteIndexF = tex2D(TextureSampler, clamp2(texCoord, texRgn.xy, texRgn.zw)).r;
    float4 paletteColor = tex2Dlod(PaletteSampler, float4(paletteIndexF, paletteSelector.r, 0, 0));

    addColor.rgb *= addColor.a;
    addColor.a = 0;

    result = multiplyColor * paletteColor;
    result += (addColor * result.a);
}

void BasicPixelShaderWithDiscard (
    in float4 multiplyColor : COLOR0, 
    in float4 addColor : COLOR1, 
    in float4 paletteSelector : COLOR2,
    in float2 texCoord : TEXCOORD0,
    in float4 texRgn : TEXCOORD1,
    out float4 result : COLOR0
) {
    float paletteIndexF = tex2D(TextureSampler, clamp2(texCoord, texRgn.xy, texRgn.zw)).r;
    float4 paletteColor = tex2Dlod(PaletteSampler, float4(paletteIndexF, paletteSelector.r, 0, 0));

    addColor.rgb *= addColor.a;
    addColor.a = 0;

    result = multiplyColor * paletteColor;
    result += (addColor * result.a);

    const float discardThreshold = (1.0 / 255.0);
    clip(result.a - discardThreshold);
}

technique WorldSpacePalettedBitmapTechnique
{
    pass P0
    {
        vertexShader = compile vs_3_0 WorldSpaceVertexShader();
        pixelShader = compile ps_3_0 BasicPixelShader();
    }
}

technique ScreenSpacePalettedBitmapTechnique
{
    pass P0
    {
        vertexShader = compile vs_3_0 ScreenSpaceVertexShader();
        pixelShader = compile ps_3_0 BasicPixelShader();
    }
}

technique WorldSpacePalettedBitmapWithDiscardTechnique
{
    pass P0
    {
        vertexShader = compile vs_3_0 WorldSpaceVertexShader();
        pixelShader = compile ps_3_0 BasicPixelShaderWithDiscard();
    }
}

technique ScreenSpacePalettedBitmapWithDiscardTechnique
{
    pass P0
    {
        vertexShader = compile vs_3_0 ScreenSpaceVertexShader();
        pixelShader = compile ps_3_0 BasicPixelShaderWithDiscard();
    }
}