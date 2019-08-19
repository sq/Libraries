#include "CompilerWorkarounds.fxh"
#include "ViewTransformCommon.fxh"
#include "BitmapCommon.fxh"
#include "TargetInfo.fxh"
#include "DitherCommon.fxh"

// http://dev.theomader.com/gaussian-kernel-calculator/
// Sigma 2, Kernel size 9
uniform int TapCount = 5;
uniform float TapWeights[7] = { 0.20236, 0.179044, 0.124009, 0.067234, 0.028532, 0, 0 };

uniform float MipOffset = 0;

sampler TapSampler : register(s0) {
    Texture = (BitmapTexture);
    MipFilter = LINEAR;
    MinFilter = LINEAR;
    MagFilter = LINEAR;
};

float4 tap(
    in float2 texCoord,
    in float4 texRgn
) {
    return tex2Dbias(TapSampler, float4(clamp2(texCoord, texRgn.xy, texRgn.zw), 0, MipOffset));
}

float4 gaussianBlur1D(
    in float4 centerTap,
    in float2 stepSize,
    in float2 texCoord,
    in float4 texRgn
) {
    float4 sum = centerTap * TapWeights[0];

    for (int i = 1; i < TapCount; i += 1) {
        float2 offset2 = stepSize * i;

        sum += tap(texCoord - offset2, texRgn) * TapWeights[i];
        sum += tap(texCoord + offset2, texRgn) * TapWeights[i];
    }

    return sum;
}

float4 psEpilogue (
    in float4 texColor,
    in float4 multiplyColor,
    in float4 addColor
) {
    addColor.rgb *= addColor.a;
    addColor.a = 0;

    float4 result = multiplyColor * texColor;
    result += (addColor * result.a);
    return result;
}

void HorizontalGaussianBlurPixelShader(
    in float4 multiplyColor : COLOR0, 
    in float4 addColor : COLOR1, 
    in float2 texCoord : TEXCOORD0,
    in float4 texRgn : TEXCOORD1,
    out float4 result : COLOR0
) {
    float4 centerTap = tap(texCoord, texRgn);
    float4 texColor = gaussianBlur1D(centerTap, HalfTexel * float2(2, 0), texCoord, texRgn);
    result = psEpilogue(texColor, multiplyColor, addColor);
}

void VerticalGaussianBlurPixelShader(
    in float4 multiplyColor : COLOR0, 
    in float4 addColor : COLOR1, 
    in float2 texCoord : TEXCOORD0,
    in float4 texRgn : TEXCOORD1,
    out float4 result : COLOR0
) {
    float4 centerTap = tap(texCoord, texRgn);
    float4 texColor = gaussianBlur1D(centerTap, HalfTexel * float2(0, 2), texCoord, texRgn);
    result = psEpilogue(texColor, multiplyColor, addColor);
}

void RadialGaussianBlurPixelShader(
    in float4 multiplyColor : COLOR0,
    in float4 addColor : COLOR1,
    in float2 texCoord : TEXCOORD0,
    in float4 texRgn : TEXCOORD1,
    out float4 result : COLOR0
) {
    float4 centerTap = tap(texCoord, texRgn);
    float4 texColor = gaussianBlur1D(centerTap, HalfTexel * float2(2, 2), texCoord, texRgn);
    result = psEpilogue(texColor, multiplyColor, addColor);
}

technique WorldSpaceHorizontalGaussianBlur
{
    pass P0
    {
        vertexShader = compile vs_3_0 WorldSpaceVertexShader();
        pixelShader = compile ps_3_0 HorizontalGaussianBlurPixelShader();
    }
}

technique ScreenSpaceHorizontalGaussianBlur
{
    pass P0
    {
        vertexShader = compile vs_3_0 ScreenSpaceVertexShader();
        pixelShader = compile ps_3_0 HorizontalGaussianBlurPixelShader();
    }
}

technique WorldSpaceVerticalGaussianBlur
{
    pass P0
    {
        vertexShader = compile vs_3_0 WorldSpaceVertexShader();
        pixelShader = compile ps_3_0 VerticalGaussianBlurPixelShader();
    }
}

technique ScreenSpaceVerticalGaussianBlur
{
    pass P0
    {
        vertexShader = compile vs_3_0 ScreenSpaceVertexShader();
        pixelShader = compile ps_3_0 VerticalGaussianBlurPixelShader();
    }
}

technique WorldSpaceRadialGaussianBlur
{
    pass P0
    {
        vertexShader = compile vs_3_0 WorldSpaceVertexShader();
        pixelShader = compile ps_3_0 RadialGaussianBlurPixelShader();
    }
}

technique ScreenSpaceRadialGaussianBlur
{
    pass P0
    {
        vertexShader = compile vs_3_0 ScreenSpaceVertexShader();
        pixelShader = compile ps_3_0 RadialGaussianBlurPixelShader();
    }
}