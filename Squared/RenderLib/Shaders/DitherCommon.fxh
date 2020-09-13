struct DitheringSettings {
    float4 StrengthUnitAndIndex;
    float4 BandSizeAndRange;
};

uniform DitheringSettings Dithering;

float DitheringGetFrameIndex () {
    return Dithering.StrengthUnitAndIndex.w;
}

#ifdef ENABLE_DITHERING

float DitheringGetStrength () {
    return Dithering.StrengthUnitAndIndex.x;
}

float DitheringGetUnit () {
    return Dithering.StrengthUnitAndIndex.y;
}

float DitheringGetInvUnit () {
    return Dithering.StrengthUnitAndIndex.z;
}

float DitheringGetBandSizeMinus1 () {
    return Dithering.BandSizeAndRange.x;
}

float DitheringGetRangeMin () {
    return Dithering.BandSizeAndRange.y;
}

float DitheringGetRangeMaxMinus1 () {
    return Dithering.BandSizeAndRange.z;
}

float Dither17 (float2 vpos, float frameIndexMod4) {
    uint3 k0 = uint3(2, 7, 23);
    float ret = dot(float3(vpos, frameIndexMod4), k0 / 17.0f);
    return frac(ret);
}

float Dither32 (float2 vpos, float frameIndexMod4) {
    uint3 k0 = uint3(13, 5, 15);
    float ret = dot(float3(vpos, frameIndexMod4), k0 / 32.0f);
    return frac(ret);
}

float Dither64 (float2 vpos, float frameIndexMod4) {
    uint3 k0 = uint3(33, 52, 25);
    float ret = dot(float3(vpos, frameIndexMod4), k0 / 64.0f);
    return frac(ret);
}

float3 ApplyDither (float3 rgb, float2 vpos) {
    float threshold = Dither17(vpos, (DitheringGetFrameIndex() % 4) + 0.5);

    threshold = (DitheringGetBandSizeMinus1() + 1) * threshold;
    float3 threshold3 = threshold;

    const float strengthOffset = 0.05;
    const float offset = 0.0 / 255.0;

    float3 rgb8 = (rgb + offset) * DitheringGetUnit();
    float3 a = floor(rgb8), b = ceil(rgb8);
    float3 error = (b - rgb8);
    float3 mask = step(abs(error), threshold3);
    float3 result = lerp(a, b, mask);
    float3 strength3 = DitheringGetStrength() * 
        smoothstep(DitheringGetRangeMin() - strengthOffset, DitheringGetRangeMin(), rgb) *
        1 - smoothstep(DitheringGetRangeMaxMinus1() + 1, DitheringGetRangeMaxMinus1() + 1 + strengthOffset, rgb);
    return lerp(rgb, result * DitheringGetInvUnit(), strength3);
}

#else

float3 ApplyDither (float3 rgb, float2 vpos) {
    return rgb;
}

#endif

#ifdef ENABLE_STIPPLE

uniform const float thresholdMatrix[] = {
    1.0 / 17.0,  9.0 / 17.0,  3.0 / 17.0, 11.0 / 17.0,
    13.0 / 17.0,  5.0 / 17.0, 15.0 / 17.0,  7.0 / 17.0,
    4.0 / 17.0, 12.0 / 17.0,  2.0 / 17.0, 10.0 / 17.0,
    16.0 / 17.0,  8.0 / 17.0, 14.0 / 17.0,  6.0 / 17.0
};

bool StippleReject (float index, float stippleFactor) {
    if (stippleFactor >= 1)
        return false;

    float stippleThreshold = thresholdMatrix[index % 16];
    return (stippleFactor - stippleThreshold) <= 0;
}

#endif