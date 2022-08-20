#define VALUE_CHANNEL_RGB 0
#define VALUE_CHANNEL_R 1
#define VALUE_CHANNEL_A 2
#define VALUE_CHANNEL_RG 3

#define ALPHA_CHANNEL_A 0
#define ALPHA_CONSTANT_ONE 1
#define ALPHA_CHANNEL_G 2

#define ALPHA_MODE_NORMAL 0
// ExtractRgba won't do anything, but this indicates that the RGB channels are not premultiplied
//  since it is incorrect to premultiply them
#define ALPHA_MODE_BC 1
#define ALPHA_MODE_BC7 2

// formatTraits: rgbSource, alphaSource, needsPremultiply, alphaMode
float ExtractMask (float4 input, float4 formatTraits) {
    float result = input.a;

    if ((int)formatTraits.x == VALUE_CHANNEL_R)
        result = input.r;
    else if ((int)formatTraits.x == VALUE_CHANNEL_A)
        ;

    // BC7: scale alpha because BC7 produces alpha error
    if (formatTraits.w >= ALPHA_MODE_BC7)
        result *= 254.0 / 255.0;

    return result;
}

// formatTraits: rgbSource, alphaSource, needsPremultiply, alphaMode
float ExtractAlpha (float4 input, float4 formatTraits) {
    float result = input.a;

    switch (abs((int)formatTraits.y)) {
        case ALPHA_CHANNEL_G:
            result = input.g;
            break;
        case ALPHA_CONSTANT_ONE:
            result = 1.0;
            break;
        default:
        // case ALPHA_CHANNEL_A:
            break;
    }

    // BC7: scale alpha because BC7 produces alpha error
    if (formatTraits.w >= ALPHA_MODE_BC7)
        result *= 254.0 / 255.0;

    return result;
}

// formatTraits: rgbSource, alphaSource, needsPremultiply, alphaMode
void ExtractLuminanceAlpha (float4 input, float4 formatTraits, out float luminance, out float alpha) {
    const float3 toGray = float3(0.299, 0.587, 0.144);

    int valueChannel = abs((int)formatTraits.x);
    switch (valueChannel) {
        case VALUE_CHANNEL_R:
            luminance = input.r;
            break;
        case VALUE_CHANNEL_A:
            luminance = input.a;
            break;
        case VALUE_CHANNEL_RG:
            luminance = (input.r + input.g) / 2;
            break;
        default:
        // case VALUE_CHANNEL_RGB:
            luminance = dot(input.rgb, toGray);
            break;
    }

    switch (abs((int)formatTraits.y)) {
        case ALPHA_CHANNEL_G:
            alpha = input.g;
            break;
        case ALPHA_CONSTANT_ONE:
            alpha = 1.0;
            break;
        default:
        // case ALPHA_CHANNEL_A:
            alpha = input.a;
            break;
    }

    // BC7: scale alpha because BC7 produces alpha error
    if (formatTraits.w >= ALPHA_MODE_BC7)
        alpha *= 254.0 / 255.0;

    // Premultiply
    if ((valueChannel != VALUE_CHANNEL_A) && (formatTraits.z >= 1))
        luminance *= alpha;
}

// formatTraits: rgbSource, alphaSource, needsPremultiply, alphaMode
float4 ExtractRgba (float4 input, float4 formatTraits) {
    float4 result = input;

    switch (abs((int)formatTraits.x)) {
        case VALUE_CHANNEL_R:
            result.rgb = input.r;
            break;
        case VALUE_CHANNEL_A:
            result.rgb = input.a;
            break;
        default:
        // case VALUE_CHANNEL_RGB:
        // case VALUE_CHANNEL_RG:
            break;
    }

    switch (abs((int)formatTraits.y)) {
        case ALPHA_CHANNEL_G:
            result.a = input.g;
            break;
        case ALPHA_CONSTANT_ONE:
            result.a = 1;
            break;
        default:
        // case ALPHA_CHANNEL_A:
            break;
    }

    // BC7: scale alpha because BC7 produces alpha error
    if (formatTraits.w >= ALPHA_MODE_BC7)
        result.a *= 254.0 / 255.0;

    // Premultiply
    if (formatTraits.z >= 1)
        result.rgb *= result.a;

    return result;
}