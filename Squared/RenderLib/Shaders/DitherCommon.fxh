struct DitheringSettings {
    float Strength, Unit, InvUnit, FrameIndex;
    float BandSizeMinus1, RangeMin, RangeMaxMinus1;
};

uniform DitheringSettings Dithering;

#ifdef ENABLE_DITHERING

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
    float threshold = Dither17(vpos, (Dithering.FrameIndex % 4) + 0.5);
    threshold = (Dithering.BandSizeMinus1 + 1) * threshold;
    float3 threshold3 = float3(threshold, threshold, threshold);
    const float offset = 0.05;

    float3 rgb8 = rgb * Dithering.Unit;
    float3 a = trunc(rgb8), b = ceil(rgb8);
    float3 distanceFromA = rgb8 - a;
    float3 mask = 1.0 - step(distanceFromA, threshold3);
    float3 result = lerp(a, b, mask);
    float3 strength3 = Dithering.Strength * 
        smoothstep(Dithering.RangeMin - offset, Dithering.RangeMin, rgb) *
        1 - smoothstep(Dithering.RangeMaxMinus1 + 1, Dithering.RangeMaxMinus1 + 1 + offset, rgb);
    return lerp(rgb, result / Dithering.Unit, strength3);
}

#else

float3 ApplyDither (float3 rgb, float2 vpos) {
    return rgb;
}

#endif