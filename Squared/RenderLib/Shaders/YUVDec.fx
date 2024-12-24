/*
 * This effect is based on the YUV-to-RGBA GLSL shader found in SDL.
 * Thus, it also released under the zlib license:
 * http://libsdl.org/license.php
 */

#include "CompilerWorkarounds.fxh"
#include "ViewTransformCommon.fxh"
#include "FormatCommon.fxh"
#include "BitmapCommon.fxh"
#include "TargetInfo.fxh"
#include "DitherCommon.fxh"
#include "sRGBCommon.fxh"

Texture2D ThirdTexture : register(t2);

sampler TextureSampler3 : register(s2)
{
    Texture = (ThirdTexture);
};

const float3 offset = float3(-0.0625, -0.5, -0.5);
const float3 Rcoeff = float3(1.164, 0.000, 1.793);
const float3 Gcoeff = float3(1.164, -0.213, -0.533);
const float3 Bcoeff = float3(1.164, 2.112, 0.000);

void YUVDecodePixelShader(
    in float4 multiplyColor : COLOR0, 
    in float4 addColor : COLOR1, 
    in float2 texCoord : TEXCOORD0,
    in float4 texRgn : TEXCOORD1,
    ACCEPTS_VPOS,
    out float4 result : COLOR0
) {
    addColor.rgb *= addColor.a;
    addColor.a = 0;

    float3 yuv;
    yuv.x = tex2D(TextureSampler, texCoord).r;
    yuv.y = tex2D(TextureSampler2, texCoord).r;
    yuv.z = tex2D(TextureSampler3, texCoord).r;
    yuv += offset;

    float4 texColor = float4(
        dot(yuv, Rcoeff),
        dot(yuv, Gcoeff),
        dot(yuv, Bcoeff),
        1.0
    );    
    
    result = multiplyColor * texColor;
    result.rgb = ApplyDither(result.rgb, GET_VPOS);
    result += (addColor * result.a);
}

technique YUVDecodeTechnique
{
    pass P0
    {
        vertexShader = compile vs_3_0 GenericVertexShader();
        pixelShader = compile ps_3_0 YUVDecodePixelShader();
    }
}
