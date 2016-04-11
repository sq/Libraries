#include "ViewTransformCommon.fxh"

float4 TransformPosition (float4 position, float offset) {
    // Transform to view space, then offset by half a pixel to align texels with screen pixels
    float4 modelViewPos = mul(position, Viewport.ModelView) - float4(offset, offset, 0, 0);
    // Finally project after offsetting
    return mul(modelViewPos, Viewport.Projection);
}