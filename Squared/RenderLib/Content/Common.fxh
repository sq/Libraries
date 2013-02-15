shared float2 ViewportScale;
shared float2 ViewportPosition;

shared float4x4 ProjectionMatrix;
shared float4x4 ModelViewMatrix;

float4 TransformPosition (float4 position, float offset) {
    // Transform to view space, then offset by half a pixel to align texels with screen pixels
    float4 modelViewPos = mul(position, ModelViewMatrix) - float4(offset, offset, 0, 0);
    // Finally project after offsetting
    return mul(modelViewPos, ProjectionMatrix);
}