#include "ViewTransformCommon.fxh"
#include "BitmapCommon.fxh"
#include "TargetInfo.fxh"
#include "DitherCommon.fxh"

uniform float TapOffset[3] = { 0.0, 1.3846153846, 3.2307692308 };
uniform float TapWeight[3] = { 0.2270270270, 0.3162162162, 0.0702702703 };
uniform float MipOffset = 3;

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
    return tex2Dbias(TapSampler, float4(clamp(texCoord, texRgn.xy, texRgn.zw), 0, MipOffset));
}

float4 GaussianBlur5TapPixelShaderCore(
    in float2 step,
    in float4 multiplyColor : COLOR0, 
    in float4 addColor : COLOR1, 
    in float2 texCoord : TEXCOORD0,
    in float4 texRgn : TEXCOORD1
) {
    float4 sum = tap(texCoord, texRgn) * TapWeight[0];

    for (int i = 1; i < 3; i += 1) {
        float2 offset2 = step * TapOffset[i];

        sum += tap(texCoord - offset2, texRgn) * TapWeight[i];
        sum += tap(texCoord + offset2, texRgn) * TapWeight[i];
    }

    addColor.rgb *= addColor.a;
    addColor.a = 0;

    float4 result = multiplyColor * sum;
    result += (addColor * result.a);
    return result;
}

void HorizontalGaussianBlur5TapPixelShader(
    in float4 multiplyColor : COLOR0, 
    in float4 addColor : COLOR1, 
    in float2 texCoord : TEXCOORD0,
    in float4 texRgn : TEXCOORD1,
    out float4 result : COLOR0
) {
    result = GaussianBlur5TapPixelShaderCore(
        float2(HalfTexel.x * 2, 0),
        multiplyColor,
        addColor,
        texCoord,
        texRgn
    );
}

void VerticalGaussianBlur5TapPixelShader(
    in float4 multiplyColor : COLOR0, 
    in float4 addColor : COLOR1, 
    in float2 texCoord : TEXCOORD0,
    in float4 texRgn : TEXCOORD1,
    out float4 result : COLOR0
) {
    result = GaussianBlur5TapPixelShaderCore(
        float2(0, HalfTexel.y * 2),
        multiplyColor,
        addColor,
        texCoord,
        texRgn
    );
}

technique WorldSpaceHorizontalGaussianBlur5Tap
{
    pass P0
    {
        vertexShader = compile vs_3_0 WorldSpaceVertexShader();
        pixelShader = compile ps_3_0 HorizontalGaussianBlur5TapPixelShader();
    }
}

technique ScreenSpaceHorizontalGaussianBlur5Tap
{
    pass P0
    {
        vertexShader = compile vs_3_0 ScreenSpaceVertexShader();
        pixelShader = compile ps_3_0 HorizontalGaussianBlur5TapPixelShader();
    }
}

technique WorldSpaceVerticalGaussianBlur5Tap
{
    pass P0
    {
        vertexShader = compile vs_3_0 WorldSpaceVertexShader();
        pixelShader = compile ps_3_0 VerticalGaussianBlur5TapPixelShader();
    }
}

technique ScreenSpaceVerticalGaussianBlur5Tap
{
    pass P0
    {
        vertexShader = compile vs_3_0 ScreenSpaceVertexShader();
        pixelShader = compile ps_3_0 VerticalGaussianBlur5TapPixelShader();
    }
}