#include "CompilerWorkarounds.fxh"
#include "ViewTransformCommon.fxh"
#include "FormatCommon.fxh"
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
uniform const float3 OutlineRadiusSoftnessAndPower;
uniform const float  ShadowedTopMipBias, ShadowMipBias;
uniform const bool   PremultiplyTexture;
uniform const bool   AutoPremultiplyBlockTextures, TransparentExterior;

float4 AdaptTraits(float4 traits) {
    // HACK: Assume block textures aren't premultiplied (they shouldn't be!!!!) and premultiply them
    // This ensures that the output is also premultiplied
    if (AutoPremultiplyBlockTextures && (traits.w >= ALPHA_MODE_BC))
        traits.z = 1;
    return traits;
}

void BasicPixelShader(
    in float4 multiplyColor : COLOR0, 
    in float4 addColor : COLOR1, 
    in float2 texCoord : TEXCOORD0,
    in float4 texRgn : TEXCOORD1,
    out float4 result : COLOR0
) {
    addColor.rgb *= addColor.a;
    addColor.a = 0;

    float4 texColor = tex2Dbias(TextureSampler, float4(clamp2(texCoord, texRgn.xy, texRgn.zw), 0, MIP_BIAS));
    texColor = ExtractRgba(texColor, BitmapTraits);
    result = multiplyColor * texColor;
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
    texColor = ExtractRgba(texColor, BitmapTraits);
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

    float4 texColor = tex2Dbias(TextureSampler, float4(clamp2(texCoord, texRgn.xy, texRgn.zw), 0, MIP_BIAS));
    texColor = ExtractRgba(texColor, BitmapTraits);
    result = multiplyColor * texColor;
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
    float4 traits = BitmapTraits;
    if ((shadowColorIn.a < 0) || PremultiplyTexture)
        traits.z = 1;
    texColor = ExtractRgba(texColor, traits);
    shadowColorIn.a = abs(shadowColorIn.a);

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
    float4 traits = AdaptTraits(BitmapTraits);
    if ((shadowColorIn.a < 0) || PremultiplyTexture)
        traits.z = 1;
    texColor = ExtractRgba(texColor, traits);
    shadowColorIn.a = abs(shadowColorIn.a);

    float shadowAlpha = texColor.a;
    float2 offset = (ShadowOffset * HalfTexel * 2);
    // [unroll]
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
    shadowColor *= shadowAlpha * saturate(shadowColorIn.a);

    float4 overColor = (texColor * multiplyColor);
    overColor += (addColor * overColor.a);

    // Significantly improves the appearance of colored outlines and/or colored text
    float4 overSRGB = pSRGBToPLinear(overColor),
        shadowSRGB = pSRGBToPLinear(shadowColor);
    result = lerp(shadowSRGB, overSRGB, overColor.a);
    result = pLinearToPSRGB(result);
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

// porter-duff A over B
float4 over(float4 top, float topOpacity, float4 bottom, float bottomOpacity) {
    top *= topOpacity;
    bottom *= bottomOpacity;

    return top + (bottom * (1 - top.a));
}

void DistanceFieldOutlinedPixelShader(
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
    float4 traits = AdaptTraits(BitmapTraits);
    if ((shadowColorIn.a < 0) || PremultiplyTexture)
        traits.z = 1;
    texColor = ExtractRgba(texColor, traits);
    shadowColorIn.a = abs(shadowColorIn.a);

    float2 offset = (ShadowOffset * HalfTexel * 2);
    // HACK: 5 tap averaging because the SDF may be slightly inaccurate, and not at full pixel offsets
    float3 step = float3(HalfTexel * 1, 0);
    float distance = tex2D(TextureSampler2, clamp2(texCoord + offset + step.xz, texRgn.xy, texRgn.zw)) + 
        tex2D(TextureSampler2, clamp2(texCoord + offset - step.xz, texRgn.xy, texRgn.zw)) +
        tex2D(TextureSampler2, clamp2(texCoord + offset + step.zy, texRgn.xy, texRgn.zw)) + 
        tex2D(TextureSampler2, clamp2(texCoord + offset - step.zy, texRgn.xy, texRgn.zw)) + 
        tex2D(TextureSampler2, clamp2(texCoord + offset, texRgn.xy, texRgn.zw));
    distance /= 5.0;
    distance -= OutlineRadiusSoftnessAndPower.x;
    // FIXME
    float shadowAlpha = pow(1 - saturate(distance / OutlineRadiusSoftnessAndPower.y), OutlineRadiusSoftnessAndPower.z);

    /*
    shadowAlpha = saturate(shadowAlpha / OutlineSumDivisor);
    shadowAlpha = pow(shadowAlpha, OutlineExponent);
    */
    float4 shadowColor = float4(shadowColorIn.rgb, 1);
    shadowColor = lerp(GlobalShadowColor, shadowColor, shadowColorIn.a > 0 ? 1 : 0);

    float4 overColor = (texColor * multiplyColor);
    overColor += (addColor * overColor.a);

    // Significantly improves the appearance of colored outlines and/or colored text
    float4 overSRGB = pSRGBToPLinear_Accurate(overColor),
        shadowSRGB = pSRGBToPLinear_Accurate(shadowColor);
    result = over(overSRGB, 1, shadowSRGB, shadowAlpha * saturate(shadowColorIn.a));
    result = pLinearToPSRGB_Accurate(result);

    const float discardThreshold = (1.0 / 255.0);
    clip(result.a - discardThreshold);
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

    float4 texColor = tex2Dbias(TextureSampler, float4(clamp2(texCoord, texRgn.xy, texRgn.zw), 0, MIP_BIAS));
    texColor = ExtractRgba(texColor, BitmapTraits);
    result = multiplyColor * texColor;
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

technique DistanceFieldOutlinedBitmapTechnique
{
    pass P0
    {
        vertexShader = compile vs_3_0 GenericVertexShader();
        pixelShader = compile ps_3_0 DistanceFieldOutlinedPixelShader();
    }
}
