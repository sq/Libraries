// O3 produces literally 1/3 the instructions of OD or O0 so let's just be kind to the driver
#pragma fxcparams(/O3 /Zi)

#pragma fxcflagset(Untextured,Textured)

// FIXME: false is preferable here
#define COLOR_PER_SPLAT true
// #define VISUALIZE_TEXCOORDS
// #define VISUALIZE_OVERDRAW
#include "PolygonCommon.fxh"

// HACK: A polygon may contain many line segments, few of which will be close to a given
//  pixel inside the bounding box. The normal single-line stroke rasterizer doesn't have
//  to worry about this since the line itself has a tight bounding box, but that's not
//  true here, so we do a quick distance check at the start before stepping across the
//  whole stroke and doing hit tests against every splat.
#define LINE_EARLY_REJECT (length(worldPosition - closestPoint) > (maxSize * 1.33))

#include "RasterStrokeCommon.fxh"

#define SEARCH_DISTANCE 9999
#define EARLY_REJECT false
#define GET_CLOSEST_POINT(worldPosition, result) result = 0.5
#define CALCULATE_LENGTH bezierLength
#define CALCULATE_CENTER(t) evaluateBezierAtT(a, _actual_b, b, t)
#define IMPL_INPUTS in float4 ab, in float2 _actual_b, in float bezierLength
#define IMPL_NAME rasterStrokeBezierCommon

#include "RasterStrokeLineCommonImpl.fxh"

void computeTLBR_Polygon(
    in float vertexOffset, in float vertexCount,
    out float2 tl, out float2 br, out float estimatedLengthPx
) {
    tl = 99999;
    br = -99999;
    estimatedLengthPx = 0;

    float baseRadius = (Constants2.w * 0.66) + 1;
    float2 prev = 0;
    float maxLocalRadius = 0;
    int offset = (int)vertexOffset, count = (int)vertexCount;

    while (count > 0) {
        while (count-- > 0) {
            float4 xytr = getPolyVertex(offset);
            int nodeType = (int)xytr.z;
            float2 pos = xytr.xy;
            maxLocalRadius = max(maxLocalRadius, xytr.w);
            offset++;

            REQUIRE_BRANCH
            if (nodeType == NODE_BEZIER) {
                float4 controlPoints = getPolyVertex(offset);
                offset++;
                float2 btl, bbr;
                computeTLBR_Bezier(prev, controlPoints.xy, pos, btl, bbr);
                tl = min(btl, tl);
                br = max(bbr, br);
                estimatedLengthPx += controlPoints.z;
            } else {
                // FIXME: Is this right? Not doing it seems to break our bounding boxes
                tl = min(pos, tl);
                br = max(pos, br);

                if (nodeType != NODE_START)
                    estimatedLengthPx += length(pos - prev);
            }
            prev = pos;
        }
    }

    if (br.x < tl.x)
        tl.x = br.x = -9999;
    if (br.y < tl.y)
        tl.y = br.y = -9999;

    tl -= (baseRadius + maxLocalRadius + 2);
    br += (baseRadius + maxLocalRadius + 2);
}

void RasterStrokePolygonVertexShader(
    in float4 cornerWeights : NORMAL2,
    // offset, count, unused, unused
    in float4 config_in : POSITION0,
    inout float4 seed : TEXCOORD0,
    inout float4 taper : TEXCOORD1,
    inout float4 biases : TEXCOORD2,
    inout float4 colorA : COLOR0,
    inout float4 colorB : COLOR1,
    in  int2 unusedAndWorldSpace : BLENDINDICES1,
    out float2 worldPosition : NORMAL0,
    out float4 result : POSITION0,
    // offset, count, estimatedLengthPx, unused
    out float4 config : TEXCOORD3
) {
    float2 a, b;
    computeTLBR_Polygon(config_in.x, config_in.y, a, b, config.z);
    config.xy = config_in.xy;

    float4 position = float4(lerp(a, b, cornerWeights.xy), 0, 1);
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

    int offset = (int)ab.x, count = (int)ab.y, overdraw = 0;
    float estimatedLengthPx = ab.z, distanceTraveled = 0, totalSteps = 0,
        stepPx = max(Constants2.w * Constants2.z, 0.05),
        // HACK: We want to search in a larger area for beziers since the math
        //  we use to find the closest bezier is inaccurate.
        searchRadius = Constants2.w + 1, searchRadius2 = searchRadius * searchRadius;
    float4 prev = 0;

    while (count > 0) {
        while (count-- > 0) {
            float4 xytr = getPolyVertex(offset);
            int nodeType = (int)xytr.z;
            float2 pos = xytr.xy, localRadiuses = float2(prev.w, xytr.w);
            float steps = 0;

            offset++;
            REQUIRE_BRANCH
            if (nodeType == NODE_BEZIER) {
                float4 controlPoints = getPolyVertex(offset);
                offset++;

                // HACK: Try to locate the closest point on the bezier. If even it is far enough away
                //  that it can't overlap the current pixel, skip processing the entire bezier.
                float2 a = prev.xy - worldPosition, b = controlPoints.xy - worldPosition,
                    c = pos - worldPosition;
                // First check the middle and endpoints
                float ct = 0, cd2 = distanceSquaredToBezierAtT(a, b, c, ct);
                float td = distanceSquaredToBezierAtT(a, b, c, 1);
                pickClosestT(cd2, ct, td, 1);
                td = distanceSquaredToBezierAtT(a, b, c, 0.5);
                pickClosestT(cd2, ct, td, 0.5);

                // Then do an analytical check (we can't rely entirely on this, the math breaks down at some spots)
                pickClosestTOnBezierForAxis(a, b, c, float2(1, 0), cd2, ct);
                pickClosestTOnBezierForAxis(a, b, c, float2(0, 1), cd2, ct);

                float bezierLength = controlPoints.z;

                REQUIRE_BRANCH
                if (cd2 > searchRadius2) {
                    steps = bezierLength / stepPx;
                } else {
#ifdef VISUALIZE_OVERDRAW
                    overdraw++;
#endif
                    steps = rasterStrokeBezierCommon(
                        localRadiuses, worldPosition, float4(prev.xy, pos), controlPoints.xy, bezierLength, seed, taper, biases,
                        distanceTraveled, estimatedLengthPx, totalSteps, GET_VPOS, colorA, colorB, result
                    );
                }
                distanceTraveled += bezierLength;
                totalSteps += steps;
            } else if (nodeType == NODE_LINE) {
                steps = rasterStrokeLineCommon(
                    localRadiuses, worldPosition, float4(prev.xy, pos), seed, taper, biases,
                    distanceTraveled, estimatedLengthPx, totalSteps, GET_VPOS, colorA, colorB, result
                );
                distanceTraveled += length(pos - prev.xy);
                totalSteps += steps;
            }
            prev = xytr;
        }
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

#ifdef VISUALIZE_OVERDRAW
    result += float4((0.1 * overdraw), 0, 0, saturate(overdraw));
#endif

    result = ApplyDither4(result, GET_VPOS);
}

technique __VARIANT_TECHNIQUE_NAME
{
    pass P0
    {
        vertexShader = compile vs_3_0 RasterStrokePolygonVertexShader();
        pixelShader = compile ps_3_0 __VARIANT_FS_NAME();
    }
}