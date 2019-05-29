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

float3 ReadLUT (sampler2D s, float resolution, float3 value) {
    float resMinus1 = resolution - 1.0;

    float x1, x2, weight;
    BlueToLUTBaseX(resolution, resMinus1, clamp(value.b, 0, 1), x1, x2, weight);
    
    float2 size = float2(1.0 / resolution, 1);
    // HACK: Rescale input values to account for the fact that a texture's coordinates are [-0.5, size+0.5] instead of [0, 1]
    float rescale = resMinus1 / resolution;
    // Half-texel offset
    float2 offset = float2(1.0 / (2.0 * (resolution * resolution)), 1.0 / (2.0 * resolution));
    // Compute u/v within a slice for given red/green values (selected based on blue)
    float2 uvrg = (clamp(value.rg, 0, 1) * rescale * size) + offset;

    float3 value1, value2;
    value1 = tex2Dlod(s, float4(uvrg + float2(x1, 0), 0, 0)).rgb;
    value2 = tex2Dlod(s, float4(uvrg + float2(x2, 0), 0, 0)).rgb;
    return lerp(value1, value2, weight);
}