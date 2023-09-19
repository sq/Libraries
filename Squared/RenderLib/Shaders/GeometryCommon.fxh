float4 TransformPosition (float4 position, bool unused) {
    // Transform to view space, then offset by half a pixel to align texels with screen pixels
    float4 modelViewPos = mul(position, GetViewportModelViewMatrix());
    // Finally project after offsetting
    return mul(modelViewPos, GetViewportProjectionMatrix());
}