// Approximations from http://chilliant.blogspot.com/2012/08/srgb-approximations-for-hlsl.html

float4 pSRGBToPLinear (float4 psrgba) {
    float3 srgb = psrgba.rgb / max(psrgba.a, 0.00001);
    float3 linearRgb = srgb * (srgb * (srgb * 0.305306011 + 0.682171111) + 0.012522878);
    float4 pLinear = float4(linearRgb * psrgba.a, psrgba.a);
    return pLinear;
}

float4 pLinearToPSRGB (float4 pLinear) {
    float3 rgb = pLinear.rgb / max(pLinear.a, 0.00001);
    float3 S1 = sqrt(rgb);
    float3 S2 = sqrt(S1);
    float3 S3 = sqrt(S2);
    float3 srgb = 0.662002687 * S1 + 0.684122060 * S2 - 0.323583601 * S3 - 0.0225411470 * rgb;
    float4 pSrgb = float4(srgb * pLinear.a, pLinear.a);
    return pSrgb;
}