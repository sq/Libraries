#define LUTSlicesX 4
#define LUTSlicesY 4
#define LUTSliceCount (LUTSlicesX * LUTSlicesY)
#define LUTWidthPixels 64
#define LUTHeightPixels 64
#define SliceSizeX (1.0 / LUTSlicesX)
#define SliceSizeY (1.0 / LUTSlicesY)

uniform const float LUT2Weight;

Texture2D LUT1 : register(t4);

sampler LUT1Sampler : register(s4) {
    Texture = (LUT1);
    AddressU  = CLAMP;
    AddressV  = CLAMP;
    MipFilter = LINEAR;
    MinFilter = LINEAR;
    MagFilter = LINEAR;
};

Texture2D LUT2 : register(t5);

sampler LUT2Sampler : register(s5) {
    Texture = (LUT2);
    AddressU  = CLAMP;
    AddressV  = CLAMP;
    MipFilter = LINEAR;
    MinFilter = LINEAR;
    MagFilter = LINEAR;
};

float2 SliceIndexToBaseUV (float sliceIndex) {
    float sliceIndexY = floor(sliceIndex / LUTSlicesX);
    float sliceIndexX = sliceIndex - (sliceIndexY * LUTSlicesX);
    return float2(sliceIndexX * SliceSizeX, sliceIndexY * SliceSizeY);
}

void BlueToLUTBaseUV (float value, out float2 uv1, out float2 uv2, out float weight) {
    float maxSliceIndex = LUTSliceCount - 1;
    float sliceIndexF = lerp(0, maxSliceIndex, value);
    float sliceIndex1 = clamp(floor(sliceIndexF), 0, maxSliceIndex);
    float sliceIndex2 = clamp(ceil(sliceIndexF), 0, maxSliceIndex);
    weight = (sliceIndexF - sliceIndex1);
    uv1 = SliceIndexToBaseUV(sliceIndex1);
    uv2 = SliceIndexToBaseUV(sliceIndex2);
}

float3 ApplyLUT (float3 value) {
    float2 uvA, uvB;
    float weight;
    float2 size = float2(SliceSizeX, SliceSizeY);
    float2 texel = float2(1.0 / LUTWidthPixels, 1.0 / LUTHeightPixels);
    float2 half = texel * 0.5;
    float2 uvrg = clamp(value.rg * size, half, size - half);
    float3 lutValueA, lutValueB, lut1Value, lut2Value;

    BlueToLUTBaseUV(value.b, uvA, uvB, weight);
    lutValueA = tex2Dlod(LUT1Sampler, float4(uvA + uvrg, 0, 0)).rgb;
    lutValueB = tex2Dlod(LUT1Sampler, float4(uvB + uvrg, 0, 0)).rgb;
    lut1Value = lerp(lutValueA, lutValueB, weight);

    lutValueA = tex2Dlod(LUT2Sampler, float4(uvA + uvrg, 0, 0)).rgb;
    lutValueB = tex2Dlod(LUT2Sampler, float4(uvB + uvrg, 0, 0)).rgb;
    lut2Value = lerp(lutValueA, lutValueB, weight);

    return lerp(lut1Value, lut2Value, LUT2Weight);
}