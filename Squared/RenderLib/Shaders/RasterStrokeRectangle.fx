// O3 produces literally 1/3 the instructions of OD or O0 so let's just be kind to the driver
#pragma fxcparams(/O3 /Zi)

#pragma fxcflagset(Untextured,Textured)

#define COLOR_PER_SPLAT false
#include "RasterStrokeCommon.fxh"

void computePosition(
    float2 a, float2 b,
    float4 cornerWeights, out float2 xy
) {
    // HACK: Slightly increase the radius and pad it to account for
    //  pixel overhang and antialiasing
    float totalRadius = (Constants2.w * 0.55) + 1;

    // FIXME: Tighten box based on start and end offset
    xy = lerp(a - totalRadius, b + totalRadius, cornerWeights.xy);
}

void RasterStrokeRectangleVertexShader (
    in float4 cornerWeights : NORMAL2,
    in float4 ab_in : POSITION0,
    inout float4 seed : TEXCOORD0,
    inout float4 taper : TEXCOORD1,
    inout float4 biases : TEXCOORD2,
    inout float4 colorA : COLOR0,
    inout float4 colorB : COLOR1,
    in  int2 unusedAndWorldSpace : BLENDINDICES1,
    out float2 worldPosition : NORMAL0,
    out float4 result : POSITION0,
    out float4 ab : TEXCOORD3
) {
    ab = ab_in;
    float4 position = float4(ab_in.x, ab_in.y, 0, 1);
    float2 a = ab.xy, b = ab.zw;
    if (b.y < a.y)
        ab = float4(b.x, b.y, a.x, a.y);

    computePosition(a, b, cornerWeights, position.xy);

    float2 adjustedPosition = position.xy;
    worldPosition = adjustedPosition.xy;

    if (unusedAndWorldSpace.y > 0.5) {
        adjustedPosition -= GetViewportPosition().xy;
        adjustedPosition *= GetViewportScale().xy;
    }

    result = TransformPosition(
        float4(adjustedPosition, position.z, 1), true
    );

    // We use the _Accurate conversion function here because the approximation introduces
    //  visible noise for values like (64, 64, 64) when they are dithered.
    // We do the initial conversion in the VS to avoid paying the cost per-fragment, and also
    //  take the opportunity to do a final conversion here for 'simple' fragments so that
    //  it doesn't have to be done per-fragment
    // FIXME: Is this reasonably correct for simple shapes with outlines? Probably
    if (BlendInLinearSpace) {
        colorA = pSRGBToPLinear_Accurate(colorA);
        colorB = pSRGBToPLinear_Accurate(colorB);
    }
}

void __VARIANT_FS_NAME (
    RASTERSTROKE_FS_ARGS
) {
    result = 0;
    float maxSize = Constants2.w,
        // HACK: Low spacings are way worse for rectangles
        stepPx = max(maxSize * Constants2.z, 2),
        h = ab.w - ab.y,
        centerY = worldPosition.y,
        startY = max(0, centerY - (stepPx + maxSize)),
        endY = min(centerY + (stepPx + maxSize), ab.w),
        firstIteration = floor((startY - ab.y) / stepPx),
        lastIteration = ceil((endY - ab.y) / stepPx);

    for (float i = firstIteration; i <= lastIteration; i += 1.0) {
        float y = ab.y + (stepPx * i);
        if ((y < ab.y) || (y > ab.w))
            continue;

        float4 _ab = float4(ab.x, y, ab.z, y);
        float4 _seed = float4(seed.x + (i * seed.w * 1.276), seed.y + (i * seed.z * 0.912), seed.z, seed.w);
        // FIXME
        float shuffle = 0; // floor(i * 0.7);
        rasterStrokeLineCommon(
            shuffle, worldPosition, _ab, _seed, taper, biases, 0, 0, 0, GET_VPOS, colorA, colorB, result
        );
    }

    // Unpremultiply the output, because if we don't we get unpleasant stairstepping artifacts
    //  for alpha gradients because the A we premultiply by does not match the A the GPU selected
    // It's also important to do dithering and sRGB conversion on a result that is not premultiplied
    result.rgb = float4(result.rgb / max(result.a, 0.0001), result.a);

    if (BlendInLinearSpace != OutputInLinearSpace) {
        if (OutputInLinearSpace)
            result.rgb = SRGBToLinear(result).rgb;
        else
            result.rgb = LinearToSRGB(result.rgb);
    }

    result = ApplyDither4(result, GET_VPOS);
}

technique __VARIANT_TECHNIQUE_NAME
{
    pass P0
    {
        vertexShader = compile vs_3_0 RasterStrokeRectangleVertexShader();
        pixelShader = compile ps_3_0 __VARIANT_FS_NAME();
    }
}