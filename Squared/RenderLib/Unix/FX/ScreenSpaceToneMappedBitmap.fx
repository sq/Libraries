float4x4 ProjectionMatrix;
float4x4 ModelViewMatrix;

float4 TransformPosition (float4 position, float offset) {
    // Transform to view space, then offset by half a pixel to align texels with screen pixels
    float4 modelViewPos = mul(position, ModelViewMatrix) - float4(offset, offset, 0, 0);
    // Finally project after offsetting
    return mul(modelViewPos, ProjectionMatrix);
}

float2 BitmapTextureSize;

Texture2D BitmapTexture : register(t0);

sampler TextureSampler : register(s0) {
    Texture = (BitmapTexture);
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
/*
	const float2 Corners[] = {
	    {0, 0},
	    {1, 0},
	    {1, 1},
	    {0, 1}
	};
*/
	if (cornerIndex.x == 0) return regionSize * float2(0, 0);
	if (cornerIndex.x == 1) return regionSize * float2(1, 0);
	if (cornerIndex.x == 2) return regionSize * float2(1, 1);
	return regionSize * float2(0, 1);
/*
    float2 corner = Corners[cornerIndex.x];
    return corner * regionSize;
*/
}

inline float2 ComputeTexCoord(
    in int2 cornerIndex : BLENDINDICES0,
    in float2 corner,
    in float4 texRgn : POSITION1,
    out float2 texTL : TEXCOORD1,
    out float2 texBR : TEXCOORD2
) {
    texTL = min(texRgn.xy, texRgn.zw);
    texBR = max(texRgn.xy, texRgn.zw);
    return clamp(texRgn.xy + corner, texTL, texBR);
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
	);
}

void ScreenSpaceVertexShader(
    in float3 position : POSITION0, // x, y
    in float4 texRgn : POSITION1, // x1, y1, x2, y2
    in float4 scaleOrigin : POSITION2, // scalex, scaley, originx, originy
    in float rotation : POSITION3,
    inout float4 multiplyColor : COLOR0,
    inout float4 addColor : COLOR1,
    in int2 cornerIndex : BLENDINDICES0, // 0-3
    out float2 texCoord : TEXCOORD0,
    out float2 texTL : TEXCOORD1,
    out float2 texBR : TEXCOORD2,
    out float4 result : POSITION0
) {
	float2 regionSize = ComputeRegionSize(texRgn);
	float2 corner = ComputeCorner(cornerIndex, regionSize);
    texCoord = ComputeTexCoord(cornerIndex, corner, texRgn, texTL, texBR);
    float2 rotatedCorner = ComputeRotatedCorner(corner, texRgn, scaleOrigin, rotation);
    
    position.xy += rotatedCorner;

    result = TransformPosition(float4(position.xy, position.z, 1), 0.5);
}

float InverseScaleFactor;

float Exposure;
float WhitePoint;

float Uncharted2Tonemap1 (float value)
{
	const float kA = 0.15;
	const float kB = 0.50;
	const float kC = 0.10;
	const float kD = 0.20;
	const float kE = 0.02;
	const float kF = 0.30;
    return (
        (value * (kA * value + kC * kB) + kD * kE) / 
        (value * (kA * value + kB ) + kD * kF)
    ) - kE / kF;
}

float3 Uncharted2Tonemap (float3 rgb)
{
	const float kA = 0.15;
	const float kB = 0.50;
	const float kC = 0.10;
	const float kD = 0.20;
	const float kE = 0.02;
	const float kF = 0.30;
    return (
        (rgb * (kA * rgb + kC * kB) + kD * kE) / 
        (rgb * (kA * rgb + kB ) + kD * kF)
    ) - kE / kF;
}

void ToneMappedPixelShader(
    in float4 multiplyColor : COLOR0, 
    in float4 addColor : COLOR1, 
    in float2 texCoord : TEXCOORD0,
    in float2 texTL : TEXCOORD1,
    in float2 texBR : TEXCOORD2,
    out float4 result : COLOR0
) {
	addColor.rgb *= addColor.a;
	addColor.a = 0;

	result = multiplyColor * (tex2D(TextureSampler, clamp(texCoord, texTL, texBR)) * InverseScaleFactor);
	result += (addColor * result.a);

    result = float4(Uncharted2Tonemap(result.rgb * Exposure) / Uncharted2Tonemap1(WhitePoint), result.a);
}

technique ScreenSpaceToneMappedBitmap
{
    pass P0
    {
        vertexShader = compile vs_2_0 ScreenSpaceVertexShader();
        pixelShader = compile ps_2_0 ToneMappedPixelShader();
    }
}
