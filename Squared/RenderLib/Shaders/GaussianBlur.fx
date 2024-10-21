// HACK: We don't really care about running this in debug mode, since
//  blur operations are so tex (and to a lesser degree arith) intensive
//  that we want to optimize the hell out of them no matter what
#pragma fxcparams(/O3 /Zi)

#include "CompilerWorkarounds.fxh"
#include "ViewTransformCommon.fxh"
#include "FormatCommon.fxh"
#include "BitmapCommon.fxh"
#include "TargetInfo.fxh"
#include "DitherCommon.fxh"
#include "sRGBCommon.fxh"

static const float OutlineMin = 0.2, OutlineMax = 0.4;

uniform const float TapSpacingFactor = 1.0;

// Tap Count, Sigma, Axis X, Axis Y
uniform const float4 BlurConfiguration = float4(5, 2, 1, 0);

// HACK: The default mip bias for things like text atlases is unnecessarily blurry, especially if
//  the atlas is high-DPI
#define DefaultShadowedTopMipBias MIP_BIAS
uniform const float  ShadowedTopMipBias, ShadowMipBias, OutlineExponent = 1.2;
uniform const bool   PremultiplyTexture, TransparentExterior;

uniform const float2 ShadowOffset;

uniform const float4 GlobalShadowColor;

sampler TapSampler : register(s0) {
    Texture = (BitmapTexture);
    MipFilter = POINT;
    MinFilter = LINEAR;
    MagFilter = LINEAR;
    AddressU = CLAMP;
    AddressV = CLAMP;
};

float computeMip (in float2 texCoordPx) {
    float2 dx = ddx(texCoordPx), dy = ddy(texCoordPx);
    float mag = max(dot(dx, dx), dot(dy, dy));
    return 0.5 * log2(mag);
}

float tapA(
    in float2 texCoord,
    in float4 texRgn,
    in float mipBias
) {
    // FIXME: Use extract value so this works with single channel textures
    // HACK: We can't use tex2dbias here because we're inside a loop and it forces it to unroll
    float4 texColor = tex2Dlod(TapSampler, float4(clamp2(texCoord, texRgn.xy, texRgn.zw), 0, mipBias));
    return AutoClampAlpha1(ExtractMask(texColor, BitmapTraits), texCoord, saturate(texRgn), BitmapTexelSize, TransparentExterior);
}

float tapO(
    in float2 texCoord,
    in float4 texRgn,
    in float mipBias
) {
    float a = tapA(texCoord, texRgn, mipBias);
    return smoothstep(OutlineMin, OutlineMax, a);
}

float4 tap(
    in float2 texCoord,
    in float4 texRgn,
    in float mipBias
) {
    float4 pSRGB = tex2Dlod(TapSampler, float4(clamp2(texCoord, texRgn.xy, texRgn.zw), 0, mipBias));
    return pSRGBToPLinear(pSRGB);
}

float4 tapLinear(
    in float2 texCoord,
    in float4 texRgn,
    in float mipBias
) {
    return tex2Dlod(TapSampler, float4(clamp2(texCoord, texRgn.xy, texRgn.zw), 0, mipBias));
}

float4 gaussianBlur1D(
    in float2 stepSize,
    in float2 texCoord,
    in float4 texRgn,
    in float mipBias
) {
    float4 sum = 0;
    float divisor = 0;

    [loop]
    for (float i = 0; i <= BlurConfiguration.x; i += 1) {
        float weight = exp(-0.5 * pow(i / BlurConfiguration.y, 2.0));
        float2 offset = i * stepSize;
        if (abs(i) < 0.01)
            weight *= 0.5;

        divisor += weight;
        sum += tap(texCoord - offset, texRgn, mipBias) * weight;
        sum += tap(texCoord + offset, texRgn, mipBias) * weight;
    }

    return sum / (divisor * 2);
}

float4 gaussianBlur1DLinear(
    in float2 stepSize,
    in float2 texCoord,
    in float4 texRgn,
    in float mipBias
) {
    float4 sum = 0;
    float divisor = 0;

    [loop]
    for (float i = 0; i <= BlurConfiguration.x; i += 1) {
        float weight = exp(-0.5 * pow(i / BlurConfiguration.y, 2.0));
        float2 offset = i * stepSize;
        if (abs(i) < 0.01)
            weight *= 0.5;

        divisor += weight;
        sum += tapLinear(texCoord - offset, texRgn, mipBias) * weight;
        sum += tapLinear(texCoord + offset, texRgn, mipBias) * weight;
    }

    return sum / (divisor * 2);
}

float gaussianBlurA(
    in float2 stepSize,
    in float2 texCoord,
    in float4 texRgn,
    in float mipBias
) {
    float sum = 0, divisor = 0;

    [loop]
    for (float i = 0; i <= BlurConfiguration.x; i += 1) {
        float weight = exp(-0.5 * pow(i / BlurConfiguration.y, 2.0));
        float2 offset = i * stepSize;
        if (abs(i) < 0.01)
            weight *= 0.5;

        divisor += weight;
        sum += tapA(texCoord - offset, texRgn, mipBias) * weight;
        sum += tapA(texCoord + offset, texRgn, mipBias) * weight;
    }

    return sum / (divisor * 2);
}

float gaussianBlurO(
    in float2 stepSize,
    in float2 texCoord,
    in float4 texRgn,
    in float mipBias
) {
    float sum = 0, divisor = 0;

    [loop]
    for (float i = 0; i <= BlurConfiguration.x; i += 1) {
        float weight = exp(-0.5 * pow(i / BlurConfiguration.y, 2.0));
        float2 offset = i * stepSize;
        if (abs(i) < 0.01)
            weight *= 0.5;

        divisor += weight;
        sum += tapO(texCoord - offset, texRgn, mipBias) * weight;
        sum += tapO(texCoord + offset, texRgn, mipBias) * weight;
    }

    return saturate(sum / divisor);
}

float4 psEpilogue (
    in float4 texColor,
    in float4 multiplyColor,
    in float4 addColor
) {
    texColor = ExtractRgba(texColor, BitmapTraits);
    texColor = pLinearToPSRGB(texColor);

    if (PremultiplyTexture)
        texColor.rgb *= texColor.a;

    addColor.rgb *= addColor.a;
    addColor.a = 0;

    float4 result = multiplyColor * texColor;
    result += (addColor * result.a);
    return result;
}

float4 psEpilogueLinear (
    in float4 texColor,
    in float4 multiplyColor,
    in float4 addColor
) {
    float4 result = multiplyColor * texColor;
    result += addColor;
    return result;
}

void AxialGaussianBlurPixelShader(
    in float4 multiplyColor : COLOR0, 
    in float4 addColor : COLOR1, 
    in float2 texCoord : TEXCOORD0,
    in float4 texRgn : TEXCOORD1,
    out float4 result : COLOR0
) {
    float mip = computeMip(texCoord * BitmapTextureSize.xy) + MIP_BIAS;
    float4 sum = gaussianBlur1D(BitmapTexelSize * BlurConfiguration.zw, texCoord, texRgn, mip);
    result = psEpilogue(sum, multiplyColor, addColor);
}

void AxialGaussianBlurLinearPixelShader(
    in float4 multiplyColor : COLOR0, 
    in float4 addColor : COLOR1, 
    in float2 texCoord : TEXCOORD0,
    in float4 texRgn : TEXCOORD1,
    out float4 result : COLOR0
) {
    float mip = computeMip(texCoord * BitmapTextureSize.xy) + MIP_BIAS;
    float4 sum = gaussianBlur1DLinear(BitmapTexelSize * BlurConfiguration.zw, texCoord, texRgn, mip);
    result = psEpilogueLinear(sum, multiplyColor, addColor);
}

void RadialGaussianBlurPixelShader(
    in float4 multiplyColor : COLOR0,
    in float4 addColor : COLOR1,
    in float2 texCoord : TEXCOORD0,
    in float4 texRgn : TEXCOORD1,
    out float4 result : COLOR0
) {
    float mip = computeMip(texCoord * BitmapTextureSize.xy) + MIP_BIAS;

    float2 innerStepSize = BitmapTexelSize * float2(1, 0),
        outerStepSize = BitmapTexelSize * float2(0, 1);
    
    float4 sum = 0;
    float divisor = 0;

    [loop]
    for (float i = 0; i <= BlurConfiguration.x; i += 1) {
        float weight = exp(-0.5 * pow(i / BlurConfiguration.y, 2.0));
        float2 outerOffset = outerStepSize * i;
        
        sum += gaussianBlur1D(innerStepSize, texCoord - outerOffset, texRgn, mip) * weight;
        sum += gaussianBlur1D(innerStepSize, texCoord + outerOffset, texRgn, mip) * weight;
        divisor += weight;
    }

    sum /= (divisor * 2);
    result = psEpilogue(sum, multiplyColor, addColor);
}

void RadialMaskSofteningPixelShader(
    in float4 multiplyColor : COLOR0,
    in float4 addColor : COLOR1,
    in float2 texCoord : TEXCOORD0,
    in float4 texRgn : TEXCOORD1,
    // height scale, mip bias, masking strength, unused
    in float4 params : COLOR2,
    out float4 result : COLOR0
) {
    result = 0;
    float2 innerStepSize = BitmapTexelSize * float2(TapSpacingFactor, 0), 
        outerStepSize = BitmapTexelSize * float2(0, TapSpacingFactor);

    float4 traits = BitmapTraits;
    bool needPremul = PremultiplyTexture || (traits.z >= 1) || (traits.w >= ALPHA_MODE_BC);
    traits.z = 0;

    float mipBias = params.y,
        sum = 0,
        divisor = 0,
        centerTapAlpha = ExtractMask(tap(texCoord, texRgn, mipBias), traits);

    [loop]
    for (float i = 0; i <= BlurConfiguration.x; i += 1) {
        float weight = exp(-0.5 * pow(i / BlurConfiguration.y, 2.0));
        float2 outerOffset = outerStepSize * i;
        
        sum += gaussianBlurA(innerStepSize, texCoord - outerOffset, texRgn, mipBias) * weight;
        sum += gaussianBlurA(innerStepSize, texCoord + outerOffset, texRgn, mipBias) * weight;
        divisor += weight;
    }

    float resultValue = max(sum / (divisor * 2) * params.x, 0);

    result = (float4(resultValue, resultValue, resultValue, resultValue > 0 ? 1 : 0) + addColor) * multiplyColor;
    result.a = lerp(result.a, min(result.a, centerTapAlpha), params.z);

    const float discardThreshold = (0.5 / 255.0);
    clip(result.a - discardThreshold);
}

// porter-duff A over B
float4 over(float4 top, float topOpacity, float4 bottom, float bottomOpacity) {
    top *= topOpacity;
    bottom *= bottomOpacity;

    return top + (bottom * (1 - top.a));
}

void GaussianOutlinedPixelShader(
    in float4 multiplyColor : COLOR0,
    in float4 addColor : COLOR1,
    in float4 shadowColorIn : COLOR2,
    in float2 texCoord : TEXCOORD0,
    in float4 texRgn : TEXCOORD1,
    out float4 result : COLOR0
) {
    result = 0;
    
    addColor.rgb *= addColor.a;
    addColor.a = 0;

    float4 traits = BitmapTraits;
    float4 texColor = tex2Dbias(TapSampler, float4(clamp2(texCoord, texRgn.xy, texRgn.zw), 0, ShadowedTopMipBias + DefaultShadowedTopMipBias));
    bool needPremul = PremultiplyTexture || (shadowColorIn.a < 0) || (traits.z >= 1) || (traits.w >= ALPHA_MODE_BC);
    traits.z = 0;
    shadowColorIn.a = abs(shadowColorIn.a);

    // Artificially expand spacing since we're going for outlines
    // We compute the mip bias (using ddx/ddy) to determine how far out we can space the taps if the source texture
    //  is being scaled down, since the actual texels are farther apart if we're not reading from mip 0
    // This should provide a roughly constant outline size in screen space pixels regardless of scale
    // TODO: Factor in the mip bias as well
    float mip = computeMip(texCoord * BitmapTextureSize.xy) + MIP_BIAS;
    float spacingFactor = TapSpacingFactor * clamp(mip + 1, 1, 8);
    float2 innerStepSize = BitmapTexelSize * float2(spacingFactor, 0), 
        outerStepSize = BitmapTexelSize * float2(0, spacingFactor);

    texCoord -= ShadowOffset * BitmapTexelSize;

    // FIXME: Why is this here and implicitly truncating?
    texColor = ExtractRgba(texColor, traits);
    
    float sum = 0, divisor = 0;

    [loop]
    for (float i = 0; i <= BlurConfiguration.x; i += 1) {
        float weight = exp(-0.5 * pow(i / BlurConfiguration.y, 2.0));
        float2 outerOffset = outerStepSize * i;
        
        sum += gaussianBlurO(innerStepSize, texCoord - outerOffset, texRgn, mip) * weight;
        sum += gaussianBlurO(innerStepSize, texCoord + outerOffset, texRgn, mip) * weight;
        divisor += weight;
    }

    float shadowAlpha = saturate(sum / divisor);
    shadowAlpha = saturate(shadowAlpha * max(1, shadowColorIn.a));
    shadowAlpha = pow(shadowAlpha, OutlineExponent);

    float4 shadowColor = float4(shadowColorIn.rgb, 1) * saturate(shadowColorIn.a);
    shadowColor = lerp(GlobalShadowColor, shadowColor, shadowColorIn.a > 0 ? 1 : 0);

    float4 overColor;
    if (needPremul) {
        overColor = float4(SRGBToLinear(texColor.rgb), 1) * texColor.a;
    } else {
        overColor = pSRGBToPLinear_Accurate(texColor);
    }
    
    result = pLinearToPSRGB(over(overColor, 1, pSRGBToPLinear(shadowColor), shadowAlpha));
    result *= multiplyColor;
    result += (addColor * result.a);
}

void GaussianOutlinedPixelShaderWithDiscard(
    in float4 multiplyColor : COLOR0,
    in float4 addColor : COLOR1,
    in float4 outlineColorIn : COLOR2,
    in float2 texCoord : TEXCOORD0,
    in float4 texRgn : TEXCOORD1,
    out float4 result : COLOR0
) {
    GaussianOutlinedPixelShader(
        multiplyColor, addColor,
        outlineColorIn, texCoord, texRgn,
        result
    );

    const float discardThreshold = (0.5 / 255.0);
    clip(result.a - discardThreshold);
}

technique AxialGaussianBlur
{
    pass P0
    {
        vertexShader = compile vs_3_0 GenericVertexShader();
        pixelShader = compile ps_3_0 AxialGaussianBlurPixelShader();
    }
}

technique AxialGaussianBlurLinear
{
    pass P0
    {
        vertexShader = compile vs_3_0 GenericVertexShader();
        pixelShader = compile ps_3_0 AxialGaussianBlurLinearPixelShader();
    }
}

technique RadialGaussianBlur
{
    pass P0
    {
        vertexShader = compile vs_3_0 GenericVertexShader();
        pixelShader = compile ps_3_0 RadialGaussianBlurPixelShader();
    }
}

technique GaussianOutlined
{
    pass P0
    {
        vertexShader = compile vs_3_0 GenericVertexShader();
        pixelShader = compile ps_3_0 GaussianOutlinedPixelShader();
    }
}

technique GaussianOutlinedWithDiscard
{
    pass P0
    {
        vertexShader = compile vs_3_0 GenericVertexShader();
        pixelShader = compile ps_3_0 GaussianOutlinedPixelShaderWithDiscard();
    }
}

technique RadialMaskSoftening
{
    pass P0
    {
        vertexShader = compile vs_3_0 GenericVertexShader();
        pixelShader = compile ps_3_0 RadialMaskSofteningPixelShader();
    }
}