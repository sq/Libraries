#define ENABLE_DITHERING 1

#include "ViewTransformCommon.fxh"
#include "TargetInfo.fxh"
#include "DitherCommon.fxh"

uniform float BlendInLinearSpace = 1;
uniform float OutlineGammaMinusOne = 0;

// Approximations from http://chilliant.blogspot.com/2012/08/srgb-approximations-for-hlsl.html

// Input is premultiplied, output is not
float4 pSRGBToLinear(float4 srgba) {
    float3 srgb = srgba.rgb / max(srgba.a, 0.0001);
    float3 result = srgb * (srgb * (srgb * 0.305306011 + 0.682171111) + 0.012522878);
    return float4(result, srgba.a);
}

// Neither input or output are premultiplied!
float4 LinearToSRGB(float4 rgba) {
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
    inout float2 outlineSizeAndType : TEXCOORD0, \
    inout float4 centerColor : COLOR0, \
    inout float4 edgeColor : COLOR1, \
    inout float4 outlineColor : COLOR2, \
    out float4 result : POSITION0, \
    out float4 ab : TEXCOORD1, \
    out float4 cd : TEXCOORD2, \
    out float2 screenPosition : NORMAL0

#define RASTERSHAPE_VS_PROLOGUE \
    ab = ab_in; cd = cd_in; \
    float4 position = float4(ab_in.x, ab_in.y, 0, 1); \
    float2 a = ab.xy, b = ab.zw, c = cd.xy, radius = cd.zw; \
    float outlineSize = outlineSizeAndType.x; \
    int type = outlineSizeAndType.y;

#define RASTERSHAPE_FS_ARGS \
    in float2 screenPosition : NORMAL0, \
    in float4 ab : TEXCOORD1, \
    in float4 cd : TEXCOORD2, \
    in float2 outlineSizeAndType : TEXCOORD0, \
    in float4 centerColor : COLOR0, \
    in float4 edgeColor : COLOR1, \
    in float4 outlineColor : COLOR2, \
    ACCEPTS_VPOS, \
    out float4 result : COLOR0 \

#define RASTERSHAPE_FS_PROLOGUE \
    float2 a = ab.xy, b = ab.zw, c = cd.xy, radius = cd.zw; \
    float outlineSize = outlineSizeAndType.x; \
    int type = outlineSizeAndType.y; \
    if (BlendInLinearSpace) { \
        centerColor = pSRGBToLinear(centerColor); \
        edgeColor = pSRGBToLinear(edgeColor); \
        outlineColor = pSRGBToLinear(outlineColor); \
    };

uniform float HalfPixelOffset;

float4 TransformPosition(float4 position, bool halfPixelOffset) {
    // Transform to view space, then offset by half a pixel to align texels with screen pixels
    float4 modelViewPos = mul(position, Viewport.ModelView);
    if (halfPixelOffset && HalfPixelOffset)
        modelViewPos.xy -= 0.5;
    // Finally project after offsetting
    return mul(modelViewPos, Viewport.Projection);
}

void computeTLBR (
    int type, float2 totalRadius, 
    float2 a, float2 b, float2 c,
    out float2 tl, out float2 br
) {
    if (type == TYPE_Ellipse) {
        tl = a - totalRadius;
        br = a + totalRadius;
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
    }
}

void computePosition (
    int type, float2 totalRadius, 
    float2 a, float2 b, float2 c,
    float2 tl, float2 br, int cornerIndex,
    out float2 xy
) {
    DEFINE_QuadCorners
        
    if (type == TYPE_LineSegment) {
        // Oriented bounding box around the line segment
        float2 along = b - a,
            alongNorm = normalize(along) * totalRadius,
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
        // FIXME: Fit a better hull around triangles. Oriented bounding box?
        xy = lerp(tl, br, QuadCorners[cornerIndex.x]);
    }
}

void ScreenSpaceRasterShapeVertexShader (
    RASTERSHAPE_VS_ARGS
) {
    RASTERSHAPE_VS_PROLOGUE
    float2 totalRadius = radius + (outlineSize * 2) + 1;
    float2 tl, br;

    computeTLBR(type, totalRadius, a, b, c, tl, br);
    computePosition(type, totalRadius, a, b, c, tl, br, cornerIndex.x, position.xy);

    result = TransformPosition(
        float4(position.xy, position.z, 1), true
    );
    screenPosition = position.xy;
}

void WorldSpaceRasterShapeVertexShader(
    RASTERSHAPE_VS_ARGS
) {
    RASTERSHAPE_VS_PROLOGUE
    float2 totalRadius = radius + (outlineSize * 2) + 1;
    float2 tl, br;

    computeTLBR(type, totalRadius, a, b, c, tl, br);
    computePosition(type, totalRadius, a, b, c, tl, br, cornerIndex.x, position.xy);

    result = TransformPosition(
        float4(position.xy * GetViewportScale().xy, position.z, 1), true
    );
    screenPosition = position.xy;
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

void RasterShapePixelShader(
    RASTERSHAPE_FS_ARGS
) {
    RASTERSHAPE_FS_PROLOGUE;

    const float threshold = (1 / 512.0);

    float2 totalRadius = radius + (outlineSize * 2) + 1;
    float  radiusLength = max(length(radius), 0.1);
    float2 invRadius = 1.0 / max(radius, float2(0.1, 0.1));

    float distanceF, distance, gradientWeight;
    float2 distanceXy;

    float2 tl, br;
    computeTLBR(type, totalRadius, a, b, c, tl, br);

    if (type == TYPE_Ellipse) {
        distanceXy = screenPosition - a;
        distanceF = length(distanceXy * invRadius);
        distance = distanceF * radiusLength;
        gradientWeight = saturate(distanceF);
    } else if (type == TYPE_LineSegment) {
        float t;
        float2 closestPoint = closestPointOnLineSegment2(a, b, screenPosition, t);
        distanceXy = screenPosition - closestPoint;
        distanceF = length(distanceXy * invRadius);
        distance = distanceF * radiusLength;
        if (c.x >= 0.5)
            gradientWeight = saturate(t);
        else
            gradientWeight = saturate(distanceF);
    } else if (type == TYPE_Rectangle) {
        float2 tl = min(a, b), br = max(a, b), center = (a + b) * 0.5;
        float2 position = screenPosition - center;
        float2 size = (br - tl) * 0.5;

        float2 d = abs(position) - size;
        distance = min(
            max(d.x, d.y),
            0.0    
        ) + length(max(d, 0.0)) + radius;

        distanceF = distance / size;
        // gradientWeight = 1 - saturate(-(distance - radius) / size);
        float2 gradientSize = size + (radius * 0.5);

        if (c.x >= 0.5)
            gradientWeight = length(position / gradientSize);
        else
            gradientWeight = max(abs(position.x / gradientSize.x), abs(position.y / gradientSize.y));
    } else if (type == TYPE_Triangle) {
        // FIXME: Transform to origin?
        float2 p = screenPosition;
        float2 e0 = b - a, e1 = c - b, e2 = a - c;
        float2 v0 = p - a, v1 = p - b, v2 = p - c;
        float2 pq0 = v0 - e0 * clamp(dot(v0, e0) / dot(e0, e0), 0.0, 1.0);
        float2 pq1 = v1 - e1 * clamp(dot(v1, e1) / dot(e1, e1), 0.0, 1.0);
        float2 pq2 = v2 - e2 * clamp(dot(v2, e2) / dot(e2, e2), 0.0, 1.0);
        float s = sign(e0.x*e2.y - e0.y*e2.x);
        float2 d = min(min(float2(dot(pq0, pq0), s*(v0.x*e0.y - v0.y*e0.x)),
            float2(dot(pq1, pq1), s*(v1.x*e1.y - v1.y*e1.x))),
            float2(dot(pq2, pq2), s*(v2.x*e2.y - v2.y*e2.x)));
        distanceF = -sqrt(d.x)*sign(d.y);
        distance = float2(distanceF, 0);

        // FIXME: What is the correct divisor here?
        float2 center = (a + b + c) / 3;
        float gradientScale = max(max(length(a - center), length(b - center)), length(c - center)) / 2;
        gradientWeight = saturate(-distanceF / gradientScale);
    }

    float4 gradient = lerp(centerColor, edgeColor, gradientWeight);

    if (outlineSize <= 0.0001)
        // This eliminates a dark halo around the edges of some shapes but has the downside
        //  of making everything look rounded instead of sharp-edged.
        // outlineColor = gradient;
        outlineColor = 0;

    float  outlineDistance = (distance - radiusLength) / max(outlineSize, 1);
    float  outlineWeight = saturate(outlineDistance);
    float  outlineGamma = OutlineGammaMinusOne + 1;
    float4 gradientToOutline = lerp(gradient, outlineColor, pow(outlineWeight, outlineGamma));
    float  transparentWeight = saturate(outlineDistance - 1);
    if (transparentWeight > (1 - threshold)) {
        discard;
        return;
    }

    transparentWeight = 1 - pow(1 - transparentWeight, outlineGamma);
    float4 color = BlendInLinearSpace ? LinearToSRGB(gradientToOutline) : gradientToOutline;
    float newAlpha = lerp(color.a, 0, transparentWeight);

    if (newAlpha <= threshold) {
        discard;
        return;
    }

    if (BlendInLinearSpace) {
        result = float4(ApplyDither(color.rgb * newAlpha, GET_VPOS), newAlpha);
    } else {
        result = lerp(color, 0, transparentWeight);
        result.rgb = ApplyDither(result.rgb, GET_VPOS);
    }
}

technique WorldSpaceRasterShape
{
    pass P0
    {
        vertexShader = compile vs_3_0 WorldSpaceRasterShapeVertexShader();
        pixelShader = compile ps_3_0 RasterShapePixelShader();
    }
}

technique ScreenSpaceRasterShape
{
    pass P0
    {
        vertexShader = compile vs_3_0 ScreenSpaceRasterShapeVertexShader();
        pixelShader = compile ps_3_0 RasterShapePixelShader();
    }
}