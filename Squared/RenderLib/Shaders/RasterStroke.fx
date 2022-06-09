// O3 produces literally 1/3 the instructions of OD or O0 so let's just be kind to the driver
#pragma fxcparams(/O3 /Zi)

#pragma warning ( disable: 3571 )

#define ENABLE_DITHERING 1
#define COLOR_PER_SPLAT false

#include "CompilerWorkarounds.fxh"
#include "ViewTransformCommon.fxh"
#include "TargetInfo.fxh"
#include "DitherCommon.fxh"
#include "sRGBCommon.fxh"

Texture2D NozzleTexture : register(t0);
sampler NozzleSampler : register(s0) {
    Texture = (NozzleTexture);
};

uniform bool BlendInLinearSpace, OutputInLinearSpace;
uniform float HalfPixelOffset;

uniform float2 NozzleCountXy;
// Brush size, brush spacing factor (n * size), rotation per splat (radians), flow
uniform float4 Params1;

#define RASTERSTROKE_FS_ARGS \
    in float2 worldPosition: NORMAL0, \
    in float4 ab : TEXCOORD3, \
    in float4 colorA : COLOR0, \
    in float4 colorB : COLOR1, \
    ACCEPTS_VPOS, \
    out float4 result : COLOR0

float4 TransformPosition(float4 position, bool halfPixelOffset) {
    // Transform to view space, then offset by half a pixel to align texels with screen pixels
    float4 modelViewPos = mul(position, Viewport.ModelView);
    if (halfPixelOffset && HalfPixelOffset)
        modelViewPos.xy -= 0.5;
    // Finally project after offsetting
    return mul(modelViewPos, Viewport.Projection);
}

void computePosition(
    float2 a, float2 b, 
    float4 cornerWeights, out float2 xy
) {
    xy = 0;

    // HACK: Tighter bounding box
    float totalRadius = (Params1.x / 1.44) + 1;

    // Oriented bounding box around the line segment
    float2 along = b - a,
        alongNorm = normalize(along) * (totalRadius + 1),
        left = alongNorm.yx * float2(-1, 1),
        right = alongNorm.yx * float2(1, -1);

    // FIXME
    xy = lerp(a - alongNorm, b + alongNorm, cornerWeights.x) + lerp(left, right, cornerWeights.y);
}

void RasterStrokeVertexShader_Core(
    in float4 cornerWeights,
    in float4 ab_in,
    inout float4 colorA,
    inout float4 colorB,
    in  int2 unusedAndWorldSpace,
    out float2 worldPosition,
    out float4 result,
    out float4 ab
) {
    ab = ab_in;
    float4 position = float4(ab_in.x, ab_in.y, 0, 1);
    float2 a = ab.xy, b = ab.zw;

    computePosition(a, b, cornerWeights, position.xy);

    float2 adjustedPosition = position.xy;
    if (unusedAndWorldSpace.y > 0.5) {
        adjustedPosition -= GetViewportPosition().xy;
        adjustedPosition *= GetViewportScale().xy;
    }

    // FIXME
    worldPosition = adjustedPosition.xy;

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

void RasterStrokeLineSegmentVertexShader(
    in float4 cornerWeights : NORMAL2,
    in float4 ab_in : POSITION0,
    inout float4 colorA : COLOR0,
    inout float4 colorB : COLOR1,
    in  int2 unusedAndWorldSpace : BLENDINDICES1,
    out float2 worldPosition : NORMAL0,
    out float4 result : POSITION0,
    out float4 ab : TEXCOORD3
) {
    RasterStrokeVertexShader_Core(
        cornerWeights,
        ab_in,
        colorA,
        colorB,
        unusedAndWorldSpace,
        worldPosition,
        result,
        ab
    );
}

float2 closestPointOnLine2(float2 a, float2 b, float2 pt, out float t) {
    float2  ab = b - a;
    float d = dot(ab, ab);
    if (abs(d) < 0.001)
        d = 0.001;
    t = dot(pt - a, ab) / d;
    return a + t * ab;
}

float2 closestPointOnLineSegment2(float2 a, float2 b, float2 pt, out float t) {
    float2  ab = b - a;
    float d = dot(ab, ab);
    if (abs(d) < 0.001)
        d = 0.001;
    t = saturate(dot(pt - a, ab) / d);
    return a + t * ab;
}

void evaluateLineSegment(
    in float2 worldPosition, in float2 a, in float2 b, in float2 c,
    in float2 radius, out float distance,
    out float2 gradientWeight
) {
    float t;
    float2 closestPoint = closestPointOnLineSegment2(a, b, worldPosition, t);
    float localRadius = radius.x + lerp(c.y, radius.y, t);
    distance = length(worldPosition - closestPoint) - localRadius;

    float fakeY = 0; // FIXME
    gradientWeight = float2(saturate(t), fakeY);
}

// Assumes y is 0
bool quadraticBezierTFromY(
    in float y0, in float y1, in float y2,
    out float t1, out float t2
) {
    float divisor = (y0 - (2 * y1) + y2);
    if (abs(divisor) <= 0.001) {
        t1 = t2 = 0;
        return false;
    }
    float rhs = sqrt(-(y0 * y2) + (y1 * y1));
    t1 = ((y0 - y1) + rhs) / divisor;
    t2 = ((y0 - y1) - rhs) / divisor;
    return true;
}

float2 evaluateBezierAtT(
    in float2 a, in float2 b, in float2 c, in float t
) {
    float2 ab = lerp(a, b, t),
        bc = lerp(b, c, t);
    return lerp(ab, bc, t);
}

void pickClosestT(
    inout float cd, inout float ct, in float d, in float t
) {
    if (d < cd) {
        ct = t;
        cd = d;
    }
}

// Assumes worldPosition is 0 relative to the control points
float distanceSquaredToBezierAtT(
    in float2 a, in float2 b, in float2 c, in float t
) {
    float2 pt = evaluateBezierAtT(a, b, c, t);
    return abs(dot(pt, pt));
}

void pickClosestTForAxis(
    in float2 a, in float2 b, in float2 c, in float2 mask,
    inout float cd, inout float ct
) {
    // For a given x or y value on the bezier there are two candidate T values that are closest,
    //  so we compute both and then pick the closest of the two. If the divisor is too close to zero
    //  we will have failed to compute any valid T values, so we bail out
    float t1, t2;
    float2 _a = a * mask, _b = b * mask, _c = c * mask;
    if (!quadraticBezierTFromY(_a.x + _a.y, _b.x + _b.y, _c.x + _c.y, t1, t2))
        return;
    float d1 = distanceSquaredToBezierAtT(a, b, c, t1);
    if ((t1 > 0) && (t1 < 1))
        pickClosestT(cd, ct, d1, t1);
    float d2 = distanceSquaredToBezierAtT(a, b, c, t2);
    if ((t2 > 0) && (t2 < 1))
        pickClosestT(cd, ct, d2, t2);
}

// porter-duff A over B
float4 over(float4 top, float topOpacity, float4 bottom, float bottomOpacity) {
    top *= topOpacity;
    bottom *= bottomOpacity;

    return top + (bottom * (1 - top.a));
}

/*
void evaluateRasterShape(
    int type, float2 radius, float outlineSize, float4 params,
    in float2 worldPosition, in float2 a, in float2 b, in float2 c, in bool simple,
    out float distance, inout float2 tl, inout float2 br,
    inout int gradientType, out float2 gradientWeight, inout float gradientAngle
) {
    type = EVALUATE_TYPE;
    bool needTLBR = false;

    distance = 0;
    gradientWeight = 0;

    evaluateLineSegment(
        worldPosition, a, b, c,
        radius, distance,
        gradientType, gradientWeight
    );
    needTLBR = true;

    if (needTLBR)
        computeTLBR(type, radius, outlineSize, params, a, b, c, tl, br);
}

*/

inline float2 rotate2D(
    in float2 corner, in float radians
) {
    float2 sinCos;
    sincos(radians, sinCos.x, sinCos.y);
    return float2(
        (sinCos.y * corner.x) - (sinCos.x * corner.y),
        (sinCos.x * corner.x) + (sinCos.y * corner.y)
    );
}

void rasterStrokeLineCommon(
    in float2 worldPosition,
    in float4 ab, in float2 vpos,
    in float4 colorA, in float4 colorB,
    out float4 result
) {
    float2 a = ab.xy, b = ab.zw, ba = b - a,
        atlasScale = float2(1.0 / NozzleCountXy.x, 1.0 / NozzleCountXy.y),
        texCoordScale = atlasScale / Params1.x;
    result = 0;

    const float threshold = (1 / 512.0);

    // Locate the closest point on the line segment. This will be the center of our search area
    // We search outwards from the center in both directions to find splats that may overlap us
    float stepPx = Params1.y * Params1.x, radius = Params1.x * 0.5,
        l = length(ba), centerT, splatCount = (l / stepPx),
        stepT = 1.0 / splatCount;
    closestPointOnLineSegment2(a, b, worldPosition, centerT);
    float centerD = centerT * l,
        startD = max(0, centerD - Params1.x),
        endD = min(centerD + Params1.x, l),
        firstIteration = floor(startD / stepPx), 
        lastIteration = ceil(endD / stepPx);

    for (float i = firstIteration; i <= lastIteration; i += 1.0) {
        // TODO: scaling
        float t = i * stepT, r = i * Params1.z;
        float2 center = a + (ba * t);
        float distance = length(center - worldPosition);

        float2 posSplatRotated = worldPosition - center,
            posSplatDerotated = rotate2D(posSplatRotated, -r),
            posSplatDecentered = posSplatDerotated + radius;

        float alpha;
        float4 color;

        if (
            (abs(posSplatDerotated.x) > radius) ||
            (abs(posSplatDerotated.y) > radius)
        ) {
            continue;
        } else {
            // FIXME: random?
            float splatIndexY = floor(i / NozzleCountXy.x), splatIndexX = i - (splatIndexY * NozzleCountXy.x);
            float2 texCoord = (posSplatDecentered * texCoordScale) + (atlasScale * float2(splatIndexX, splatIndexY));

            if (false) {
                // HACK: Diagnostic view of splat rects
                alpha = 1.0;
                color = float4(texCoord.x, texCoord.y, 0, 1);
            } else {
                float4 texel = tex2Dlod(NozzleSampler, float4(texCoord.xy, 0, 0));
                // FIXME
                alpha = texel.r * Params1.w;
                color = lerp(
                    colorA, colorB, COLOR_PER_SPLAT ? t : centerT
                );
            }
        }

        result = over(color, alpha, result, 1);
    }
}

float4 rasterStrokeCommon(
    in float2 worldPosition,
    in float4 ab, in float2 vpos,
    in float4 colorA, in float4 colorB
) {
    float4 result;
    rasterStrokeLineCommon(
        worldPosition, ab, vpos, colorA, colorB, result
    );

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

    return ApplyDither4(result, vpos);
}

void RasterStrokeLineSegmentFragmentShader(
    RASTERSTROKE_FS_ARGS
) {
    result = rasterStrokeCommon(
        worldPosition, ab, GET_VPOS,
        colorA, colorB
    );
}

technique RasterStrokeLineSegment
{
    pass P0
    {
        vertexShader = compile vs_3_0 RasterStrokeLineSegmentVertexShader();
        pixelShader = compile ps_3_0 RasterStrokeLineSegmentFragmentShader();
    }
}
