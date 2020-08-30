#include "CompilerWorkarounds.fxh"
#include "ViewTransformCommon.fxh"
#include "BitmapCommon.fxh"
#include "TargetInfo.fxh"
#include "DitherCommon.fxh"
#include "sRGBCommon.fxh"

void StippledPixelShader(
    in float4 multiplyColor : COLOR0,
    in float4 addColor : COLOR1,
    in float4 userData : COLOR2,
    in float2 texCoord : TEXCOORD0,
    in float4 texRgn : TEXCOORD1,
    ACCEPTS_VPOS,
    out float4 result : COLOR0
) {
    addColor.rgb *= addColor.a;
    addColor.a = 0;

    result = multiplyColor * tex2D(TextureSampler, clamp2(texCoord, texRgn.xy, texRgn.zw));
    result += (addColor * result.a);

    // Allows scaling the dither pattern up to larger sizes
    float stippleRatio = userData.z + 1;

    float2 vpos = GET_VPOS;
    // HACK: Without this, even at an opacity of 1/255 everything will be dithered at the lowest level.
    // This ensures that extremely transparent pixels are just plain invisible
    const float discardThreshold = (6.0 / 255.0);
    // HACK: Similar but for fully opaque
    const float forceOpaqueThreshold = (251 / 255.0);
    // If userData.R >= 0.5, the dithering will shift from frame to frame. You probably don't want this
    int frameIndex = userData.r >= 0.5 ? (DitheringGetFrameIndex() % 4) : 0;
    float ditherGamma = (userData.y >= 0.1 ? userData.y : 1);
    if (
        (result.a <= forceOpaqueThreshold) && (
            pow(result.a, ditherGamma) <= Dither64(floor(vpos / stippleRatio), frameIndex) || 
            (result.a <= discardThreshold)
        )
    ) {
        result = 0;
        discard;
        return;
    } else {
        // FIXME: Should we be doing this?
        float alpha = max(result.a, 0.0001);
        result.rgb /= alpha;
        result.a = 1;
    }
}

technique WorldSpaceStippledBitmapTechnique
{
    pass P0
    {
        vertexShader = compile vs_3_0 WorldSpaceVertexShader();
        pixelShader = compile ps_3_0 StippledPixelShader();
    }
}

technique ScreenSpaceStippledBitmapTechnique
{
    pass P0
    {
        vertexShader = compile vs_3_0 ScreenSpaceVertexShader();
        pixelShader = compile ps_3_0 StippledPixelShader();
    }
}
