const float HueUnit = 1.0 / 6;

float4 pRGBAFromHSVA (float4 hsva) {
    float hue = hsva.x;
    float saturation = hsva.y;
    float value = hsva.z;
    float alpha = hsva.w;

    if (value <= 0)
        return float4(0, 0, 0, alpha);
    if ((value >= 1) && (saturation <= 0))
        return alpha;

    // value = min(1, value);
    // saturation = min(1, saturation);

    if (saturation <= 0)
        return float4(value, value, value, 1) * alpha;

    float segment = hue / HueUnit % 6;
    float c = saturation * value;
    // FIXME: floor segment here?
    float x = c * (1 - abs((segment % 2) - 1));

    float3 selector;

    if (segment <= 1)
        selector = float3(c, x, 0);
    else if (segment <= 2)
        selector = float3(x, c, 0);
    else if (segment <= 3)
        selector = float3(0, c, x);
    else if (segment <= 4)
        selector = float3(0, x, c);
    else if (segment <= 5)
        selector = float3(x, 0, c);
    else
        selector = float3(c, 0, x);

    float m = value - c;
    return float4(selector + m, 1) * alpha;
}

float4 hsvaFromPRGBA (float4 rgba) {
    // undo premultiplication
    rgba.rgb /= max(rgba.a, 0.0001);

    float minimum = min(rgba.r, min(rgba.g, rgba.b));
    float maximum = max(rgba.r, max(rgba.g, rgba.b));

    // Covers grayscale, black, and white
    if (maximum == minimum)
        return float4(0, 0, minimum, rgba.a);

    float b = maximum - minimum, d, hue;

    // value = (UInt16)(max * ValueMax / 255);
    // saturation = (UInt16)(b * SaturationMax / max);

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