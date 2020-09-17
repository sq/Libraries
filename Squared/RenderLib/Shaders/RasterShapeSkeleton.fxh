#define ENABLE_DITHERING 1

#include "CompilerWorkarounds.fxh"
#include "ViewTransformCommon.fxh"
#include "TargetInfo.fxh"
#include "DitherCommon.fxh"
#include "sRGBCommon.fxh"
#include "SDF2D.fxh"

Texture2D RasterTexture : register(t0);

sampler TextureSampler : register(s0) {
    Texture = (RasterTexture);
};

// HACK suggested by Sean Barrett: Increase all line widths to ensure that a diagonal 1px-thick line covers one pixel
#define OutlineSizeCompensation 2.2

#define PI 3.1415926535897931
#define DEG_TO_RAD (PI / 180.0)

// A bunch of the distance formulas in here are thanks to inigo quilez
// http://iquilezles.org/www/articles/distfunctions2d/distfunctions2d.htm
// http://iquilezles.org/www/articles/distfunctions/distfunctions.htm

#define TYPE_Ellipse 0
#define TYPE_LineSegment 1
#define TYPE_Rectangle 2
#define TYPE_Triangle 3
#define TYPE_QuadraticBezier 4
#define TYPE_Arc 5

// Generic gradient types that operate on the bounding box or something
//  similar to it
#define GRADIENT_TYPE_Natural 0
#define GRADIENT_TYPE_Linear 1
#define GRADIENT_TYPE_Linear_Enclosing 2
#define GRADIENT_TYPE_Linear_Enclosed 3
#define GRADIENT_TYPE_Radial 4
#define GRADIENT_TYPE_Radial_Enclosing 5
#define GRADIENT_TYPE_Radial_Enclosed 6
// The gradient weight has already been computed by the evaluate function
#define GRADIENT_TYPE_Other 500
// Generic gradient that operates on the bounding box, with the angle added
//  to the base
#define GRADIENT_TYPE_Angular 512

#define ANGULAR_GRADIENT_BASE 512

#define RASTERSHAPE_FS_ARGS \
    in float4 worldPositionTypeAndWorldSpace : NORMAL0, \
    in float4 ab : TEXCOORD3, \
    in float4 cd : TEXCOORD4, \
    in float4 params : TEXCOORD0, \
    in float4 params2 : TEXCOORD1, \
    in float4 texRgn : TEXCOORD2, \
    in float4 centerColor : COLOR0, \
    in float4 edgeColor : COLOR1, \
    in float4 outlineColor : COLOR2, \
    ACCEPTS_VPOS, \
    out float4 result : COLOR0

// We use the _Accurate conversion function here because the approximation introduces
//  visible noise for values like (64, 64, 64) when they are dithered
#define RASTERSHAPE_PREPROCESS_COLORS \
    if (BlendInLinearSpace) { \
        centerColor = pSRGBToPLinear_Accurate(centerColor); \
        edgeColor = pSRGBToPLinear_Accurate(edgeColor); \
        outlineColor = pSRGBToPLinear_Accurate(outlineColor); \
    }

uniform bool BlendInLinearSpace;
uniform float HalfPixelOffset;

// offsetx, offsety, softness, fillSuppression
uniform float4 ShadowOptions;
uniform float4 ShadowColorLinear;

#define ShadowSoftness ShadowOptions.z
#define ShadowOffset ShadowOptions.xy
#define ShadowFillSuppression ShadowOptions.w

float4 TransformPosition(float4 position, bool halfPixelOffset) {
    // Transform to view space, then offset by half a pixel to align texels with screen pixels
    float4 modelViewPos = mul(position, Viewport.ModelView);
    if (halfPixelOffset && HalfPixelOffset)
        modelViewPos.xy -= 0.5;
    // Finally project after offsetting
    return mul(modelViewPos, Viewport.Projection);
}

float computeTotalRadius (float2 radius, float outlineSize) {
    return radius.x + outlineSize;
}

void computeTLBR (
    int type, float2 radius, float totalRadius, float4 params,
    float2 a, float2 b, float2 c,
    out float2 tl, out float2 br
) {
    switch (abs(type)) {
        case TYPE_Ellipse:
            tl = a - b - totalRadius;
            br = a + b + totalRadius;
            break;

        case TYPE_LineSegment:
            tl = min(a, b);
            br = max(a, b);
            break;

        case TYPE_Rectangle:
            tl = min(a, b) - totalRadius;
            br = max(a, b) + totalRadius;
            break;

        case TYPE_Triangle:
            totalRadius += 1;
            tl = min(min(a, b), c) - totalRadius;
            br = max(max(a, b), c) + totalRadius;
            break;

#ifdef INCLUDE_BEZIER
        case TYPE_QuadraticBezier:
            totalRadius += 1;
            float2 mi = min(a, c);
            float2 ma = max(a, c);

            if (b.x<mi.x || b.x>ma.x || b.y<mi.y || b.y>ma.y)
            {
                float2 t = clamp((a - b) / (a - 2.0*b + c), 0.0, 1.0);
                float2 s = 1.0 - t;
                float2 q = s*s*a + 2.0*s*t*b + t*t*c;
                mi = min(mi, q);
                ma = max(ma, q);
            }

            tl = mi - totalRadius;
            br = ma + totalRadius;
            break;
#endif

        case TYPE_Arc:
            tl = a - totalRadius - radius.y;
            br = a + totalRadius + radius.y;
            break;

        default:
            tl = -1; br = 1;
            return;
    }

    float annularRadius = params.y;
    if (annularRadius > 0) {
        tl -= annularRadius;
        br += annularRadius;
    }

    tl -= ShadowSoftness;
    br += ShadowSoftness;
    if (ShadowOffset.x >= 0)
        br.x += ShadowOffset.x;
    else 
        tl.x += ShadowOffset.x;

    if (ShadowOffset.y >= 0)
        br.y += ShadowOffset.y;
    else
        tl.y += ShadowOffset.y;
}

void computePosition (
    int type, float totalRadius, 
    float2 a, float2 b, float2 c,
    float2 tl, float2 br, float3 cornerWeights,
    out float2 xy
) {
    if (type == TYPE_LineSegment) {
        // Oriented bounding box around the line segment
        float2 along = b - a,
            alongNorm = normalize(along) * (totalRadius + 1),
            left = alongNorm.yx * float2(-1, 1),
            right = alongNorm.yx * float2(1, -1);

        // FIXME
        xy = lerp(a - alongNorm, b + alongNorm, cornerWeights.x) + lerp(left, right, cornerWeights.y);

        /*
        if (cornerIndex.x == 0)
            xy = a + left - alongNorm;
        else if (cornerIndex.x == 1)
            xy = b + left + alongNorm;
        else if (cornerIndex.x == 2)
            xy = b + right + alongNorm;
        else
            xy = a + right - alongNorm;
        */
    } else {
        // HACK: Padding
        tl -= 1; br += 1;
        // FIXME: Fit a better hull around triangles. Oriented bounding box?
        xy = lerp(tl, br, cornerWeights.xy);
    }
}

void RasterShapeVertexShader (
    in float3 cornerWeights : NORMAL2,
    in float4 ab_in : POSITION0,
    in float4 cd_in : POSITION1,
    inout float4 params : TEXCOORD0,
    inout float4 params2 : TEXCOORD1,
    inout float4 texRgn : TEXCOORD2,
    inout float4 centerColor : COLOR0,
    inout float4 edgeColor : COLOR1,
    inout float4 outlineColor : COLOR2,
    in  int2 typeAndWorldSpace : BLENDINDICES1,
    out float4 result : POSITION0,
    out float4 ab : TEXCOORD3,
    out float4 cd : TEXCOORD4,
    out float4 worldPositionTypeAndWorldSpace : NORMAL0
) {
    ab = ab_in; cd = cd_in;
    float4 position = float4(ab_in.x, ab_in.y, 0, 1);
    float2 a = ab.xy, b = ab.zw, c = cd.xy, radius = cd.zw;
    params.x *= OutlineSizeCompensation;
    float outlineSize = abs(params.x);
    int type = abs(typeAndWorldSpace.x);

    float totalRadius = computeTotalRadius(radius, outlineSize) + 1;
    float2 tl, br;

    computeTLBR(type, radius, totalRadius, params, a, b, c, tl, br);
    computePosition(type, totalRadius, a, b, c, tl, br, cornerWeights.xyz, position.xy);

    float2 adjustedPosition = position.xy;
    if (typeAndWorldSpace.y > 0.5) {
        adjustedPosition -= GetViewportPosition().xy;
        adjustedPosition *= GetViewportScale().xy;
    }

    result = TransformPosition(
        float4(adjustedPosition, position.z, 1), true
    );
    worldPositionTypeAndWorldSpace = float4(position.xy, typeAndWorldSpace.x, typeAndWorldSpace.y);

    // If we're not using an approximate Linear-sRGB conversion here it could add
    //  measurable overhead to the fragment shader, so why not do it here in the VS instead
    RASTERSHAPE_PREPROCESS_COLORS
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

float getWindowAlpha (
    float position, float windowStart, float windowEnd,
    float startAlpha, float centerAlpha, float endAlpha
) {
    float t = (position - windowStart) / (windowEnd - windowStart);
    if (t <= 0.5)
        return lerp(startAlpha, centerAlpha, saturate(t * 2));
    else
        return lerp(centerAlpha, endAlpha, saturate((t - 0.5) * 2));
}

void evaluateEllipse (
    in float2 worldPosition, in float2 a, in float2 b, in float2 c,
    out float distance, out float2 tl, out float2 br,
    inout int gradientType, out float gradientWeight
) {
    // FIXME: sdEllipse is massively broken. What is wrong with it?
    // distance = sdEllipse(worldPosition - a, b);
    float2 distanceXy = worldPosition - a;
    float distanceF = length(distanceXy / b);
    distance = (distanceF - 1) * length(b);
    gradientWeight = saturate(distanceF);
    tl = a - b;
    br = a + b;

    PREFER_FLATTEN
    switch (gradientType) {
        case GRADIENT_TYPE_Natural:
            gradientType = GRADIENT_TYPE_Radial;
            break;
        case GRADIENT_TYPE_Linear:
            // FIXME
        case GRADIENT_TYPE_Linear_Enclosing:
        case GRADIENT_TYPE_Linear_Enclosed:
            // Options:
            // * 2 = touches corners of a box enclosing the ellipse
            // * 2 * sqrt(2) == touches corners of a box enclosed by the ellipse
            float2 distance2 = abs(worldPosition - a) / (br - tl) * (
                (gradientType == GRADIENT_TYPE_Linear_Enclosed) 
                    ? (2 * sqrt(2)) 
                    : 2
            );
            gradientWeight = saturate(max(distance2.x, distance2.y));
            gradientType = GRADIENT_TYPE_Other;
            break;
    }
}

void evaluateLineSegment (
    in float2 worldPosition, in float2 a, in float2 b, in float2 c,
    in float2 radius, out float distance,
    inout int gradientType, out float gradientWeight
) {
    float t;
    float2 closestPoint = closestPointOnLineSegment2(a, b, worldPosition, t);
    float localRadius = radius.x + lerp(c.y, radius.y, t);
    distance = length(worldPosition - closestPoint) - localRadius;

    PREFER_FLATTEN
    if (c.x >= 0.5)
        gradientWeight = saturate(t);
    else
        gradientWeight = 1 - saturate(-distance / localRadius);
}

void evaluateRectangle (
    in float2 worldPosition, in float2 a, in float2 b, in float2 c,
    in float2 radius, out float distance,
    inout int gradientType, out float gradientWeight
) {
    float2 center = (a + b) * 0.5;
    float2 boxSize = abs(b - a) * 0.5;
    distance = sdBox(worldPosition - center, boxSize) - radius.x;

    float centerDistance = sdBox(0, boxSize) - radius.x;
    gradientWeight = 0;

    PREFER_FLATTEN
    switch (gradientType) {
        case GRADIENT_TYPE_Natural:
            gradientType = GRADIENT_TYPE_Linear;
            break;
    }
}

void evaluateTriangle (
    in float2 worldPosition, in float2 a, in float2 b, in float2 c,
    in float2 radius, out float distance,
    inout int gradientType, out float gradientWeight,
    out float2 tl, out float2 br
) {
    distance = sdTriangle(worldPosition, a, b, c);

    float2 center = (a + b + c) / 3;
    // float centerDistance = sdTriangle(center, a, b, c);
    // FIXME: Why is this necessary?
    float ac = length(a - center), bc = length(b - center), cc = length(c - center);
    float targetDistance = ((min(ac, min(bc, cc)) + max(ac, max(bc, cc))) * -0.25) - radius.x;

    tl = min(min(a, b), c);
    br = max(max(a, b), c);

    // FIXME: Recenter non-natural gradients around our centerpoint instead of the
    //  center of our bounding box

    PREFER_FLATTEN
    switch (abs(gradientType)) {
        case GRADIENT_TYPE_Natural:
            gradientWeight = 1 - saturate((distance - radius.x) / targetDistance);
            gradientType = GRADIENT_TYPE_Other;
            break;
        default:
            gradientWeight = 0;
            break;
    }

    distance -= radius.x;
}

float evaluateGradient (
    int gradientType, float gradientAngle, float gradientWeight,
    float2 worldPosition, float2 tl, float2 br, float radius
) {
    float2 gradientCenter = (tl + br) / 2;
    float2 radialSize2 = ((br - tl) + (radius * 2)) * 0.5;

    PREFER_FLATTEN
    switch (abs(gradientType)) {
        case GRADIENT_TYPE_Natural:
        case GRADIENT_TYPE_Other:
            break;
        case GRADIENT_TYPE_Linear:
        case GRADIENT_TYPE_Linear_Enclosing:
        case GRADIENT_TYPE_Linear_Enclosed:
            float2 linearRadialSize2 = (gradientType == GRADIENT_TYPE_Linear_Enclosing)
                ? max(radialSize2.x, radialSize2.y)
                : (
                    (gradientType == GRADIENT_TYPE_Linear_Enclosed)
                        ? min(radialSize2.x, radialSize2.y)
                        : radialSize2
                );
            float2 linearDist2 = abs(worldPosition - gradientCenter) / linearRadialSize2;
            return max(linearDist2.x, linearDist2.y);
        case GRADIENT_TYPE_Radial:
        case GRADIENT_TYPE_Radial_Enclosing:
        case GRADIENT_TYPE_Radial_Enclosed:
            float2 radialSize = (gradientType == GRADIENT_TYPE_Radial_Enclosing)
                ? max(radialSize2.x, radialSize2.y)
                : (
                    (gradientType == GRADIENT_TYPE_Radial_Enclosed)
                        ? min(radialSize2.x, radialSize2.y)
                        : radialSize2
                );
            return length((worldPosition - gradientCenter) / max(radialSize, 0.0001));
        case GRADIENT_TYPE_Angular:
            float2 scaled = (worldPosition - gradientCenter) / ((br - tl) * 0.5);
            gradientAngle += atan2(scaled.y, scaled.x);
            return ((sin(gradientAngle) * length(scaled)) / 2) + 0.5;
    }

    return gradientWeight;
}

void evaluateRasterShape (
    int type, float2 radius, float totalRadius, float4 params,
    in float2 worldPosition, in float2 a, in float2 b, in float2 c,
    out float distance, inout float2 tl, inout float2 br,
    inout int gradientType, out float gradientWeight
) {
PREFER_BRANCH
    switch (type) {
#ifdef INCLUDE_ELLIPSE
        case TYPE_Ellipse: {
            evaluateEllipse(
                worldPosition, a, b, c,
                distance, tl, br,
                gradientType, gradientWeight
            );

            break;
        }
#endif

#ifdef INCLUDE_LINE
        case TYPE_LineSegment: {
            evaluateLineSegment(
                worldPosition, a, b, c,
                radius, distance,
                gradientType, gradientWeight
            );

            computeTLBR(type, radius, totalRadius, params, a, b, c, tl, br);

            break;
        }
#endif

#ifdef INCLUDE_BEZIER
        case TYPE_QuadraticBezier: {
            distance = sdBezier(worldPosition, a, b, c) - radius.x;
            gradientWeight = 1 - saturate(-distance / radius.x);

            computeTLBR(type, radius, totalRadius, params, a, b, c, tl, br);

            break;
        }
#endif

#ifdef INCLUDE_RECTANGLE
        case TYPE_Rectangle: {
            evaluateRectangle(
                worldPosition, a, b, c,
                radius, distance,
                gradientType, gradientWeight
            );

            break;
        }
#endif

#ifdef INCLUDE_TRIANGLE
        case TYPE_Triangle: {
            evaluateTriangle(
                worldPosition, a, b, c,
                radius, distance,
                gradientType, gradientWeight, 
                tl, br
            );

            break;
        }
#endif

#ifdef INCLUDE_ARC
        case TYPE_Arc: {
            distance = sdArc(worldPosition - a, b, c, radius.x, radius.y);
            if (gradientType == GRADIENT_TYPE_Natural)
                gradientWeight = 1 - saturate(-distance / radius.y);

            computeTLBR(type, radius, totalRadius, params, a, b, c, tl, br);

            break;
        }
#endif
    }
}

float computeShadowAlpha (
    int type, float2 radius, float totalRadius, float4 params,
    float2 worldPosition, float2 a, float2 b, float2 c,
    float shadowEndDistance
) {
    float2 tl, br;
    int gradientType = 0;
    float gradientWeight = 0;

    float distance;
    evaluateRasterShape(
        abs(type), radius, totalRadius, params,
        worldPosition, a, b, c,
        distance, tl, br,
        gradientType, gradientWeight
    );

    float result = getWindowAlpha(
        distance, shadowEndDistance - 0.5, shadowEndDistance + ShadowSoftness,
        1, 1, 0
    );
    return result;
}

void rasterShapeCommon (
    in float4 worldPositionTypeAndWorldSpace,
    in float4 ab, in float4 cd,
    in float4 params, in float4 params2,
    in float4 centerColor, in float4 edgeColor, in float2 vpos,
    out float2 tl, out float2 br,
    out float4 fill, out float fillAlpha, 
    out float outlineAlpha, out float shadowAlpha
) {
    float2 worldPosition = worldPositionTypeAndWorldSpace.xy;
    int type = abs(worldPositionTypeAndWorldSpace.z);
    float2 a = ab.xy, b = ab.zw, c = cd.xy, radius = cd.zw;
    float outlineSize = abs(params.x);
    bool HardOutline = (params.x >= 0);
    float OutlineGammaMinusOne = params.w;

    // HACK
    outlineSize = max(abs(outlineSize), 0.0001);

    const float threshold = (1 / 512.0);

    float totalRadius = computeTotalRadius(radius, outlineSize);
    float2 invRadius = 1.0 / max(radius, 0.0001);

    float distance = 0, gradientWeight = 0;

    tl = min(a, b);
    br = max(a, b);

    float gradientOffset = params2.z, gradientSize = params2.w, gradientAngle;
    int gradientType;

    if (params.z >= ANGULAR_GRADIENT_BASE) {
        gradientType = GRADIENT_TYPE_Angular;
        gradientAngle = (params.z - ANGULAR_GRADIENT_BASE) * DEG_TO_RAD;
    } else {
        gradientType = abs(trunc(params.z));
        gradientAngle = 0;
    }

    evaluateRasterShape(
        type, radius, totalRadius, params,
        worldPosition, a, b, c,
        distance, tl, br,
        gradientType, gradientWeight
    );

    gradientWeight = evaluateGradient(
        gradientType, gradientAngle, gradientWeight, 
        worldPosition, tl, br, radius.x
    );

    gradientWeight += gradientOffset;

    if (gradientSize > 0) {
        gradientWeight = saturate(gradientWeight / gradientSize);
    } else {
        gradientSize = max(abs(gradientSize), 0.0001);
        gradientWeight /= gradientSize;
        gradientWeight = gradientWeight % 2;
        gradientWeight = 1 - abs(gradientWeight - 1);
    }

    gradientWeight = saturate(pow(gradientWeight, max(params2.x, 0.001)));
    gradientWeight = 1 - saturate(pow(1 - gradientWeight, max(params2.y, 0.001)));

    float outlineSizeAlpha = saturate(outlineSize / 2);
    float clampedOutlineSize = max(outlineSize / 2, sqrt(2)) * 2;

    float outlineStartDistance = -(outlineSize * 0.5) + 0.5, 
        outlineEndDistance = outlineStartDistance + outlineSize,
        fillStartDistance = -0.5,
        fillEndDistance = 0.5;
    
    float annularRadius = params.y;
    if (annularRadius > 0.001)
        distance = abs(distance) - annularRadius;

    fillAlpha = getWindowAlpha(distance, fillStartDistance, fillEndDistance, 1, 1, 0);
    fill = lerp(centerColor, edgeColor, gradientWeight);

    PREFER_BRANCH
    if (outlineSize > 0.001) {
        if ((outlineSize >= sqrt(2)) && HardOutline) {
            outlineAlpha = (
                getWindowAlpha(distance, outlineStartDistance, min(outlineStartDistance + sqrt(2), outlineEndDistance), 0, 1, 1) *
                getWindowAlpha(distance, max(outlineStartDistance, outlineEndDistance - sqrt(2)), outlineEndDistance, 1, 1, 0)
            );
        } else {
            outlineAlpha = getWindowAlpha(distance, outlineStartDistance, outlineEndDistance, 0, 1, 0) * outlineSizeAlpha;
        }
        float outlineGamma = OutlineGammaMinusOne + 1;
        outlineAlpha = saturate(pow(outlineAlpha, outlineGamma));
    } else {
        outlineAlpha = 0;
    }

    shadowAlpha = computeShadowAlpha(
        type, radius, totalRadius, params,
        worldPosition - ShadowOffset, a, b, c,
        max(outlineEndDistance, fillEndDistance)
    );
    // HACK: We don't want to composite the fill on top of the shadow (this will look awful if the fill is semiopaque),
    //  but it makes some amount of sense to allow blending outline and shadow pixels.
    // This is not going to be 100% right for fills without outlines...
    shadowAlpha = saturate (shadowAlpha - (fillAlpha * ShadowFillSuppression));
}

// porter-duff A over B
float4 over (float4 top, float topOpacity, float4 bottom, float bottomOpacity) {
    top *= topOpacity;
    bottom *= bottomOpacity;

    float3 rgb = top.rgb + (bottom.rgb * (1 - top.a));
    float a = top.a + (bottom.a * (1 - top.a));
    return float4(rgb, a);
}

float4 composite (float4 fillColor, float4 outlineColor, float fillAlpha, float outlineAlpha, float shadowAlpha, bool convertToSRGB, float2 vpos) {
    float4 result;
    result = over(outlineColor, outlineAlpha, fillColor, fillAlpha);
    result = over(result, 1, ShadowColorLinear, shadowAlpha);

    result.rgb = float4(result.rgb / max(result.a, 0.0001), result.a);

    if (convertToSRGB)
        result.rgb = LinearToSRGB(result.rgb);

    return ApplyDither4(result, vpos);
}

float4 texturedShapeCommon (
    in float2 worldPosition, in float4 texRgn,
    in float4 ab, in float4 cd,
    in float4 fill, in float4 outlineColor,
    in float fillAlpha, in float outlineAlpha, in float shadowAlpha,
    in float4 params, in float4 params2, in float2 tl, in float2 br,
    in float2 vpos
) {
    // HACK: Increasing the radius of shapes like rectangles
    //  causes the shapes to expand, so we need to expand the
    //  rectangular area the texture is being applied to
    tl -= cd.zw;
    br += cd.zw;

    float2 sizePx = br - tl;
    float2 posRelative = worldPosition - tl;
    float2 texSize = (texRgn.zw - texRgn.xy);
    
    float2 texCoord = ((posRelative / sizePx) * texSize) + texRgn.xy;
    texCoord = clamp(texCoord, texRgn.xy, texRgn.zw);

    float4 texColor = tex2D(TextureSampler, texCoord);
    if (BlendInLinearSpace)
        texColor = pSRGBToPLinear(texColor);

    fill *= texColor;

    float4 result = composite(fill, outlineColor, fillAlpha, outlineAlpha, shadowAlpha, BlendInLinearSpace, vpos);
    return result;
}