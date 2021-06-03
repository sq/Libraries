#include "CompilerWorkarounds.fxh"
#include "ViewTransformCommon.fxh"
#include "BitmapCommon.fxh"
#include "TargetInfo.fxh"
#include "DitherCommon.fxh"
#include "sRGBCommon.fxh"

#define LUT_BITMAP
#include "LUTCommon.fxh"

// HACK: The default mip bias for things like text atlases is unnecessarily blurry, especially if
//  the atlas is high-DPI
#define DefaultShadowedTopMipBias MIP_BIAS

// FIXME: Make these adjustable uniforms
#define OutlineSumDivisor 1.45 // HACK: Make the outline thicker than a normal drop shadow
#define OutlineExponent 1.5 // HACK: Make the outlines sharper even if the texture edge is soft

uniform const float4 GlobalShadowColor;
uniform const float2 ShadowOffset;
uniform const float  ShadowedTopMipBias, ShadowMipBias;

void BasicPixelShader(
    in float4 multiplyColor : COLOR0, 
    in float4 addColor : COLOR1, 
    in float2 texCoord : TEXCOORD0,
    in float4 texRgn : TEXCOORD1,
    out float4 result : COLOR0
) {
    addColor.rgb *= addColor.a;
    addColor.a = 0;

    result = multiplyColor * tex2Dbias(TextureSampler, float4(clamp2(texCoord, texRgn.xy, texRgn.zw), 0, MIP_BIAS));
    result += (addColor * result.a);
}

void BasicPixelShaderWithLUT(
    in float4 multiplyColor : COLOR0,
    in float4 addColor : COLOR1,
    in float2 texCoord : TEXCOORD0,
    in float4 texRgn : TEXCOORD1,
    ACCEPTS_VPOS,
    out float4 result : COLOR0
) {
    addColor.rgb *= addColor.a;
    addColor.a = 0;

    float4 texColor = tex2Dbias(TextureSampler, float4(clamp2(texCoord, texRgn.xy, texRgn.zw), 0, MIP_BIAS));
    texColor.rgb = ApplyLUT(texColor.rgb, LUT2Weight);
    texColor.rgb = ApplyDither(texColor.rgb, GET_VPOS);

    result = multiplyColor * texColor;
    result += (addColor * result.a);

    const float discardThreshold = (1.0 / 255.0);
    clip(result.a - discardThreshold);
}

void ToSRGBPixelShader(
    in float4 multiplyColor : COLOR0,
    in float4 addColor : COLOR1,
    in float2 texCoord : TEXCOORD0,
    in float4 texRgn : TEXCOORD1,
    ACCEPTS_VPOS,
    out float4 result : COLOR0
) {
    addColor.rgb *= addColor.a;
    addColor.a = 0;

    result = multiplyColor * tex2Dbias(TextureSampler, float4(clamp2(texCoord, texRgn.xy, texRgn.zw), 0, MIP_BIAS));
    result += (addColor * result.a);
    result = pLinearToPSRGB(result);
    result.rgb = ApplyDither(result.rgb, GET_VPOS);
}

void ShadowedPixelShader (
    in float4 multiplyColor : COLOR0,
    in float4 addColor : COLOR1,
    in float4 shadowColorIn : COLOR2,
    in float2 texCoord : TEXCOORD0,
    in float4 texRgn : TEXCOORD1,
    out float4 result : COLOR0
) {
    addColor.rgb *= addColor.a;
    addColor.a = 0;

    float2 shadowTexCoord = clamp2(texCoord - (ShadowOffset * HalfTexel * 2), texRgn.xy, texRgn.zw);
    float4 texColor = tex2Dbias(TextureSampler, float4(clamp2(texCoord, texRgn.xy, texRgn.zw), 0, ShadowedTopMipBias + DefaultShadowedTopMipBias));
    float4 shadowColor = lerp(GlobalShadowColor, shadowColorIn, shadowColorIn.a > 0 ? 1 : 0) * tex2Dbias(TextureSampler, float4(shadowTexCoord, 0, ShadowMipBias));
    if (shadowColor.a > 1)
        shadowColor = normalize(shadowColor);
    float4 texColorSRGB = pSRGBToPLinear((texColor * multiplyColor) + (addColor * texColor.a)),
        shadowColorSRGB = pSRGBToPLinear(shadowColor);
    result = texColorSRGB + (shadowColorSRGB * (1 - texColorSRGB.a));
    result = pLinearToPSRGB(result);
}

void OutlinedPixelShader(
    in float4 multiplyColor : COLOR0,
    in float4 addColor : COLOR1,
    in float4 shadowColorIn : COLOR2,
    in float2 texCoord : TEXCOORD0,
    in float4 texRgn : TEXCOORD1,
    out float4 result : COLOR0
) {
    addColor.rgb *= addColor.a;
    addColor.a = 0;

    float4 texColor = tex2Dbias(TextureSampler, float4(clamp2(texCoord, texRgn.xy, texRgn.zw), 0, ShadowedTopMipBias + DefaultShadowedTopMipBias));

    float shadowAlpha = texColor.a;
    float2 offset = (ShadowOffset * HalfTexel * 2);
    [flatten]
    for (int i = 0; i < 4; i++) {
        float x = (i % 2) == 0 ? 1 : -1,
            y = (i / 2) == 0 ? 1 : -1;
        float2 shadowTexCoord = clamp2(texCoord + float2(offset.x * x, offset.y * y), texRgn.xy, texRgn.zw);
        shadowAlpha += tex2Dbias(TextureSampler, float4(shadowTexCoord, 0, ShadowMipBias)).a;
    }

    shadowAlpha = saturate(shadowAlpha / OutlineSumDivisor);
    shadowAlpha = pow(shadowAlpha, OutlineExponent);
    float4 shadowColor = float4(shadowColorIn.rgb, 1);
    shadowColor = lerp(GlobalShadowColor, shadowColor, shadowColorIn.a > 0 ? 1 : 0);
    shadowColor *= shadowAlpha;

    float4 overColor = (texColor * multiplyColor);
    overColor += (addColor * overColor.a);

    // Significantly improves the appearance of colored outlines and/or colored text
    float4 overSRGB = pSRGBToPLinear(overColor),
        shadowSRGB = pSRGBToPLinear(shadowColor);
    result = lerp(shadowSRGB, overSRGB, overColor.a);
    result = pLinearToPSRGB(result);
}

void BasicPixelShaderWithDiscard (
    in float4 multiplyColor : COLOR0, 
    in float4 addColor : COLOR1, 
    in float2 texCoord : TEXCOORD0,
    in float4 texRgn : TEXCOORD1,
    out float4 result : COLOR0
) {
    addColor.rgb *= addColor.a;
    addColor.a = 0;

    result = multiplyColor * tex2Dbias(TextureSampler, float4(clamp2(texCoord, texRgn.xy, texRgn.zw), 0, MIP_BIAS));
    result += (addColor * result.a);

    const float discardThreshold = (1.0 / 255.0);
    clip(result.a - discardThreshold);
}

void ShadowedPixelShaderWithDiscard (
    in float4 multiplyColor : COLOR0,
    in float4 addColor : COLOR1,
    in float4 shadowColorIn : COLOR2,
    in float2 texCoord : TEXCOORD0,
    in float4 texRgn : TEXCOORD1,
    out float4 result : COLOR0
) {
    ShadowedPixelShader(
        multiplyColor, addColor,
        shadowColorIn, texCoord, texRgn,
        result
    );

    const float discardThreshold = (1.0 / 255.0);
    clip(result.a - discardThreshold);
}

void OutlinedPixelShaderWithDiscard(
    in float4 multiplyColor : COLOR0,
    in float4 addColor : COLOR1,
    in float4 outlineColorIn : COLOR2,
    in float2 texCoord : TEXCOORD0,
    in float4 texRgn : TEXCOORD1,
    out float4 result : COLOR0
) {
    OutlinedPixelShader(
        multiplyColor, addColor,
        outlineColorIn, texCoord, texRgn,
        result
    );

    const float discardThreshold = (1.0 / 255.0);
    clip(result.a - discardThreshold);
}

void HighlightColorPixelShaderWithDiscard(
    in float4 multiplyColor : COLOR0,
    in float4 addColor : COLOR1,
    in float4 targetColor : COLOR2,
    in float2 texCoord : TEXCOORD0,
    in float4 texRgn : TEXCOORD1,
    out float4 result : COLOR0
) {
    addColor.rgb *= addColor.a;
    addColor.a = 0;

    float4 texColor = tex2Dbias(TextureSampler, float4(clamp2(texCoord, texRgn.xy, texRgn.zw), 0, MIP_BIAS));
    float3 distance = targetColor.rgb - texColor.rgb;
    float distanceF = length(distance);
    float epsilon = 1 / 256, threshold = epsilon + targetColor.a;
    if (distanceF > threshold) {
        result = 0;
        discard;
    } else {
        result = multiplyColor * (1 - saturate(distanceF / threshold));
        result += (addColor * result.a);
        result *= texColor.a;
    }
}

void CrossfadePixelShaderWithDiscard(
    in float4 multiplyColor : COLOR0,
    in float4 addColor : COLOR1,
    in float4 blendWeight : COLOR2,
    in float2 texCoord : TEXCOORD0,
    in float4 texRgn : TEXCOORD1,
    in float2 texCoord2 : TEXCOORD2,
    in float4 texRgn2 : TEXCOORD3,
    out float4 result : COLOR0
) {
    addColor.rgb *= addColor.a;
    addColor.a = 0;

    float4 sample1 = tex2Dbias(TextureSampler, float4(clamp2(texCoord, texRgn.xy, texRgn.zw), 0, MIP_BIAS));
    float4 sample2 = tex2Dbias(TextureSampler2, float4(clamp2(texCoord2, texRgn2.xy, texRgn2.zw), 0, MIP_BIAS));
    float4 lerped = lerp(sample1, sample2, blendWeight);

    result = multiplyColor * lerped;
    result += (addColor * result.a);

    const float discardThreshold = (1.0 / 255.0);
    clip(result.a - discardThreshold);
}

void OverPixelShaderWithDiscard(
    in float4 multiplyColor : COLOR0,
    in float4 addColor : COLOR1,
    in float4 blendWeight : COLOR2,
    in float2 texCoord : TEXCOORD0,
    in float4 texRgn : TEXCOORD1,
    in float2 texCoord2 : TEXCOORD2,
    in float4 texRgn2 : TEXCOORD3,
    out float4 result : COLOR0
) {
    addColor.rgb *= addColor.a;
    addColor.a = 0;

    float4 sample1 = tex2Dbias(TextureSampler, float4(clamp2(texCoord, texRgn.xy, texRgn.zw), 0, MIP_BIAS));
    float4 sample2 = tex2Dbias(TextureSampler2, float4(clamp2(texCoord2, texRgn2.xy, texRgn2.zw), 0, MIP_BIAS)) * blendWeight;
    float4 composited = sample1 + (sample2 * (1 - sample1.a));

    result = multiplyColor * composited;
    result += (addColor * result.a);

    const float discardThreshold = (1.0 / 255.0);
    clip(result.a - discardThreshold);
}

void UnderPixelShaderWithDiscard(
    in float4 multiplyColor : COLOR0,
    in float4 addColor : COLOR1,
    in float4 blendWeight : COLOR2,
    in float2 texCoord : TEXCOORD0,
    in float4 texRgn : TEXCOORD1,
    in float2 texCoord2 : TEXCOORD2,
    in float4 texRgn2 : TEXCOORD3,
    out float4 result : COLOR0
) {
    addColor.rgb *= addColor.a;
    addColor.a = 0;

    float4 sample1 = tex2Dbias(TextureSampler, float4(clamp2(texCoord, texRgn.xy, texRgn.zw), 0, MIP_BIAS));
    float4 sample2 = tex2Dbias(TextureSampler2, float4(clamp2(texCoord2, texRgn2.xy, texRgn2.zw), 0, MIP_BIAS)) * blendWeight;
    float4 composited = sample2 + (sample1 * (1 - sample2.a));

    result = multiplyColor * composited;
    result += (addColor * result.a);

    const float discardThreshold = (1.0 / 255.0);
    clip(result.a - discardThreshold);
}

technique BitmapTechnique
{
    pass P0
    {
        vertexShader = compile vs_3_0 GenericVertexShader();
        pixelShader = compile ps_3_0 BasicPixelShader();
    }
}

technique WorldSpaceBitmapToSRGBTechnique
{
    pass P0
    {
        vertexShader = compile vs_3_0 WorldSpaceVertexShader();
        pixelShader = compile ps_3_0 ToSRGBPixelShader();
    }
}

technique ScreenSpaceBitmapToSRGBTechnique
{
    pass P0
    {
        vertexShader = compile vs_3_0 ScreenSpaceVertexShader();
        pixelShader = compile ps_3_0 ToSRGBPixelShader();
    }
}

technique WorldSpaceShadowedBitmapTechnique
{
    pass P0
    {
        vertexShader = compile vs_3_0 WorldSpaceVertexShader();
        pixelShader = compile ps_3_0 ShadowedPixelShader();
    }
}

technique ScreenSpaceShadowedBitmapTechnique
{
    pass P0
    {
        vertexShader = compile vs_3_0 ScreenSpaceVertexShader();
        pixelShader = compile ps_3_0 ShadowedPixelShader();
    }
}

technique WorldSpaceShadowedBitmapWithDiscardTechnique
{
    pass P0
    {
        vertexShader = compile vs_3_0 WorldSpaceVertexShader();
        pixelShader = compile ps_3_0 ShadowedPixelShaderWithDiscard();
    }
}

technique ScreenSpaceShadowedBitmapWithDiscardTechnique
{
    pass P0
    {
        vertexShader = compile vs_3_0 ScreenSpaceVertexShader();
        pixelShader = compile ps_3_0 ShadowedPixelShaderWithDiscard();
    }
}

technique BitmapWithDiscardTechnique
{
    pass P0
    {
        vertexShader = compile vs_3_0 GenericVertexShader();
        pixelShader = compile ps_3_0 BasicPixelShaderWithDiscard();
    }
}

technique WorldSpaceBitmapWithLUTTechnique
{
    pass P0
    {
        vertexShader = compile vs_3_0 WorldSpaceVertexShader();
        pixelShader = compile ps_3_0 BasicPixelShaderWithLUT();
    }
}

technique ScreenSpaceBitmapWithLUTTechnique
{
    pass P0
    {
        vertexShader = compile vs_3_0 ScreenSpaceVertexShader();
        pixelShader = compile ps_3_0 BasicPixelShaderWithLUT();
    }
}

technique HighlightColorBitmapTechnique
{
    pass P0
    {
        vertexShader = compile vs_3_0 GenericVertexShader();
        pixelShader = compile ps_3_0 HighlightColorPixelShaderWithDiscard();
    }
}

technique CrossfadeBitmapTechnique
{
    pass P0
    {
        vertexShader = compile vs_3_0 GenericVertexShader();
        pixelShader = compile ps_3_0 CrossfadePixelShaderWithDiscard();
    }
}

technique OverBitmapTechnique
{
    pass P0
    {
        vertexShader = compile vs_3_0 GenericVertexShader();
        pixelShader = compile ps_3_0 OverPixelShaderWithDiscard();
    }
}

technique UnderBitmapTechnique
{
    pass P0
    {
        vertexShader = compile vs_3_0 GenericVertexShader();
        pixelShader = compile ps_3_0 UnderPixelShaderWithDiscard();
    }
}

technique OutlinedBitmapTechnique
{
    pass P0
    {
        vertexShader = compile vs_3_0 GenericVertexShader();
        pixelShader = compile ps_3_0 OutlinedPixelShader();
    }
}

technique OutlinedBitmapWithDiscardTechnique
{
    pass P0
    {
        vertexShader = compile vs_3_0 GenericVertexShader();
        pixelShader = compile ps_3_0 OutlinedPixelShaderWithDiscard();
    }
}