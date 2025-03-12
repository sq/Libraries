#pragma warning ( disable: 3571 )

#ifndef BACKGROUND_MIP_BIAS
#define BACKGROUND_MIP_BIAS -0.5
#endif

#ifndef VARIANT_SIMPLE
#define VARIANT_SIMPLE 0
#endif

#define ENABLE_DITHERING 1

#include "CompilerWorkarounds.fxh"
#include "ViewTransformCommon.fxh"
#include "TargetInfo.fxh"
#include "DitherCommon.fxh"
#include "sRGBCommon.fxh"
#include "FormatCommon.fxh"
#include "SDF2D.fxh"
#include "BezierCommon.fxh"
#include "RasterComposites.fxh"

#if VARIANT_TEXTURED
Texture2D RasterTexture : register(t0);

sampler TextureSampler : register(s0) {
    Texture = (RasterTexture);
};

uniform const float2 TextureSizePx;

// Mode, ScaleX, ScaleY
uniform const float4 TextureModeAndSize;
// Origin, Position
uniform const float4 TexturePlacement;
uniform const float4 TextureTraits <string traitsOf="RasterTexture";>;
// Saturation, Brightness, Clamp X, Clamp Y
uniform const float4 TextureOptions;
#endif

// A bunch of the distance formulas in here are thanks to inigo quilez
// http://iquilezles.org/www/articles/distfunctions2d/distfunctions2d.htm
// http://iquilezles.org/www/articles/distfunctions/distfunctions.htm


#define RASTERSHAPE_FS_ARGS \
    in float4 worldPositionTypeAndWorldSpace : NORMAL0, \
    in float4 ab : TEXCOORD3, \
    in float4 cd : TEXCOORD4, \
    in float4 params : TEXCOORD0, \
    in float4 params2 : TEXCOORD1, \
    in float4 texRgn : TEXCOORD2, \
    in float4 params3 : TEXCOORD6, \
    in float4 centerColor : COLOR0, \
    in float4 edgeColor : COLOR1, \
    in float4 outlineColor : COLOR2, \
    ACCEPTS_VPOS, \
    out float4 result : COLOR0

uniform const bool BlendInLinearSpace, OutputInLinearSpace, BlendInOkLab;

// offsetx, offsety, softness, fillSuppression
uniform const float4 ShadowOptions;
// expansion, inside
uniform const float4 ShadowOptions2;
uniform const float4 ShadowColorLinear;

#define ShadowSoftness ShadowOptions.z
#define ShadowOffset ShadowOptions.xy
#define ShadowFillSuppression ShadowOptions.w
#define ShadowExpansion ShadowOptions2.x
#define ShadowInside (ShadowOptions2.y > 0.5)

float4 TransformPosition(float4 position, bool unused) {
    // Transform to view space, then offset by half a pixel to align texels with screen pixels
    float4 modelViewPos = mul(position, Viewport.ModelView);
    // Finally project after offsetting
    return mul(modelViewPos, Viewport.Projection);
}

// Quaternion multiplication
// http://mathworld.wolfram.com/Quaternion.html
float4 qmul(float4 q1, float4 q2)
{
    return float4(
        q2.xyz * q1.w + q1.xyz * q2.w + cross(q1.xyz, q2.xyz),
        q1.w * q2.w - dot(q1.xyz, q2.xyz)
    );
}

// http://mathworld.wolfram.com/Quaternion.html
float4 rotateLocalPosition(float4 localPosition, float4 rotation)
{
    float4 r_c = rotation * float4(-1, -1, -1, 1);
    return float4(qmul(rotation, qmul(float4(localPosition.xyz, 0), r_c)).xyz, localPosition.w);
}

void adjustTLBR (
    inout float2 tl, inout float2 br, float4 params
) {
    float annularRadius = params.y;
    if (annularRadius > 0) {
        tl -= annularRadius;
        br += annularRadius;
    }

    if (!ShadowInside) {
        float halfSoftness = (ShadowSoftness * 0.6) + ShadowExpansion;
        tl -= halfSoftness;
        br += halfSoftness;
        if (ShadowOffset.x >= 0)
            br.x += ShadowOffset.x;
        else 
            tl.x += ShadowOffset.x;

        if (ShadowOffset.y >= 0)
            br.y += ShadowOffset.y;
        else
            tl.y += ShadowOffset.y;
    }
}

void computeTLBR (
    int type, float2 radius, float outlineSize, float4 params,
    float2 a, float2 b, float2 c,
    out float2 tl, out float2 br, bool expandBoxForComposites
) {
    type = EVALUATE_TYPE;

    tl = br = 0;

    if (false) {
    }

#ifdef INCLUDE_ELLIPSE
    else if (type == TYPE_Ellipse) {
        tl = a - b - outlineSize;
        br = a + b + outlineSize;
    }
#endif

#ifdef INCLUDE_LINE
    else if (type == TYPE_LineSegment) {
        tl = min(a, b);
        br = max(a, b);
    }
#endif

#ifdef INCLUDE_RECTANGLE
    else if (type == TYPE_Rectangle) {
        tl = min(a, b) - outlineSize;
        br = max(a, b) + outlineSize;
    }
#endif

#ifdef INCLUDE_TRIANGLE
    else if (type == TYPE_Triangle) {
        outlineSize += 1;
        tl = min(min(a, b), c) - outlineSize;
        br = max(max(a, b), c) + outlineSize;
    }
#endif

#ifdef INCLUDE_BEZIER
    else if (type == TYPE_QuadraticBezier) {
        outlineSize += 1;

        computeTLBR_Bezier(a, b, c, tl, br);

        tl -= outlineSize + radius.x;
        br += outlineSize + radius.x;
    }
#endif

#ifdef INCLUDE_ARC
    else if (type == TYPE_Arc) {
        tl = a - outlineSize - radius.x - radius.y;
        br = a + outlineSize + radius.x + radius.y;
    }
#endif

#ifdef INCLUDE_STAR
    else if (type == TYPE_Star) {
        tl = a - outlineSize - radius.x;
        br = a + outlineSize + radius.x;
    }
#endif

#ifdef INCLUDE_POLYGON
    else if (type == TYPE_Polygon) {
        computeTLBR_Polygon(
            radius, outlineSize, params,
            a.x, a.y, b.x, c, tl, br
        );
    }
#endif
    
#ifdef INCLUDE_COMPOSITES
    computeTLBR_Composite(
        expandBoxForComposites, tl, br
    );
#endif
}

void computeHullCorners (
    in int type, in float index,
    float2 c, float2 radius, float outlineSize,
    inout float2 tl, inout float2 br
) {
    float2 _tl = tl, _br = br;
    float edgeSize = max(
        max(radius.x, radius.y),
        max(c.x, c.y)
    ) + (outlineSize * 2);

    edgeSize += (length(ShadowOffset) + ShadowSoftness) * 0.6 + ShadowExpansion;

    float2 offsetPositive = max(0, ShadowOffset),
        offsetNegative = min(0, ShadowOffset);
    float x1 = min(br.x, tl.x + edgeSize + offsetPositive.x);
    float x2 = max(tl.x, br.x - edgeSize + offsetNegative.x);
    float y1 = min(br.y, tl.y + edgeSize + offsetPositive.y);
    float y2 = max(tl.y, br.y - edgeSize + offsetNegative.y);

    if (index < 1) {
    } else if (index < 2) {
        // top
        br.y = y1;
    } else if (index < 3) {
        // left
        tl.y = y1;
        br.y = y2;
        br.x = x1;
    } else if (index < 4) {
        // right
        tl.y = y1;
        br.y = y2;
        tl.x = x2;
    } else {
        // bottom
        tl.y = y2;
    }
}

void computePosition (
    int type, float outlineSize, 
    float2 a, float2 b, float2 c, float2 radius,
    float2 tl, float2 br, float4 cornerWeights,
    float4 params, bool isSimpleRectangle,
    out float2 xy
) {
    xy = 0;

    if (type == TYPE_LineSegment) {
#ifdef INCLUDE_LINE
        float totalRadius = radius.x;

        // HACK: Too hard to calculate precise offsets here so just pad it out.
        // FIXME: This is bad for performance!
        if (!ShadowInside)
            totalRadius += (length(ShadowOffset) + ShadowSoftness) * 0.6 + ShadowExpansion;

        totalRadius += outlineSize;

        float annularRadius = params.y;
        totalRadius += annularRadius;

        // HACK
        totalRadius += 1;

        // Oriented bounding box around the line segment
        float2 along = b - a,
            alongNorm = normalize(along) * (totalRadius + 1),
            left = alongNorm.yx * float2(-1, 1),
            right = alongNorm.yx * float2(1, -1);

        // FIXME
        xy = lerp(a - alongNorm, b + alongNorm, cornerWeights.x) + lerp(left, right, cornerWeights.y);
#endif
    } else if (type == TYPE_Triangle) {
#ifdef INCLUDE_TRIANGLE
        adjustTLBR(tl, br, params);
        // HACK: Rounding triangles makes their bounding box expand for some reason
        tl -= (1 + radius.x * 0.33); br += (1 + radius.x * 0.33);
        // FIXME: Fit a better hull around triangles. Oriented bounding box?
        xy = lerp(tl, br, cornerWeights.xy);
#endif
    } else {
        adjustTLBR(tl, br, params);
        // HACK: Padding
        tl -= 1; br += 1;
        if (isSimpleRectangle)
            computeHullCorners(type, cornerWeights.w, c, radius, outlineSize, tl, br);
        // FIXME: Fit a better hull around triangles. Oriented bounding box?
        xy = lerp(tl, br, cornerWeights.xy);
    }
}

void RasterShapeVertexShader_Core (
    in bool isSimple,
    in float4 cornerWeights,
    in float4 ab_in,
    in float4 cd_in,
    // outline size (negative for soft outline), annular radius, fill mode, outline gamma minus one
    inout float4 params,
    // gradient power, fill range 1, fill range 2, fill offset
    inout float4 params2,
    inout float4 texRgn,
    inout float4 centerColor,
    inout float4 edgeColor,
    inout float4 outlineColor,
    in  int2 typeAndWorldSpace,
    out float4 result,
    out float4 ab,
    out float4 cd,
    out float4 worldPositionTypeAndInteriorFlag,
    in  float4 orientation
) {
    int type = abs(typeAndWorldSpace.x);
    type = EVALUATE_TYPE ;

    params.x *= OutlineSizeCompensation;
    float outlineSize = abs(params.x);

    bool isUnshadowed = (ShadowColorLinear.a <= 0) || (ShadowInside <= 0);
    bool isSimpleRectangle = all(centerColor == edgeColor) && 
        (type == TYPE_Rectangle) && 
        (params.y <= 0); /* FIXME: Annular radius */
    bool isHollow = isSimpleRectangle && 
        (centerColor.a <= 0) &&
        // HACK: If a rectangle's outline is big enough, it can cause the hollow optimization
        //  to produce overlapping halves, and then we get overdraw at the intersection
        // FIXME: Is this a problem for shadows too?
        (abs(ab_in.z - ab_in.x) > (outlineSize * 2)) &&
        (abs(ab_in.w - ab_in.y) > (outlineSize * 2));

    bool dead;
    if (isHollow) {
        dead = cornerWeights.w < 1;
    } else {
        dead = cornerWeights.w >= 1;
    }

    if (dead) {
        result = -9999;
        ab = cd = worldPositionTypeAndInteriorFlag = 0;
        return;
    }

    ab = ab_in; cd = cd_in;
    float4 position = float4(ab_in.x, ab_in.y, 0, 1);
    float2 a = ab.xy, b = ab.zw, c = cd.xy, radius = cd.zw;

    float2 tl, br;

    computeTLBR(type, radius, outlineSize, params, a, b, c, tl, br, true);
    computePosition(type, outlineSize, a, b, c, radius, tl, br, cornerWeights, params, isSimpleRectangle, position.xy);

    float2 adjustedPosition = position.xy;
    if (typeAndWorldSpace.y > 0.5) {
        adjustedPosition -= GetViewportPosition().xy;
        adjustedPosition *= GetViewportScale().xy;
    }
    
    float4 centering = float4((tl + br) * 0.5, 0, 0);
    float4 orientedPosition = float4(adjustedPosition, position.z, 1);
    orientedPosition = rotateLocalPosition(orientedPosition - centering, orientation) + centering;
    // HACK
    orientedPosition.z = position.z;

    result = TransformPosition(
        orientedPosition, true
    );
    worldPositionTypeAndInteriorFlag = float4(
        position.xy, typeAndWorldSpace.x, 
        isSimpleRectangle && isUnshadowed && (cornerWeights.w < 1)
    );

    // We use the _Accurate conversion function here because the approximation introduces
    //  visible noise for values like (64, 64, 64) when they are dithered.
    // We do the initial conversion in the VS to avoid paying the cost per-fragment, and also
    //  take the opportunity to do a final conversion here for 'simple' fragments so that
    //  it doesn't have to be done per-fragment
    // FIXME: Is this reasonably correct for simple shapes with outlines? Probably
    if (
        (OutputInLinearSpace && isSimple) || 
        (BlendInLinearSpace && !isSimple) ||
        BlendInOkLab
    ) {
        REQUIRE_BRANCH
        if (BlendInOkLab) {
            // HACK: This isn't premultiplied since it doesn't make sense for it to be!
            centerColor = pSRGBToOkLab(centerColor);
            edgeColor = pSRGBToOkLab(edgeColor);
            // HACK: Doing compositing in oklab space is a nightmare, so we won't
            // outlineColor = pSRGBToOkLab(outlineColor);
            outlineColor = pSRGBToPLinear_Accurate(outlineColor);
        } else {
            centerColor = pSRGBToPLinear_Accurate(centerColor);
            edgeColor = pSRGBToPLinear_Accurate(edgeColor);
            outlineColor = pSRGBToPLinear_Accurate(outlineColor);
        }
    }
}

void RasterShapeVertexShader (
    in float4 cornerWeights : NORMAL2,
    in float4 ab_in : POSITION0,
    in float4 cd_in : POSITION1,
    inout float4 params : TEXCOORD0,
    inout float4 params2 : TEXCOORD1,
    inout float4 params3 : TEXCOORD6,
    inout float4 texRgn : TEXCOORD2,
    inout float4 centerColor : COLOR0,
    inout float4 edgeColor : COLOR1,
    inout float4 outlineColor : COLOR2,
    in    float4 orientation : TEXCOORD5,
    in  int2 typeAndWorldSpace : BLENDINDICES1,
    out float4 result : POSITION0,
    out float4 ab : TEXCOORD3,
    out float4 cd : TEXCOORD4,
    out float4 worldPositionTypeAndInteriorFlag : NORMAL0
) {
    RasterShapeVertexShader_Core (
        VARIANT_SIMPLE != 0,
        cornerWeights,
        ab_in,
        cd_in,
        params,
        params2,
        texRgn,
        centerColor,
        edgeColor,
        outlineColor,
        typeAndWorldSpace,
        result,
        ab,
        cd,
        worldPositionTypeAndInteriorFlag,
        orientation
    );
}

void RasterShapeVertexShader_Simple (
    in float4 cornerWeights : NORMAL2,
    in float4 ab_in : POSITION0,
    in float4 cd_in : POSITION1,
    inout float4 params : TEXCOORD0,
    inout float4 params2 : TEXCOORD1,
    inout float4 params3 : TEXCOORD6,
    inout float4 texRgn : TEXCOORD2,
    inout float4 centerColor : COLOR0,
    inout float4 edgeColor : COLOR1,
    inout float4 outlineColor : COLOR2,
    in    float4 orientation : TEXCOORD5,
    in  int2 typeAndWorldSpace : BLENDINDICES1,
    out float4 result : POSITION0,
    out float4 ab : TEXCOORD3,
    out float4 cd : TEXCOORD4,
    out float4 worldPositionTypeAndInteriorFlag : NORMAL0
) {
    RasterShapeVertexShader_Core (
        true,
        cornerWeights,
        ab_in,
        cd_in,
        params,
        params2,
        texRgn,
        centerColor,
        edgeColor,
        outlineColor,
        typeAndWorldSpace,
        result,
        ab,
        cd,
        worldPositionTypeAndInteriorFlag,
        orientation
    );
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
    in float2 worldPosition, in float2 a, in float2 b, in float2 c, in bool simple,
    out float distance, out float2 tl, out float2 br,
    inout int gradientType, out float2 gradientWeight
) {
    // FIXME: sdEllipse is massively broken. What is wrong with it?
    b = abs(b);
    distance = sdEllipse(worldPosition - a, b);
    tl = a - b;
    br = a + b;
    
    // FIXME: This is wrong for 'along' gradient mode. But ellipses probably shouldn't use it.
    evaluateComposites(worldPosition, false, distance, tl, br);

    float distanceF = distance / ((b.x + b.y) * 0.5);

    if (simple) {
        gradientWeight = 0;
        return;
    }

    float fakeY = worldPosition.x + worldPosition.y; // FIXME
    // HACK: Sadly the distance-derived gradient looks kind of strange,
    //  so it's still best to force our custom Radial
    gradientWeight = float2(1 - saturate(-distanceF), fakeY);
    if (gradientType == GRADIENT_TYPE_Natural)
        gradientType = GRADIENT_TYPE_Radial;
        
    /*
    if (
        (gradientType == GRADIENT_TYPE_Radial) ||
        (gradientType == GRADIENT_TYPE_Radial_Enclosing) ||
        (gradientType == GRADIENT_TYPE_Radial_Enclosed)
    ) {
        float2 radialSize = (gradientType == GRADIENT_TYPE_Radial_Enclosing)
            ? max(b.x, b.y)
            : (
                (gradientType == GRADIENT_TYPE_Radial_Enclosed)
                    ? min(b.x, b.y)
                    : b
            ),
            center = a + (c * b),
            dist = worldPosition - center,
            distScaled = dist / radialSize;
        gradientWeight = length(distScaled);        
        gradientType = GRADIENT_TYPE_Other;
    } else if (
        (gradientType == GRADIENT_TYPE_Linear_Enclosing) ||
        (gradientType == GRADIENT_TYPE_Linear_Enclosed)
    ) {
        // Options:
        // * 2 = touches corners of a box enclosing the ellipse
        // * 2 * sqrt(2) == touches corners of a box enclosed by the ellipse
        float2 distance2 = abs(worldPosition - a + (c * b)) / (br - tl) * (
            (gradientType == GRADIENT_TYPE_Linear_Enclosed) 
                ? (2 * sqrt(2)) 
                : 2
        );
        gradientWeight = float2(saturate(max(distance2.x, distance2.y)), fakeY);
        gradientType = GRADIENT_TYPE_Other;
    }
    */
}

void evaluateLineSegment (
    in float2 worldPosition, in float2 a, in float2 b, in float2 c,
    in float2 radius, out float distance,
    inout int gradientType, out float2 gradientWeight,
    inout float2 tl, inout float2 br
) {
    float t;
    float2 closestPoint = closestPointOnLineSegment2(a, b, worldPosition, t),
        distance2 = worldPosition - closestPoint;
    float localRadius = radius.x + lerp(c.y, radius.y, t);
    distance = length(distance2) - localRadius;

    evaluateComposites(worldPosition, false, distance, tl, br);

    if (VARIANT_SIMPLE) {
        gradientWeight = 0;
        return;
    }

    float distanceWeight = 1 - saturate(-distance / localRadius);
    
    PREFER_FLATTEN
    if (gradientType == GRADIENT_TYPE_Along) {
        gradientWeight = float2(saturate(t), distanceWeight);
        gradientType == GRADIENT_TYPE_Other;
    } else
        gradientWeight = float2(distanceWeight, saturate(t));
}

void evaluateBezier (
    in float2 worldPosition, in float2 a, in float2 b, in float2 c,
    in float2 radius, out float distance,
    inout int gradientType, out float2 gradientWeight,
    inout float2 tl, inout float2 br
) {
    distance = sdBezier(worldPosition, a, b, c) - radius.x;
    evaluateComposites(worldPosition, false, distance, tl, br);

    if (VARIANT_SIMPLE) {
        gradientWeight = 0;
        return;
    }
    
    REQUIRE_BRANCH
    float fakeY = 0; // FIXME
    if (gradientType == GRADIENT_TYPE_Along) {
        // Translating the control points so worldPosition is 0 simplifies all the math
        a -= worldPosition;
        b -= worldPosition;
        c -= worldPosition;
        // First generate a reasonable approximation using a reliable brute force approach
        float ct = 0, cd = distanceSquaredToBezierAtT(a, b, c, ct), step = 0.05;
        // HACK: Also test one step past the end of the time window, because if
        //  the bezier has a radius > 0 we can end up shading pixels with t > 1
        REQUIRE_LOOP
        for (float tt = ct + step; tt <= (1 + step); tt += step) {
            float td = distanceSquaredToBezierAtT(a, b, c, tt);
            pickClosestT(cd, ct, td, tt);
        }
        // Attempt to use precise bezier math to come up with a more accurate location
        // This math produces very glitchy results if the control point b is near to the
        //  line connecting a and c, because the bezier overlaps itself in that scenario
        pickClosestTOnBezierForAxis(a, b, c, float2(1, 0), cd, ct);
        pickClosestTOnBezierForAxis(a, b, c, float2(0, 1), cd, ct);
        gradientWeight = float2(saturate(ct), fakeY);
        gradientType = GRADIENT_TYPE_Other;
    } else
        gradientWeight = float2(1 - saturate(-distance / radius.x), fakeY);
}

float computeLocalRectangleRadius (
    in float2 worldPosition, 
    in float2 a, in float2 b, 
    in float2 radiusTLTR, in float2 radiusBRBL
) {
    // Smoothly interpolate the transition between radius values over the boundary
    //  region, because otherwise the immediate jump can cause very weird discontinuities
    float2 radiusWeight = saturate((
        (
            (worldPosition - a) / max((b - a), 0.001)
        ) - 0.5) * 4
    );
    return lerp(
        lerp(radiusTLTR.x, radiusTLTR.y, radiusWeight.x),
        lerp(radiusBRBL.y, radiusBRBL.x, radiusWeight.x),
        radiusWeight.y
    );
}

void evaluateRectangle (
    in float2 worldPosition, in float2 a, in float2 b, in float radius, 
    out float distance, inout int gradientType, out float2 gradientWeight,
    inout float2 tl, inout float2 br
) {
    float2 center = (a + b) * 0.5;
    float2 boxSize = abs(b - a) * 0.5;
    float2 localPosition = worldPosition - center;
    
    float2 q = abs(localPosition) - boxSize + radius;
    distance = min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - radius;
    gradientWeight = 0;

    PREFER_FLATTEN
    if (gradientType == GRADIENT_TYPE_Natural)
        gradientType = GRADIENT_TYPE_Linear;
    
    evaluateComposites(worldPosition, false, distance, tl, br);
}

#ifdef INCLUDE_TRIANGLE
void evaluateTriangle (
    in float2 worldPosition, in float2 a, in float2 b, in float2 c,
    in float2 radius, out float distance,
    inout int gradientType, out float2 gradientWeight,
    out float2 tl, out float2 br
) {
    // FIXME: This function makes fxc compiles INCREDIBLY slow.

    // length of the side opposite the vertex (between its neighbors)
    float sa = length(b - c), sb = length(c - a), sc = length(b - a);
    float perimeter = max(sa + sb + sc, 0.001);
    float2 incenter = ((sa * a) + (sb * b) + (sc * c)) / perimeter;

    // HACK: Subtracting radius for rounding expands the triangle, so compensate by shrinking the endpoints in towards the center
    // FIXME: This grows outside the bounding box sometimes?
    float2 ra = a, rb = b, rc = c;
    rb -= normalize(b - incenter) * radius.x;
    ra -= normalize(a - incenter) * radius.x;
    rc -= normalize(c - incenter) * radius.x;

    distance = sdTriangle(worldPosition, ra, rb, rc);
    distance -= radius.x;

    // FIXME: Not quite right, the center of the fill expands a bit
    float targetDistance = sdTriangle(incenter, a, b, c);

    tl = min(min(a, b), c);
    br = max(max(a, b), c);

    if (VARIANT_SIMPLE) {
        evaluateComposites(worldPosition, false, distance, tl, br);
        gradientWeight = 0;
        return;
    }

    // FIXME: Recenter non-natural gradients around our incenter instead of the
    //  center of our bounding box

    PREFER_FLATTEN
    if (gradientType == GRADIENT_TYPE_Natural) {
        float2 cdist = normalize(worldPosition - incenter);
        float angleCw = atan2(cdist.y, cdist.x) + PI; // [0, 2 * PI]
        gradientWeight = float2(1 - saturate(distance / targetDistance), angleCw / (2 * PI));
        gradientType = GRADIENT_TYPE_Other;
    } else {
        gradientWeight = 0;
    }

    // HACK: Evaluating composites late fixes natural gradients for triangles in subtract/intersection mode
    evaluateComposites(worldPosition, false, distance, tl, br);
}
#endif

float2 evaluateGradient (
    int gradientType, float gradientAngle, float2 gradientWeight,
    float2 worldPosition, float2 tl, float2 br, float radius,
    float4 params3
) {
    float2 gradientCenter = lerp(tl, br, params3.xy);
    float2 boxSize = (br - tl);
    boxSize = max(abs(boxSize), 0.001) * sign(boxSize);
    float2 radialSize2 = (boxSize /* + (radius * 2) */) * 0.5;

    PREFER_FLATTEN
    if (
        (gradientType == GRADIENT_TYPE_Natural) ||
        (gradientType == GRADIENT_TYPE_Other)
    ) {
        ;
    } else if (
        (gradientType == GRADIENT_TYPE_Linear) ||
        (gradientType == GRADIENT_TYPE_Linear_Enclosing) ||
        (gradientType == GRADIENT_TYPE_Linear_Enclosed)
    ) {
        float2 linearRadialSize2 = (gradientType == GRADIENT_TYPE_Linear_Enclosing)
            ? max(radialSize2.x, radialSize2.y)
            : (
                (gradientType == GRADIENT_TYPE_Linear_Enclosed)
                    ? min(radialSize2.x, radialSize2.y)
                    : radialSize2
            );
        float2 linearDist2 = abs(worldPosition - gradientCenter) / linearRadialSize2;
        gradientWeight.x = max(linearDist2.x, linearDist2.y);
    } else if (
        (gradientType == GRADIENT_TYPE_Radial) ||
        (gradientType == GRADIENT_TYPE_Radial_Enclosing) ||
        (gradientType == GRADIENT_TYPE_Radial_Enclosed)
    ) {
        float2 radialSize = (gradientType == GRADIENT_TYPE_Radial_Enclosing)
            ? max(radialSize2.x, radialSize2.y)
            : (
                (gradientType == GRADIENT_TYPE_Radial_Enclosed)
                    ? min(radialSize2.x, radialSize2.y)
                    : radialSize2
            );
        gradientWeight.x = length((worldPosition - gradientCenter) / max(radialSize, 0.0001));
    } else if (
        (gradientType >= GRADIENT_TYPE_Angular)
    ) {
        float2 scaled = (worldPosition - gradientCenter) / (boxSize * 0.5);
        float scaledLength = length(scaled);
        if (scaledLength < 0.001) {
            scaled.x = scaled.y = 0.001;
            scaledLength = 0.001;
        }
        if (gradientType >= GRADIENT_TYPE_Conical) {
            // atan2 is typically counter-clockwise and starts on the left, so (-1, 0) is 0rad.
            // We want a clockwise gradient starting at the top.
            float angleCw = atan2(-scaled.x, scaled.y) + PI; // [0, 2 * PI]
            float angleCwScaled = (angleCw + ((2 * PI) - gradientAngle)) / (2 * PI);
            gradientWeight.x = angleCwScaled % 1;
            gradientWeight.y = scaled.y; // FIXME
        } else {
            float2 sc;
            sincos(gradientAngle + (PI / 2), sc.x, sc.y);
            float tan2 = atan2(scaled.y, scaled.x);
            gradientWeight.x = ((sin(gradientAngle + tan2) * scaledLength) / 2) + 0.5;
            gradientWeight.y = dot(sc, scaled);
        }
    }

    return gradientWeight;
}

void evaluateRasterShape (
    int type, float2 radius, float outlineSize, float4 params,
    in float2 worldPosition, in float2 a, in float2 b, in float2 c, in bool simple,
    out float distance, inout float2 tl, inout float2 br,
    inout int gradientType, out float2 gradientWeight, inout float gradientAngle
) {
    type = EVALUATE_TYPE;
    bool needTLBR = false;

    distance = 0;
    gradientWeight = 0;

    if (false) {
    }

#ifdef INCLUDE_ELLIPSE
    else if (type == TYPE_Ellipse) {
        evaluateEllipse(
            worldPosition, a, b, c, simple,
            distance, tl, br,
            gradientType, gradientWeight
        );
    }
#endif

#ifdef INCLUDE_LINE
    else if (type == TYPE_LineSegment) {
        evaluateLineSegment(
            worldPosition, a, b, c,
            radius, distance,
            gradientType, gradientWeight,
            tl, br
        );
        needTLBR = true;
    }
#endif

#ifdef INCLUDE_BEZIER
    else if (type == TYPE_QuadraticBezier) {
        evaluateBezier(
            worldPosition, a, b, c,
            radius, distance,
            gradientType, gradientWeight,
            tl, br
        );

        computeTLBR(type, radius, outlineSize, params, a, b, c, tl, br, false);
    }
#endif

#ifdef INCLUDE_RECTANGLE
    else if (type == TYPE_Rectangle) {
        evaluateRectangle(
            worldPosition, a, b, radius.x, 
            distance, gradientType, gradientWeight,
            tl, br
        );
    }
#endif

#ifdef INCLUDE_TRIANGLE
    else if (type == TYPE_Triangle) {
        evaluateTriangle(
            worldPosition, a, b, c,
            radius, distance,
            gradientType, gradientWeight, 
            tl, br
        );
    }
#endif

#ifdef INCLUDE_ARC
    else if (type == TYPE_Arc) {
        float2 arcB, arcC, triA, triB;
        sincos(b.x, arcB.x, arcB.y);
        sincos(b.y, arcC.x, arcC.y);
        float triBaseAngle = (-b.x - b.y) + (PI * 0.5);
        sincos(triBaseAngle + (b.y * 2), triA.x, triA.y);
        sincos(triBaseAngle, triB.x, triB.y);
        distance = sdArc(worldPosition - a, arcB, arcC, radius.x, radius.y);
        // FIXME: this SDF is defined in a useless way AND has artifacts
        // float hardDistance = sdPie(worldPosition - a, arcB, radius.x + radius.y);
        float distant = 99999.0;
        float2 triangleA = a,
            triangleB = triangleA + (triA * distant),
            triangleC = triangleA + (triB * distant);
        float hardDistance = sdTriangle(worldPosition, triangleA, triangleB, triangleC);
        if (b.y >= (PI * 0.5))
            hardDistance = -hardDistance;
        distance = lerp(distance, max(distance, hardDistance), c.y);
        float fakeY = 0; // FIXME
        if (!simple) {
            if (gradientType == GRADIENT_TYPE_Natural) {
                gradientWeight = float2(1 - saturate(-distance / radius.y), fakeY);
            } else if (gradientType == GRADIENT_TYPE_Along) {
                gradientType = GRADIENT_TYPE_Conical;
                // FIXME: Size scaling
                gradientAngle = c.x;
            }
        }

        needTLBR = true;
    }
#endif

#ifdef INCLUDE_STAR
    else if (type == TYPE_Star) {
        float2 starPosition = worldPosition - a;
        float twirl = smoothstep(0, radius.x, length(starPosition));
        starPosition = rotate2D(starPosition, twirl * c.x);
        float taper = radius.y * smoothstep(0, radius.x, length(starPosition));
        float m = clamp(b.y + taper, 2, b.x);
        distance = sdStar(starPosition, radius.x, (int)b.x, m);
        // HACK: it doesn't evaluate properly at 0,0
        float targetDistance = sdStar(float2(0, -0.5), radius.x, (int)b.x, b.y);

        if (!simple) {
            if (gradientType == GRADIENT_TYPE_Natural) {
                float2 cdist = normalize(worldPosition - a);
                float angleCw = atan2(cdist.y, cdist.x) + PI; // [0, 2 * PI]
                gradientWeight = float2(1 - saturate(distance / targetDistance), angleCw / (2 * PI));
            }
        }

        needTLBR = true;
    }
#endif

#ifdef INCLUDE_POLYGON
    else if (type == TYPE_Polygon) {
        evaluatePolygon(
            radius, outlineSize, params,
            worldPosition - c, a.x, a.y, b.x, simple,
            distance, tl, br,
            gradientType, gradientWeight, gradientAngle
        );
        tl += c;
        br += c;
    }
#endif
    
    float annularRadius = params.y;
    if (annularRadius > 0.001)
        distance = abs(distance) - annularRadius;
    if (needTLBR)
        computeTLBR(type, radius, outlineSize, params, a, b, c, tl, br, false);
}

float computeShadowAlpha (
    int type, float2 radius, float4 params,
    float2 worldPosition, float2 a, float2 b, float2 c,
    float shadowEndDistance
) {
    float2 tl, br, gradientWeight = 0;
    int gradientType = 0;
    float gradientAngle = 0, distance;

    evaluateRasterShape(
        type, radius, 0 /* outlineSize, previously totalRadius */, params,
        // Force simple on since we don't use gradient value in shadow calc
        worldPosition, a, b, c, true,
        distance, tl, br,
        gradientType, gradientWeight, gradientAngle
    );

    distance -= ShadowExpansion;

    float offset = (ShadowSoftness * 0.5);
    float result;
    if (ShadowInside) {
        result = getWindowAlpha(
            distance, -shadowEndDistance - 0.5 - offset, -shadowEndDistance + 0.5 + offset,
            0, 1, 1
        );
    } else {
        result = getWindowAlpha(
            distance, shadowEndDistance - (0.5 + offset), shadowEndDistance + offset,
            1, 1, 0
        );
    }
    result *= result;
    return result;
}

void rasterShapeCommon (
    in float4 worldPositionTypeAndInteriorFlag, in bool enableShadow, in bool simple,
    in float4 ab, in float4 cd,
    in float4 params, in float4 params2, in float4 params3, in float2 vpos,
    out float2 tl, out float2 br,
    out float2 gradientWeight, out float fillAlpha, 
    out float outlineAlpha, out float shadowAlpha
) {
    float2 worldPosition = worldPositionTypeAndInteriorFlag.xy;
    int type = abs(worldPositionTypeAndInteriorFlag.z);
    type = EVALUATE_TYPE;
    float2 a = ab.xy, b = ab.zw, c = cd.xy, radius = cd.zw;

    const float threshold = (1 / 512.0);

    tl = min(a, b);
    br = max(a, b);

    float distance = 0;
    float outlineSize = abs(params.x);
    // HACK
    outlineSize = max(abs(outlineSize), 0.0001);
    bool HardOutline = (params.x >= 0);
    float OutlineGammaMinusOne = params.w, FillGamma = 0;
    if (OutlineGammaMinusOne < -1) {
        FillGamma = -OutlineGammaMinusOne - 1;
        OutlineGammaMinusOne = FillGamma - 1;
    }        

#ifdef INCLUDE_RECTANGLE
    if (type == TYPE_Rectangle)
        radius = computeLocalRectangleRadius(worldPosition, tl, br, cd.xy, cd.zw);
#endif

    float2 invRadius = 1.0 / max(radius, 0.0001);

    int gradientType = GRADIENT_TYPE_Other;
    float gradientOffset = 0, gradientPower = 0, gradientAngle = 0;
    float2 gradientRange = 0;
    bool repeatGradient = false;
    if (!simple) {
        gradientRange = params2.yz;
        gradientOffset = params2.w;
        gradientPower = params2.x;
        repeatGradient = (gradientPower <= 0);
        gradientPower = max(abs(gradientPower), 0.0001);

        if (params.z >= GRADIENT_TYPE_Conical) {
            gradientType = GRADIENT_TYPE_Conical;
            gradientAngle = min((params.z - gradientType) * DEG_TO_RAD, 2 * PI);
        } else if (params.z >= GRADIENT_TYPE_Angular) {
            gradientType = GRADIENT_TYPE_Angular;
            gradientAngle = (params.z - gradientType) * DEG_TO_RAD;
        } else {
            gradientType = abs(trunc(params.z));
            gradientAngle = 0;
        }
    }

    evaluateRasterShape(
        type, radius, outlineSize, params,
        worldPosition, a, b, c, simple,
        distance, tl, br,
        gradientType, gradientWeight, gradientAngle
    );

    if (simple) {
        gradientWeight = 0;
    } else {
        gradientWeight = evaluateGradient(
            gradientType, gradientAngle, gradientWeight, 
            worldPosition, tl, br, radius.x, params3
        );

        gradientWeight.x += gradientOffset;

        // FIXME: A bunch of this doesn't seem to be necessary anymore

        float gradientSize = gradientRange.y - gradientRange.x;
        if (repeatGradient) {
            // Gradient weight rescaled into 0-1
            gradientWeight.x /= gradientSize;
            // Remap the gradient weight so that it ping-pongs A B A
            gradientWeight.x = gradientWeight.x % 2;
            gradientWeight.x = 1 - abs(gradientWeight.x - 1);
            float gradientDivisor = clamp(1 - (gradientRange.x * 2), 0.001, 1);
            gradientWeight.x = clamp(gradientWeight.x - gradientRange.x, 0, gradientDivisor) / gradientDivisor;
        } else {
            gradientWeight.x = clamp(gradientWeight.x - gradientRange.x, 0, gradientSize) / gradientSize;
        }
        
        if (params3.z != 0) {
            float bevel = saturate(-distance / abs(params3.z));
            if (params3.z < 0)
                bevel = 1 - bevel;

            /*            
            if (params3.w > -999) {
                float2 normal = normalize(float2(ddx(distance), ddy(distance)));
                float2 incoming;
                sincos(params3.w, incoming.y, incoming.x);
                bevel *= saturate((dot(normal, incoming) + 0.15) * 2.0);
            }
            */
            
            // HACK: Beveling needs to force the gradient to the outer color, not the inner color,
            //  in order to be useful
            gradientWeight.x = 1 - ((1 - gradientWeight.x) * bevel);
        }

        if ((gradientWeight.x > 0) && (gradientWeight.x < 1)) {
            float tE = pow(gradientWeight.x, gradientPower);
            gradientWeight.x = tE / (tE + pow(1 - gradientWeight.x, gradientPower));
        }
    }

    float outlineSizeAlpha = saturate(outlineSize / 2);
    float clampedOutlineSize = max(outlineSize / 2, sqrt(2)) * 2;

    float outlineStartDistance = -(outlineSize * 0.5) + 0.5,
        outlineEndDistance = outlineStartDistance + outlineSize,
        // Ideally this range would be smaller, but a larger range produces softer fill outlines
        //  for shapes like ellipses and lines. If FillGamma is set we try to ramp over the whole interior
        fillStartDistance = FillGamma > 0 ? min(-abs(radius.x) + 0.5, -1.01) : -1.01,
        // Expand the fill if there is an outline to reduce the seam between fill and outline
        fillEndDistance = 0.5 + min(outlineSize, 0.5);

    if (FillGamma > 0)
        fillAlpha = 1.0 - pow(saturate((distance - fillStartDistance) / (fillEndDistance - fillStartDistance)), FillGamma);
    else
        fillAlpha = getWindowAlpha(distance, fillStartDistance, fillEndDistance, 1, 1, 0);

    PREFER_FLATTEN
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
        if ((outlineAlpha > 0) && (outlineGamma > 0))
            outlineAlpha = saturate(pow(outlineAlpha, 1.0 / outlineGamma));
    } else {
        outlineAlpha = 0;
    }

    PREFER_BRANCH
    if (enableShadow) {
        shadowAlpha = computeShadowAlpha(
            type, radius, params,
            worldPosition - ShadowOffset, a, b, c,
            ShadowInside ? fillEndDistance : max(outlineEndDistance, fillEndDistance)
        );

        if (ShadowInside) {
            shadowAlpha = saturate (shadowAlpha - saturate(1 - fillAlpha));
        } else {
            // HACK: We don't want to composite the fill on top of the shadow (this will look awful if the fill is semiopaque),
            //  but it makes some amount of sense to allow blending outline and shadow pixels.
            // This is not going to be 100% right for fills without outlines...
            float suppressionValue = (fillAlpha * ShadowFillSuppression);
            shadowAlpha = saturate (shadowAlpha - suppressionValue);
        }
    } else {
        shadowAlpha = 0;
    }
}

// porter-duff A over B
float4 over (float4 top, float topOpacity, float4 bottom, float bottomOpacity) {
    top *= topOpacity;
    bottom *= bottomOpacity;

    return top + (bottom * (1 - top.a));
}

float4 compositeFirstStep (float4 fillColor, float4 outlineColor, float fillAlpha, float outlineAlpha, float shadowAlpha, bool isSimple, bool enableShadow, float2 vpos) {
    float4 result = fillColor;
    REQUIRE_BRANCH
    if (!isSimple && BlendInOkLab)
        // Premultiply math (and standard over) don't work for oklab colors
        result.rgb = OkLabToLinearSRGB(result.rgb) * result.a;

    result *= fillAlpha;

    // FIXME: eliminating aa/ab breaks shadowing for line segments entirely. fxc bug?
    float4 ca = ShadowInside ? ShadowColorLinear : result, cb = ShadowInside ? result : ShadowColorLinear;
    float aa = ShadowInside ? shadowAlpha : 1, ab = ShadowInside ? 1 : shadowAlpha;

    // HACK: Try to keep this goop out of the common shaders that need to be really fast
    if (enableShadow)
        result = over(ca, aa, cb, ab);
    return over(outlineColor, outlineAlpha, result, 1);
}

float4 compositeSecondStep (float4 pLinear, bool isSimple, float2 vpos) {
    float4 result = pLinear;
    // Unpremultiply the output, because if we don't we get unpleasant stairstepping artifacts
    //  for alpha gradients because the A we premultiply by does not match the A the GPU selected
    // It's also important to do dithering and sRGB conversion on a result that is not premultiplied
    result = float4(result.rgb / max(result.a, 0.0001), result.a);

    if (isSimple)
        return result;

    if (BlendInLinearSpace != OutputInLinearSpace) {
        if (OutputInLinearSpace)
            result.rgb = SRGBToLinear(result.rgb);
        else
            result.rgb = LinearToSRGB(result.rgb);
    }

    return ApplyDither4(result, vpos);
}

float4 composite (float4 fillColor, float4 outlineColor, float fillAlpha, float outlineAlpha, float shadowAlpha, bool isSimple, bool enableShadow, float2 vpos) {
    return compositeSecondStep(compositeFirstStep(fillColor, outlineColor, fillAlpha, outlineAlpha, shadowAlpha, isSimple, enableShadow, vpos), isSimple, vpos);
}

float4 texComposite (float4 fill, float fillAlpha, float4 texColor, float mode) {
    // Under
    if (mode > 1.5) {
        fill = over(fill, fillAlpha, texColor, fillAlpha);
        fillAlpha = 1;
    // Over
    } else if (mode > 0.5) {
        fill = over(texColor, fillAlpha, fill, fillAlpha);
        fillAlpha = 1;
    // Multiply
    } else
        fill *= texColor;

    return fill;
}

float computeMip (in float2 texCoordPx) {
    float2 dx = ddx(texCoordPx), dy = ddy(texCoordPx);
    float _ddx = dot(dx, dx), _ddy = dot(dy, dy);
    // FIXME: If the x and y scale ratios differ significantly this can result in extreme blurring
    float mag = max(_ddx, _ddy);
    return 0.5 * log2(mag);
}

#if VARIANT_TEXTURED
float4 texturedShapeCommon (
    in float2 worldPosition, in float4 texRgn,
    in float4 ab, in float4 cd,
    in float4 fill, in float4 outlineColor,
    in float fillAlpha, in float outlineAlpha, in float shadowAlpha,
    in float4 params, in float4 params2, in float2 tl, in float2 br,
    in bool enableShadow, in float2 vpos
) {
    float2 sizePx = br - tl;
    
    sizePx = max(abs(sizePx), 0.001) * sign(sizePx);
    float mode = TextureModeAndSize.x;
    bool afterOutline = afterOutline = mode >= 1024;
    if (afterOutline)
        mode -= 1024;

    bool screenSpace = mode >= 128, screenSpaceLocal = mode >= 192;
    if (screenSpaceLocal)
        mode -= 192;
    else if (screenSpace)
        mode -= 128;

    float2 texCoord;

    if (screenSpace) {
        float2 texSize = (texRgn.zw - texRgn.xy);
        // HACK: Approximate the ratio between world position and vpos so we can try to align things
        //  with the top-left corner of the bounding box
        float screenScale = rcp((length(ddx(worldPosition)) + length(ddy(worldPosition))) * 0.5);
        if (screenSpaceLocal)
            vpos -= tl * screenScale;
        texCoord = (
            ((vpos / TextureModeAndSize.zw) - TexturePlacement.zw) * texSize
        ) + TexturePlacement.xy;
    } else {
        float2 posRelative = worldPosition - tl;
        posRelative -= TexturePlacement.zw * sizePx;

        if (TextureModeAndSize.y > 0.5)
            sizePx = min(sizePx.x, sizePx.y);
        float2 posRelativeScaled = posRelative / sizePx;

        float2 posTextureScaled = posRelativeScaled / (TextureModeAndSize.zw);
        posTextureScaled += TexturePlacement.xy;

        float2 texSize = (texRgn.zw - texRgn.xy);
        texCoord = (posTextureScaled * texSize) + texRgn.xy;
    }

    float mipLevel = computeMip(texCoord * TextureSizePx) + BACKGROUND_MIP_BIAS;
    float2 texCoordClamped = clamp(texCoord, texRgn.xy, texRgn.zw);
    texCoord = lerp(texCoord, texCoordClamped, TextureOptions.zw);

    // TODO: Will the automatic mip selection work correctly here? Probably not
    float4 texColor = tex2Dlod(TextureSampler, float4(texCoord, 0, mipLevel));
    texColor = ExtractRgba(texColor, TextureTraits);

    // FIXME: OkLab
    if (BlendInLinearSpace && (TextureTraits.x < VALUE_CHANNEL_SRGB))
        texColor = pSRGBToPLinear(texColor);

    float texColorGray = dot(texColor.rgb, float3(0.299, 0.587, 0.144));
    // FIXME: Oversaturate will break the premultiplication
    texColor.rgb += ((texColor.rgb - texColorGray) * TextureOptions.x);
    // FIXME: This will break premul too if the value is greater than 1 i think
    texColor.rgb *= (TextureOptions.y + 1);
    // Blend towards white as brightness increases so that it looks okay for textures that are pure red/green/blue
    texColor.rgb = lerp(texColor.rgb, 1, TextureOptions.y);

    if (!afterOutline)
        fill = texComposite(fill, fillAlpha, texColor, mode);

    float4 result = compositeFirstStep(fill, outlineColor, fillAlpha, outlineAlpha, shadowAlpha, false, enableShadow, vpos);

    if (afterOutline && (result.a > 0))
        result = texComposite(result, 1, texColor, mode);

    return compositeSecondStep(result, false, vpos);
}
#endif