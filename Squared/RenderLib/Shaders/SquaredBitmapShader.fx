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
uniform const float3 TextDistanceScaleOffsetAndPower = float3(0.25, 1.0, 1.8);
uniform const float  ShadowedTopMipBias, ShadowMipBias;
uniform const bool   PremultiplyTexture, MultiplyShadowAlpha = true;
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
    texColor = ExtractRgba(texColor, AdaptTraits(BitmapTraits));
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
    texColor = ExtractRgba(texColor, AdaptTraits(BitmapTraits));
    texColor.rgb = ApplyLUT(texColor.rgb, LUT2Weight);
    texColor.rgb = ApplyDither(texColor.rgb, GET_VPOS);

    result = multiplyColor * texColor;
    result += (addColor * result.a);

    const float discardThreshold = (1.0 / 255.0);
    clip(result.a - discardThreshold);
}

void ToLinearPixelShader(
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
    texColor = ExtractRgba(texColor, AdaptTraits(BitmapTraits));
    texColor = pSRGBToPLinear_Accurate(texColor);
    result = multiplyColor * texColor;
    result += (addColor * result.a);
    result.rgb = ApplyDither(result.rgb, GET_VPOS);
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
    texColor = ExtractRgba(texColor, AdaptTraits(BitmapTraits));
    result = multiplyColor * texColor;
    result += (addColor * result.a);
    result = pLinearToPSRGB_Accurate(result);
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

    float2 shadowTexCoord = clamp2(texCoord - (ShadowOffset * BitmapTexelSize), texRgn.xy, texRgn.zw);
    float4 texColor = tex2Dbias(TextureSampler, float4(clamp2(texCoord, texRgn.xy, texRgn.zw), 0, ShadowedTopMipBias + DefaultShadowedTopMipBias));
    float4 traits = BitmapTraits;
    if ((shadowColorIn.a < 0) || PremultiplyTexture)
        traits.z = 1;
    texColor = ExtractRgba(texColor, traits);
    shadowColorIn.a = abs(shadowColorIn.a);

    float4 shadowColor = lerp(GlobalShadowColor, shadowColorIn, shadowColorIn.a > 0 ? 1 : 0) * tex2Dbias(TextureSampler, float4(shadowTexCoord, 0, ShadowMipBias)).a;
    if (shadowColor.a > 1)
        shadowColor = normalize(shadowColor);
    float4 texColorSRGB = pSRGBToPLinear((texColor * multiplyColor) + (addColor * texColor.a));
    if (MultiplyShadowAlpha)
        shadowColor *= multiplyColor.a;
    float4 shadowColorSRGB = pSRGBToPLinear(shadowColor);
    result = texColorSRGB + (shadowColorSRGB * (1 - texColorSRGB.a));
    
    if (!GetRenderTargetIsLinearSpace())
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
    float2 offset = (ShadowOffset * BitmapTexelSize);
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

    if (MultiplyShadowAlpha)
        shadowColor *= multiplyColor.a;

    // Significantly improves the appearance of colored outlines and/or colored text
    float4 overSRGB = pSRGBToPLinear(overColor),
        shadowSRGB = pSRGBToPLinear(shadowColor);
    result = lerp(shadowSRGB, overSRGB, overColor.a);
    
    if (!GetRenderTargetIsLinearSpace())
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

float distanceTapEpilogue(float2 uv, float2 coord, float tap, float2 texelSize) {
    // For taps outside the texture region, increase the length artificially
    // This allows you to expand the bounds of your image and set TransparentExterior to give
    //  it a satisfactory border even if it lacks whitespace around the outside
    float2 clampedDistancePx = -min(coord, 0) + (max(coord, 1) - 1);
    clampedDistancePx /= texelSize;
    // HACK: Ensure that the input tap isn't negative since we know by definition exterior texels can never
    //  be 'inside' the image represented by the distance field, no matter what the corner texel says
    tap = max(0, tap) + length(clampedDistancePx);
    return tap;
}

float distanceTap(float2 coord, float4 texRgn, float bias) {
    float2 uv = clamp2(coord, texRgn.xy, texRgn.zw);
    float tap = tex2Dbias(TextureSampler, float4(uv, 0, bias)).r;
    return distanceTapEpilogue(uv, coord, tap, BitmapTexelSize);
}

float distanceTap2(float2 coord, float4 texRgn, float bias) {
    float2 uv = clamp2(coord, texRgn.xy, texRgn.zw);
    float tap = tex2Dbias(TextureSampler2, float4(uv, 0, bias)).r;
    return distanceTapEpilogue(uv, coord, tap, BitmapTexelSize2);
}

float computeMip(in float2 texCoordPx) {
    float2 dx = ddx(texCoordPx), dy = ddy(texCoordPx);
    float mag = max(dot(dx, dx), dot(dy, dy));
    return 0.5 * log2(mag);
}

void DistanceFieldTextPixelShader(
    in float4 multiplyColor : COLOR0,
    in float4 addColor : COLOR1,
    in float4 shadowColorIn : COLOR2,
    in float2 texCoord : TEXCOORD0,
    in float4 texRgn : TEXCOORD1,
    out float4 result : COLOR0
) {
    addColor.rgb *= addColor.a;
    addColor.a = 0;
    shadowColorIn.a = abs(shadowColorIn.a);

    float3 step = float3(BitmapTexelSize, 0);
    // HACK: Use the mip value as an approximation of how much we're being scaled down. If we're being scaled down,
    //  we need to adjust the distance parameters appropriately to maintain smooth edges because the size of a distance
    //  field pixel stays fixed while the size of a screen pixel changes.
    float bias = -0.5,
        mip = computeMip(texCoord / BitmapTexelSize),
        effectiveScale = TextDistanceScaleOffsetAndPower.x / (1 + abs(mip)),
        effectiveOffset = TextDistanceScaleOffsetAndPower.y; // FIXME: Scale the offset too?
    // HACK: 5 tap averaging because the SDF may be slightly inaccurate, and not at full pixel offsets
    float distance = distanceTap(texCoord, texRgn, bias) +
        distanceTap(texCoord - step.xz, texRgn, bias) +
        distanceTap(texCoord + step.zy, texRgn, bias) +
        distanceTap(texCoord - step.zy, texRgn, bias) +
        distanceTap(texCoord, texRgn, bias);
    distance /= 5.0;
    float overAlpha = pow(1.0 - saturate((distance + effectiveOffset) * effectiveScale), TextDistanceScaleOffsetAndPower.z);
    float4 overColor = multiplyColor;
    overColor += (addColor * overColor.a);

    if (abs(OutlineRadiusSoftnessAndPower.x) + abs(OutlineRadiusSoftnessAndPower.y) > 0) {
        float2 offset = (ShadowOffset * BitmapTexelSize);
        // HACK: 5 tap averaging because the SDF may be slightly inaccurate, and not at full pixel offsets
        float shadowDistance = distanceTap(texCoord + offset, texRgn, 0) +
            distanceTap(texCoord + offset - step.xz, texRgn, 0) +
            distanceTap(texCoord + offset + step.zy, texRgn, 0) +
            distanceTap(texCoord + offset - step.zy, texRgn, 0) +
            distanceTap(texCoord + offset, texRgn, 0);
        shadowDistance /= 5.0;
        shadowDistance -= OutlineRadiusSoftnessAndPower.x;
        // FIXME
        float shadowAlpha = pow(1 - saturate(shadowDistance / OutlineRadiusSoftnessAndPower.y), OutlineRadiusSoftnessAndPower.z);
        float4 shadowColor = float4(shadowColorIn.rgb, 1);
        shadowColor = lerp(GlobalShadowColor, shadowColor, shadowColorIn.a > 0 ? 1 : 0);

        // HACK: Ensure the outline does not paint under opaque pixels while faded out
        if (multiplyColor.a < 1.0)
            shadowAlpha -= overAlpha;

        // Significantly improves the appearance of colored outlines and/or colored text
        // FIXME: Why do we have to double multiply here?
        float4 overSRGB = overColor;
        overSRGB.rgb /= overSRGB.a;
        overSRGB.rgb = SRGBToLinear(saturate(overSRGB.rgb));
        float4 shadowSRGB = shadowColor;
        shadowSRGB.rgb = SRGBToLinear(shadowSRGB.rgb);
        result = over(overSRGB, overSRGB.a * overAlpha, shadowColor, saturate(shadowAlpha * shadowColor.a));
        result.rgb = LinearToSRGB(result.rgb);
        result.rgb *= result.a;
    } else {
        result = overColor * overAlpha;
    }

    const float discardThreshold = (1.0 / 255.0);
    clip(result.a - discardThreshold);
}

void DistanceFieldOutlinedPixelShader(
    in float4 multiplyColor : COLOR0,
    in float4 addColor : COLOR1,
    in float4 shadowColorIn : COLOR2,
    in float2 texCoord : TEXCOORD0,
    in float4 texRgn : TEXCOORD1,
    in float2 texCoord2 : TEXCOORD2,
    in float4 texRgn2 : TEXCOORD3,
    out float4 result : COLOR0
) {
    addColor.rgb *= addColor.a;
    addColor.a = 0;

    float4 texColor = tex2Dbias(TextureSampler, float4(clamp2(texCoord, texRgn.xy, texRgn.zw), 0, ShadowedTopMipBias + DefaultShadowedTopMipBias));
    float4 traits = AdaptTraits(BitmapTraits);
    if ((shadowColorIn.a < 0) || PremultiplyTexture)
        traits.z = 1;
    texColor = ExtractRgba(texColor, traits);
    texColor = AutoClampAlpha4(texColor, texCoord, texRgn, BitmapTexelSize, TransparentExterior);
    shadowColorIn.a = abs(shadowColorIn.a);

    float2 offset = (ShadowOffset * BitmapTexelSize2);
    // HACK: 5 tap averaging because the SDF may be slightly inaccurate, and not at full pixel offsets
    float3 step = float3(BitmapTexelSize2, 0);
    float distance = distanceTap2(texCoord2 + offset, texRgn2, 0) + 
        distanceTap2(texCoord2 + offset - step.xz, texRgn2, 0) +
        distanceTap2(texCoord2 + offset + step.zy, texRgn2, 0) +
        distanceTap2(texCoord2 + offset - step.zy, texRgn2, 0) +
        distanceTap2(texCoord2 + offset, texRgn2, 0);
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

    // HACK: Ensure the outline does not paint under opaque pixels while faded out
    if (multiplyColor.a < 1.0)
        shadowAlpha -= texColor.a;

    // Significantly improves the appearance of colored outlines and/or colored text
    float4 overSRGB = overColor,
        shadowSRGB = shadowColor;
    overSRGB.rgb /= overSRGB.a;
    overSRGB.rgb = SRGBToLinear(saturate(overSRGB.rgb));
    shadowSRGB.rgb = SRGBToLinear(shadowSRGB.rgb);
    result = over(overSRGB, overSRGB.a, shadowColor, shadowAlpha * saturate(shadowColorIn.a));
    
    // over() produces a premultiplied result, but it's in linear space so we need to depremultiply it, then
    //  turn it into premultiplied sRGB    
    if (!GetRenderTargetIsLinearSpace())
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

technique BitmapToSRGBTechnique
{
    pass P0
    {
        vertexShader = compile vs_3_0 GenericVertexShader();
        pixelShader = compile ps_3_0 ToSRGBPixelShader();
    }
}

technique BitmapToLinearTechnique
{
    pass P0
    {
        vertexShader = compile vs_3_0 GenericVertexShader();
        pixelShader = compile ps_3_0 ToLinearPixelShader();
    }
}

technique ShadowedBitmapTechnique
{
    pass P0
    {
        vertexShader = compile vs_3_0 GenericVertexShader();
        pixelShader = compile ps_3_0 ShadowedPixelShader();
    }
}

technique ShadowedBitmapWithDiscardTechnique
{
    pass P0
    {
        vertexShader = compile vs_3_0 GenericVertexShader();
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

technique BitmapWithLUTTechnique
{
    pass P0
    {
        vertexShader = compile vs_3_0 GenericVertexShader();
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

technique DistanceFieldTextTechnique
{
    pass P0
    {
        vertexShader = compile vs_3_0 GenericVertexShader();
        pixelShader = compile ps_3_0 DistanceFieldTextPixelShader();
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
