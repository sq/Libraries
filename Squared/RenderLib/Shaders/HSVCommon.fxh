const float HueUnit = 1.0 / 6.0;

float4 pRGBAFromHSVA (float4 hsva) {
    float hue = hsva.x;
    float saturation = hsva.y;
    float value = hsva.z;
    float alpha = hsva.w;

    if (value <= 0)
        return float4(0, 0, 0, alpha);
    if ((value >= 1) && (saturation <= 0))
        return alpha;

    value = min(1, value);
    saturation = min(1, saturation);

    if (saturation <= 0)
        return float4(value, value, value, 1) * alpha;

    uint segment = floor(hue / HueUnit);
    float remainder = hue % HueUnit;
    float saturatedValue = saturation * value;

    float c = (1 - saturation) * saturatedValue;
    float b = (remainder * saturatedValue) / HueUnit + c;
    float rb = saturatedValue - b + (c * 2);
    float a = saturatedValue + c;

    switch (segment) {
        case 0:
            return float4(a, b, c, 1) * alpha;
        case 1:
            return float4(rb, a, c, 1) * alpha;
        case 2:
            return float4(c, a, b, 1) * alpha;
        case 3:
            return float4(c, rb, a, 1) * alpha;
        case 4:
            return float4(b, c, a, 1) * alpha;
        case 5:
        default:
            return float4(a, c, rb, 1) * alpha;
    }
}

float4 hsvaFromPRGBA (float4 rgba) {
    // undo premultiplication
    rgba.rgb /= max(rgba.a, 0.0001);

    float minimum = min(rgba.r, min(rgba.g, rgba.b));
    float maximum = max(rgba.r, max(rgba.g, rgba.b));

    if (maximum == minimum) {
        return float4(0, 0, minimum, rgba.a);
    } else if (maximum <= 0) {
        return float4(0, 0, 0, rgba.a);
    }

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