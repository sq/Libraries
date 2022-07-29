// O3 produces literally 1/3 the instructions of OD or O0 so let's just be kind to the driver
#pragma fxcparams(/O3 /Zi)

// FIXME: false is preferable here
#define COLOR_PER_SPLAT true
#include "PolygonCommon.fxh"
#include "RasterStrokeCommon.fxh"

void computeTLBR_Bezier(
    float2 a, float2 b, float2 c,
    out float2 tl, out float2 br
) {
    tl = min(a, c);
    br = max(a, c);

    if (any(b < tl) || any(b > br))
    {
        float2 t = clamp((a - b) / (a - 2.0*b + c), 0.0, 1.0);
        float2 s = 1.0 - t;
        float2 q = s * s*a + 2.0*s*t*b + t * t*c;
        tl = min(tl, q);
        br = max(br, q);
    }
}

void computeTLBR_Polygon(
    in float vertexOffset, in float vertexCount,
    out float2 tl, out float2 br, out float estimatedLengthPx
) {
    tl = 99999;
    br = -99999;
    estimatedLengthPx = 0;

    float baseRadius = (Constants2.w * 0.55) + 1;
    int offset = (int)vertexOffset;
    int count = (int)vertexCount;
    float2 prev = 0;
    float maxLocalRadius = 0;

    for (int i = 0; i < count; i++) {
        float4 xytr = getPolyVertex(offset);
        int nodeType = (int)xytr.z;
        float2 pos = xytr.xy;
        maxLocalRadius = max(maxLocalRadius, xytr.w);
        offset++;

        REQUIRE_BRANCH
        if (nodeType == NODE_BEZIER) {
            float4 controlPoints = getPolyVertex(offset);
            offset++;
            if (i > 0) {
                float2 btl, bbr;
                computeTLBR_Bezier(prev, controlPoints.xy, pos, btl, bbr);
                tl = min(btl, tl);
                br = max(bbr, br);
                // FIXME: Not correct
                estimatedLengthPx += length(pos - prev);
            }
        } else if (i > 0) {
            // FIXME: Is this right? Not doing it seems to break our bounding boxes
            tl = min(pos, tl);
            br = max(pos, br);
            if (nodeType != NODE_SKIP)
                estimatedLengthPx += length(pos - prev);
        }
        prev = pos;
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

void RasterStrokePolygonFragmentShader(
    RASTERSTROKE_FS_ARGS
) {
    result = 0;

    int offset = (int)ab.x, count = (int)ab.y;
    float estimatedLengthPx = ab.z, distanceTraveled = 0, totalSteps = 0;
    float2 prev = 0;

    for (int i = 0; i < count; i++) {
        float4 xytr = getPolyVertex(offset);
        int nodeType = (int)xytr.z;
        float2 pos = xytr.xy;
        float4 localBiases = biases;
        localBiases.x += xytr.w;

        offset++;
        REQUIRE_BRANCH
        if (nodeType == NODE_BEZIER) {
            float4 controlPoints = getPolyVertex(offset);
            offset++;
            // FIXME
        } else if (nodeType == NODE_LINE) {
        } else {
            prev = pos;
            continue;
        }

        if (i > 0) {
            float steps = rasterStrokeLineCommon(
                0, worldPosition, float4(prev, pos), seed, taper, localBiases, 
                distanceTraveled, estimatedLengthPx, totalSteps, GET_VPOS, colorA, colorB, result
            );
            distanceTraveled += length(pos - prev);
            totalSteps += steps;
        }
        prev = pos;
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

technique RasterStrokePolygon
{
    pass P0
    {
        vertexShader = compile vs_3_0 RasterStrokePolygonVertexShader();
        pixelShader = compile ps_3_0 RasterStrokePolygonFragmentShader();
    }
}
