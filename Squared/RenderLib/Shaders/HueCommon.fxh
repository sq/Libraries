const float HueUnit = 1.0 / 6;

float convertHSLAComponent (float n, float k, float s, float l) {
  float a = s * min(l, 1 - l);
  k = (k + n) % 12;
  float m = min(min(k - 3, 9 - k), 1);
  return l - (a * max(m, -1));
}

float4 pRGBAFromHSLA (float4 hsla) {
    float k = hsla.x / (HueUnit / 2);
    float s = saturate(hsla.y);
    float l = saturate(hsla.z);
    if (k < 0)
        k += (1 + floor(k / -12)) * 12;
    float4 rgba = float4(
        convertHSLAComponent(0, k, s, l),
        convertHSLAComponent(8, k, s, l),
        convertHSLAComponent(4, k, s, l),
        1
    );
    return rgba * hsla.a;
}

float4 hslaFromPRGBA (float4 rgba) {
    rgba.rgb /= max(rgba.a, 0.0001);

    float minimum = min(rgba.r, min(rgba.g, rgba.b));
    float maximum = max(rgba.r, max(rgba.g, rgba.b));

    float luminance = saturate((maximum + minimum) / 2);

    if (maximum == minimum)
        return float4(0, 0, luminance, rgba.a);

    float b = maximum - minimum,
        saturation = (maximum - luminance) / max(min(luminance, 1 - luminance), 0.0001), 
        d, hue;

    if (rgba.r == maximum) {
        d = rgba.g - rgba.b;
        hue = d * HueUnit / b;
    } else if (rgba.g == maximum) {
        d = rgba.b - rgba.r;
        hue = (d * HueUnit / b) + (2 * HueUnit);
    } else {
        d = rgba.r - rgba.g;
        hue = (d * HueUnit / b) + (4 * HueUnit);
    }

    return float4(hue, saturation, luminance, rgba.a);
}


float convertHSVAComponent (float n, float k, float s, float v) {
  k = (k + n) % 6;
  return v - v * s * max(min(min(k, 4 - k), 1), 0);
}

float4 pRGBAFromHSVA (float4 hsva) {
    float k = hsva.x / HueUnit;
    float s = saturate(hsva.y);
    float v = saturate(hsva.z);
    if (k < 0)
        k += (1 + floor(k / -6)) * 6;
    float4 rgba = float4(
        convertHSVAComponent(5, k, s, v),
        convertHSVAComponent(3, k, s, v),
        convertHSVAComponent(1, k, s, v),
        1
    );
    return rgba * hsva.a;
}

float4 hsvaFromPRGBA (float4 rgba) {
    rgba.rgb /= max(rgba.a, 0.0001);

    float minimum = min(rgba.r, min(rgba.g, rgba.b));
    float maximum = max(rgba.r, max(rgba.g, rgba.b));

    // Covers grayscale, black, and white
    if (maximum == minimum)
        return float4(0, 0, minimum, rgba.a);

    float b = maximum - minimum, d, hue;

    if (rgba.r == maximum) {
        d = rgba.g - rgba.b;
        hue = d * HueUnit / b;
    } else if (rgba.g == maximum) {
        d = rgba.b - rgba.r;
        hue = (d * HueUnit / b) + (2 * HueUnit);
    } else {
        d = rgba.r - rgba.g;
        hue = (d * HueUnit / b) + (4 * HueUnit);
    }

    return float4(hue, b / maximum, maximum, rgba.a);
}