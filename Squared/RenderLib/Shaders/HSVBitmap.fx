#include "CompilerWorkarounds.fxh"
#include "ViewTransformCommon.fxh"
#include "BitmapCommon.fxh"
#include "TargetInfo.fxh"
#include "DitherCommon.fxh"
#include "HSVCommon.fxh"

sampler TextureSamplerPoint : register(s5) {
    Texture = (BitmapTexture);
    MipLODBias = MIP_BIAS;
    MipFilter = POINT;
    MinFilter = POINT;
    MagFilter = POINT;
};

void BasicPixelShader(
    in float4 multiplyColor : COLOR0, 
    in float4 addColor : COLOR1, 
    in float4 hsvShift : COLOR2,
    in float2 texCoord : TEXCOORD0,
    in float4 texRgn : TEXCOORD1,
    out float4 result : COLOR0
) {
    addColor.rgb *= addColor.a;
    addColor.a = 0;

    result = multiplyColor * tex2D(TextureSampler, clamp2(texCoord, texRgn.xy, texRgn.zw));
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
    addColor.rgb *= addColor.a;
    addColor.a = 0;

    result = multiplyColor * tex2D(TextureSampler, clamp2(texCoord, texRgn.xy, texRgn.zw));
    result += (addColor * result.a);

    const float discardThreshold = (1.0 / 255.0);
    clip(result.a - discardThreshold);
}

technique WorldSpaceHSVBitmapTechnique
{
    pass P0
    {
        vertexShader = compile vs_3_0 WorldSpaceVertexShader();
        pixelShader = compile ps_3_0 BasicPixelShader();
    }
}

technique ScreenSpaceHSVBitmapTechnique
{
    pass P0
    {
        vertexShader = compile vs_3_0 ScreenSpaceVertexShader();
        pixelShader = compile ps_3_0 BasicPixelShader();
    }
}

technique WorldSpaceHSVBitmapWithDiscardTechnique
{
    pass P0
    {
        vertexShader = compile vs_3_0 WorldSpaceVertexShader();
        pixelShader = compile ps_3_0 BasicPixelShaderWithDiscard();
    }
}

technique ScreenSpaceHSVBitmapWithDiscardTechnique
{
    pass P0
    {
        vertexShader = compile vs_3_0 ScreenSpaceVertexShader();
        pixelShader = compile ps_3_0 BasicPixelShaderWithDiscard();
    }
}