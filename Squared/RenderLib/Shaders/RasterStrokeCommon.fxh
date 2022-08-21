#pragma warning ( disable: 3571 )

#if UntexturedShadowed
#define Shadowed 1
#define Untextured 1
#define Textured 0
#endif

#if TexturedShadowed
#define Shadowed 1
#define Untextured 0
#define Textured 1
#endif

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
#include "BezierCommon.fxh"

// TODO: Leave this out if not textured

Texture2D NozzleTexture : register(t0);
sampler NozzleSampler : register(s0) {
    Texture = (NozzleTexture);
};

Texture2D NoiseTexture : register(t1);
sampler NoiseSampler : register(s1) {
    Texture = (NoiseTexture);
};

uniform const bool BlendInLinearSpace, OutputInLinearSpace, UsesNoise;
uniform const float HalfPixelOffset;

// Count x, count y, base size for lod calculation, unused
uniform const float4 NozzleParams;

#if Shadowed

// offset x, offset y, unused, unused
uniform const float4 ShadowSettings, 
    // pSRGB or linear depending on blend parameter
    ShadowColor;

#else

#define ShadowSettings float4(0, 0, 0, 0)
#define ShadowColor float4(0, 0, 0, 0)

#endif

// size, angle, flow, brushIndex
uniform const float4 Constants1;
// hardness, color, spacing, baseSize
uniform const float4 Constants2;
// TaperFactor, Increment, NoiseFactor, AngleFactor
uniform const float4 SizeDynamics, AngleDynamics, FlowDynamics, 
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
    float constant, float4 dynamics, float4 data, float limit
) {
    bool wrap = constant < 0;
    float result = abs(constant) + (data.y * dynamics.y) +
        (data.z * dynamics.z) + (data.w * dynamics.w);
    if (wrap)
        result = abs(result % 1);
    else
        result = clamp(result, 0, limit);
    float taper = (dynamics.x < 0) ? 1 - data.x : data.x;
    return lerp(result, result * taper, abs(dynamics.x));
}

float evaluateDynamics2(
    float offset, float maxValue, float4 dynamics, float4 data, bool wrap, float limit
) {
    float result = offset + (data.y * dynamics.y) +
        (maxValue * data.z * dynamics.z) + (maxValue * data.w * dynamics.w);
    if (wrap)
        result = abs(result % limit);
    else
        result = clamp(result, 0, limit);
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

// porter-duff A over B
float4 over(float4 top, float topOpacity, float4 bottom, float bottomOpacity) {
    top *= topOpacity;
    bottom *= bottomOpacity;

    return top + (bottom * (1 - top.a));
}

inline float2x2 make2DRotation (in float radians) {
    float2 sinCos;
    sincos(radians, sinCos.x, sinCos.y);
    return float2x2(
        sinCos.y, -sinCos.x,
        sinCos.x, sinCos.y
    );
}

inline float2 rotate2D(
    in float2 corner, in float radians
) {
    float2x2 rotationMatrix = make2DRotation(radians);
    return mul(corner, rotationMatrix);
}

#ifdef LINE_EARLY_REJECT
    #define EARLY_REJECT LINE_EARLY_REJECT
#else
    #define EARLY_REJECT false
#endif
#define SEARCH_DISTANCE (stepPx + maxSize)
#define GET_CLOSEST_POINT(worldPosition, result) closestPointOnLineSegment2(a, b, worldPosition, result)
#define CALCULATE_LENGTH length(ba)
#define CALCULATE_CENTER(t) (a + (ba * (t)))
#define IMPL_INPUTS in float4 ab
#define IMPL_NAME rasterStrokeLineCommon

#if Shadowed
    #define SHADOW_LOOP_HEADER \
        float4 temp1 = 0, temp2 = 0; \
        int shadowIterations = ShadowColor.a > 0 ? 2 : 1; \
        for (int si = 0; si < shadowIterations; si++) { \
            float4 shadowTemp = (si == 0) ? temp1 : temp2;

    #define SHADOW_OUTPUT shadowTemp

    #define SHADOW_LOOP_FOOTER \
            if (si == 0) \
                temp1 = shadowTemp; \
            else \
                temp2 = shadowTemp; \
            worldPosition -= ShadowSettings.xy; \
            localBiases.z += (ShadowSettings.z - Constants2.x + 1); \
            localRadiuses.z += ShadowSettings.w; \
        } \

    #define SHADOW_MERGE { \
        result = over(over(temp1, 1, ShadowColor, temp2.a), 1, result, 1); \
        temp1 = temp2 = 0; \
        }
#else
    #define SHADOW_LOOP_HEADER { ; }
    #define SHADOW_LOOP_FOOTER { ; }
    #define SHADOW_OUTPUT result
    #define SHADOW_MERGE { ; }
#endif

#include "RasterStrokeLineCommonImpl.fxh"