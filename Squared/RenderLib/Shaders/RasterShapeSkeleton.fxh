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

// A bunch of the distance formulas in here are thanks to inigo quilez
// http://iquilezles.org/www/articles/distfunctions2d/distfunctions2d.htm
// http://iquilezles.org/www/articles/distfunctions/distfunctions.htm


#define TYPE_Ellipse 0
#define TYPE_LineSegment 1
#define TYPE_Rectangle 2
#define TYPE_Triangle 3
#define TYPE_QuadraticBezier 4
#define TYPE_Arc 5

#define RASTERSHAPE_FS_ARGS \
    in float2 worldPosition : NORMAL0, \
    in float4 ab : TEXCOORD3, \
    in float4 cd : TEXCOORD4, \
    in float4 params : TEXCOORD0, \
    in float4 params2 : TEXCOORD1, \
    in float4 texRgn : TEXCOORD2, \
    in float4 centerColor : COLOR0, \
    in float4 edgeColor : COLOR1, \
    in float4 outlineColor : COLOR2, \
    in int2 _type : BLENDINDICES1, \
    ACCEPTS_VPOS, \
    out float4 result : COLOR0

#define RASTERSHAPE_FS_PROLOGUE \
    float2 a = ab.xy, b = ab.zw, c = cd.xy, radius = cd.zw; \
    float outlineSize = abs(params.x); \
    bool HardOutline = (params.x >= 0); \
    float OutlineGammaMinusOne = params.w;

// We use the _Accurate conversion function here because the approximation introduces
//  visible noise for values like (64, 64, 64) when they are dithered
#define RASTERSHAPE_PREPROCESS_COLORS \
    if (params.z > 0.5) { \
        centerColor = pSRGBToPLinear_Accurate(centerColor); \
        edgeColor = pSRGBToPLinear_Accurate(edgeColor); \
        outlineColor = pSRGBToPLinear_Accurate(outlineColor); \
    }

    /*
    centerColor = pSRGBToPLinear(centerColor); \
    edgeColor = pSRGBToPLinear(edgeColor); \
    outlineColor = pSRGBToPLinear(outlineColor); \
    // FIXME: Why is this necessary? Is it an fxc bug? \
    // It only seems to be needed when using certain blend modes \
    // Maybe mojoshader? Driver bug? \
    if (params.z < 0.5) { \
        centerColor = pLinearToPSRGB(centerColor); \
        edgeColor = pLinearToPSRGB(edgeColor); \
        outlineColor = pLinearToPSRGB(outlineColor); \
    }
    */

uniform float HalfPixelOffset;

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
    uint type, float2 radius, float totalRadius, float4 params,
    float2 a, float2 b, float2 c,
    out float2 tl, out float2 br
) {
    switch (type) {
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
    }

    float annularRadius = params.y;
    if (annularRadius > 0) {
        tl -= annularRadius;
        br += annularRadius;
    }
}

void computePosition (
    uint type, float totalRadius, 
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
    inout int2 typeAndWorldSpace : BLENDINDICES1,
    out float4 result : POSITION0,
    out float4 ab : TEXCOORD3,
    out float4 cd : TEXCOORD4,
    out float2 worldPosition : NORMAL0
) {
    ab = ab_in; cd = cd_in;
    float4 position = float4(ab_in.x, ab_in.y, 0, 1);
    float2 a = ab.xy, b = ab.zw, c = cd.xy, radius = cd.zw;
    params.x *= OutlineSizeCompensation;
    float outlineSize = abs(params.x);
    uint type = abs(typeAndWorldSpace.x);

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
    worldPosition = position.xy;

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

    gradientType = abs(c.x);

    PREFER_FLATTEN
    switch (gradientType) {
        // Linear
        case 0:
        case 4:
            float2 center = (tl + br) / 2;
            // Options:
            // * 2 = touches corners of a box enclosing the ellipse
            // * 2 * sqrt(2) == touches corners of a box enclosed by the ellipse
            float2 distance2 = abs(worldPosition - center) / (br - tl) * (
                (gradientType == 4) 
                    ? (2 * sqrt(2)) 
                    : 2
            );
            gradientWeight = saturate(max(distance2.x, distance2.y));
            gradientType = 999;
            break;
        // Radial
        case 1:
            gradientType = 999;
            break;
    }
}

void evaluateLineSegment (
    in float2 worldPosition, in float2 a, in float2 b, in float2 c,
    in float2 radius, out float distance,
    out float gradientWeight
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
    gradientType = abs(c.x);

    PREFER_FLATTEN
    switch (gradientType) {
        // Linear
        case 0:
        case 4:
            gradientWeight = saturate(1 - saturate(distance / centerDistance));
            gradientType = 999;
            break;
    }
}

void evaluateTriangle (
    in float2 worldPosition, in float2 a, in float2 b, in float2 c,
    in float2 radius, out float distance,
    out float gradientWeight, out float2 tl, out float2 br
) {
    distance = sdTriangle(worldPosition, a, b, c);

    float2 center = (a + b + c) / 3;
    // float centroidDistance = sdTriangle(center, a, b, c);
    // FIXME: Why is this necessary?
    float ac = length(a - center), bc = length(b - center), cc = length(c - center);
    float targetDistance = (min(ac, min(bc, cc)) + max(ac, max(bc, cc))) * -0.25;
    gradientWeight = 1 - saturate(distance / targetDistance);

    distance -= radius;

    tl = min(min(a, b), c);
    br = max(max(a, b), c);
}

void rasterShapeCommon (
    in float2 worldPosition,
    in float4 ab, in float4 cd,
    in float4 params, in float4 params2, in uint type,
    in float4 centerColor, in float4 edgeColor, in float2 vpos,
    out float2 tl, out float2 br,
    out float4 fill, out float fillAlpha, 
    out float outlineAlpha
) {
    RASTERSHAPE_FS_PROLOGUE;

    // HACK
    outlineSize = max(abs(outlineSize), 0.0001);

    const float threshold = (1 / 512.0);

    float totalRadius = computeTotalRadius(radius, outlineSize);
    float2 invRadius = 1.0 / max(radius, 0.0001);

    float distance = 0, gradientWeight = 0;

    tl = min(a, b);
    br = max(a, b);

    float gradientOffset = params2.z, gradientSize = params2.w;
    int gradientType = 999;

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
                gradientWeight
            );

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
                gradientWeight, tl, br
            );

            break;
        }
#endif

#ifdef INCLUDE_ARC
        case TYPE_Arc: {
            distance = sdArc(worldPosition - a, b, c, radius.x, radius.y);
            gradientWeight = 1 - saturate(-distance / radius.y);

            break;
        }
#endif
    }

    PREFER_FLATTEN
    switch (gradientType) {
        // Radial
        case 1:
            float2 center = (tl + br) / 2;
            gradientWeight = length((worldPosition - center) / ((br - tl) * 0.5));
            break;
        // Horizontal
        case 2:
            gradientWeight = saturate((worldPosition.x - tl.x) / (br.x - tl.x));
            break;
        // Vertical
        case 3:
            gradientWeight = saturate((worldPosition.y - tl.y) / (br.y - tl.y));
            break;
    }

    gradientWeight += gradientOffset;

    if (gradientSize > 0) {
        gradientWeight = saturate(gradientWeight / gradientSize);
    } else {
        gradientSize = max(abs(gradientSize), 0.0001);
        gradientWeight /= gradientSize;
        gradientWeight = (gradientWeight % 2);
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
}

// porter-duff A over B
float4 over (float4 top, float topOpacity, float4 bottom, float bottomOpacity) {
    top *= topOpacity;
    bottom *= bottomOpacity;

    float3 rgb = top.rgb + (bottom.rgb * (1 - top.a));
    float a = top.a + (bottom.a * (1 - top.a));
    return float4(rgb, a);
}

float4 composite (float4 fillColor, float4 outlineColor, float fillAlpha, float outlineAlpha, float convertToSRGB, float2 vpos) {
    float4 result;
    result = over(outlineColor, outlineAlpha, fillColor, fillAlpha);

    result.rgb = float4(result.rgb / max(result.a, 0.0001), result.a);

    if (convertToSRGB)
        result.rgb = LinearToSRGB(result.rgb);

    float4 ditheredResult = ApplyDither4(result, vpos);

    // You know what, whatever. The output of this isn't premultiplied anymore.
    /*
     * HACK: The ideal outcome is that we just premultiply by our random alpha value
     *  and that everything looks good. Unfortunately, it seems like what the GPU does
     *  with overly-precise alpha values is "eh, whatever" so we get weird artifacts and
     *  uneven brightness on alpha gradients unless we snap the alpha first.
     * An alternative is to not premultiply the output (and let the GPU do it in the
     *  blending stage at the end), which looks great, but then we're not using
     *  premultiplied alpha anymore, which is not so great.
     * Note the disgusting +0.5 offset. Without this it still looks awful on my RTX card.
    float snappedResultAlpha = (round(ditheredResult.a * 255) + 0.5) / 255;
    ditheredResult.a = snappedResultAlpha;
    ditheredResult.rgb *= ditheredResult.a;
    */

    return ditheredResult;
}

float4 texturedShapeCommon (
    in float2 worldPosition, in float4 texRgn,
    in float4 ab, in float4 cd,
    in float4 fill, in float4 outlineColor,
    in float fillAlpha, in float outlineAlpha,
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
    if (params.z)
        texColor = pSRGBToPLinear(texColor);

    fill *= texColor;

    float4 result = composite(fill, outlineColor, fillAlpha, outlineAlpha, params.z, vpos);
    return result;
}