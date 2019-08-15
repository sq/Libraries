const float HueUnit = 1.0 / 6;

float convertHSVAComponent (float n, float k, float s, float v) {
  k = (k + n) % 6;
  return v - v * s * max(min(min(k, 4 - k), 1), 0);
}

float4 pRGBAFromHSVA (float4 hsva) {
    float k = hsva.x / HueUnit;
    if (k < 0)
        k += (1 + floor(k / -6)) * 6;
    float4 rgba = float4(
        convertHSVAComponent(5, k, hsva.y, hsva.z),
        convertHSVAComponent(3, k, hsva.y, hsva.z),
        convertHSVAComponent(1, k, hsva.y, hsva.z),
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