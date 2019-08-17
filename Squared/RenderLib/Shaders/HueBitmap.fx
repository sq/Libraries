#include "CompilerWorkarounds.fxh"
#include "ViewTransformCommon.fxh"
#include "BitmapCommon.fxh"
#include "TargetInfo.fxh"
#include "DitherCommon.fxh"
#include "HueCommon.fxh"

const float3 RgbToGray = float3(0.299, 0.587, 0.144);

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
    hsla.rgb += hslShift.rgb;
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
    hsla.rgb += hslShift.rgb;
    float4 prgba = pRGBAFromHSLA(hsla);

    addColor.rgb *= addColor.a;
    addColor.a = 0;

    result = multiplyColor * prgba;
    result += (addColor * result.a);

    const float discardThreshold = (1.0 / 255.0);
    clip(result.a - discardThreshold);
}

void SepiaPixelShader(
    in float4 multiplyColor : COLOR0, 
    in float4 addColor : COLOR1, 
    in float4 colorHSVAndBlend : COLOR2,
    in float2 texCoord : TEXCOORD0,
    in float4 texRgn : TEXCOORD1,
    out float4 result : COLOR0
) {
    float4 texColor = tex2D(TextureSampler, clamp2(texCoord, texRgn.xy, texRgn.zw));
    float3 texGray = texColor.rgb * RgbToGray;
    float  gray = (texGray.r + texGray.g + texGray.b) * (1 + saturate(colorHSVAndBlend.a - 1));

    float3 sepiaColor = pRGBAFromHSLA(float4(colorHSVAndBlend.rgb, 1)).rgb * gray;
    // FIXME: Is converting the color to grayscale gonna mess up premultiplication? Does it matter?
    float4 sepia = float4(sepiaColor, 1) * texColor.a;

    float4 prgba = lerp(texColor, sepia, saturate(colorHSVAndBlend.a));

    addColor.rgb *= addColor.a;
    addColor.a = 0;

    result = multiplyColor * prgba;
    result += (addColor * result.a);
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

technique WorldSpaceSepiaBitmapTechnique
{
    pass P0
    {
        vertexShader = compile vs_3_0 WorldSpaceVertexShader();
        pixelShader = compile ps_3_0 SepiaPixelShader();
    }
}

technique ScreenSpaceSepiaBitmapTechnique
{
    pass P0
    {
        vertexShader = compile vs_3_0 ScreenSpaceVertexShader();
        pixelShader = compile ps_3_0 SepiaPixelShader();
    }
}
