#pragma warning ( disable: 3571 )

#define PI 3.14159265358979323846
// If we use 0.0 - 1.0 range values, denormals cause artifacts at small sizes :(
#define PIXEL_COVERAGE_BIAS 500.0
// HACK: A radius of 0.7071 would exactly contain a 1x1 box,
//  but we bias it up slightly to avoid occasional artifacts near edges
// 0.71 * 500.0
#define PIXEL_COVERAGE_RADIUS 355.0
// 1.0 / (System.Math.PI * (PIXEL_COVERAGE_RADIUS * PIXEL_COVERAGE_RADIUS))
#define PIXEL_COVERAGE_FACTOR 2.5257677935631083E-06
#define ENABLE_DITHERING 1

#include "CompilerWorkarounds.fxh"
#include "ViewTransformCommon.fxh"
#include "TargetInfo.fxh"
#include "DitherCommon.fxh"
#include "sRGBCommon.fxh"

Texture2D NozzleTexture : register(t0);
sampler NozzleSampler : register(s0) {
    Texture = (NozzleTexture);
};

Texture2D NoiseTexture : register(t1);
sampler NoiseSampler : register(s1) {
    Texture = (NoiseTexture);
};

uniform bool BlendInLinearSpace, OutputInLinearSpace, UsesNoise, Textured;
uniform float HalfPixelOffset;

// Count x, count y, base size for lod calculation, unused
uniform float4 NozzleParams;

// size, angle, flow, brushIndex
uniform float4 Constants1;
// hardness, color, spacing, baseSize
uniform float4 Constants2;
// TaperFactor, Increment, NoiseFactor, AngleFactor
uniform float4 SizeDynamics, AngleDynamics, FlowDynamics, 
    BrushIndexDynamics, HardnessDynamics, ColorDynamics;
// TODO: Spacing dynamics?

#define RASTERSTROKE_FS_ARGS \
    in float2 worldPosition: NORMAL0, \
    in float4 ab : TEXCOORD3, \
    in float4 seed : TEXCOORD0, \
    in float4 taper : TEXCOORD1, \
    in float4 biases : TEXCOORD2, \
    in float4 colorA : COLOR0, \
    in float4 colorB : COLOR1, \
    ACCEPTS_VPOS, \
    out float4 result : COLOR0

// approximate coverage of pixel-circle intersection (for a round pixel)
// we ideally would model this as a square-circle intersection, but the math for that
//  is MUCH more complex for a minimal quality increase. so instead, we use a circle that
//  fully encloses a 1x1 square. (a circle with radius 1 is too big, and a circle of radius
//  0.5 is too small and will leave gaps between pixels.)
float approxPixelCoverage (float2 pixel, float2 center, float radius) {
    radius *= PIXEL_COVERAGE_BIAS;
    float2 distance = center - pixel;
    distance *= distance;
    float rr0 = PIXEL_COVERAGE_RADIUS * PIXEL_COVERAGE_RADIUS,
        rr1 = radius * radius,
        c = sqrt(distance.x + distance.y),
        phi = (acos((rr0 + (c*c) - rr1) / (2 * PIXEL_COVERAGE_RADIUS*c))) * 2,
        theta = (acos((rr1 + (c*c) - rr0) / (2 * radius*c))) * 2,
        area1 = 0.5*theta*rr1 - 0.5*rr1*sin(theta),
        area2 = 0.5*phi*rr0 - 0.5*rr0*sin(phi);
    return saturate((area1 + area2) * PIXEL_COVERAGE_FACTOR);
}

float evaluateDynamics(
    float constant, float4 dynamics, float4 data
) {
    float result = constant + (data.y * dynamics.y) +
        (data.z * dynamics.z) + (data.w * dynamics.w);
    float taper = (dynamics.x < 0) ? 1 - data.x : data.x;
    return lerp(result, result * taper, abs(dynamics.x));
}

float evaluateDynamics2(
    float offset, float maxValue, float4 dynamics, float4 data
) {
    float result = offset + (data.y * dynamics.y) +
        (maxValue * data.z * dynamics.z) + (maxValue * data.w * dynamics.w);
    float taper = (dynamics.x < 0) ? 1 - data.x : data.x;
    return lerp(result, result * taper, abs(dynamics.x));
}

float4 TransformPosition(float4 position, bool halfPixelOffset) {
    // Transform to view space, then offset by half a pixel to align texels with screen pixels
    float4 modelViewPos = mul(position, Viewport.ModelView);
    if (halfPixelOffset && HalfPixelOffset)
        modelViewPos.xy -= 0.5;
    // Finally project after offsetting
    return mul(modelViewPos, Viewport.Projection);
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

float rasterStrokeLineCommon(
    in float shuffle, in float2 worldPosition, in float4 ab, 
    in float4 seed, in float4 taperRanges, in float4 biases,
    in float distanceTraveled, in float totalLength, in float stepOffset,
    in float2 vpos, in float4 colorA, in float4 colorB,
    inout float4 result
) {
    float2 a = ab.xy, b = ab.zw, ba = b - a,
        atlasScale = float2(1.0 / NozzleParams.x, 1.0 / NozzleParams.y);

    if (totalLength <= 0)
        totalLength = length(ba);

    const float threshold = (1 / 512.0);

    // Locate the closest point on the line segment. This will be the center of our search area
    // We search outwards from the center in both directions to find splats that may overlap us
    float maxSize = Constants2.w,
        // FIXME: A spacing of 1.0 still produces overlap
        stepPx = max(maxSize * Constants2.z, 0.05), maxRadius = maxSize * 0.5,
        l = max(length(ba), 0.01), centerT, splatCount = ceil(l / stepPx);
    taperRanges.zw *= l;

    float taperedL = max(totalLength - taperRanges.z - taperRanges.w, 0.01),
        stepT = 1.0 / splatCount,
        angleRadians = atan2(ba.y, ba.x),
        // FIXME: 360deg -> 1.0
        angleFactor = angleRadians;

    closestPointOnLineSegment2(a, b, worldPosition, centerT);
    float centerD = centerT * l,
        startD = max(0, centerD - (stepPx + maxSize)),
        endD = min(centerD + (stepPx + maxSize), l),
        firstIteration = floor(startD / stepPx), 
        lastIteration = ceil(endD / stepPx),
        brushCount = NozzleParams.x * NozzleParams.y;

    for (float i = firstIteration; i <= lastIteration; i += 1.0) {
        float globalI = i + stepOffset;
        float4 noise1, noise2;
        if (UsesNoise) {
            float4 seedUv = float4(seed.x + (i * 2 * seed.z), seed.y + (i * seed.w), 0, 0);
            noise1 = tex2Dlod(NoiseSampler, seedUv);
            seedUv.x += seed.z;
            noise2 = tex2Dlod(NoiseSampler, seedUv);
        } else {
            noise1 = 0;
            noise2 = 0;
        }

        float t = i * stepT,
            d = t * l,            
            // FIXME: Right now if tapering is enabled the taper1 value for the first splat is always 0
            // The ideal would be for it to start at a very low value based on spacing or length
            taper1 = abs(taperRanges.x) >= 1 ? saturate((d + distanceTraveled - taperRanges.z) / abs(taperRanges.x)) : 1,
            taper2 = abs(taperRanges.y) >= 1 ? saturate((taperedL - (d + distanceTraveled - taperRanges.z)) / taperRanges.y) : 1,
            taper = min(taper1, taper2);
        float sizePx = clamp(evaluateDynamics2((Constants1.x + biases.x) * maxSize, maxSize, SizeDynamics, float4(taper, i, noise1.x, angleFactor)), 0, maxSize);
        if (sizePx <= 0)
            continue;

        // biases are: (Size, Flow, Hardness, Color)
        float splatAngleFactor = evaluateDynamics(Constants1.y, AngleDynamics, float4(taper, globalI, noise1.y, angleFactor)),
            splatAngle = splatAngleFactor * PI * 2,
            flow = clamp(evaluateDynamics(Constants1.z + biases.y, FlowDynamics, float4(taper, globalI, noise1.z, angleFactor)), 0, 2),
            brushIndex = evaluateDynamics2(Constants1.w, brushCount, BrushIndexDynamics, float4(taper, globalI, noise1.w, angleFactor)),
            hardness = saturate(evaluateDynamics(Constants2.x + biases.z, HardnessDynamics, float4(taper, globalI, noise2.x, angleFactor))),
            // HACK: Increment here is scaled by t instead of i
            colorT = COLOR_PER_SPLAT ? t : centerT,
            colorFactor = saturate(evaluateDynamics(Constants2.y + biases.w, ColorDynamics, float4(taper, colorT, noise2.y, angleFactor)));

        bool outOfRange = (d < taperRanges.z) || (d > (l - taperRanges.w));

        float r = i * sizePx, radius = sizePx * 0.5;
        float2 center = a + (ba * t), texCoordScale = atlasScale / sizePx;
        float distance = length(center - worldPosition);

        float2 posSplatRotated = (worldPosition - center),
            posSplatDerotated = rotate2D(posSplatRotated, -splatAngle),
            posSplatDecentered = (posSplatDerotated + radius);

        float g = length(posSplatDerotated), discardDistance = (hardness < 1) ? radius + 1 : radius;
        float4 color;

        if (Textured) {
            if (
                outOfRange ||
                any(abs(posSplatDerotated) > radius)
            ) {
                continue;
            } else {
                brushIndex = floor(brushIndex) % brushCount;
                float splatIndexY = floor(brushIndex / NozzleParams.x), splatIndexX = brushIndex - (splatIndexY * NozzleParams.x);
                float2 texCoord = (posSplatDecentered * texCoordScale) + (atlasScale * float2(splatIndexX, splatIndexY));
                float lod = max(log2(NozzleParams.z / sizePx) - 0.35, 0);

                if (false) {
                    // HACK: Diagnostic view of splat rects
                    color = float4(texCoord.x, texCoord.y, lod / 6, 1);
                } else {
                    float4 texel = tex2Dlod(NozzleSampler, float4(texCoord.xy, 0, lod));
                    if (BlendInLinearSpace)
                        texel = pSRGBToPLinear(texel);
                    color = texel;
                }
            }

            // Ramp the alpha channel more or less aggressively
            if ((color.a < 1) && (color.a > 0) && (hardness != 0.5)) {
                float softness = (1 - saturate(hardness)) * 2 - 1.0;
                float e = softness > 0
                    ? lerp(1, 3, softness)
                    : lerp(1, 0.25, -softness);
                float softA = pow(color.a, e);
                color *= (softA / color.a);
            }
        } else {
            // HACK: We do the early-out more conservatively because of the need to compute
            //  partial coverage at circle edges, so an exact rejection is not right
            if (outOfRange || (g >= discardDistance))
                continue;

            PREFER_BRANCH
            // FIXME: radius - 1 might be too conservative
            if ((distance >= (radius - 1)) || (radius <= 2)) {
                // HACK: Approximate pixel coverage calculation near edges / for tiny circles
                color = approxPixelCoverage(worldPosition, center, radius);
            } else {
                color = 1;
            }

            if (hardness < 1) {
                float falloff = max((1 - hardness) * radius, 1.05);
                // HACK
                g -= (hardness * radius);
                g /= falloff;
                g = saturate(g + 0.05);
                g = sin(g * PI * 0.5);
                color *= 1 - g;
            }
        }

        // HACK: Avoid accumulating error
        if (color.a > 0) {
            // TODO: Interpolate based on outer edges instead of center points,
            //  so that we don't get a nasty hard edge at the end
            color = lerp(
                colorA, colorB, colorFactor
            ) * color;

            result = over(color, flow, result, 1);
        }
    }

    return ceil(l / stepPx);
}