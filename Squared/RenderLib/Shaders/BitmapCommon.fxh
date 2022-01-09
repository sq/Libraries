#ifndef MIP_BIAS
#define MIP_BIAS -0.5
#endif

#define ENABLE_DITHERING

uniform float HalfPixelOffset;

float4 TransformPosition (float4 position, bool halfPixelOffset) {
    // Transform to view space, then offset by half a pixel to align texels with screen pixels
    float4 modelViewPos = mul(position, GetViewportModelViewMatrix());
    if (halfPixelOffset && HalfPixelOffset)
        modelViewPos.xy -= 0.5;
    // Finally project after offsetting
    float4 result = mul(modelViewPos, GetViewportProjectionMatrix());
    return result;
}

uniform const float2 BitmapTextureSize, BitmapTextureSize2;
uniform const float2 HalfTexel, HalfTexel2;

Texture2D BitmapTexture : register(t0);

sampler TextureSampler : register(s0) {
    Texture = (BitmapTexture);
};

Texture2D SecondTexture : register(t1);

sampler TextureSampler2 : register(s1) {
    Texture = (SecondTexture);
};

inline float2 ComputeRegionSize (
    in float4 texRgn : POSITION1
) {
    return texRgn.zw - texRgn.xy;
}

inline float2 ComputeCorner (
    in float3 cornerWeights : NORMAL2,
    in float2 regionSize
) {
    return cornerWeights.xy * regionSize;
}

inline float2 ComputeTexCoord (
    in float2 corner,
    in float4 texRgn,
    out float4 newTexRgn
) {
    float2 texTL = min(texRgn.xy, texRgn.zw);
    float2 texBR = max(texRgn.xy, texRgn.zw);
    newTexRgn = float4(texTL.x, texTL.y, texBR.x, texBR.y);
    return clamp2(
        texRgn.xy + corner, texTL, texBR
    );
}

inline float2 ComputeTexCoord2 (
    in float2 cornerWeight,
    in float4 texRgn1, 
    in float4 texRgn2,
    out float4 newTexRgn
) {
    float4 actualRgn = (texRgn2.x <= -99999) ? texRgn1 : texRgn2;
    float2 texTL = min(actualRgn.xy, actualRgn.zw);
    float2 texBR = max(actualRgn.xy, actualRgn.zw);
    newTexRgn = float4(texTL.x, texTL.y, texBR.x, texBR.y);
    return lerp(texTL, texBR, cornerWeight);
}

inline float2 ComputeRotatedCorner (
    in float2 corner,
    in float4 texRgn : POSITION1,
    in float4 scaleOrigin : POSITION2, // scalex, scaley, originx, originy
    in float rotation : POSITION3
) {
    float2 regionSize = abs(texRgn.zw - texRgn.xy);

    corner = abs(corner);
    corner -= (scaleOrigin.zw * regionSize);
    float2 sinCos, rotatedCorner;
    corner *= scaleOrigin.xy;
    corner *= BitmapTextureSize;
    sincos(rotation, sinCos.x, sinCos.y);
    return float2(
        (sinCos.y * corner.x) - (sinCos.x * corner.y),
        (sinCos.x * corner.x) + (sinCos.y * corner.y)
    );
}

void ScreenSpaceVertexShader (
    in float4 positionAndRotation : POSITION0, // x, y, z, rot
    in float4 texRgn1 : POSITION1, // x1, y1, x2, y2
    in float4 texRgn2 : POSITION2, // x1, y1, x2, y2
    in float4 scaleOrigin : POSITION3, // scalex, scaley, originx, originy
    inout float4 multiplyColor : COLOR0,
    inout float4 addColor : COLOR1,
    inout float4 userData : COLOR2,
    in float3 cornerWeights : NORMAL2,
    out float2 texCoord1 : TEXCOORD0,
    out float4 newTexRgn1 : TEXCOORD1,
    out float2 texCoord2 : TEXCOORD2,
    out float4 newTexRgn2 : TEXCOORD3,
    out float4 result : POSITION0
) {
    float2 regionSize = ComputeRegionSize(texRgn1);
    float2 corner = ComputeCorner(cornerWeights, regionSize);
    texCoord1 = ComputeTexCoord(corner, texRgn1, newTexRgn1);
    texCoord2 = ComputeTexCoord2(cornerWeights.xy, texRgn1, texRgn2, newTexRgn2);
    float2 rotatedCorner = ComputeRotatedCorner(corner, texRgn1, scaleOrigin, positionAndRotation.w);
    
    positionAndRotation.xy += rotatedCorner;

    float z = ScaleZIntoViewTransformSpace(positionAndRotation.z);
    result = TransformPosition(float4(positionAndRotation.xy, z, 1), true);
}

void WorldSpaceVertexShader (
    in float4 positionAndRotation : POSITION0, // x, y
    in float4 texRgn1 : POSITION1, // x1, y1, x2, y2
    in float4 texRgn2 : POSITION2, // x1, y1, x2, y2
    in float4 scaleOrigin : POSITION3, // scalex, scaley, originx, originy
    inout float4 multiplyColor : COLOR0,
    inout float4 addColor : COLOR1,
    inout float4 userData : COLOR2,
    in float3 cornerWeights : NORMAL2,
    out float2 texCoord1 : TEXCOORD0,
    out float4 newTexRgn1 : TEXCOORD1,
    out float2 texCoord2 : TEXCOORD2,
    out float4 newTexRgn2 : TEXCOORD3,
    out float4 result : POSITION0
) {
    float2 regionSize = ComputeRegionSize(texRgn1);
    float2 corner = ComputeCorner(cornerWeights, regionSize);
    texCoord1 = ComputeTexCoord(corner, texRgn1, newTexRgn1);
    texCoord2 = ComputeTexCoord2(cornerWeights.xy, texRgn1, texRgn2, newTexRgn2);
    float2 rotatedCorner = ComputeRotatedCorner(corner, texRgn1, scaleOrigin, positionAndRotation.w);
    
    positionAndRotation.xy += rotatedCorner - GetViewportPosition().xy;
    
    float z = ScaleZIntoViewTransformSpace(positionAndRotation.z);
    result = TransformPosition(float4(positionAndRotation.xy * GetViewportScale().xy, z, 1), true);
}

void GenericVertexShader (
    in float4 positionAndRotation : POSITION0, // x, y, z, rot
    in float4 texRgn1 : POSITION1, // x1, y1, x2, y2
    in float4 texRgn2 : POSITION2, // x1, y1, x2, y2
    in float4 scaleOrigin : POSITION3, // scalex, scaley, originx, originy
    inout float4 multiplyColor : COLOR0,
    inout float4 addColor : COLOR1,
    inout float4 userData : COLOR2,
    in float3 cornerWeights : NORMAL2,
    inout int2 worldSpace : BLENDINDICES1,
    out float2 texCoord1 : TEXCOORD0,
    out float4 newTexRgn1 : TEXCOORD1,
    out float2 texCoord2 : TEXCOORD2,
    out float4 newTexRgn2 : TEXCOORD3,
    // originX, originY, vertexX, vertexY
    out float4 originalPositionData : TEXCOORD7,
    out float4 result : POSITION0
) {
    float2 regionSize = ComputeRegionSize(texRgn1);
    float2 corner = ComputeCorner(cornerWeights, regionSize);
    texCoord1 = ComputeTexCoord(corner, texRgn1, newTexRgn1);
    texCoord2 = ComputeTexCoord2(cornerWeights.xy, texRgn1, texRgn2, newTexRgn2);
    float2 rotatedCorner = ComputeRotatedCorner(corner, texRgn1, scaleOrigin, positionAndRotation.w);
    
    float2 adjustedPosition = positionAndRotation.xy + rotatedCorner;
    originalPositionData = float4(positionAndRotation.xy, adjustedPosition.xy);

    if (worldSpace.x > 0.5) {
        adjustedPosition.xy -= GetViewportPosition().xy;
        adjustedPosition.xy *= GetViewportScale().xy;
    }
    
    float z = ScaleZIntoViewTransformSpace(positionAndRotation.z);
    result = TransformPosition(float4(adjustedPosition, z, 1), true);
}

void ExtractLuminanceAndAlpha (float4 input, float4 valueMask, float channelCount, out float luminance, out float alpha) {
    const float3 toGray = float3(0.299, 0.587, 0.144);

    switch (abs((int)channelCount)) {
        case 1:
            luminance = dot(input * valueMask, 1);
            alpha = 1;
            break;
        case 2:
            // Assuming RG for use as LuminanceAlpha
            luminance = input.r;
            alpha = input.g;
            break;
        case 3:
            luminance = dot(input.rgb * toGray, 1);
            alpha = 1;
            break;
        default:
            luminance = dot(input.rgb * toGray, 1);
            alpha = input.a;
            break;
    }
}