shared float2 ViewportScale;
shared float2 ViewportPosition;

shared float4x4 ProjectionMatrix;
shared float4x4 ModelViewMatrix;

float4 TransformPosition (float4 position) {
    return mul(mul(position, ModelViewMatrix), ProjectionMatrix);
}