// TODO: Detect we're building in release mode and delegate directly to saturate? maybe it doesn't matter

float2 saturate2 (float2 v) {
    return float2(saturate(v.x), saturate(v.y));
}

float3 saturate3 (float3 v) {
    return float3(saturate(v.x), saturate(v.y), saturate(v.z));
}

float4 saturate4 (float4 v) {
    return float4(saturate(v.x), saturate(v.y), saturate(v.z), saturate(v.w));
}

// TODO: Should we use these workarounds or not?

#if 0
float2 clamp2 (float2 v, float2 minimum, float2 maximum) {
    // why the hell doesn't the constructor work
    float2 r;
    r.x = clamp(v.x, minimum.x, maximum.x);
    r.y = clamp(v.y, minimum.y, maximum.y);
    return r;
}

float3 clamp3 (float3 v, float3 minimum, float3 maximum) {
    // why the hell doesn't the constructor work
    float3 r;
    r.x = clamp(v.x, minimum.x, maximum.x);
    r.y = clamp(v.y, minimum.y, maximum.y);
    r.z = clamp(v.z, minimum.z, maximum.z);
    return r;
}

float4 clamp4 (float4 v, float4 minimum, float4 maximum) {
    // why the hell doesn't the constructor work
    float4 r;
    r.x = clamp(v.x, minimum.x, maximum.x);
    r.y = clamp(v.y, minimum.y, maximum.y);
    r.z = clamp(v.z, minimum.z, maximum.z);
    r.w = clamp(v.w, minimum.w, maximum.w);
    return r;
}
#else
float2 clamp2 (float2 v, float2 minimum, float2 maximum) {
    return clamp(v, minimum, maximum);
}

float3 clamp3 (float3 v, float3 minimum, float3 maximum) {
    return clamp(v, minimum, maximum);
}

float4 clamp4 (float4 v, float4 minimum, float4 maximum) {
    return clamp(v, minimum, maximum);
}
#endif

// HACK: branch hints can cause very bad things to happen when the conditionals are translated to GLSL by mojoshader.
// Sometimes it works without a hint at all but other times you need a flatten hint.

#define REQUIRE_FLATTEN [flatten]
#define REQUIRE_BRANCH [branch]
#define REQUIRE_LOOP [loop]

#define PREFER_FLATTEN [flatten]
#define PREFER_BRANCH [branch]