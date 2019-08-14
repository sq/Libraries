struct DitheringSettings {
    float4 StrengthUnitAndIndex;
    float4 BandSizeAndRange;
};

uniform DitheringSettings Dithering;

float DitheringGetStrength () {
    return Dithering.StrengthUnitAndIndex.x;
}

float DitheringGetUnit () {
    return Dithering.StrengthUnitAndIndex.y;
}

float DitheringGetInvUnit () {
    return Dithering.StrengthUnitAndIndex.z;
}

float DitheringGetFrameIndex () {
    return Dithering.StrengthUnitAndIndex.w;
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
    // FIXME: This flickers every few frames in OpenGL
    // float threshold = Dither32(vpos, (DitheringGetFrameIndex() % 4) + 0.5);
    // So does this
    // float threshold = Dither64(vpos, (DitheringGetFrameIndex() % 4) + 0.5);
    // This does not
    float threshold = Dither17(vpos, (DitheringGetFrameIndex() % 4) + 0.5);

    threshold = (DitheringGetBandSizeMinus1() + 1) * threshold;
    float3 threshold3 = threshold;
    const float offset = 0.05;

    // HACK: The dithering seems to bias values down slightly, so bias the input up
    // Without this you get unusually large dark areas at the center of a black->white radial gradient
    const float colorOffset = 0.5 / 255;
    float3 rgb8 = rgb * DitheringGetUnit();

    float3 a = trunc(rgb8), b = ceil(rgb8);
    float3 distanceFromA = rgb8 - a;
    float3 mask = 1.0 - step(distanceFromA, threshold3);
    float3 result = lerp(a, b, mask);
    float3 strength3 = DitheringGetStrength() * 
        smoothstep(DitheringGetRangeMin() - offset, DitheringGetRangeMin(), rgb) *
        1 - smoothstep(DitheringGetRangeMaxMinus1() + 1, DitheringGetRangeMaxMinus1() + 1 + offset, rgb);
    return lerp(rgb, result * DitheringGetInvUnit(), strength3);
}

#else

float3 ApplyDither (float3 rgb, float2 vpos) {
    return rgb;
}

#endif