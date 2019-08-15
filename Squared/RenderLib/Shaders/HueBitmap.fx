#include "CompilerWorkarounds.fxh"
#include "ViewTransformCommon.fxh"
#include "BitmapCommon.fxh"
#include "TargetInfo.fxh"
#include "DitherCommon.fxh"
#include "HueCommon.fxh"

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
    in float4 hslShift : COLOR2,
    in float2 texCoord : TEXCOORD0,
    in float4 texRgn : TEXCOORD1,
    out float4 result : COLOR0
) {
    float4 texColor = tex2D(TextureSampler, clamp2(texCoord, texRgn.xy, texRgn.zw));

    float4 hsla = hslaFromPRGBA(texColor);
    hsla += hslShift;
    float4 prgba = pRGBAFromHSLA(hsla);

    addColor.rgb *= addColor.a;
    addColor.a = 0;

    result = multiplyColor * prgba;
    result += (addColor * result.a);
}

void BasicPixelShaderWithDiscard (
    in float4 multiplyColor : COLOR0, 
    in float4 addColor : COLOR1, 
    in float4 hslShift : COLOR2,
    in float2 texCoord : TEXCOORD0,
    in float4 texRgn : TEXCOORD1,
    out float4 result : COLOR0
) {
    float4 texColor = tex2D(TextureSampler, clamp2(texCoord, texRgn.xy, texRgn.zw));

    float4 hsla = hslaFromPRGBA(texColor);
    hsla += hslShift;
    float4 prgba = pRGBAFromHSLA(hsla);

    addColor.rgb *= addColor.a;
    addColor.a = 0;

    result = multiplyColor * prgba;
    result += (addColor * result.a);

    const float discardThreshold = (1.0 / 255.0);
    clip(result.a - discardThreshold);
}

technique WorldSpaceHueBitmapTechnique
{
    pass P0
    {
        vertexShader = compile vs_3_0 WorldSpaceVertexShader();
        pixelShader = compile ps_3_0 BasicPixelShader();
    }
}

technique ScreenSpaceHueBitmapTechnique
{
    pass P0
    {
        vertexShader = compile vs_3_0 ScreenSpaceVertexShader();
        pixelShader = compile ps_3_0 BasicPixelShader();
    }
}

technique WorldSpaceHueBitmapWithDiscardTechnique
{
    pass P0
    {
        vertexShader = compile vs_3_0 WorldSpaceVertexShader();
        pixelShader = compile ps_3_0 BasicPixelShaderWithDiscard();
    }
}

technique ScreenSpaceHueBitmapWithDiscardTechnique
{
    pass P0
    {
        vertexShader = compile vs_3_0 ScreenSpaceVertexShader();
        pixelShader = compile ps_3_0 BasicPixelShaderWithDiscard();
    }
}