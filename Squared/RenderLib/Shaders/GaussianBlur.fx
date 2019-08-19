#include "CompilerWorkarounds.fxh"
#include "ViewTransformCommon.fxh"
#include "BitmapCommon.fxh"
#include "TargetInfo.fxh"
#include "DitherCommon.fxh"

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

float4 GaussianBlurPixelShaderCore(
    in float2 halfTexel,
    in float4 multiplyColor : COLOR0, 
    in float4 addColor : COLOR1, 
    in float2 texCoord : TEXCOORD0,
    in float4 texRgn : TEXCOORD1
) {
    // http://dev.theomader.com/gaussian-kernel-calculator/
    // Sigma 2, Kernel size 9

    // FIXME: This creates a weird halo effect, which seems wrong
    float2 stepSize = halfTexel * 2; 

    const int taps = 5;
    const float TapWeight[taps] = { 
        0.20236, 0.179044, 0.124009, 0.067234, 0.028532
    };

    float4 sum = tap(texCoord, texRgn) * TapWeight[0];

    for (int i = 1; i < taps; i += 1) {
        float2 offset2 = stepSize * i;

        sum += tap(texCoord - offset2, texRgn) * TapWeight[i];
        sum += tap(texCoord + offset2, texRgn) * TapWeight[i];
    }

    addColor.rgb *= addColor.a;
    addColor.a = 0;

    float4 result = multiplyColor * sum;
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
    result = GaussianBlurPixelShaderCore(
        float2(HalfTexel.x, 0),
        multiplyColor,
        addColor,
        texCoord,
        texRgn
    );
}

void VerticalGaussianBlurPixelShader(
    in float4 multiplyColor : COLOR0, 
    in float4 addColor : COLOR1, 
    in float2 texCoord : TEXCOORD0,
    in float4 texRgn : TEXCOORD1,
    out float4 result : COLOR0
) {
    result = GaussianBlurPixelShaderCore(
        float2(0, HalfTexel.y),
        multiplyColor,
        addColor,
        texCoord,
        texRgn
    );
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