shared float2 ViewportScale;
shared float2 ViewportPosition;

shared float4x4 ProjectionMatrix;

uniform const float2 BitmapTextureSize;
uniform const float2 Texel;

Texture2D BitmapTexture : register(t0);

sampler TextureSampler : register(s0) {
    Texture = (BitmapTexture);
};

Texture2D SecondTexture : register(t1);

sampler TextureSampler2 : register(s1) {
    Texture = (SecondTexture);
};

const float2 Corners[] = {
    {0, 0},
    {1, 0},
    {1, 1},
    {0, 1}
};

inline float2 ComputeRegionSize(
	in float4 texRgn : POSITION1
) {
	return texRgn.zw - texRgn.xy;
}

inline float2 ComputeCorner(
    in int2 cornerIndex : BLENDINDICES0,
    in float2 regionSize
) {
    float2 corner = Corners[cornerIndex.x];
    return corner * regionSize;
}

inline float2 ComputeTexCoord(
    in int2 cornerIndex : BLENDINDICES0,
    in float2 corner,
    in float4 texRgn : POSITION1
) {
    return (texRgn.xy + corner) + (Texel * 0.5);
}

inline float2 ComputeRotatedCorner(
	in float2 corner,
    in float4 texRgn : POSITION1,
    in float4 scaleOrigin : POSITION2, // scalex, scaley, originx, originy
    in float rotation : POSITION3
) {
	corner = abs(corner);
    corner -= (scaleOrigin.zw * abs(texRgn.zw - texRgn.xy));
    float2 sinCos, rotatedCorner;
    corner *= scaleOrigin.xy;
    corner *= BitmapTextureSize;
    sincos(rotation, sinCos.x, sinCos.y);
    return float2(
		(sinCos.y * corner.x) - (sinCos.x * corner.y),
		(sinCos.x * corner.x) + (sinCos.y * corner.y)
	) - (Texel * 0.5);
}

inline void OutputRegions(
    in float4 texRgn : POSITION1,
    out float2 texTL : TEXCOORD1,
    out float2 texBR : TEXCOORD2
) {
    float2 halfTexel = Texel * 0.5f;
    texTL = min(texRgn.xy, texRgn.zw) + halfTexel;
    texBR = max(texRgn.xy, texRgn.zw) - halfTexel;
}

void ScreenSpaceVertexShader(
    in float3 position : POSITION0, // x, y
    in float4 texRgn : POSITION1, // x1, y1, x2, y2
    in float4 scaleOrigin : POSITION2, // scalex, scaley, originx, originy
    in float rotation : POSITION3,
    in int2 cornerIndex : BLENDINDICES0, // 0-3
    inout float4 multiplyColor : COLOR0,
    inout float4 addColor : COLOR1,
    out float2 texCoord : TEXCOORD0,
    out float4 result : POSITION0,
    out float2 texTL : TEXCOORD1,
    out float2 texBR : TEXCOORD2
) {
	float2 regionSize = ComputeRegionSize(texRgn);
	float2 corner = ComputeCorner(cornerIndex, regionSize);
    texCoord = ComputeTexCoord(cornerIndex, corner, texRgn);
    float2 rotatedCorner = ComputeRotatedCorner(corner, texRgn, scaleOrigin, rotation);
    
    position.xy += rotatedCorner;
    
    result = mul(float4(position.xy, position.z, 1), ProjectionMatrix);
    OutputRegions(texRgn, texTL, texBR);
}

void WorldSpaceVertexShader(
    in float3 position : POSITION0, // x, y
    in float4 texRgn : POSITION1, // x1, y1, x2, y2
    in float4 scaleOrigin : POSITION2, // scalex, scaley, originx, originy
    in float rotation : POSITION3,
    in int2 cornerIndex : BLENDINDICES0, // 0-3
    inout float4 multiplyColor : COLOR0,
    inout float4 addColor : COLOR1,
    out float2 texCoord : TEXCOORD0,
    out float4 result : POSITION0,
    out float2 texTL : TEXCOORD1,
    out float2 texBR : TEXCOORD2
) {
	float2 regionSize = ComputeRegionSize(texRgn);
	float2 corner = ComputeCorner(cornerIndex, regionSize);
    texCoord = ComputeTexCoord(cornerIndex, corner, texRgn);
    float2 rotatedCorner = ComputeRotatedCorner(corner, texRgn, scaleOrigin, rotation);
    
    position.xy += rotatedCorner - ViewportPosition;
    
    result = mul(float4(position.xy * ViewportScale, position.z, 1), ProjectionMatrix);
    OutputRegions(texRgn, texTL, texBR);
}

void BasicPixelShader(
    in float4 multiplyColor : COLOR0, 
    in float4 addColor : COLOR1, 
    in float2 texCoord : TEXCOORD0,
    in float2 texTL : TEXCOORD1,
    in float2 texBR : TEXCOORD2,
    out float4 result : COLOR0
) {
	texCoord = clamp(texCoord, texTL, texBR);
	addColor.rgb *= addColor.a;
	addColor.a = 0;

	result = multiplyColor * tex2D(TextureSampler, texCoord);
	result += (addColor * result.a);

    if (result.a < (1 / 255))
        discard;
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
