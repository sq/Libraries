#include "CompilerWorkarounds.fxh"
#include "ViewTransformCommon.fxh"
#include "BitmapCommon.fxh"
#include "TargetInfo.fxh"
#include "sRGBCommon.fxh"

uniform const float2 BitmapTextureChannels;
uniform const float4 BitmapValueMask2;

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

float gradientMask (float maskValue, float progress, float rangeBottom, float rangeTop, float windowSize) {
    if (windowSize <= 0)
        // HACK
        windowSize = 0.1f;

    progress *= (1.0 + windowSize);
    float scale = (1.0 / windowSize);
    maskValue = (1.0 - maskValue) * scale;
    progress = max((progress - rangeBottom) / (rangeTop - rangeBottom), 0) * scale;
    return saturate(progress - maskValue);
}

void GradientMaskedPixelShaderWithDiscard(
    in float4 multiplyColor : COLOR0,
    in float4 addColor : COLOR1,
    // progress, min, max, windowSize
    in float4 userData : COLOR2,
    in float2 texCoord : TEXCOORD0,
    in float4 texRgn : TEXCOORD1,
    in float2 texCoord2 : TEXCOORD2,
    in float4 texRgn2 : TEXCOORD3,
    out float4 result : COLOR0
) {
    addColor.rgb *= addColor.a;
    addColor.a = 0;

    float4 texColor = tex2Dbias(TextureSampler, float4(clamp2(texCoord, texRgn.xy, texRgn.zw), 0, MIP_BIAS));
    // FIXME: Use a different value?
    float4 maskColor = tex2Dbias(TextureSampler2, float4(clamp2(texCoord2, texRgn2.xy, texRgn2.zw), 0, MIP_BIAS));
    float mask, alpha;
    ExtractLuminanceAndAlpha(maskColor, BitmapValueMask2, BitmapTextureChannels.y, mask, alpha);
    alpha *= gradientMask(mask.r, userData.x, userData.y, userData.z, userData.w);

    result = texColor * multiplyColor * alpha;
    result += (addColor * result.a);

    const float discardThreshold = (1.0 / 255.0);
    clip(result.a - discardThreshold);
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

technique GradientMaskedBitmapTechnique
{
    pass P0
    {
        vertexShader = compile vs_3_0 GenericVertexShader();
        pixelShader = compile ps_3_0 GradientMaskedPixelShaderWithDiscard();
    }
}
