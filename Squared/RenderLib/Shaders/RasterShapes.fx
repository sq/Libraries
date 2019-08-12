#define ENABLE_DITHERING 1

#include "ViewTransformCommon.fxh"
#include "TargetInfo.fxh"
#include "DitherCommon.fxh"

Texture2D RasterTexture : register(t0);

sampler TextureSampler : register(s0) {
    Texture = (RasterTexture);
};

// HACK suggested by Sean Barrett: Increase all line widths to ensure that a diagonal 1px-thick line covers one pixel
#define OutlineSizeCompensation 2.4

// Approximations from http://chilliant.blogspot.com/2012/08/srgb-approximations-for-hlsl.html

// Input is premultiplied, output is not
float4 pSRGBToLinear (float4 srgba) {
    float3 srgb = srgba.rgb / max(srgba.a, 0.0001);
    float3 result = srgb * (srgb * (srgb * 0.305306011 + 0.682171111) + 0.012522878);
    return float4(result, srgba.a);
}

float4 Unpremultiply (float4 color) {
    color.rgb /= max(color.a, 0.0001);
    return color;
}

// Neither input or output are premultiplied!
float4 LinearToSRGB (float4 rgba) {
    float3 rgb = rgba.rgb;
    float3 S1 = sqrt(rgb);
    float3 S2 = sqrt(S1);
    float3 S3 = sqrt(S2);
    float3 result = 0.662002687 * S1 + 0.684122060 * S2 - 0.323583601 * S3 - 0.0225411470 * rgb;
    return float4(result, rgba.a);
}

// A bunch of the distance formulas in here are thanks to inigo quilez
// http://iquilezles.org/www/articles/distfunctions2d/distfunctions2d.htm
// http://iquilezles.org/www/articles/distfunctions/distfunctions.htm

#define TYPE_Ellipse 0
#define TYPE_LineSegment 1
#define TYPE_Rectangle 2
#define TYPE_Triangle 3
#define TYPE_QuadraticBezier 4

#define DEFINE_QuadCorners const float2 QuadCorners[] = { \
    {0, 0}, \
    {1, 0}, \
    {1, 1}, \
    {0, 1} \
};

#define RASTERSHAPE_VS_ARGS \
    in int2 cornerIndex : BLENDINDICES0, \
    in float4 ab_in : POSITION0, \
    in float4 cd_in : POSITION1, \
    inout float4 params : TEXCOORD0, \
    inout float4 centerColor : COLOR0, \
    inout float4 edgeColor : COLOR1, \
    inout float4 outlineColor : COLOR2, \
    out float4 result : POSITION0, \
    out float4 ab : TEXCOORD2, \
    out float4 cd : TEXCOORD3, \
    inout float4 texRgn : TEXCOORD1, \
    out float2 worldPosition : NORMAL0

#define RASTERSHAPE_VS_PROLOGUE \
    ab = ab_in; cd = cd_in; \
    float4 position = float4(ab_in.x, ab_in.y, 0, 1); \
    float2 a = ab.xy, b = ab.zw, c = cd.xy, radius = cd.zw; \
    params.x *= OutlineSizeCompensation; \
    float outlineSize = params.x; \
    int type = params.y;

#define RASTERSHAPE_FS_ARGS \
    in float2 worldPosition : NORMAL0, \
    in float4 ab : TEXCOORD2, \
    in float4 cd : TEXCOORD3, \
    in float4 params : TEXCOORD0, \
    in float4 centerColor : COLOR0, \
    in float4 edgeColor : COLOR1, \
    in float4 outlineColor : COLOR2, \
    in float4 texRgn : TEXCOORD1, \
    ACCEPTS_VPOS, \
    out float4 result : COLOR0

#define RASTERSHAPE_FS_PASS_ARGS \
    worldPosition, ab, cd, \
    params, centerColor, edgeColor, outlineColor, \
    texRgn, RAW_VPOS, result

#define RASTERSHAPE_FS_PROLOGUE \
    float2 a = ab.xy, b = ab.zw, c = cd.xy, radius = cd.zw; \
    float outlineSize = params.x; \
    int type = params.y; \
    float BlendInLinearSpace = params.z; \
    if (BlendInLinearSpace) { \
        centerColor = pSRGBToLinear(centerColor); \
        edgeColor = pSRGBToLinear(edgeColor); \
        outlineColor = pSRGBToLinear(outlineColor); \
    } else { \
        centerColor = Unpremultiply(centerColor); \
        edgeColor = Unpremultiply(edgeColor); \
        outlineColor = Unpremultiply(outlineColor); \
    } \
    float OutlineGammaMinusOne = params.w;

uniform float HalfPixelOffset;

float4 TransformPosition(float4 position, bool halfPixelOffset) {
    // Transform to view space, then offset by half a pixel to align texels with screen pixels
    float4 modelViewPos = mul(position, Viewport.ModelView);
    if (halfPixelOffset && HalfPixelOffset)
        modelViewPos.xy -= 0.5;
    // Finally project after offsetting
    return mul(modelViewPos, Viewport.Projection);
}

float computeTotalRadius (float radius, float outlineSize) {
    return radius + outlineSize;
}

void computeTLBR (
    int type, float totalRadius, 
    float2 a, float2 b, float2 c,
    out float2 tl, out float2 br
) {
    if (type == TYPE_Ellipse) {
        tl = a - b - totalRadius;
        br = a + b + totalRadius;
    } else if (type == TYPE_LineSegment) {
        tl = min(a, b);
        br = max(a, b);
    } else if (type == TYPE_Rectangle) {
        tl = min(a, b) - totalRadius;
        br = max(a, b) + totalRadius;
    } else if (type == TYPE_Triangle) {
        totalRadius += 1;
        tl = min(min(a, b), c) - totalRadius;
        br = max(max(a, b), c) + totalRadius;
    } else if (type == TYPE_QuadraticBezier) {
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
    }
}

void computePosition (
    int type, float totalRadius, 
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

void ScreenSpaceRasterShapeVertexShader (
    RASTERSHAPE_VS_ARGS
) {
    RASTERSHAPE_VS_PROLOGUE
    float totalRadius = computeTotalRadius(radius, outlineSize * 2);
    float2 tl, br;

    computeTLBR(type, totalRadius, a, b, c, tl, br);
    computePosition(type, totalRadius, a, b, c, tl, br, cornerIndex.x, position.xy);

    result = TransformPosition(
        float4(position.xy, position.z, 1), true
    );
    worldPosition = position.xy;
}

void WorldSpaceRasterShapeVertexShader(
    RASTERSHAPE_VS_ARGS
) {
    RASTERSHAPE_VS_PROLOGUE
    float totalRadius = computeTotalRadius(radius, outlineSize * 2);
    float2 tl, br;

    computeTLBR(type, totalRadius, a, b, c, tl, br);
    computePosition(type, totalRadius, a, b, c, tl, br, cornerIndex.x, position.xy);

    result = TransformPosition(
        float4(position.xy * GetViewportScale().xy, position.z, 1), true
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

float sdBox (in float2 p, in float2 b) {
    float2 d = abs(p) - b;
    return length(max(d, 0)) + min(max(d.x, d.y), 0.0);
}

float sdEllipse (in float2 p, in float2 ab) {
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
    } else {
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

float sdBezier (in float2 pos, in float2 A, in float2 B, in float2 C) {
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

float sdTriangle (in float2 p, in float2 p0, in float2 p1, in float2 p2) {
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

void rasterShapeCommon (
    RASTERSHAPE_FS_ARGS,
    out float2 tl, out float2 br
) {
    RASTERSHAPE_FS_PROLOGUE;

    // HACK
    outlineSize = max(abs(outlineSize), 0.0001);

    const float threshold = (1 / 512.0);

    float totalRadius = computeTotalRadius(radius, outlineSize);
    float2 invRadius = 1.0 / max(radius, 0.0001);

    float distance = 0, gradientWeight = 0;

    computeTLBR(type, totalRadius, a, b, c, tl, br);

    if (type == TYPE_Ellipse) {
        distance = sdEllipse(worldPosition - a, b);
        /*
        float centerDistance = sdEllipse(0, b);
        gradientWeight = length((worldPosition - a) / b);
        */
    } else if (type == TYPE_LineSegment) {
        float t;
        float2 closestPoint = closestPointOnLineSegment2(a, b, worldPosition, t);
        distance = length(worldPosition - closestPoint) - totalRadius;

        if (c.x >= 0.5)
            gradientWeight = saturate(t);
        else
            gradientWeight = 1 - saturate(-distance / totalRadius);
    }
    else if (type == TYPE_QuadraticBezier) {
        // FIXME: There's a lot wrong here
        distance = sdBezier(worldPosition, a, b, c) - totalRadius;
        gradientWeight = 1 - saturate(-distance / totalRadius);

        // HACK: I randomly guessed that I need to bias the distance value by sqrt(2)
        // Doing this makes the thickness of the line and its outline roughly match that
        //  of a regular line segment. No, I don't know why this works
        // Also we need to compute the gradient weight before doing this for some reason
        // distance *= sqrt(2);
    } else if (type == TYPE_Rectangle) {
        float2 center = (a + b) * 0.5;
        float2 boxSize = abs(b - a) * 0.5;
        distance = sdBox(worldPosition - center, boxSize) - totalRadius;

        float centerDistance = sdBox(0, boxSize) - totalRadius;
        // Implement radial gradient
        if (c.x >= 0.5)
            gradientWeight = saturate(length((worldPosition - center) / (boxSize + totalRadius)));
        else
            gradientWeight = 1 - saturate(distance / centerDistance);

        // FIXME: Gradient
        /*
        float2 d = abs(position) - size;
        distance = min(
            max(d.x, d.y),
            0.0    
        ) + length(max(d, 0.0)) + radius;

        float2 gradientSize = size + (radius * 0.5);

        if (c.x >= 0.5)
            gradientWeight = saturate(length(position / gradientSize));
        else
            gradientWeight = max(abs(position.x / gradientSize.x), abs(position.y / gradientSize.y));
        */
    } else if (type == TYPE_Triangle) {
        // FIXME: Transform to origin?
        distance = sdTriangle(worldPosition, a, b, c) - totalRadius;

        // FIXME: What is the correct divisor here?
        float2 center = (a + b + c) / 3;
        float centroidDistance = sdTriangle(center, a, b, c) - totalRadius;
        gradientWeight = saturate(distance / centroidDistance);
    }

    float4 gradient = lerp(centerColor, edgeColor, gradientWeight);

    float outlineStartDistance = -(outlineSize * 0.5), 
        outlineEndDistance = outlineStartDistance + outlineSize,
        fillStartDistance = -0.5,
        fillEndDistance = 0.5;

    float4 composited;
    float fillAlpha = getWindowAlpha(distance, fillStartDistance, fillEndDistance, 1, 1, 0);
    if (outlineSize > 0.001) {
        float outlineAlpha = getWindowAlpha(distance, outlineStartDistance, outlineEndDistance, 0, 1, 0);
        float outlineGamma = OutlineGammaMinusOne + 1;
        outlineAlpha = saturate(pow(outlineAlpha, outlineGamma));

        composited = lerp(gradient * fillAlpha, outlineColor, outlineAlpha);
    } else {
        composited = gradient * fillAlpha;
    }

    if (composited.a <= threshold) {
        result = 0;
        discard;
        return;
    }

    if (BlendInLinearSpace)
        composited = LinearToSRGB(composited);

    result = composited;
}

void RasterShapeUntextured (
    RASTERSHAPE_FS_ARGS
) {
    float2 tl, br;
    rasterShapeCommon(
        RASTERSHAPE_FS_PASS_ARGS,
        tl, br
    );

    // result += float4(0.3, 0, 0, 0.3);

    result.rgb = ApplyDither(result.rgb, GET_VPOS);
}

void RasterShapeTextured (
    RASTERSHAPE_FS_ARGS
) {
    float2 tl, br;
    rasterShapeCommon(
        RASTERSHAPE_FS_PASS_ARGS,
        tl, br
    );

    float2 sizePx = br - tl;
    float2 posRelative = worldPosition - tl;
    float2 texSize = (texRgn.zw - texRgn.xy);
    
    float2 texCoord = ((posRelative / sizePx) * texSize) + texRgn.xy;
    texCoord = clamp(texCoord, texRgn.xy, texRgn.zw);

    float4 texColor = tex2D(TextureSampler, texCoord);
    result *= texColor;

    // result += float4(0.3, 0, 0, 0.3);
    
    result.rgb = ApplyDither(result.rgb, GET_VPOS);
}

technique WorldSpaceRasterShape
{
    pass P0
    {
        vertexShader = compile vs_3_0 WorldSpaceRasterShapeVertexShader();
        pixelShader = compile ps_3_0 RasterShapeUntextured();
    }
}

technique ScreenSpaceRasterShape
{
    pass P0
    {
        vertexShader = compile vs_3_0 ScreenSpaceRasterShapeVertexShader();
        pixelShader = compile ps_3_0 RasterShapeUntextured();
    }
}

technique WorldSpaceTexturedRasterShape
{
    pass P0
    {
        vertexShader = compile vs_3_0 WorldSpaceRasterShapeVertexShader();
        pixelShader = compile ps_3_0 RasterShapeTextured();
    }
}

technique ScreenSpaceTexturedRasterShape
{
    pass P0
    {
        vertexShader = compile vs_3_0 ScreenSpaceRasterShapeVertexShader();
        pixelShader = compile ps_3_0 RasterShapeTextured();
    }
}