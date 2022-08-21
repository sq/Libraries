#include "CompilerWorkarounds.fxh"
#include "ViewTransformCommon.fxh"
#include "FormatCommon.fxh"
#include "BitmapCommon.fxh"
#include "TargetInfo.fxh"

#define MaxDistance 10240

float ScreenDistanceSquared(float2 xy) {
    return dot(xy, xy);
}

void JumpFloodInitShader(
    in float2 texCoord : TEXCOORD0,
    in float4 texRgn : TEXCOORD1,
    out float4 result : COLOR0
) {
    float md2 = ScreenDistanceSquared(float2(MaxDistance, MaxDistance));
    float2 coordClamped = clamp(texCoord, texRgn.xy, texRgn.zw);
    float4 input = tex2D(TextureSampler, coordClamped);
    float alpha = ExtractMask(input, BitmapTraits);
    result = float4(MaxDistance, MaxDistance, md2, alpha > 0 ? 1.0 : 0.0);
}

void JumpFloodJumpShader(
    in float2 texCoord : TEXCOORD0,
    in float4 texRgn : TEXCOORD1,
    // stepX, stepY, unused, unused
    in float4 params : COLOR2,
    out float4 result : COLOR0
) {
    float md2 = ScreenDistanceSquared(float2(MaxDistance, MaxDistance));
    float4 self = tex2D(TextureSampler, clamp(texCoord, texRgn.xy, texRgn.zw));
    float2 texel = HalfTexel * 2 * params.xy;

    for (int y = -1; y < 2; y++) {
        for (int x = -1; x < 2; x++) {
            if ((x == 0) && (y == 0))
                continue;
            float2 uv = texCoord + (float2(x, y) * texel);
            if (any(uv < texRgn.xy) || any(uv > texRgn.zw))
                continue;

            uv = clamp(uv, texRgn.xy, texRgn.zw);
            float4 n = tex2D(TextureSampler, uv);

            if (n.w != self.w)
                n.xyz = 0;

            n.x += x;
            n.y += y;
            float distance = ScreenDistanceSquared(n.xy);
            if (distance < self.z) {
                self.xy = n.xy;
                self.z = distance;
            }
        }
    }

    result = self;
}

void JumpFloodResolveShader(
    in float2 texCoord : TEXCOORD0,
    in float4 texRgn : TEXCOORD1,
    out float4 result : COLOR0
) {
    float2 coordClamped = clamp(texCoord, texRgn.xy, texRgn.zw);
    float4 input = tex2D(TextureSampler, coordClamped);
    float distance = sqrt(input.z);
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
