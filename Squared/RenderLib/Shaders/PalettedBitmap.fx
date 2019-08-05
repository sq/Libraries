#include "CompilerWorkarounds.fxh"
#include "ViewTransformCommon.fxh"
#include "BitmapCommon.fxh"
#include "TargetInfo.fxh"
#include "DitherCommon.fxh"

sampler TextureSamplerPoint : register(s5) {
    Texture = (BitmapTexture);
    MipLODBias = MIP_BIAS;
    MipFilter = POINT;
    MinFilter = POINT;
    MagFilter = POINT;
};

uniform float2 PaletteSize;
Texture2D Palette : register(t4);

sampler PaletteSampler : register(s4) {
    Texture = (Palette);
    AddressU = CLAMP;
    AddressV = CLAMP;
    MipFilter = POINT;
    MinFilter = LINEAR;
    MagFilter = LINEAR;
};

float4 readPalette (float index, float4 paletteSelector4) {
    float2 paletteSize = PaletteSize > 0 ? PaletteSize : float2(256, 1);
    float paletteSelector = paletteSelector4.r;
    float2 selector = float2(floor(index * paletteSize.x) / paletteSize.x, paletteSelector - (0.5f / paletteSize.y));
    return tex2Dlod(PaletteSampler, float4(selector, 0, 0));
}

void BasicPixelShader(
    in float4 multiplyColor : COLOR0, 
    in float4 addColor : COLOR1, 
    in float4 paletteSelector : COLOR2,
    in float2 texCoord : TEXCOORD0,
    in float4 texRgn : TEXCOORD1,
    out float4 result : COLOR0
) {
    float paletteIndexF = tex2D(TextureSamplerPoint, clamp2(texCoord, texRgn.xy, texRgn.zw)).r;
    float4 paletteColor = readPalette(paletteIndexF, paletteSelector);

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
    float paletteIndexF = tex2D(TextureSamplerPoint, clamp2(texCoord, texRgn.xy, texRgn.zw)).r;
    float4 paletteColor = readPalette(paletteIndexF, paletteSelector);

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