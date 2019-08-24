#define ENABLE_DITHERING 1

#include "CompilerWorkarounds.fxh"
#include "ViewTransformCommon.fxh"
#include "TargetInfo.fxh"
#include "DitherCommon.fxh"
#include "sRGBCommon.fxh"

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

#define DEFINE_QuadCorners const float2 QuadCorners[] = { \
    {0, 0}, \
    {1, 0}, \
    {1, 1}, \
    {0, 1} \
};

#define RASTERSHAPE_FS_ARGS \
    in float2 worldPosition : NORMAL0, \
    in float4 ab : TEXCOORD2, \
    in float4 cd : TEXCOORD3, \
    in float4 params : TEXCOORD0, \
    in float4 centerColor : COLOR0, \
    in float4 edgeColor : COLOR1, \
    in float4 outlineColor : COLOR2, \
    in int2 _type : BLENDINDICES1, \
    in float4 texRgn : TEXCOORD1, \
    ACCEPTS_VPOS, \
    out float4 result : COLOR0

#define RASTERSHAPE_FS_PROLOGUE \
    float2 a = ab.xy, b = ab.zw, c = cd.xy, radius = cd.zw; \
    float outlineSize = params.x; \
    float HardOutline = params.y; \
    float OutlineGammaMinusOne = params.w;

#define RASTERSHAPE_PREPROCESS_COLORS \
    if (params.z) { \
        centerColor = pSRGBToPLinear(centerColor); \
        edgeColor = pSRGBToPLinear(edgeColor); \
        outlineColor = pSRGBToPLinear(outlineColor); \
    }


float sdBox(in float2 p, in float2 b) {
    float2 d = abs(p) - b;
    return length(max(d, 0)) + min(max(d.x, d.y), 0.0);
}

float sdEllipse(in float2 p, in float2 ab) {
    p = abs(p);
    if (p.x > p.y) {
        p = p.yx; ab = ab.yx;
    }
    float l = ab.y*ab.y - ab.x*ab.x;
    float m = ab.x*p.x / l;
    float m2 = m*m;
    float n = ab.y*p.y / l;
    float n2 = n*n;
    float c = (m2 + n2 - 1.0) / 3.0;
    float c3 = c*c*c;
    float q = c3 + m2*n2*2.0;
    float d = c3 + m2*n2;
    float g = m + m*n2;
    float co;
    if (d<0.0) {
        float h = acos(q / c3) / 3.0;
        float s = cos(h);
        float t = sin(h)*sqrt(3.0);
        float rx = sqrt(-c*(s + t + 2.0) + m2);
        float ry = sqrt(-c*(s - t + 2.0) + m2);
        co = (ry + sign(l)*rx + abs(g) / (rx*ry) - m) / 2.0;
    }
    else {
        float h = 2.0*m*n*sqrt(d);
        float s = sign(q + h)*pow(abs(q + h), 1.0 / 3.0);
        float u = sign(q - h)*pow(abs(q - h), 1.0 / 3.0);
        float rx = -s - u - c*4.0 + 2.0*m2;
        float ry = (s - u)*sqrt(3.0);
        float rm = sqrt(rx*rx + ry*ry);
        co = (ry / sqrt(rm - rx) + 2.0*g / rm - m) / 2.0;
    }
    float2 r = ab * float2(co, sqrt(1.0 - co*co));
    return length(r - p) * sign(p.y - r.y);
}

float sdBezier(in float2 pos, in float2 A, in float2 B, in float2 C) {
    float2 a = B - A;
    float2 b = A - 2.0*B + C;
    float2 c = a * 2.0;
    float2 d = A - pos;
    float dotB = dot(b, b);
    if (abs(dotB) < 0.0001)
        dotB = 0.0001 * sign(dotB);
    float kk = 1.0 / dotB;
    float kx = kk * dot(a, b);
    float ky = kk * (2.0*dot(a, a) + dot(d, b)) / 3.0;
    float kz = kk * dot(d, a);
    float res = 0.0;
    float p = ky - kx*kx;
    float p3 = p*p*p;
    float q = kx*(2.0*kx*kx - 3.0*ky) + kz;
    float h = q*q + 4.0*p3;
    if (h >= 0.0)
    {
        h = sqrt(h);
        float2 x = (float2(h, -h) - q) / 2.0;
        float2 uv = sign(x)*pow(abs(x), 1.0 / 3.0);
        float t = uv.x + uv.y - kx;
        t = clamp(t, 0.0, 1.0);
        float2 qos = d + (c + b*t)*t;
        res = dot(qos, qos);
    }
    else
    {
        float z = sqrt(-p);
        float pz2 = (p*z*2.0);
        if (abs(pz2) < 0.0001)
            pz2 = 0.0001 * sign(pz2);
        float v = acos(q / pz2) / 3.0;
        float m = cos(v);
        float n = sin(v)*1.732050808;
        float3 t = float3(m + m, -n - m, n - m) * z - kx;
        t = clamp(t, 0.0, 1.0);
        float2 qos = d + (c + b*t.x)*t.x;
        res = dot(qos, qos);
        qos = d + (c + b*t.y)*t.y;
        res = min(res, dot(qos, qos));
        qos = d + (c + b*t.z)*t.z;
        res = min(res, dot(qos, qos));
    }
    return sqrt(res);
}

float sdTriangle(in float2 p, in float2 p0, in float2 p1, in float2 p2) {
    float2 e0 = p1 - p0, e1 = p2 - p1, e2 = p0 - p2;
    float2 v0 = p - p0, v1 = p - p1, v2 = p - p2;
    float2 pq0 = v0 - e0*clamp(dot(v0, e0) / dot(e0, e0), 0.0, 1.0);
    float2 pq1 = v1 - e1*clamp(dot(v1, e1) / dot(e1, e1), 0.0, 1.0);
    float2 pq2 = v2 - e2*clamp(dot(v2, e2) / dot(e2, e2), 0.0, 1.0);
    float s = sign(e0.x*e2.y - e0.y*e2.x);
    float2 d = min(min(float2(dot(pq0, pq0), s*(v0.x*e0.y - v0.y*e0.x)),
        float2(dot(pq1, pq1), s*(v1.x*e1.y - v1.y*e1.x))),
        float2(dot(pq2, pq2), s*(v2.x*e2.y - v2.y*e2.x)));
    return -sqrt(d.x)*sign(d.y);
}

float sdArc (in float2 p, in float2 sca, in float2 scb, in float ra, float rb) {
    p = mul(p, float2x2(sca.x, sca.y, -sca.y, sca.x));
    p.x = abs(p.x);
    float k = (scb.y*p.x>scb.x*p.y) ? dot(p.xy, scb) : length(p.xy);
    return sqrt(dot(p, p) + ra*ra - 2.0*ra*k) - rb;
}


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
    uint type, float2 radius, float totalRadius, 
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

        case TYPE_Arc:
            tl = a - totalRadius - radius.y;
            br = a + totalRadius + radius.y;
            break;
    }
}

void computePosition (
    uint type, float totalRadius, 
    float2 a, float2 b, float2 c,
    float2 tl, float2 br, int cornerIndex,
    out float2 xy
) {
    DEFINE_QuadCorners
        
    if (type == TYPE_LineSegment) {
        // Oriented bounding box around the line segment
        float2 along = b - a,
            alongNorm = normalize(along) * (totalRadius + 1),
            left = alongNorm.yx * float2(-1, 1),
            right = alongNorm.yx * float2(1, -1);

        if (cornerIndex.x == 0)
            xy = a + left - alongNorm;
        else if (cornerIndex.x == 1)
            xy = b + left + alongNorm;
        else if (cornerIndex.x == 2)
            xy = b + right + alongNorm;
        else
            xy = a + right - alongNorm;
    } else {
        // HACK: Padding
        tl -= 1; br += 1;
        // FIXME: Fit a better hull around triangles. Oriented bounding box?
        xy = lerp(tl, br, QuadCorners[cornerIndex.x]);
    }
}

void RasterShapeVertexShader (
    in int2 cornerIndex : BLENDINDICES0,
    in float4 ab_in : POSITION0,
    in float4 cd_in : POSITION1,
    inout float4 params : TEXCOORD0,
    inout float4 centerColor : COLOR0,
    inout float4 edgeColor : COLOR1,
    inout float4 outlineColor : COLOR2,
    inout int2 typeAndWorldSpace : BLENDINDICES1,
    out float4 result : POSITION0,
    out float4 ab : TEXCOORD2,
    out float4 cd : TEXCOORD3,
    inout float4 texRgn : TEXCOORD1,
    out float2 worldPosition : NORMAL0
) {
    ab = ab_in; cd = cd_in;
    float4 position = float4(ab_in.x, ab_in.y, 0, 1);
    float2 a = ab.xy, b = ab.zw, c = cd.xy, radius = cd.zw;
    params.x *= OutlineSizeCompensation;
    float outlineSize = params.x;
    uint type = abs(typeAndWorldSpace.x);

    float totalRadius = computeTotalRadius(radius, outlineSize) + 1;
    float2 tl, br;

    computeTLBR(type, radius, totalRadius, a, b, c, tl, br);
    computePosition(type, totalRadius, a, b, c, tl, br, cornerIndex.x, position.xy);

    float2 adjustedPosition = position.xy;
    if (typeAndWorldSpace.y > 0.5) {
        adjustedPosition -= GetViewportPosition().xy;
        adjustedPosition *= GetViewportScale().xy;
    }

    result = TransformPosition(
        float4(adjustedPosition, position.z, 1), true
    );
    worldPosition = position.xy;
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
    float gradientOffset = c.y;
    distance = (distanceF - 1) * length(b);
    gradientWeight = saturate(distanceF + gradientOffset);
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
            gradientWeight = saturate(max(distance2.x, distance2.y) + gradientOffset);
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
    float gradientOffset = c.y;

    PREFER_FLATTEN
    switch (gradientType) {
        // Linear
        case 0:
        case 4:
            gradientWeight = saturate(1 - saturate(distance / centerDistance) + gradientOffset);
            gradientType = 999;
            break;
    }
}

void evaluateTriangle (
    in float2 worldPosition, in float2 a, in float2 b, in float2 c,
    in float2 radius, out float distance,
    out float gradientWeight, out float2 tl, out float2 br
) {
    float gradientOffset = radius.y;
    distance = sdTriangle(worldPosition, a, b, c) - radius.x;

    float2 center = (a + b + c) / 3;
    float centroidDistance = sdTriangle(center, a, b, c) - radius.x;
    gradientWeight = saturate(distance / centroidDistance + gradientOffset);

    tl = min(min(a, b), c);
    br = max(max(a, b), c);
}

void rasterShapeCommon (
    in float2 worldPosition,
    in float4 ab, in float4 cd,
    in float4 params, in uint type,
    in float4 centerColor, in float4 edgeColor,
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

    float gradientOffset = 0;
    int gradientType = 999;

    PREFER_BRANCH
    switch (type) {
        case TYPE_Ellipse: {
            evaluateEllipse(
                worldPosition, a, b, c,
                distance, tl, br,
                gradientType, gradientWeight
            );

            break;
        }
        case TYPE_LineSegment: {
            evaluateLineSegment(
                worldPosition, a, b, c,
                radius, distance,
                gradientWeight
            );

            break;
        }
        case TYPE_QuadraticBezier: {
            distance = sdBezier(worldPosition, a, b, c) - radius.x;
            gradientWeight = 1 - saturate(-distance / radius.x);

            computeTLBR(type, radius, totalRadius, a, b, c, tl, br);

            break;
        }
        case TYPE_Rectangle: {
            evaluateRectangle(
                worldPosition, a, b, c,
                radius, distance,
                gradientType, gradientWeight
            );

            break;
        }
        case TYPE_Triangle: {
            evaluateTriangle(
                worldPosition, a, b, c,
                radius, distance,
                gradientWeight, tl, br
            );

            break;
        }
        case TYPE_Arc: {
            distance = sdArc(worldPosition - a, b, c, radius.x, radius.y);
            gradientWeight = 1 - saturate(-distance / radius.y);

            break;
        }
    }

    PREFER_FLATTEN
    switch (gradientType) {
        // Radial
        case 1:
            float2 center = (tl + br) / 2;
            gradientWeight = saturate(length((worldPosition - center) / ((br - tl) * 0.5)) + gradientOffset);
            break;
        // Horizontal
        case 2:
            gradientWeight = saturate((worldPosition.x - tl.x) / (br.x - tl.x) + gradientOffset);
            break;
        // Vertical
        case 3:
            gradientWeight = saturate((worldPosition.y - tl.y) / (br.y - tl.y) + gradientOffset);
            break;
    }

    float outlineSizeAlpha = saturate(outlineSize / 2);
    float clampedOutlineSize = max(outlineSize / 2, sqrt(2)) * 2;

    float outlineStartDistance = -(outlineSize * 0.5) + 0.5, 
        outlineEndDistance = outlineStartDistance + outlineSize,
        fillStartDistance = -0.5,
        fillEndDistance = 0.5;

    float4 composited;
    fillAlpha = getWindowAlpha(distance, fillStartDistance, fillEndDistance, 1, 1, 0);
    fill = lerp(centerColor, edgeColor, gradientWeight);

    PREFER_BRANCH
    if (outlineSize > 0.001) {
        if ((outlineSize >= sqrt(2)) && (HardOutline >= 0.5)) {
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

float4 composite (float4 fillColor, float4 outlineColor, float fillAlpha, float outlineAlpha, float convertToSRGB) {
    float4 result;
    result = over(outlineColor, outlineAlpha, fillColor, fillAlpha);

    if (convertToSRGB)
        result = pLinearToPSRGB(result);

    return result;
}

float4 texturedShapeCommon (
    in float2 worldPosition, in float4 texRgn,
    in float4 ab, in float4 cd,
    in float4 fill, in float4 outlineColor,
    in float fillAlpha, in float outlineAlpha,
    in float4 params, in float2 tl, in float2 br
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

    return composite(fill, outlineColor, fillAlpha, outlineAlpha, params.z);
}