// O3 produces literally 1/3 the instructions of OD or O0 so let's just be kind to the driver
#pragma fxcparams(/O3 /Zi)

#pragma warning ( disable: 3571 )

#define PI 3.14159265358979323846
#define PIXEL_COVERAGE_RADIUS 0.71
// 1.0 / (System.Math.PI * (0.71 * 0.71))
#define PIXEL_COVERAGE_FACTOR 0.631441948390777
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
// hardness, width, spacing, baseSize
uniform float4 Constants2;
// TaperFactor, Increment, NoiseFactor, AngleFactor
uniform float4 SizeDynamics, AngleDynamics, FlowDynamics, 
    BrushIndexDynamics, HardnessDynamics, WidthDynamics;
// TODO: Spacing dynamics?

#define RASTERSTROKE_FS_ARGS \
    in float2 worldPosition: NORMAL0, \
    in float4 ab : TEXCOORD3, \
    in float4 seed : TEXCOORD0, \
    in float4 taper : TEXCOORD1, \
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
    // HACK: A radius of 0.7071 would exactly contain a 1x1 box,
    //  but we bias it up slightly to avoid occasional artifacts near edges
    float x0 = pixel.x, y0 = pixel.y, r0 = PIXEL_COVERAGE_RADIUS,
        x1 = center.x, y1 = center.y, r1 = radius;
    float rr0 = r0 * r0,
        rr1 = r1 * r1,
        c = sqrt((x1 - x0)*(x1 - x0) + (y1 - y0)*(y1 - y0)),
        phi = (acos((rr0 + (c*c) - rr1) / (2 * r0*c))) * 2,
        theta = (acos((rr1 + (c*c) - rr0) / (2 * r1*c))) * 2,
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

void computePosition(
    float2 a, float2 b, 
    float4 cornerWeights, out float2 xy
) {
    xy = 0;

    // HACK: Tighter bounding box
    float totalRadius = (Constants2.w / 1.44) + 1;

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
    in float4 seed,
    in float4 taper,
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

void RasterStrokeLineSegmentVertexShader(
    in float4 cornerWeights : NORMAL2,
    in float4 ab_in : POSITION0,
    inout float4 seed : TEXCOORD0,
    inout float4 taper : TEXCOORD1,
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
        seed,
        taper,
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
    in float2 worldPosition, in float4 ab, 
    in float4 seed, in float4 taperRanges, in float2 vpos,
    in float4 colorA, in float4 colorB,
    out float4 result
) {
    float2 a = ab.xy, b = ab.zw, ba = b - a,
        atlasScale = float2(1.0 / NozzleParams.x, 1.0 / NozzleParams.y);
    result = 0;

    const float threshold = (1 / 512.0);

    // Locate the closest point on the line segment. This will be the center of our search area
    // We search outwards from the center in both directions to find splats that may overlap us
    float maxSize = Constants2.w,
        // FIXME: A spacing of 1.0 still produces overlap
        stepPx = max(maxSize * Constants2.z, 0.05), maxRadius = maxSize * 0.5,
        l = max(length(ba), 0.01), centerT, splatCount = (l / stepPx);
    taperRanges.zw *= l;

    float taperedL = max(l - taperRanges.z - taperRanges.w, 0.01),
        stepT = 1.0 / splatCount,
        angleRadians = atan2(ba.y, ba.x),
        // FIXME: 360deg -> 1.0
        angleFactor = angleRadians;

    closestPointOnLineSegment2(a, b, worldPosition, centerT);
    float centerD = centerT * l,
        startD = max(0, centerD - maxSize),
        endD = min(centerD + maxSize, l),
        firstIteration = floor(startD / stepPx), 
        lastIteration = ceil(endD / stepPx),
        brushCount = NozzleParams.x * NozzleParams.y;

    for (float i = firstIteration; i <= lastIteration; i += 1.0) {
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
            taper1 = abs(taperRanges.x) >= 1 ? saturate((d - taperRanges.z) / abs(taperRanges.x)) : 1,
            taper2 = abs(taperRanges.y) >= 1 ? saturate((taperedL - (d - taperRanges.z)) / taperRanges.y) : 1,
            taper = min(taper1, taper2);
        float sizePx = clamp(evaluateDynamics2(Constants1.x * maxSize, maxSize, SizeDynamics, float4(taper, i, noise1.x, angleFactor)), 0, maxSize),
            splatAngleFactor = evaluateDynamics(Constants1.y, AngleDynamics, float4(taper, i, noise1.y, angleFactor)),
            splatAngle = splatAngleFactor * PI * 2,
            flow = clamp(evaluateDynamics(Constants1.z, FlowDynamics, float4(taper, i, noise1.z, angleFactor)), 0, 2),
            brushIndex = evaluateDynamics2(Constants1.w, brushCount, BrushIndexDynamics, float4(taper, i, noise1.w, angleFactor)),
            hardness = saturate(evaluateDynamics(Constants2.x, HardnessDynamics, float4(taper, i, noise2.x, angleFactor))),
            widthFactor = saturate(evaluateDynamics(Constants2.y, WidthDynamics, float4(taper, i, noise2.y, angleFactor)));

        bool outOfRange = (d < taperRanges.z) || (d > (l - taperRanges.w));

        float r = i * sizePx, widthScale = 1.0 / widthFactor, radius = sizePx * 0.5;
        float2 center = a + (ba * t), texCoordScale = atlasScale / sizePx;
        float distance = length(center - worldPosition);

        float2 posSplatRotated = (worldPosition - center),
            posSplatDerotated = rotate2D(posSplatRotated, -splatAngle),
            // FIXME: Width doesn't work
            posSplatDecentered = (posSplatDerotated + radius) * float2(widthScale, 1);

        float g = length(posSplatDerotated);
        float4 color;

        if (Textured) {
            if (
                outOfRange ||
                (abs(posSplatDerotated.x) > radius) ||
                (abs(posSplatDerotated.y) > radius)
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
        } else {
            // HACK: We do the early-out more conservatively because of the need to compute
            //  partial coverage at circle edges, so an exact rejection is not right
            if (outOfRange || (g > (radius + 1)))
                continue;

            PREFER_BRANCH
            // FIXME: radius - 1 might be too conservative
            if ((distance >= (radius - 1)) || (radius <= 2)) {
                // HACK: Approximate pixel coverage calculation near edges / for tiny circles
                color = approxPixelCoverage(worldPosition, center, radius);
            } else {
                color = 1;
            }
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

        // HACK: Avoid accumulating error
        if (color.a > 0) {
            // TODO: Interpolate based on outer edges instead of center points,
            //  so that we don't get a nasty hard edge at the end
            color = lerp(
                colorA, colorB, COLOR_PER_SPLAT ? t : centerT
            ) * color;

            result = over(color, flow, result, 1);
        }
    }
}

float4 rasterStrokeCommon(
    in float2 worldPosition, in float4 ab, 
    in float4 seed, in float4 taper, in float2 vpos,
    in float4 colorA, in float4 colorB
) {
    float4 result;
    rasterStrokeLineCommon(
        worldPosition, ab, seed, taper, vpos, colorA, colorB, result
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
        worldPosition, ab, seed, taper,
        GET_VPOS, colorA, colorB
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
