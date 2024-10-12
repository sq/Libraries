#pragma warning ( disable: 3571 )
#define PI 3.14159265358979323846

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
    if (psrgba.a <= (1 / 512))
        return 0;
    float a = min(psrgba.a, 1);
    float3 srgb = psrgba.rgb / a;
    float3 linearRgb = SRGBToLinear(srgb);
    return float4(linearRgb * a, a);
}

float4 pLinearToPSRGB_Accurate (float4 pLinear) {
    if (pLinear.a <= (1 / 512))
        return 0;
    float a = min(pLinear.a, 1);
    float3 rgb = pLinear.rgb / a;
    float3 srgb = LinearToSRGB(rgb);
    float4 pSrgb = float4(srgb * a, a);
    return pSrgb;
}

float4 pSRGBToPLinear (float4 psrgba) {
    if (psrgba.a <= (1 / 512))
        return 0;
    float3 srgb = psrgba.rgb / psrgba.a;
    float3 linearRgb = approxSRGBToLinear(srgb);
    return float4(linearRgb * psrgba.a, psrgba.a);
}

float4 pLinearToPSRGB (float4 pLinear) {
    if (pLinear.a <= (1 / 512))
        return 0;
    float3 rgb = pLinear.rgb / pLinear.a;
    float3 srgb = approxLinearToSRGB(rgb);
    float4 pSrgb = float4(srgb * pLinear.a, pLinear.a);
    return pSrgb;
}

float cbrtf (float value) {
	return pow(value, 1/3.0);
}

// OkLab: https://bottosson.github.io/posts/oklab/
// Copyright (c) 2020 Björn Ottosson
// Permission is hereby granted, free of charge, to any person obtaining a copy of
// this software and associated documentation files (the "Software"), to deal in
// the Software without restriction, including without limitation the rights to
// use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies
// of the Software, and to permit persons to whom the Software is furnished to do
// so, subject to the following conditions:
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

float3 LinearSRGBToOkLab (float3 srgb) {
    float l = 0.4122214708f * srgb.r + 0.5363325363f * srgb.g + 0.0514459929f * srgb.b;
	float m = 0.2119034982f * srgb.r + 0.6806995451f * srgb.g + 0.1073969566f * srgb.b;
	float s = 0.0883024619f * srgb.r + 0.2817188376f * srgb.g + 0.6299787005f * srgb.b;

    float l_ = cbrtf(l);
    float m_ = cbrtf(m);
    float s_ = cbrtf(s);

    return float3(
        0.2104542553f*l_ + 0.7936177850f*m_ - 0.0040720468f*s_,
        1.9779984951f*l_ - 2.4285922050f*m_ + 0.4505937099f*s_,
        0.0259040371f*l_ + 0.7827717662f*m_ - 0.8086757660f*s_
    );    
}

float3 OkLabToLinearSRGB (float3 oklab) {
    float l_ = oklab.x + 0.3963377774f * oklab.y + 0.2158037573f * oklab.z;
    float m_ = oklab.x - 0.1055613458f * oklab.y - 0.0638541728f * oklab.z;
    float s_ = oklab.x - 0.0894841775f * oklab.y - 1.2914855480f * oklab.z;

    float l = l_*l_*l_;
    float m = m_*m_*m_;
    float s = s_*s_*s_;

    return float3(
		 4.0767416621f * l - 3.3077115913f * m + 0.2309699292f * s,
		-1.2684380046f * l + 2.6097574011f * m - 0.3413193965f * s,
		-0.0041960863f * l - 0.7034186147f * m + 1.7076147010f * s
    );
}

float4 pSRGBToOkLab (float4 psrgba) {
    if (psrgba.a <= (1 / 512))
        return 0;
    float3 srgb = psrgba.rgb / psrgba.a;
    float3 rgb = SRGBToLinear(srgb);
    float3 oklab = LinearSRGBToOkLab(rgb);
    return float4(oklab.rgb, psrgba.a);
}

float4 OkLabToPSRGB (float4 oklab) {
    if (oklab.a <= (1 / 512))
        return 0;
    float3 rgb = OkLabToLinearSRGB(oklab.rgb);
    float3 srgb = LinearToSRGB(rgb);
    return float4(srgb * oklab.a, oklab.a);
}

float4 OkLabToOkLCh(float4 oklab) {
    float C = sqrt((oklab.y * oklab.y) + (oklab.z * oklab.z));
    float h = atan2(oklab.z, oklab.y) * 180 / PI;
    if (!isfinite(C))
        C = 0;
    
    if (!isfinite(h))
        h = 0;
    else if (h < 0)
        h += 360;
    
    return float4(oklab.x, C, h, oklab.w);
}

float4 OkLChToOkLab(float4 oklch) {
    float h = oklch.z * PI / 180;
    return float4(
        oklch.x,
        oklch.y * cos(h),
        oklch.y * sin(h),
        oklch.w
    );
}

float4 OkLChToLinearSRGB(float4 oklch) {
    return float4(
        OkLabToLinearSRGB(OkLChToOkLab(oklch).xyz), 1
    ) * oklch.w;
}
// end oklab