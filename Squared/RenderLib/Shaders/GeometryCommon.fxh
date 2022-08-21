uniform const float HalfPixelOffset;

float4 TransformPosition (float4 position, bool halfPixelOffset) {
    // Transform to view space, then offset by half a pixel to align texels with screen pixels
    float4 modelViewPos = mul(position, GetViewportModelViewMatrix());
    if (halfPixelOffset && HalfPixelOffset)
        modelViewPos -= 0.5;
    // Finally project after offsetting
    return mul(modelViewPos, GetViewportProjectionMatrix());
}