#include "ViewTransformCommon.fxh"
#include "BitmapCommon.fxh"

uniform float2 LightmapUVOffset;

void LightmappedPixelShader(
    in float4 multiplyColor : COLOR0, 
    in float4 addColor : COLOR1, 
    in float2 texCoord : TEXCOORD0,
    in float2 texTL : TEXCOORD1,
    in float2 texBR : TEXCOORD2,
    out float4 result : COLOR0
) {
    float2 lightmapTexCoord = clamp(texCoord + LightmapUVOffset, texTL, texBR);

    float4 lightmapColor = tex2D(TextureSampler2, lightmapTexCoord) * 2;
    lightmapColor.a = 1;

    addColor.rgb *= addColor.a;
    addColor.a = 0;

    multiplyColor = multiplyColor * lightmapColor;

    texCoord = clamp(texCoord, texTL, texBR);

    result = multiplyColor * tex2D(TextureSampler, texCoord);
    result += (addColor * result.a);

    const float discardThreshold = (1.0 / 255.0);
    clip(result.a - discardThreshold);
}

void sRGBLightmappedPixelShader(
    in float4 multiplyColor : COLOR0,
    in float4 addColor : COLOR1,
    in float2 texCoord : TEXCOORD0,
    in float2 texTL : TEXCOORD1,
    in float2 texBR : TEXCOORD2,
    out float4 result : COLOR0
) {
    float2 lightmapTexCoord = clamp(texCoord + LightmapUVOffset, texTL, texBR);

    float4 lightmapColor = tex2D(TextureSampler2, lightmapTexCoord) * 2;
    lightmapColor.a = 1;

    addColor.rgb *= addColor.a;
    addColor.a = 0;

    multiplyColor = multiplyColor * lightmapColor;

    texCoord = clamp(texCoord, texTL, texBR);

    float4 linearTexColor = tex2D(TextureSampler, texCoord);
    linearTexColor.rgb = SRGBToLinear(linearTexColor.rgb);

    result = multiplyColor * linearTexColor;
    result += (addColor * result.a);

    result.rgb = LinearToSRGB(result.rgb);

    const float discardThreshold = (1.0 / 255.0);
    clip(result.a - discardThreshold);
}

technique ScreenSpaceLightmappedBitmap
{
    pass P0
    {
        vertexShader = compile vs_3_0 ScreenSpaceVertexShader();
        pixelShader = compile ps_3_0 LightmappedPixelShader();
    }
}

technique WorldSpaceLightmappedBitmap
{
    pass P0
    {
        vertexShader = compile vs_3_0 WorldSpaceVertexShader();
        pixelShader = compile ps_3_0 LightmappedPixelShader();
    }
}

technique ScreenSpaceLightmappedsRGBBitmap
{
    pass P0
    {
        vertexShader = compile vs_3_0 ScreenSpaceVertexShader();
        pixelShader = compile ps_3_0 sRGBLightmappedPixelShader();
    }
}

technique WorldSpaceLightmappedsRGBBitmap
{
    pass P0
    {
        vertexShader = compile vs_3_0 WorldSpaceVertexShader();
        pixelShader = compile ps_3_0 sRGBLightmappedPixelShader();
    }
}