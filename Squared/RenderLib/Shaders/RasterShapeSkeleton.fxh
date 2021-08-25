#ifndef BACKGROUND_MIP_BIAS
#define BACKGROUND_MIP_BIAS -0.5
#endif

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

// Mode, ScaleX, ScaleY
uniform float4 TextureModeAndSize;
// Origin, Position
uniform float4 TexturePlacement;

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
    in float4 centerColor : COLOR0, \
    in float4 edgeColor : COLOR1, \
    in float4 outlineColor : COLOR2, \
    ACCEPTS_VPOS, \
    out float4 result : COLOR0

uniform bool BlendInLinearSpace, OutputInLinearSpace;
uniform float HalfPixelOffset;

// offsetx, offsety, softness, fillSuppression
uniform float4 ShadowOptions;
// expansion, inside
uniform float4 ShadowOptions2;
uniform float4 ShadowColorLinear;

// FIXME: This is not currently faster even though it seems like it could be,
//  and it also breaks gradient fills in a weird way somehow
#define OptimizeRectangleInterior false

#define ShadowSoftness ShadowOptions.z
#define ShadowOffset ShadowOptions.xy
#define ShadowFillSuppression ShadowOptions.w
#define ShadowExpansion ShadowOptions2.x
#define ShadowInside (ShadowOptions2.y > 0.5)

float4 TransformPosition(float4 position, bool halfPixelOffset) {
    // Transform to view space, then offset by half a pixel to align texels with screen pixels
    float4 modelViewPos = mul(position, Viewport.ModelView);
    if (halfPixelOffset && HalfPixelOffset)
        modelViewPos.xy -= 0.5;
    // Finally project after offsetting
    return mul(modelViewPos, Viewport.Projection);
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

void computeTLBR_Bezier (
    float2 a, float2 b, float2 c,
    out float2 tl, out float2 br
) {
    tl = min(a, c);
    br = max(a, c);

    if (b.x<tl.x || b.x>br.x || b.y<tl.y || b.y>br.y)
    {
        float2 t = clamp((a - b) / (a - 2.0*b + c), 0.0, 1.0);
        float2 s = 1.0 - t;
        float2 q = s*s*a + 2.0*s*t*b + t*t*c;
        tl = min(tl, q);
        br = max(br, q);
    }
}

void computeTLBR (
    int type, float2 radius, float outlineSize, float4 params,
    float2 a, float2 b, float2 c,
    out float2 tl, out float2 br
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

#ifdef INCLUDE_POLYGON
    else if (type == TYPE_Polygon) {
        computeTLBR_Polygon(
            radius, outlineSize, params,
            a.x, a.y, b.x, tl, br
        );
    }
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
        if (OptimizeRectangleInterior) {
            tl += edgeSize + offsetPositive;
            br -= edgeSize + offsetNegative;
        }
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
    inout float4 params,
    inout float4 params2,
    inout float4 texRgn,
    inout float4 centerColor,
    inout float4 edgeColor,
    inout float4 outlineColor,
    in  int2 typeAndWorldSpace,
    out float4 result,
    out float4 ab,
    out float4 cd,
    out float4 worldPositionTypeAndInteriorFlag
) {
    int type = abs(typeAndWorldSpace.x);
    type = EVALUATE_TYPE ;

    bool isUnshadowed = (ShadowColorLinear.a <= 0) || (ShadowInside <= 0);
    bool isSimpleRectangle = (centerColor == edgeColor) && 
        (type == TYPE_Rectangle) && 
        (params.y <= 0); /* FIXME: Annular radius */
    bool isHollow = isSimpleRectangle && 
        (centerColor.a <= 0);

    bool dead;
    if (isHollow) {
        dead = cornerWeights.w < 1;
    } else if (OptimizeRectangleInterior && isSimpleRectangle) {
        dead = false;
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
    params.x *= OutlineSizeCompensation;
    float outlineSize = abs(params.x);

    float2 tl, br;

    computeTLBR(type, radius, outlineSize, params, a, b, c, tl, br);
    computePosition(type, outlineSize, a, b, c, radius, tl, br, cornerWeights, params, isSimpleRectangle, position.xy);

    float2 adjustedPosition = position.xy;
    if (typeAndWorldSpace.y > 0.5) {
        adjustedPosition -= GetViewportPosition().xy;
        adjustedPosition *= GetViewportScale().xy;
    }

    result = TransformPosition(
        float4(adjustedPosition, position.z, 1), true
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
        (BlendInLinearSpace && !isSimple)
    ) {
        centerColor = pSRGBToPLinear_Accurate(centerColor);
        edgeColor = pSRGBToPLinear_Accurate(edgeColor);
        outlineColor = pSRGBToPLinear_Accurate(outlineColor);
    }
}

void RasterShapeVertexShader (
    in float4 cornerWeights : NORMAL2,
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
    out float4 worldPositionTypeAndInteriorFlag : NORMAL0
) {
    RasterShapeVertexShader_Core (
        false,
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
        worldPositionTypeAndInteriorFlag
    );
}

void RasterShapeVertexShader_Simple (
    in float4 cornerWeights : NORMAL2,
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
        worldPositionTypeAndInteriorFlag
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
    inout int gradientType, out float gradientWeight
) {
    // FIXME: sdEllipse is massively broken. What is wrong with it?
    // distance = sdEllipse(worldPosition - a, b);
    float2 distanceXy = worldPosition - a;
    float distanceF = length(distanceXy / b);
    distance = (distanceF - 1) * length(b);
    tl = a - b;
    br = a + b;

    if (simple) {
        gradientWeight = 0;
        return;
    }

    gradientWeight = saturate(distanceF);
    if (gradientType == GRADIENT_TYPE_Natural)
        gradientType = GRADIENT_TYPE_Radial;
    else if (
        (gradientType == GRADIENT_TYPE_Linear_Enclosing) ||
        (gradientType == GRADIENT_TYPE_Linear_Enclosed)
    ) {
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
    if (gradientType == GRADIENT_TYPE_Along) {
        gradientWeight = saturate(t);
        gradientType == GRADIENT_TYPE_Other;
    } else
        gradientWeight = 1 - saturate(-distance / localRadius);
}

bool quadraticBezierTFromY (
    in float y0, in float y1, in float y2, in float y,
    out float t1, out float t2
) {
    float divisor = (y0 - (2 * y1) + y2);
    if (abs(divisor) <= 0.001) {
        t1 = t2 = 0;
        return false;
    }
    float rhs = sqrt((y * y0) - (2 * y * y1) + (y * y2) - (y0 * y2) + (y1 * y1));
    t1 = ((y0 - y1) + rhs) / divisor;
    t2 = ((y0 - y1) - rhs) / divisor;
    return true;
}

// Assumes worldPosition is 0 relative to the control points
float2 evaluateBezierAtT (
    in float2 a, in float2 b, in float2 c, in float t
) {
    float2 ab = lerp(a, b, t),
        bc = lerp(b, c, t);
    return lerp(ab, bc, t);
}

float distanceSquaredToBezierAtT (
    in float2 a, in float2 b, in float2 c, float2 worldPosition, in float t
) {
    float2 pt = evaluateBezierAtT(a, b, c, t);
    float2 dist = worldPosition - pt;
    return dot(dist, dist);
}

void pickClosestTForAxis (
    in float2 a, in float2 b, in float2 c, in float2 worldPosition, in float2 mask,
    inout float cd, inout float ct
) {
    // For a given x or y value on the bezier there are two candidate T values that are closest,
    //  so we compute both and then pick the closest of the two. If the divisor is too close to zero
    //  we will have failed to compute any valid T values, so we bail out
    float t1, t2;
    float2 _a = a * mask, _b = b * mask, _c = c * mask, _wp = worldPosition * mask;
    if (!quadraticBezierTFromY(_a.x+_a.y, _b.x+_b.y, _c.x+_c.y, _wp.x+_wp.y, t1, t2))
        return;
    float d1 = distanceSquaredToBezierAtT(a, b, c, worldPosition, t1);
    if (d1 < cd) {
        ct = t1; cd = d1;
    }
    float d2 = distanceSquaredToBezierAtT(a, b, c, worldPosition, t2);
    if (d2 < cd) {
        ct = t2; cd = d2;
    }
}

void evaluateBezier (
    in float2 worldPosition, in float2 a, in float2 b, in float2 c,
    in float2 radius, out float distance,
    inout int gradientType, out float gradientWeight
) {
    distance = sdBezier(worldPosition, a, b, c) - radius.x;

    REQUIRE_BRANCH
    if (gradientType == GRADIENT_TYPE_Along) {

        // If the control point b lies along the line a-c, all the math below is busted
        //  so we want to detect that case and bail out.
        float temp;
        float2 closestPointToAC = closestPointOnLineSegment2(a, c, b, temp);
        float2 distanceFromLine = (b - closestPointToAC);
        // While this catches the common cases there are still scenarios where all this math is busted :(
        if (dot(distanceFromLine, distanceFromLine) >= 0.5) {
            // Analytically locate the closest point on the bezier for this position.
            float ct = 0.5, cd = distanceSquaredToBezierAtT(a, b, c, worldPosition, ct);
            pickClosestTForAxis(a, b, c, worldPosition, float2(1, 0), cd, ct);
            pickClosestTForAxis(a, b, c, worldPosition, float2(0, 1), cd, ct);
            gradientWeight = saturate(ct);
        } else {
            closestPointOnLineSegment2(a, c, worldPosition, temp);
            gradientWeight = saturate(temp);
        }
        gradientType == GRADIENT_TYPE_Other;
    } else
        gradientWeight = 1 - saturate(-distance / radius.x);
}

float computeLocalRectangleRadius (
    in float2 worldPosition, 
    in float2 a, in float2 b, 
    in float2 radiusTLTR, in float2 radiusBRBL
) {
    // Smoothly interpolate the transition between radius values over the [0.25 - 0.75]
    //  region, because otherwise the immediate jump can cause very weird discontinuities
    float2 radiusWeight = saturate((
        (
            (worldPosition - a) / max((b - a), 0.001)
        ) - 0.5) * 2
    );
    return lerp(
        lerp(radiusTLTR.x, radiusTLTR.y, radiusWeight.x),
        lerp(radiusBRBL.y, radiusBRBL.x, radiusWeight.x),
        radiusWeight.y
    );
}

void evaluateRectangle (
    in float2 worldPosition, in float2 a, in float2 b, in float radius, 
    out float distance, inout int gradientType, out float gradientWeight
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
}

#ifdef INCLUDE_TRIANGLE
void evaluateTriangle (
    in float2 worldPosition, in float2 a, in float2 b, in float2 c,
    in float2 radius, out float distance,
    inout int gradientType, out float gradientWeight,
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

    // FIXME: Recenter non-natural gradients around our incenter instead of the
    //  center of our bounding box

    PREFER_FLATTEN
    if (gradientType == GRADIENT_TYPE_Natural) {
        gradientWeight = 1 - saturate(distance / targetDistance);
        gradientType = GRADIENT_TYPE_Other;
    } else {
        gradientWeight = 0;
    }
}
#endif

float evaluateGradient (
    int gradientType, float gradientAngle, float gradientWeight,
    float2 worldPosition, float2 tl, float2 br, float radius
) {
    float2 gradientCenter = (tl + br) / 2;
    float2 boxSize = (br - tl);
    boxSize = max(abs(boxSize), 0.001) * sign(boxSize);
    float2 radialSize2 = (boxSize /* + (radius * 2) */) * 0.5;

    PREFER_FLATTEN
    if (
        (gradientType == GRADIENT_TYPE_Natural) ||
        (gradientType == GRADIENT_TYPE_Other)
    ) {
        return gradientWeight;
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
        return max(linearDist2.x, linearDist2.y);
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
        return length((worldPosition - gradientCenter) / max(radialSize, 0.0001));
    } else if (
        (gradientType >= GRADIENT_TYPE_Angular)
    ) {
        float2 scaled = (worldPosition - gradientCenter) / (boxSize * 0.5);
        float scaledLength = length(scaled);
        if (scaledLength < 0.001) {
            scaled.x = 0.001;
            scaledLength = 0.001;
        }
        if (gradientType >= GRADIENT_TYPE_Conical) {
            // atan2 is typically counter-clockwise and starts on the left, so (-1, 0) is 0rad.
            // We want a clockwise gradient starting at the top.
            float angleCw = atan2(-scaled.x, scaled.y) + PI; // [0, 2 * PI]
            float angleCwScaled = (angleCw + ((2 * PI) - gradientAngle)) / (2 * PI);
            return angleCwScaled % 1;
        } else {
            float tan2 = atan2(scaled.y, scaled.x);
            return ((sin(gradientAngle + tan2) * scaledLength) / 2) + 0.5;
        }
    }

    return gradientWeight;
}

void evaluateRasterShape (
    int type, float2 radius, float outlineSize, float4 params,
    in float2 worldPosition, in float2 a, in float2 b, in float2 c, in bool simple,
    out float distance, inout float2 tl, inout float2 br,
    inout int gradientType, out float gradientWeight, inout float gradientAngle
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
            gradientType, gradientWeight
        );
        needTLBR = true;
    }
#endif

#ifdef INCLUDE_BEZIER
    else if (type == TYPE_QuadraticBezier) {
        evaluateBezier(
            worldPosition, a, b, c,
            radius, distance,
            gradientType, gradientWeight
        );

        computeTLBR(type, radius, outlineSize, params, a, b, c, tl, br);
    }
#endif

#ifdef INCLUDE_RECTANGLE
    else if (type == TYPE_Rectangle) {
        evaluateRectangle(
            worldPosition, a, b, radius.x, 
            distance, gradientType, gradientWeight
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
        if (gradientType == GRADIENT_TYPE_Natural) {
            gradientWeight = 1 - saturate(-distance / radius.y);
        } else if (gradientType == GRADIENT_TYPE_Along) {
            gradientType = GRADIENT_TYPE_Conical;
            // FIXME: Size scaling
            gradientAngle = c.x;
        }

        needTLBR = true;
    }
#endif

#ifdef INCLUDE_POLYGON
    else if (type == TYPE_Polygon) {
        evaluatePolygon(
            radius, outlineSize, params,
            worldPosition, a.x, a.y, b.x, simple,
            distance, tl, br,
            gradientType, gradientWeight, gradientAngle
        );
    }
#endif
    
    float annularRadius = params.y;
    if (annularRadius > 0.001)
        distance = abs(distance) - annularRadius;

    if (needTLBR)
        computeTLBR(type, radius, outlineSize, params, a, b, c, tl, br);
}

float computeShadowAlpha (
    int type, float2 radius, float4 params,
    float2 worldPosition, float2 a, float2 b, float2 c,
    float shadowEndDistance
) {
    float2 tl, br;
    int gradientType = 0;
    float gradientWeight = 0, gradientAngle = 0;

    float distance;
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
    in float4 params, in float4 params2, in float2 vpos,
    out float2 tl, out float2 br,
    out float gradientWeight, out float fillAlpha, 
    out float outlineAlpha, out float shadowAlpha
) {
    float2 worldPosition = worldPositionTypeAndInteriorFlag.xy;
    int type = abs(worldPositionTypeAndInteriorFlag.z);
    type = EVALUATE_TYPE;
    float2 a = ab.xy, b = ab.zw, c = cd.xy, radius = cd.zw;

    const float threshold = (1 / 512.0);

    tl = min(a, b);
    br = max(a, b);

#ifdef INCLUDE_RECTANGLE
    bool isSimpleInterior = worldPositionTypeAndInteriorFlag.w > 0;
    PREFER_BRANCH
    if (OptimizeRectangleInterior && (type == TYPE_Rectangle) && (isSimpleInterior)) {
        gradientWeight = 0;
        fillAlpha = 1;
        outlineAlpha = 0;
        shadowAlpha = 0;
        return;
    }
#endif

    float distance = 0;
    float outlineSize = abs(params.x);
    // HACK
    outlineSize = max(abs(outlineSize), 0.0001);
    bool HardOutline = (params.x >= 0);
    float OutlineGammaMinusOne = params.w;

#ifdef INCLUDE_RECTANGLE
    if (type == TYPE_Rectangle)
        radius = computeLocalRectangleRadius(worldPosition, tl, br, cd.xy, cd.zw);
#endif

    float2 invRadius = 1.0 / max(radius, 0.0001);

    float gradientOffset = params2.z, gradientSize = params2.w, gradientAngle;
    int gradientType;

    if (simple) {
        gradientType = GRADIENT_TYPE_Other;
    } else if (params.z >= GRADIENT_TYPE_Conical) {
        gradientType = GRADIENT_TYPE_Conical;
        gradientAngle = min((params.z - gradientType) * DEG_TO_RAD, 2 * PI);
    } else if (params.z >= GRADIENT_TYPE_Angular) {
        gradientType = GRADIENT_TYPE_Angular;
        gradientAngle = (params.z - gradientType) * DEG_TO_RAD;
    } else {
        gradientType = abs(trunc(params.z));
        gradientAngle = 0;
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
            worldPosition, tl, br, radius.x
        );

        gradientWeight += gradientOffset;

        // FIXME: A bunch of this doesn't seem to be necessary anymore

        if (gradientSize > 0) {
            gradientWeight = saturate(gradientWeight / gradientSize);
        } else {
            gradientSize = max(abs(gradientSize), 0.0001);
            gradientWeight /= gradientSize;
            gradientWeight = gradientWeight % 2;
            gradientWeight = 1 - abs(gradientWeight - 1);
        }

        if (gradientWeight > 0)
            gradientWeight = saturate(pow(gradientWeight, max(params2.x, 0.001)));
        if (gradientWeight < 1)
            gradientWeight = 1 - saturate(pow(1 - gradientWeight, max(params2.y, 0.001)));
    }

    float outlineSizeAlpha = saturate(outlineSize / 2);
    float clampedOutlineSize = max(outlineSize / 2, sqrt(2)) * 2;

    float outlineStartDistance = -(outlineSize * 0.5) + 0.5,
        outlineEndDistance = outlineStartDistance + outlineSize,
        // Ideally this range would be smaller, but a larger range produces softer fill outlines
        //  for shapes like ellipses and lines
        fillStartDistance = -1.01,
        // Expand the fill if there is an outline to reduce the seam between fill and outline
        fillEndDistance = 0.5 + min(outlineSize, 0.5);

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

float4 composite (float4 fillColor, float4 outlineColor, float fillAlpha, float outlineAlpha, float shadowAlpha, bool isSimple, bool enableShadow, float2 vpos) {
    float4 result = fillColor * fillAlpha;
    if (enableShadow) {
        // FIXME: eliminating aa/ab breaks shadowing for line segments entirely. fxc bug?
        float4 ca = ShadowInside ? ShadowColorLinear : result, cb = ShadowInside ? result : ShadowColorLinear;
        float aa = ShadowInside ? shadowAlpha : 1, ab = ShadowInside ? 1 : shadowAlpha;
        result = over(ca, aa, cb, ab);
    }
    result = over(outlineColor, outlineAlpha, result, 1);

    // Unpremultiply the output, because if we don't we get unpleasant stairstepping artifacts
    //  for alpha gradients because the A we premultiply by does not match the A the GPU selected
    // It's also important to do dithering and sRGB conversion on a result that is not premultiplied
    result.rgb = float4(result.rgb / max(result.a, 0.0001), result.a);

    if (isSimple) {
        return result;
    } else if (BlendInLinearSpace != OutputInLinearSpace) {
        if (OutputInLinearSpace)
            result.rgb = SRGBToLinear(result).rgb;
        else
            result.rgb = LinearToSRGB(result.rgb);
    }

    return ApplyDither4(result, vpos);
}

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
    float2 posRelative = worldPosition - tl;
    posRelative -= TexturePlacement.zw * sizePx;

    if (TextureModeAndSize.y > 0.5)
        sizePx = min(sizePx.x, sizePx.y);
    float2 posRelativeScaled = posRelative / sizePx;

    float2 posTextureScaled = posRelativeScaled / (TextureModeAndSize.zw + 1);
    posTextureScaled += TexturePlacement.xy;

    float2 texSize = (texRgn.zw - texRgn.xy);
    float2 texCoord = (posTextureScaled * texSize) + texRgn.xy;
    texCoord = clamp(texCoord, texRgn.xy, texRgn.zw);

    float4 texColor = tex2Dbias(TextureSampler, float4(texCoord, 0, BACKGROUND_MIP_BIAS));
    if (BlendInLinearSpace)
        texColor = pSRGBToPLinear(texColor);

    // Under
    if (TextureModeAndSize.x > 1.5) {
        fill = over(fill, fillAlpha, texColor, fillAlpha);
        fillAlpha = 1;
    // Over
    } else if (TextureModeAndSize.x > 0.5) {
        fill = over(texColor, fillAlpha, fill, fillAlpha);
        fillAlpha = 1;
    // Multiply
    } else
        fill *= texColor;

    float4 result = composite(fill, outlineColor, fillAlpha, outlineAlpha, shadowAlpha, false, enableShadow, vpos);
    return result;
}