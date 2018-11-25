#define LUTSlicesX 4
#define LUTSlicesY 4
#define LUTSliceCount (LUTSlicesX * LUTSlicesY)
#define LUTWidthPixels 64
#define LUTHeightPixels 64
#define LUTSliceWidthPixels (LUTWidthPixels / LUTSlicesX)
#define LUTSliceHeightPixels (LUTHeightPixels / LUTSlicesY)
#define SliceSizeX (1.0 / LUTSlicesX)
#define SliceSizeY (1.0 / LUTSlicesY)

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

float3 ApplyLUT (float3 value, float lut2Weight) {
    float2 uvA, uvB;
    float weight;
    float3 lutValueA, lutValueB, lut1Value, lut2Value;

    float2 size = float2(SliceSizeX, SliceSizeY);
    // HACK: Rescale input values to account for the fact that a texture's coordinates are [-0.5, size+0.5] instead of [0, 1]
    float2 rescale = float2((LUTSliceWidthPixels - 1.0) / LUTSliceWidthPixels, (LUTSliceHeightPixels - 1.0) / LUTSliceHeightPixels);
    // Half-texel offset
    float2 offset = float2(1.0 / (2.0 * LUTWidthPixels), 1.0 / (2.0 * LUTHeightPixels));
    // Compute u/v within a slice for given red/green values (selected based on blue)
    float2 uvrg = (value.rg * size * rescale) + offset;

    BlueToLUTBaseUV(value.b, uvA, uvB, weight);
    lutValueA = tex2Dlod(LUT1Sampler, float4(uvA + uvrg, 0, 0)).rgb;
    lutValueB = tex2Dlod(LUT1Sampler, float4(uvB + uvrg, 0, 0)).rgb;
    lut1Value = lerp(lutValueA, lutValueB, weight);

    lutValueA = tex2Dlod(LUT2Sampler, float4(uvA + uvrg, 0, 0)).rgb;
    lutValueB = tex2Dlod(LUT2Sampler, float4(uvB + uvrg, 0, 0)).rgb;
    lut2Value = lerp(lutValueA, lutValueB, weight);

    return lerp(lut1Value, lut2Value, lut2Weight);
}