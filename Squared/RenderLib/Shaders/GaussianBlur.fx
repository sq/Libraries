// HACK: We don't really care about running this in debug mode, since
//  blur operations are so tex (and to a lesser degree arith) intensive
//  that we want to optimize the hell out of them no matter what
#pragma fxcparams(/O3 /Zi)

#include "CompilerWorkarounds.fxh"
#include "ViewTransformCommon.fxh"
#include "BitmapCommon.fxh"
#include "TargetInfo.fxh"
#include "DitherCommon.fxh"

// http://dev.theomader.com/gaussian-kernel-calculator/
// Sigma 2, Kernel size 9
uniform int TapCount = 5;
uniform float TapWeights[10] = { 0.20236, 0.179044, 0.124009, 0.067234, 0.028532, 0, 0, 0, 0, 0 };
uniform float InverseTapDivisor = 1;

// HACK: Setting this any higher than 1 produces weird ringing artifacts.
// In practice we're basically super-sampling the matrix... it doesn't make it blurry with a low
//  sigma value at least?
const float TapSpacingFactor = 1;

sampler TapSampler : register(s0) {
    Texture = (BitmapTexture);
    MipFilter = POINT;
    MinFilter = LINEAR;
    MagFilter = LINEAR;
};

float4 tap(
    in float2 texCoord,
    in float4 texRgn
) {
    return tex2Dlod(TapSampler, float4(clamp2(texCoord, texRgn.xy, texRgn.zw), 0, 0));
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
    float4 sum = gaussianBlur1D(centerTap, HalfTexel * float2(TapSpacingFactor, 0), texCoord, texRgn);
    result = psEpilogue(sum * InverseTapDivisor, multiplyColor, addColor);
}

void VerticalGaussianBlurPixelShader(
    in float4 multiplyColor : COLOR0, 
    in float4 addColor : COLOR1, 
    in float2 texCoord : TEXCOORD0,
    in float4 texRgn : TEXCOORD1,
    out float4 result : COLOR0
) {
    float4 centerTap = tap(texCoord, texRgn);
    float4 sum = gaussianBlur1D(centerTap, HalfTexel * float2(0, TapSpacingFactor), texCoord, texRgn);
    result = psEpilogue(sum * InverseTapDivisor, multiplyColor, addColor);
}

void RadialGaussianBlurPixelShader(
    in float4 multiplyColor : COLOR0,
    in float4 addColor : COLOR1,
    in float2 texCoord : TEXCOORD0,
    in float4 texRgn : TEXCOORD1,
    out float4 result : COLOR0
) {
    float2 innerStepSize = HalfTexel * float2(TapSpacingFactor, 0), outerStepSize = HalfTexel * float2(0, TapSpacingFactor);

    float4 centerTap = tap(texCoord, texRgn);
    float4 centerValue = gaussianBlur1D(centerTap, innerStepSize, texCoord, texRgn);

    float4 sum = centerValue * TapWeights[0];

    for (int i = 1; i < TapCount; i += 1) {
        float2 outerOffset = outerStepSize * i;
        
        sum += gaussianBlur1D(centerTap, innerStepSize, texCoord - outerOffset, texRgn) * TapWeights[i];
        sum += gaussianBlur1D(centerTap, innerStepSize, texCoord + outerOffset, texRgn) * TapWeights[i];
    }

    result = psEpilogue(sum * InverseTapDivisor * InverseTapDivisor, multiplyColor, addColor);
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