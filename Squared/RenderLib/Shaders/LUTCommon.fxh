#define LUTWidthPixels (LUTSliceWidthPixels * LUTSliceCount)
#define LUTHeightPixels LUTSliceHeightPixels
#define SliceSizeX (1.0 / LUTSliceCount)

void BlueToLUTBaseX (const float res, const float resMinus1, float value, out float x1, out float x2, out float weight) {
    float sliceIndexF = value * resMinus1;
    float sliceIndex1 = trunc(sliceIndexF);
    float sliceIndex2 = ceil(sliceIndexF);
    weight = (sliceIndexF - sliceIndex1);
    x1 = sliceIndex1 / res;
    x2 = sliceIndex2 / res;
}

float3 ReadLUT (sampler2D s, float2 resolutionAndRowCount, float3 value, float2 globalOffset) {
    float xResolution = resolutionAndRowCount.x, 
        yResolution = resolutionAndRowCount.x * resolutionAndRowCount.y;
    float resMinus1 = xResolution - 1.0;

    float x1, x2, weight;
    BlueToLUTBaseX(xResolution, resMinus1, value.b, x1, x2, weight);
    
    float2 size = float2(1.0 / xResolution, 1.0 / resolutionAndRowCount.y);
    // HACK: Rescale input values to account for the fact that a texture's coordinates are [-0.5 - size+0.5] instead of [0 - size]
    float rescale = float2(resMinus1 / xResolution, (yResolution - 1.0) / yResolution);
    // Half-texel offset
    // FIXME: Is the double multiply here right? I don't remember why it's here
    float2 offset = float2(1.0 / (2.0 * (xResolution * xResolution)), 1.0 / (2.0 * (yResolution * yResolution)));
    // FIXME
    // offset = 0;
    // Compute u/v within a slice for given red/green values (selected based on blue)
    float2 uvrg = (value.rg * rescale * size) + offset + globalOffset;

    float3 value1, value2;
    value1 = tex2Dlod(s, float4(uvrg + float2(x1, 0), 0, 0)).rgb;
    value2 = tex2Dlod(s, float4(uvrg + float2(x2, 0), 0, 0)).rgb;
    return lerp(value1, value2, weight);
}

#ifdef LUT_BITMAP
Texture2D LUT1 : register(t4);
Texture2D LUT2 : register(t5);
uniform const float4 LUTResolutionsAndRowCounts;
uniform const float LUT2Weight;
uniform const float4 LUTOffsets;

sampler LUT1Sampler : register(s4) {
    Texture = (LUT1);
    AddressU = CLAMP;
    AddressV = CLAMP;
    MipFilter = LINEAR;
    MinFilter = LINEAR;
    MagFilter = LINEAR;
};

sampler LUT2Sampler : register(s5) {
    Texture = (LUT2);
    AddressU = CLAMP;
    AddressV = CLAMP;
    MipFilter = LINEAR;
    MinFilter = LINEAR;
    MagFilter = LINEAR;
};

float3 ApplyLUT(float3 value, float lut2Weight) {
    value = saturate3(value);
    float3 tap1 = ReadLUT(LUT1Sampler, LUTResolutionsAndRowCounts.xz, value, LUTOffsets.xy);

    PREFER_BRANCH
    if (lut2Weight > 0) {
        float3 tap2 = ReadLUT(LUT2Sampler, LUTResolutionsAndRowCounts.yw, value, LUTOffsets.zw);
        return lerp(tap1, tap2, lut2Weight);
    } else {
        return tap1;
    }
}
#endif