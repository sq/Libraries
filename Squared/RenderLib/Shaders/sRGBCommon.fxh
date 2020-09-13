// Approximations from http://chilliant.blogspot.com/2012/08/srgb-approximations-for-hlsl.html

float3 SRGBToLinear (float3 srgb) {
    float3 low = srgb / 12.92;
    float3 high = pow((srgb + 0.055) / 1.055, 2.4);
    return lerp(low, high, step(0.04045, srgb));
}

float3 LinearToSRGB (float3 rgb) {
    float3 low = rgb * 12.92;
    float3 high = 1.055 * pow(rgb, 1.0 / 2.4) - 0.055;
    return lerp(low, high, step(0.0031308, rgb));
}

float3 approxSRGBToLinear (float3 srgb) {
    return srgb * (srgb * (srgb * 0.305306011 + 0.682171111) + 0.012522878);
}

float3 approxLinearToSRGB (float3 rgb) {
    float3 S1 = sqrt(rgb);
    float3 S2 = sqrt(S1);
    float3 S3 = sqrt(S2);
    return 0.662002687 * S1 + 0.684122060 * S2 - 0.323583601 * S3 - 0.0225411470 * rgb;
}

float4 pSRGBToPLinear_Accurate (float4 psrgba) {
    float3 srgb = psrgba.rgb / max(psrgba.a, 0.00001);
    float3 linearRgb = SRGBToLinear(srgb);
    return float4(linearRgb * psrgba.a, psrgba.a);
}

float4 pLinearToPSRGB_Accurate (float4 pLinear) {
    float3 rgb = pLinear.rgb / max(pLinear.a, 0.00001);
    float3 srgb = LinearToSRGB(rgb);
    float4 pSrgb = float4(srgb * pLinear.a, pLinear.a);
    return pSrgb;
}

float4 pSRGBToPLinear (float4 psrgba) {
    float3 srgb = psrgba.rgb / max(psrgba.a, 0.00001);
    float3 linearRgb = approxSRGBToLinear(srgb);
    return float4(linearRgb * psrgba.a, psrgba.a);
}

float4 pLinearToPSRGB (float4 pLinear) {
    float3 rgb = pLinear.rgb / max(pLinear.a, 0.00001);
    float3 srgb = approxLinearToSRGB(rgb);
    float4 pSrgb = float4(srgb * pLinear.a, pLinear.a);
    return pSrgb;
}
