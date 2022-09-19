#include "CompilerWorkarounds.fxh"
#include "ViewTransformCommon.fxh"
#include "FormatCommon.fxh"
#include "BitmapCommon.fxh"
#include "TargetInfo.fxh"

#define MaxDistance 8192

uniform const bool Smoothing;

float ScreenDistanceSquared(float2 xy) {
    return dot(xy, xy);
}

float tap (float2 texCoord, float4 texRgn) {
    float2 coordClamped = clamp(texCoord, texRgn.xy, texRgn.zw);
    float4 input = tex2D(TextureSampler, coordClamped);
    return ExtractMask(input, BitmapTraits);
}

float2 calculateNormal(
    float2 texCoord, float4 texRgn
) {
    float3 spacing = float3(HalfTexel, 0);
    float epsilon = 0.001;

    float a = tap(texCoord - spacing.xz, texRgn), b = tap(texCoord + spacing.xz, texRgn),
        c = tap(texCoord - spacing.zy, texRgn), d = tap(texCoord + spacing.zy, texRgn),
        center = tap(texCoord, texRgn);

    float2 result = float2(
        a - b,
        c - d
    );
    float l = length(result);
    if (l <= epsilon)
        result = 0;
    else
        result /= l;
    return result;
}

void JumpFloodInitShader(
    in float2 texCoord : TEXCOORD0,
    in float4 texRgn : TEXCOORD1,
    // minimumAlpha, smoothingLevel, unused, unused
    in float4 params : COLOR2,
    out float4 result : COLOR0
) {
    float md2 = ScreenDistanceSquared(float2(MaxDistance, MaxDistance));
    float2 coordClamped = clamp(texCoord, texRgn.xy, texRgn.zw);
    float4 input = tex2D(TextureSampler, coordClamped);
    float alpha = ExtractMask(input, BitmapTraits);
    if (Smoothing) {
        if ((alpha > params.x) && (alpha < params.y)) {
            // For pixels with low opacity, we use the opacity as approximate coverage and generate
            //  an approximate vector pointing towards the nearest pixel with a higher coverage,
            //  which we will treat as 'inside' the volume. At a high smoothing level this results
            //  in a distance field that looks less jagged/incorrect for the exterior of antialiased
            //  shapes.
            // FIXME: This causes weird discontinuities right at the edge though :(
            float2 norm = calculateNormal(texCoord, texRgn);
            result = float4(norm.x, norm.y, ScreenDistanceSquared(norm), 0.0);
            return;
        }
    }

    // The normal CPU version of this algorithm stores the squared distance, however, if we
    //  store the non-squared distance it is able to fit easily into the range of a float16,
    //  and we can run this using HalfVector4 buffers instead of Vector4 without any issues
    result = float4(MaxDistance, MaxDistance, sqrt(md2), alpha > params.x + params.y ? 1.0 : 0.0);
}

void JumpFloodJumpShader(
    in float2 texCoord : TEXCOORD0,
    in float4 texRgn : TEXCOORD1,
    // stepU, stepV, step, unused
    in float4 params : COLOR2,
    out float4 result : COLOR0
) {
    float4 self = tex2D(TextureSampler, clamp(texCoord, texRgn.xy, texRgn.zw));
    // Distance is stored in non-squared form but we compare it squared
    self.z *= self.z;
    float2 texel = params.xy;

    for (int y = -1; y < 2; y++) {
        for (int x = -1; x < 2; x++) {
            if ((x == 0) && (y == 0))
                continue;
            float2 uv = texCoord + (float2(x, y) * texel);
            if (any(uv < texRgn.xy) || any(uv > texRgn.zw))
                continue;

            float4 n = tex2D(TextureSampler, uv);

            if (sign(n.w) != sign(self.w))
                n.xyz = 0;

            n.x += x * params.z;
            n.y += y * params.z;
            float distance = ScreenDistanceSquared(n.xy);
            if (distance < self.z) {
                self.xy = n.xy;
                self.z = distance;
            }
        }
    }

    self.z = sqrt(self.z);
    result = self;
}

void JumpFloodResolveShader(
    in float2 texCoord : TEXCOORD0,
    in float4 texRgn : TEXCOORD1,
    out float4 result : COLOR0
) {
    float2 coordClamped = clamp(texCoord, texRgn.xy, texRgn.zw);
    float4 input = tex2D(TextureSampler, coordClamped);
    float distance = input.z;
    if (input.w > 0)
        distance = -distance;
    result = float4(distance, distance, distance, input.w);
}

technique JumpFloodInit
{
    pass P0
    {
        vertexShader = compile vs_3_0 GenericVertexShader();
        pixelShader = compile ps_3_0 JumpFloodInitShader();
    }
}

technique JumpFloodJump
{
    pass P0
    {
        vertexShader = compile vs_3_0 GenericVertexShader();
        pixelShader = compile ps_3_0 JumpFloodJumpShader();
    }
}

technique JumpFloodResolve
{
    pass P0
    {
        vertexShader = compile vs_3_0 GenericVertexShader();
        pixelShader = compile ps_3_0 JumpFloodResolveShader();
    }
}
