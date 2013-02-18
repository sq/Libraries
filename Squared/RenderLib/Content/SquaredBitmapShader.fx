#include "BitmapCommon.fxh"

void BasicPixelShader(
    in float4 multiplyColor : COLOR0, 
    in float4 addColor : COLOR1, 
    in float2 texCoord : TEXCOORD0,
    out float4 result : COLOR0
) {
	addColor.rgb *= addColor.a;
	addColor.a = 0;

	result = multiplyColor * tex2D(TextureSampler, texCoord);
	result += (addColor * result.a);

    const float discardThreshold = (1.0 / 255.0);
    clip(result.a - discardThreshold);
}

technique WorldSpaceBitmapTechnique
{
    pass P0
    {
        vertexShader = compile vs_1_1 WorldSpaceVertexShader();
        pixelShader = compile ps_2_0 BasicPixelShader();
    }
}

technique ScreenSpaceBitmapTechnique
{
    pass P0
    {
        vertexShader = compile vs_1_1 ScreenSpaceVertexShader();
        pixelShader = compile ps_2_0 BasicPixelShader();
    }
}